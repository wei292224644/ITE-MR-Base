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
        }

        static readonly int[] k_MsaaSteps = { 1, 2, 4, 8 };
        static readonly int[] k_ShSteps = { 0, 1, 2, 3 };
        static readonly float[] k_DownscaleSteps = { 0f, 0.1f, 0.2f, 0.25f, 0.3f, 0.4f, 0.5f };
        static readonly int[] k_SortSteps = { 1, 2, 5, 10, 30, 60 };
        static readonly float[] k_ViewportSteps = { 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f };
        static readonly int[] k_CopySteps = { 1, 2, 4, 8, 16 };

        // 各旋钮当前所处的档位下标。
        int m_Msaa = 2; // 4x
        int m_Sh = 3; // degree 3
        int m_Downscale;
        int m_Sort;
        int m_Viewport = 5; // 1.0
        int m_Copies;

        public const int KnobCount = 6;

        public int Selected { get; private set; }

        public int MsaaSamples => k_MsaaSteps[m_Msaa];
        public int ShDegree => k_ShSteps[m_Sh];
        public float Downscale => k_DownscaleSteps[m_Downscale];
        public int SortInterval => k_SortSteps[m_Sort];
        public float ViewportScale => k_ViewportSteps[m_Viewport];
        public int RendererCopies => k_CopySteps[m_Copies];

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
            }
        }

        public void ResetToBaseline()
        {
            m_Msaa = 2;
            m_Sh = 3;
            m_Downscale = 0;
            m_Sort = 0;
            m_Viewport = 5;
            m_Copies = 0;
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

            if (conditions != null && !Mathf.Approximately(conditions.LockedViewportScale, ViewportScale))
                conditions.SetViewportScale(ViewportScale);

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

        public const string CsvHeader = "msaa,sh_degree,downscale,sort_interval,viewport_scale,renderer_copies";

        public string CsvRow() => string.Join(",",
            MsaaSamples.ToString(CultureInfo.InvariantCulture),
            ShDegree.ToString(CultureInfo.InvariantCulture),
            Downscale.ToString("F2", CultureInfo.InvariantCulture),
            SortInterval.ToString(CultureInfo.InvariantCulture),
            ViewportScale.ToString("F2", CultureInfo.InvariantCulture),
            RendererCopies.ToString(CultureInfo.InvariantCulture));
    }
}
