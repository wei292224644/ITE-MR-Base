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

        readonly FrameTiming[] m_Scratch = new FrameTiming[1];
        readonly List<double> m_Gpu = new(k_MaxSamples);
        readonly List<double> m_Cpu = new(k_MaxSamples);
        readonly List<double> m_Interval = new(k_MaxSamples);
        readonly List<double> m_SortBuffer = new(k_MaxSamples);

        /// <summary>GPU 时间是否真的拿得到。拿不到时必须显式报不可用，不得用 FPS 反推冒充。</summary>
        public bool HasGpuData { get; private set; }

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
        }

        /// <summary>每帧调用一次。</summary>
        public void Sample()
        {
            Push(m_Interval, Time.unscaledDeltaTime * 1000.0);

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, m_Scratch) == 0)
                return;

            var timing = m_Scratch[0];

            // gpuFrameTime 在部分平台/未启用 Frame Timing Stats 时恒为 0，
            // 那种情况下必须让人看见「不可用」，而不是悄悄记一串 0。
            if (timing.gpuFrameTime > 0)
            {
                HasGpuData = true;
                Push(m_Gpu, timing.gpuFrameTime);
            }

            if (timing.cpuFrameTime > 0)
                Push(m_Cpu, timing.cpuFrameTime);
        }

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
                    .Append("  (1% low ").Append(GpuLow1Ms.ToString("F2")).Append(")\n");

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
                builder.Append("  GPU  n/a  <-- enable Player Settings > Frame Timing Stats\n");
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
