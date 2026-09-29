using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Gsplat;
using UnityEngine;

namespace MRBase.GsplatBench
{
    /// <summary>
    /// 自动 sweep：按预定义序列逐档配置旋钮，每档先预热再采样，取 GPU 中位数。
    ///
    /// 默认是**累加**序列（第 N 档 = 第 N-1 档再叠一项），因为旋钮之间有交互
    /// （downscale 之后 MSAA 的成本已经不一样了），而我们要的答案是「在已有配置上
    /// 再开这个还能省多少」—— 这正是决定下一步投资哪里所需要的边际收益。
    ///
    /// 单变量模式（每档只从 baseline 改一项）归因更干净但会高估总收益，
    /// 保留它用于某档出现反直觉结果时做回归定位。
    /// </summary>
    public sealed class BenchSweep
    {
        public enum Mode
        {
            Cumulative,
            SingleVariable,
            LodComparison,
        }

        readonly struct Step
        {
            public readonly string Name;
            public readonly BenchKnobs.Knob Knob;
            public readonly float Value;
            public readonly bool IsBaseline;

            // 仅 LodComparison：切到哪个资产、哪个视点。AssetName 为 null 表示不切。
            public readonly string AssetName;
            public readonly bool AssetIsLod;
            public readonly int LodBudget;
            public readonly int Viewpoint;

            public Step(string name)
            {
                Name = name;
                Knob = default;
                Value = 0f;
                IsBaseline = true;
                AssetName = null; AssetIsLod = false; LodBudget = 0; Viewpoint = -1;
            }

            public Step(string name, BenchKnobs.Knob knob, float value)
            {
                Name = name;
                Knob = knob;
                Value = value;
                IsBaseline = false;
                AssetName = null; AssetIsLod = false; LodBudget = 0; Viewpoint = -1;
            }

            public Step(string assetName, bool assetIsLod, int lodBudget, int viewpoint)
            {
                Name = $"{assetName}{(assetIsLod ? $".gsd@{lodBudget / 10000}w" : ".ply")}-v{viewpoint}";
                Knob = default;
                Value = 0f;
                IsBaseline = false;
                AssetName = assetName;
                AssetIsLod = assetIsLod;
                LodBudget = lodBudget;
                Viewpoint = viewpoint;
            }
        }

        // SPI 不在序列里 —— 立体渲染模式是构建期设置，对比它需要两个包。
        static readonly Step[] k_Sequence =
        {
            new("baseline"),
            new("+msaa-off", BenchKnobs.Knob.Msaa, 1),
            new("+sort-1/30", BenchKnobs.Knob.SortInterval, 30),
            new("+sh0", BenchKnobs.Knob.ShDegree, 0),
            new("+downscale-0.25", BenchKnobs.Knob.Downscale, 0.25f),
            new("+viewscale-0.7", BenchKnobs.Knob.ViewportScale, 0.7f),
            new("+offscreen-0.5", BenchKnobs.Knob.OffscreenScale, 0.5f),
        };

        // LoD 对比（spec §8.2）：同一套推荐旋钮下，全量 .ply 与不同预算的 .gsd 对照。
        // 外层按资产、内层按视点：资产切换（整份重传）最少。
        static readonly (string Asset, bool Lod, int Budget)[] k_LodCases =
        {
            ("Model_50w", false, 0),
            ("Model_200w", false, 0),
            ("Model_100w", true, 300000),
            ("Model_100w", true, 500000),
            ("Model_100w", true, 800000),
            ("Model_200w", true, 300000),
            ("Model_200w", true, 500000),
            ("Model_200w", true, 800000),
        };

        Step[] m_Steps = k_Sequence;

        Step[] BuildLodSteps()
        {
            var steps = new List<Step>();
            foreach (var c in k_LodCases)
                for (var v = 0; v < m_Rig.ViewpointCount; ++v)
                    steps.Add(new Step(c.Asset, c.Lod, c.Budget, v));
            return steps.ToArray();
        }

        readonly BenchRig m_Rig;
        readonly List<string> m_Lines = new();

        public bool Running { get; private set; }
        public bool StopRequested { get; private set; }
        public Mode CurrentMode { get; private set; }
        public int StepIndex { get; private set; }
        public int StepCount => m_Steps.Length;
        public string StepName { get; private set; } = string.Empty;
        public string Phase { get; private set; } = "idle";
        public float PhaseRemaining { get; private set; }
        public string OutputPath { get; private set; } = string.Empty;

        public BenchSweep(BenchRig rig)
        {
            m_Rig = rig;
        }

        public void RequestStop() => StopRequested = true;

        public IEnumerator Run(Mode mode)
        {
            if (Running)
                yield break;

            Running = true;
            StopRequested = false;
            CurrentMode = mode;
            m_Lines.Clear();

            m_Steps = mode == Mode.LodComparison ? BuildLodSteps() : k_Sequence;
            if (mode == Mode.LodComparison)
            {
                if (m_Steps.Length == 0)
                {
                    Abort("BenchRig 没有配置 viewpoints");
                    yield break;
                }

                // D17：拿不到 GPU 时间就不跑。墙钟时间测的是另一件事，不能拿来顶替。
                m_Rig.Metrics.ResetWindow();
                for (var i = 0; i < 60; ++i)
                    yield return null;
                if (!m_Rig.Metrics.HasGpuData)
                {
                    Abort("no GPU timing: " + m_Rig.Metrics.GpuUnavailableReason);
                    yield break;
                }
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            OutputPath = Path.Combine(Application.persistentDataPath, $"gsplat-bench-{stamp}.csv");

            WriteContextHeader(mode);
            AppendLine("step_index,step_name,mode,valid," + BenchKnobs.CsvHeader +
                       ",asset,asset_is_lod,viewpoint,drawn_splats,lod_traversal_ms,lod_latency_frames" +
                       ",splat_total,renderer_count,gpu_source,gpu_median_ms,gpu_p99_ms,compositor_gpu_ms," +
                       "cpu_median_ms,display_interval_ms,budget_ms,refresh_hz,stereo,foveation,eye_res_scale," +
                       "samples,drift");

            for (StepIndex = 0; StepIndex < m_Steps.Length; ++StepIndex)
            {
                if (StopRequested)
                    break;

                StepName = m_Steps[StepIndex].Name;
                ConfigureStep(mode, StepIndex);
                if (StopRequested)
                    break;

                // 预热：这一段数据不计入。刚换配置时着色器变体编译、资源重分配、
                // GPU 频率爬升都还没稳定，采进去就是噪声。
                yield return RunPhase("warmup", m_Rig.WarmupSeconds);
                if (StopRequested)
                    break;

                m_Rig.Metrics.ResetWindow();
                m_Rig.Conditions.ClearDrift();

                yield return RunPhase("sample", m_Rig.SampleSeconds);

                RecordRow(mode);
            }

            Phase = StopRequested ? "stopped" : "done";
            PhaseRemaining = 0f;
            Running = false;
            Flush();
            BenchLog.Milestone($"sweep {Phase}: {m_Lines.Count - 1} rows -> {OutputPath}");
        }

        void Abort(string reason)
        {
            Phase = "aborted";
            PhaseRemaining = 0f;
            Running = false;
            BenchLog.Milestone("sweep aborted: " + reason);
            Debug.LogError($"{BenchLog.Tag}|sweep aborted: {reason}");
        }

        IEnumerator RunPhase(string phaseName, float seconds)
        {
            Phase = phaseName;
            var end = Time.unscaledTime + seconds;

            while (Time.unscaledTime < end)
            {
                if (StopRequested)
                    yield break;

                PhaseRemaining = end - Time.unscaledTime;
                yield return null;
            }

            PhaseRemaining = 0f;
        }

        void ConfigureStep(Mode mode, int index)
        {
            if (mode == Mode.LodComparison)
            {
                var lodStep = m_Steps[index];
                m_Rig.Knobs.ResetToRecommended();
                if (lodStep.AssetIsLod)
                    m_Rig.Knobs.SetValue(BenchKnobs.Knob.LodBudget, lodStep.LodBudget);
                m_Rig.ApplyKnobsNow();
                if (!m_Rig.SelectAsset(lodStep.AssetName, lodStep.AssetIsLod))
                {
                    // 资产没挂上就停：缺一档的对照表比空档更容易被误读。
                    var what = $"{lodStep.AssetName}{(lodStep.AssetIsLod ? ".gsd" : ".ply")}";
                    BenchLog.Milestone($"sweep: {what} is not in BenchRig.assets");
                    Debug.LogError($"{BenchLog.Tag}|sweep: {what} is not in BenchRig.assets");
                    RequestStop();
                    return;
                }

                m_Rig.ApplyViewpoint(lodStep.Viewpoint);
                return;
            }

            m_Rig.Knobs.ResetToBaseline();

            if (mode == Mode.Cumulative)
            {
                for (var i = 0; i <= index; ++i)
                {
                    var step = m_Steps[i];
                    if (!step.IsBaseline)
                        m_Rig.Knobs.SetValue(step.Knob, step.Value);
                }
            }
            else
            {
                var step = m_Steps[index];
                if (!step.IsBaseline)
                    m_Rig.Knobs.SetValue(step.Knob, step.Value);
            }

            m_Rig.ApplyKnobsNow();
        }

        void RecordRow(Mode mode)
        {
            var metrics = m_Rig.Metrics;
            var conditions = m_Rig.Conditions;

            // GPU 数据拿不到时这一档没有主指标，必须标 invalid 而不是记 0 混过去。
            var valid = !conditions.Drifted && metrics.HasGpuData && metrics.SampleCount > 0;
            var drift = conditions.Drifted ? conditions.DriftReason.Replace(',', ';') : string.Empty;
            if (!metrics.HasGpuData)
            {
                var reason = "no-gpu-timing (" + metrics.GpuUnavailableReason.Replace(',', ';') + ")";
                drift = string.IsNullOrEmpty(drift) ? reason : drift + "; " + reason;
            }

            var row = string.Join(",",
                StepIndex.ToString(CultureInfo.InvariantCulture),
                StepName,
                mode.ToString(),
                valid ? "valid" : "invalid",
                m_Rig.Knobs.CsvRow(),
                m_Rig.AssetLabel,
                (m_Rig.CurrentAsset is GsplatLodAsset).ToString(),
                m_Rig.ViewpointLabel,
                m_Rig.DrawnTotal.ToString(CultureInfo.InvariantCulture),
                m_Rig.IsLodActive ? m_Rig.MeasureLodTraversalMs().ToString("F3", CultureInfo.InvariantCulture) : string.Empty,
                m_Rig.LodLatencyFrames.ToString(CultureInfo.InvariantCulture),
                m_Rig.SplatTotal.ToString(CultureInfo.InvariantCulture),
                m_Rig.RendererCount.ToString(CultureInfo.InvariantCulture),
                metrics.GpuSource.ToString(),
                metrics.GpuMedianMs.ToString("F3", CultureInfo.InvariantCulture),
                metrics.GpuLow1Ms.ToString("F3", CultureInfo.InvariantCulture),
                metrics.CompositorGpuMs.ToString("F3", CultureInfo.InvariantCulture),
                metrics.CpuMedianMs.ToString("F3", CultureInfo.InvariantCulture),
                metrics.DisplayIntervalMedianMs.ToString("F3", CultureInfo.InvariantCulture),
                conditions.BudgetMs.ToString("F3", CultureInfo.InvariantCulture),
                conditions.RefreshHz.ToString("F1", CultureInfo.InvariantCulture),
                conditions.StereoMode,
                conditions.FoveationLevel.ToString("F2", CultureInfo.InvariantCulture),
                conditions.EyeResolutionScale.ToString("F2", CultureInfo.InvariantCulture),
                metrics.SampleCount.ToString(CultureInfo.InvariantCulture),
                drift);

            AppendLine(row);
        }

        void WriteContextHeader(Mode mode)
        {
            var conditions = m_Rig.Conditions;

            AppendLine("# gsplat-bench " + BenchRig.HarnessVersion);
            AppendLine("# utc," + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            AppendLine($"# device,{SystemInfo.deviceModel} / {SystemInfo.graphicsDeviceName} / {SystemInfo.graphicsDeviceType}");
            AppendLine($"# unity,{Application.unityVersion}");
            AppendLine($"# asset,{m_Rig.AssetLabel}");
            AppendLine($"# asset_splat_count,{m_Rig.AssetSplatCount}");
            AppendLine($"# stereo,{conditions.StereoMode}");
            AppendLine($"# locked_refresh_hz,{conditions.LockedRefreshHz.ToString("F1", CultureInfo.InvariantCulture)}");
            AppendLine($"# locked_viewport_scale,{conditions.LockedViewportScale.ToString("F2", CultureInfo.InvariantCulture)}");
            AppendLine($"# locked_foveation,{conditions.LockedFoveationLevel.ToString("F2", CultureInfo.InvariantCulture)}");
            AppendLine($"# sweep_mode,{mode}");
            AppendLine($"# warmup_s,{m_Rig.WarmupSeconds.ToString("F1", CultureInfo.InvariantCulture)}");
            AppendLine($"# sample_s,{m_Rig.SampleSeconds.ToString("F1", CultureInfo.InvariantCulture)}");
            AppendLine($"# hud_enabled,{m_Rig.HudEnabled}");
            AppendLine($"# game_view,{Screen.width}x{Screen.height}");
            AppendLine($"# viewpoints,{m_Rig.DescribeViewpoints().Replace(',', ' ')}");
        }

        /// <summary>
        /// 每行都立刻落盘。sweep 可能被中断（用户停、应用被切走、设备过热降频退出），
        /// 已经跑完的档不该跟着丢。
        /// </summary>
        void AppendLine(string line)
        {
            m_Lines.Add(line);
            BenchLog.Write("csv " + line);
            Flush();
        }

        void Flush()
        {
            if (string.IsNullOrEmpty(OutputPath))
                return;

            try
            {
                var builder = new StringBuilder();
                foreach (var line in m_Lines)
                    builder.Append(line).Append('\n');
                File.WriteAllText(OutputPath, builder.ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{BenchLog.Tag}|csv write failed: {e.Message}");
            }
        }

        public void AppendTo(StringBuilder builder)
        {
            builder.Append("== sweep ==\n");
            if (!Running && StepCount > 0 && string.IsNullOrEmpty(OutputPath))
            {
                builder.Append("  idle   (Y = start cumulative, Y+hold = single-variable)\n");
                return;
            }

            builder.Append("  ").Append(Running ? "RUN " : "END ")
                .Append(CurrentMode.ToString())
                .Append("  step ").Append(StepIndex + 1).Append('/').Append(StepCount)
                .Append("  ").Append(StepName).Append('\n');
            builder.Append("  ").Append(Phase);
            if (PhaseRemaining > 0f)
                builder.Append(' ').Append(PhaseRemaining.ToString("F1")).Append('s');
            builder.Append('\n');
        }
    }
}
