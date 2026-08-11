using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MRBase.GsplatBench
{
    /// <summary>
    /// 帧时间采集。主指标是 **GPU 毫秒中位数**，不是 FPS ——
    /// FPS 被刷新率钳制（跑得再快也只显示 72），且在 XR 上抖到没法读数。
    /// GPU ms 是连续量，能直接看出「离预算还差多少」和「这一档省了多少」。
    ///
    /// 取中位数而非均值：均值会被偶发尖刺污染，而尖刺在 XR 上遍地都是。
    /// 另报 1% low（第 99 百分位）用来发现「中位数好看但会卡」的情况。
    /// </summary>
    public sealed class BenchMetrics
    {
        /// <summary>滑窗上限。72fps 下约 28 秒，远大于单档采样时长。</summary>
        const int k_MaxSamples = 2048;

        /// <summary>
        /// 每帧向 FrameTimingManager 要的历史帧数。
        ///
        /// 不能只要 1 帧：GPU 时间戳要滞后若干帧才回填，最近那一帧的 gpuFrameTime 恒为 0。
        /// 真机上表现为 GPU 永远 n/a、整轮 sweep 全标 invalid —— 已经踩过一次。
        /// 要一个窗口，从里面挑已回填且尚未采过的。
        /// </summary>
        const int k_TimingWindow = 16;

        readonly FrameTiming[] m_Scratch = new FrameTiming[k_TimingWindow];
        readonly List<double> m_Gpu = new(k_MaxSamples);
        readonly List<double> m_Cpu = new(k_MaxSamples);
        readonly List<double> m_Interval = new(k_MaxSamples);
        readonly List<double> m_SortBuffer = new(k_MaxSamples);

        /// <summary>
        /// 当前采样窗口里是否真有 GPU 样本。
        ///
        /// 刻意**不做粘性布尔**：粘性版本会让 <see cref="ResetWindow"/> 之后的空窗口
        /// 仍然自称有数据，于是 sweep 把一整档 0.00ms 标成 valid。从窗口本身推导，
        /// 语义就不可能和数据打架。
        /// </summary>
        public bool HasGpuData => m_Gpu.Count > 0;

        /// <summary>上一次 GetLatestTimings 返回的帧数。为 0 说明 FrameTimingManager 整个没在工作。</summary>
        public int LastTimingCount { get; private set; }

        /// <summary>当前 GPU 时间取自哪条路。必须随每一档写进 CSV。</summary>
        public BenchGpuSource GpuSource { get; private set; } = BenchGpuSource.None;

        /// <summary>合成器 GPU 时间（仅 OVR 路可得）。它不算在应用预算里，但能解释「应用不满却仍掉帧」。</summary>
        public double CompositorGpuMs { get; private set; }

        ulong m_LastFrameStamp;
        int m_ZeroGpuFrames;

        /// <summary>
        /// GPU 不可用时的诊断串。只说「n/a」等于下次还得靠猜，
        /// 所以把「拿到几帧」和「其中多少帧 GPU 为 0」直接摆出来。
        /// </summary>
        public string GpuUnavailableReason =>
            LastTimingCount == 0
                ? "FrameTimingManager returned 0 timings (Frame Timing Stats off?)"
                : $"FrameTiming got {LastTimingCount} timings, {m_ZeroGpuFrames} with gpuFrameTime=0; OVR perf metrics unavailable";

        public int SampleCount => m_Interval.Count;

        public double GpuMedianMs => Median(m_Gpu);
        public double GpuLow1Ms => Percentile(m_Gpu, 0.99);
        public double CpuMedianMs => Median(m_Cpu);
        public double DisplayIntervalMedianMs => Median(m_Interval);

        public double FpsFromInterval
        {
            get
            {
                var ms = DisplayIntervalMedianMs;
                return ms > 0 ? 1000.0 / ms : 0.0;
            }
        }

        public double FpsLow1
        {
            get
            {
                var ms = Percentile(m_Interval, 0.99);
                return ms > 0 ? 1000.0 / ms : 0.0;
            }
        }

        public void ResetWindow()
        {
            m_Gpu.Clear();
            m_Cpu.Clear();
            m_Interval.Clear();
            m_ZeroGpuFrames = 0;
        }

        /// <summary>每帧调用一次。</summary>
        public void Sample()
        {
            Push(m_Interval, Time.unscaledDeltaTime * 1000.0);

            FrameTimingManager.CaptureFrameTimings();
            var count = FrameTimingManager.GetLatestTimings(k_TimingWindow, m_Scratch);
            LastTimingCount = (int)count;
            if (count == 0)
                return;

            var gotFrameTimingGpu = false;

            // GetLatestTimings 返回的是「最近 count 帧」，索引 0 最新。
            // 按 frameStartTimestamp 去重，避免同一帧被反复计入 —— 否则窗口一大，
            // 中位数就被最近几帧刷屏，采样数也虚高。
            for (var i = (int)count - 1; i >= 0; --i)
            {
                var timing = m_Scratch[i];
                if (timing.frameStartTimestamp <= m_LastFrameStamp)
                    continue;

                m_LastFrameStamp = timing.frameStartTimestamp;

                // gpuFrameTime 在尚未回填、或平台不支持时为 0。
                // 全程为 0 时必须让人看见「不可用」，而不是悄悄记一串 0。
                if (timing.gpuFrameTime > 0)
                {
                    gotFrameTimingGpu = true;
                    Push(m_Gpu, timing.gpuFrameTime);
                }
                else
                {
                    ++m_ZeroGpuFrames;
                }

                if (timing.cpuFrameTime > 0)
                    Push(m_Cpu, timing.cpuFrameTime);
            }

            // 按**本帧**的结果选源。早前用的是累计标志，一旦 OVR 那条路成功一次，
            // 下一帧就被判回 FrameTiming 且不再采 OVR —— 表现为 src 标错、GPU 恒 0。
            if (gotFrameTimingGpu)
                GpuSource = BenchGpuSource.FrameTiming;
            else
                SampleOvrPerfMetrics();
        }

        /// <summary>
        /// Quest 3 / Vulkan 上 <c>FrameTimingManager</c> 的 gpuFrameTime 实测恒为 0
        /// （真机验证：拿到 16 帧、119 帧全为 0），所以退到 Meta 的 perf metrics ——
        /// 这正是 OVR Metrics Tool 显示的那个数。
        ///
        /// 这推翻了 design D3「厂商中立优先」的原判：中立 API 在目标设备上根本不产出数字，
        /// 而本轮 PICO 已被裁掉。一个真实存在的厂商数 &gt; 一个取不到的中立数。
        /// 来源记进 GpuSource 并落 CSV，避免跨来源误比。
        /// </summary>
        void SampleOvrPerfMetrics()
        {
            // 不加 !UNITY_EDITOR：编辑器里 OVR runtime 没起来时它返回 null，安全穿过，
            // 而保留编译能让这段代码在编辑器里就被验证，不必靠一次 16 分钟的真机构建才发现拼错。
#if UNITY_ANDROID
            var app = OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.App_GpuTime_Float);
            if (app.HasValue && app.Value > 0f)
            {
                GpuSource = BenchGpuSource.OvrPerfMetrics;

                Push(m_Gpu, ToMilliseconds(app.Value));

                var compositor = OVRPlugin.GetPerfMetricsFloat(OVRPlugin.PerfMetrics.Compositor_GpuTime_Float);
                if (compositor.HasValue)
                    CompositorGpuMs = ToMilliseconds(compositor.Value);
            }
#endif
        }

        /// <summary>
        /// OVR 的 perf metrics 在不同 runtime 版本上报过秒也报过毫秒，文档没有硬承诺。
        /// 判据不含糊：一帧的 GPU 时间以秒计永远 &lt; 1，以毫秒计永远 &gt; 1，
        /// 中间没有真实取值。留这个换算而不是写死一个系数，是因为它跨 runtime 版本才站得住。
        /// </summary>
        static double ToMilliseconds(float value) => value < 1f ? value * 1000.0 : value;

        static void Push(List<double> list, double value)
        {
            if (list.Count >= k_MaxSamples)
                list.RemoveAt(0);
            list.Add(value);
        }

        double Median(List<double> source) => Percentile(source, 0.5);

        /// <param name="fraction">0.5 = 中位数；0.99 = 最差 1%。</param>
        double Percentile(List<double> source, double fraction)
        {
            if (source.Count == 0)
                return 0.0;

            m_SortBuffer.Clear();
            m_SortBuffer.AddRange(source);
            m_SortBuffer.Sort();

            var index = Mathf.Clamp(
                Mathf.RoundToInt((float)(fraction * (m_SortBuffer.Count - 1))),
                0, m_SortBuffer.Count - 1);
            return m_SortBuffer[index];
        }

        public void AppendTo(StringBuilder builder, float budgetMs)
        {
            builder.Append("== frame ==\n");

            if (HasGpuData)
            {
                var gpu = GpuMedianMs;
                builder.Append("  GPU ").Append(gpu.ToString("F2")).Append("ms")
                    .Append("  (1% low ").Append(GpuLow1Ms.ToString("F2")).Append(")")
                    .Append("  src=").Append(GpuSource).Append('\n');

                if (CompositorGpuMs > 0)
                    builder.Append("  compositor ").Append(CompositorGpuMs.ToString("F2")).Append("ms\n");

                if (budgetMs > 0f)
                {
                    var used = gpu / budgetMs;
                    builder.Append("  budget ").Append(budgetMs.ToString("F1")).Append("ms  ")
                        .Append(Bar(used)).Append(' ')
                        .Append((used * 100.0).ToString("F0")).Append("%\n");
                }
            }
            else
            {
                builder.Append("  GPU  n/a  ").Append(GpuUnavailableReason).Append('\n');
            }

            builder.Append("  CPU ").Append(CpuMedianMs.ToString("F2")).Append("ms")
                .Append("   FPS ").Append(FpsFromInterval.ToString("F1"))
                .Append(" (1% low ").Append(FpsLow1.ToString("F1")).Append(')')
                .Append("   n=").Append(SampleCount).Append('\n');
        }

        static string Bar(double fraction)
        {
            const int width = 10;
            var filled = Mathf.Clamp(Mathf.RoundToInt((float)fraction * width), 0, width);
            return new string('#', filled) + new string('.', width - filled);
        }
    }
}
