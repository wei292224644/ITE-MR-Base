using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Gsplat;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MRBase.GsplatBench
{
    /// <summary>
    /// 被测旋钮。每个旋钮都是**离散档位**而不是连续值 —— 真机上用手柄调连续值既慢又不可复现，
    /// 而我们要的是可复现的档位对比。
    ///
    /// 注意这里**没有 SPI / Multi-pass**：立体渲染模式是 OpenXR 的构建期设置，
    /// 运行时改不了。要对比就得出两个包，它在 <see cref="BenchConditions"/> 里作为
    /// 只读上下文记录。
    /// </summary>
    public sealed class BenchKnobs
    {
        public enum Knob
        {
            Msaa = 0,
            ShDegree,
            Downscale,
            SortInterval,
            ViewportScale,
            RendererCopies,
            Foveation,
            CutoutMeters,
        }

        static readonly int[] k_MsaaSteps = { 1, 2, 4, 8 };
        static readonly int[] k_ShSteps = { 0, 1, 2, 3 };
        static readonly float[] k_DownscaleSteps = { 0f, 0.1f, 0.2f, 0.25f, 0.3f, 0.4f, 0.5f };
        static readonly int[] k_SortSteps = { 1, 2, 5, 10, 30, 60 };
        static readonly float[] k_ViewportSteps = { 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f };
        static readonly int[] k_CopySteps = { 1, 2, 4, 8, 16 };
        static readonly float[] k_FoveationSteps = { 0f, 0.33f, 0.66f, 1f };

        // 0 = 关闭裁剪盒。其余是立方体边长（米）：从「几乎不裁」一路收到「只留眼前一小块」，
        // 用来量化离群 splat 到底吃掉多少 —— 扫描件的包围盒常被它们撑到几公里。
        static readonly float[] k_CutoutSteps = { 0f, 200f, 100f, 50f, 20f, 10f, 5f };

        // 各旋钮当前所处的档位下标。
        //
        // 启动值 = **推荐配置**，不是 sweep 的 baseline。两者刻意分开：
        // sweep 的 baseline 故意配成最贵档（MSAA 4x / SH3 / 每帧排序 / 全分辨率），
        // 因为它要测的是「从最差往下每开一项还能省多少」的边际收益；
        // 而人戴着头显手动看的时候，从最差档起步只意味着先拧五次才到能看的状态。
        int m_Msaa;             // off
        int m_Sh;               // degree 0
        int m_Downscale = 3;    // 0.25
        int m_Sort = 4;         // 1/30
        int m_Viewport = 2;     // 0.7
        int m_Copies;           // x1
        int m_Foveation = 2;    // 0.66
        int m_Cutout;           // off

        public const int KnobCount = 8;

        public int Selected { get; private set; }

        public int MsaaSamples => k_MsaaSteps[m_Msaa];
        public int ShDegree => k_ShSteps[m_Sh];
        public float Downscale => k_DownscaleSteps[m_Downscale];
        public int SortInterval => k_SortSteps[m_Sort];
        public float ViewportScale => k_ViewportSteps[m_Viewport];
        public int RendererCopies => k_CopySteps[m_Copies];
        public float FoveationLevel => k_FoveationSteps[m_Foveation];

        /// <summary>裁剪盒边长（米）。0 表示不启用裁剪。</summary>
        public float CutoutMeters => k_CutoutSteps[m_Cutout];

        public void SelectNext() => Selected = (Selected + 1) % KnobCount;
        public void SelectPrevious() => Selected = (Selected + KnobCount - 1) % KnobCount;

        public void AdjustSelected(int direction) => Adjust((Knob)Selected, direction);

        public void Adjust(Knob knob, int direction)
        {
            switch (knob)
            {
                case Knob.Msaa: m_Msaa = Step(m_Msaa, direction, k_MsaaSteps.Length); break;
                case Knob.ShDegree: m_Sh = Step(m_Sh, direction, k_ShSteps.Length); break;
                case Knob.Downscale: m_Downscale = Step(m_Downscale, direction, k_DownscaleSteps.Length); break;
                case Knob.SortInterval: m_Sort = Step(m_Sort, direction, k_SortSteps.Length); break;
                case Knob.ViewportScale: m_Viewport = Step(m_Viewport, direction, k_ViewportSteps.Length); break;
                case Knob.RendererCopies: m_Copies = Step(m_Copies, direction, k_CopySteps.Length); break;
                case Knob.Foveation: m_Foveation = Step(m_Foveation, direction, k_FoveationSteps.Length); break;
                case Knob.CutoutMeters: m_Cutout = Step(m_Cutout, direction, k_CutoutSteps.Length); break;
            }
        }

        /// <summary>把某个旋钮直接设到指定值（sweep 用）。找不到精确档位时取最接近的。</summary>
        public void SetValue(Knob knob, float value)
        {
            switch (knob)
            {
                case Knob.Msaa: m_Msaa = NearestInt(k_MsaaSteps, value); break;
                case Knob.ShDegree: m_Sh = NearestInt(k_ShSteps, value); break;
                case Knob.Downscale: m_Downscale = NearestFloat(k_DownscaleSteps, value); break;
                case Knob.SortInterval: m_Sort = NearestInt(k_SortSteps, value); break;
                case Knob.ViewportScale: m_Viewport = NearestFloat(k_ViewportSteps, value); break;
                case Knob.RendererCopies: m_Copies = NearestInt(k_CopySteps, value); break;
                case Knob.Foveation: m_Foveation = NearestFloat(k_FoveationSteps, value); break;
                case Knob.CutoutMeters: m_Cutout = NearestFloat(k_CutoutSteps, value); break;
            }
        }

        /// <summary>sweep 的第 0 档：全部最贵。手动模式下按重置键也回到这里，用来做对照。</summary>
        public void ResetToBaseline()
        {
            m_Msaa = 2;      // 4x
            m_Sh = 3;        // degree 3
            m_Downscale = 0;
            m_Sort = 0;      // 1/1
            m_Viewport = 5;  // 1.0
            m_Copies = 0;
            m_Foveation = 0; // 关：sweep 的边际收益要在同一 FFR 下比较
            m_Cutout = 0;    // 关
        }

        /// <summary>推荐配置：五项省电旋钮全开。启动时即此状态。</summary>
        public void ResetToRecommended()
        {
            m_Msaa = 0;
            m_Sh = 0;
            m_Downscale = 3;
            m_Sort = 4;
            m_Viewport = 2;
            m_Copies = 0;
            m_Foveation = 2; // 0.66
            m_Cutout = 0;    // 关：合适的盒子大小得在设备上现调
        }

        static int Step(int index, int direction, int length) => Mathf.Clamp(index + direction, 0, length - 1);

        static int NearestInt(IReadOnlyList<int> steps, float value)
        {
            var best = 0;
            for (var i = 1; i < steps.Count; ++i)
            {
                if (Mathf.Abs(steps[i] - value) < Mathf.Abs(steps[best] - value))
                    best = i;
            }

            return best;
        }

        static int NearestFloat(IReadOnlyList<float> steps, float value)
        {
            var best = 0;
            for (var i = 1; i < steps.Count; ++i)
            {
                if (Mathf.Abs(steps[i] - value) < Mathf.Abs(steps[best] - value))
                    best = i;
            }

            return best;
        }

        /// <summary>
        /// 把当前档位写进渲染状态。每帧调用是安全的（都是幂等赋值），
        /// 但 renderer 副本数由 <see cref="BenchRig"/> 负责实例化，这里只声明期望值。
        /// </summary>
        public void Apply(UniversalRenderPipelineAsset urp, IReadOnlyList<GsplatRenderer> renderers,
            BenchConditions conditions)
        {
            if (urp != null && urp.msaaSampleCount != MsaaSamples)
                urp.msaaSampleCount = MsaaSamples;

            if (conditions != null)
            {
                if (!Mathf.Approximately(conditions.LockedViewportScale, ViewportScale))
                    conditions.SetViewportScale(ViewportScale);
                if (!Mathf.Approximately(conditions.LockedFoveationLevel, FoveationLevel))
                    conditions.SetFoveationLevel(FoveationLevel);
            }

            for (var i = 0; i < renderers.Count; ++i)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                // SH degree 受资产实际带的 SH 阶数上限约束，超了会取到不存在的材质变体。
                var maxDegree = renderer.GsplatAsset != null ? renderer.GsplatAsset.SHBands : (byte)0;
                renderer.SHDegree = Mathf.Min(ShDegree, maxDegree);
                renderer.SplatDownscaleFactor = Downscale;

                // SortRefreshRate 只在 SortEveryNFrames 模式下生效，间隔为 1 时退回 Always
                // 才是真正的「每帧排序」，否则等于绕一圈做同一件事。
                if (SortInterval <= 1)
                {
                    renderer.SortMode = GsplatRenderer.GsplatSortMode.Always;
                    renderer.SortRefreshRate = 1;
                }
                else
                {
                    renderer.SortMode = GsplatRenderer.GsplatSortMode.SortEveryNFrames;
                    renderer.SortRefreshRate = (uint)SortInterval;
                }
            }
        }

        public string NameOf(int index) => (Knob)index switch
        {
            Knob.Msaa => "MSAA",
            Knob.ShDegree => "SH degree",
            Knob.Downscale => "downscale",
            Knob.SortInterval => "sort 1/N",
            Knob.ViewportScale => "viewScale",
            Knob.RendererCopies => "copies",
            Knob.Foveation => "FFR",
            Knob.CutoutMeters => "cutout m",
            _ => "?"
        };

        public string ValueOf(int index) => (Knob)index switch
        {
            Knob.Msaa => MsaaSamples == 1 ? "off" : MsaaSamples + "x",
            Knob.ShDegree => ShDegree.ToString(),
            Knob.Downscale => Downscale.ToString("F2", CultureInfo.InvariantCulture),
            Knob.SortInterval => "1/" + SortInterval,
            Knob.ViewportScale => ViewportScale.ToString("F2", CultureInfo.InvariantCulture),
            Knob.RendererCopies => "x" + RendererCopies,
            Knob.Foveation => FoveationLevel.ToString("F2", CultureInfo.InvariantCulture),
            Knob.CutoutMeters => CutoutMeters <= 0f ? "off" : CutoutMeters.ToString("F0", CultureInfo.InvariantCulture),
            _ => "?"
        };

        public void AppendTo(StringBuilder builder)
        {
            builder.Append("== knobs ==\n");
            for (var i = 0; i < KnobCount; ++i)
            {
                builder.Append(i == Selected ? " >" : "  ")
                    .Append(NameOf(i).PadRight(11))
                    .Append(ValueOf(i))
                    .Append('\n');
            }
        }

        public const string CsvHeader =
            "msaa,sh_degree,downscale,sort_interval,viewport_scale,renderer_copies,ffr_knob,cutout_m";

        public string CsvRow() => string.Join(",",
            MsaaSamples.ToString(CultureInfo.InvariantCulture),
            ShDegree.ToString(CultureInfo.InvariantCulture),
            Downscale.ToString("F2", CultureInfo.InvariantCulture),
            SortInterval.ToString(CultureInfo.InvariantCulture),
            ViewportScale.ToString("F2", CultureInfo.InvariantCulture),
            RendererCopies.ToString(CultureInfo.InvariantCulture),
            FoveationLevel.ToString("F2", CultureInfo.InvariantCulture),
            CutoutMeters.ToString("F0", CultureInfo.InvariantCulture));
    }
}
