using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR;

namespace MRBase.GsplatBench
{
    /// <summary>
    /// 锁定并持续监视那些会在你背后改变 GPU 工作量的系统级变量。
    ///
    /// 存在的理由：只要刷新率、渲染视口缩放或注视点等级在采样期间漂移，
    /// 「档 N 比档 N-1 快了 2ms」就无法归因，整轮数据作废。所以这些量必须
    /// 一次锁定、每帧回读**实际值**（而不是回读我们自己设进去的值），
    /// 一旦不一致就把当前档标为 invalid。
    /// </summary>
    public sealed class BenchConditions
    {
        /// <summary>只有这个比例附近才认定为「显示帧率被折半」（ASW / 重投影的症状）。</summary>
        const float k_HalfRateRatioLow = 1.7f;
        const float k_HalfRateRatioHigh = 2.3f;

        /// <summary>漂移项的固定槽位。用定长数组而不是可变集合，让 DriftReason 长度由构造保证有界。</summary>
        enum Slot
        {
            XrInactive = 0,
            Refresh,
            Viewport,
            Foveation,
            DynamicRes,
            HalfRate,
        }

        const int k_SlotCount = 6;

        readonly List<XRDisplaySubsystem> m_Displays = new();
        readonly string[] m_StickySlots = new string[k_SlotCount];
        readonly StringBuilder m_ReasonBuilder = new();

        // —— 锁定值 ——
        public float LockedRefreshHz { get; private set; }
        public float LockedViewportScale { get; private set; }
        public float LockedFoveationLevel { get; private set; }

        // —— 实时回读值 ——
        public float RefreshHz { get; private set; }
        public float ViewportScale { get; private set; }
        public float FoveationLevel { get; private set; }
        public float EyeResolutionScale { get; private set; }
        public float DynamicResScale { get; private set; }
        public string StereoMode { get; private set; } = "unknown";
        public bool XrActive { get; private set; }

        /// <summary>显示帧间隔中位数除以刷新率周期。≈2 说明合成器在折半，多半是 ASW / 重投影。</summary>
        public float DisplayIntervalRatio { get; private set; }

        public bool Drifted { get; private set; }
        public string DriftReason { get; private set; } = string.Empty;

        public float BudgetMs => RefreshHz > 0f ? 1000f / RefreshHz : 0f;

        public bool HalfRateSuspected =>
            DisplayIntervalRatio >= k_HalfRateRatioLow && DisplayIntervalRatio <= k_HalfRateRatioHigh;

        XRDisplaySubsystem Display
        {
            get
            {
                SubsystemManager.GetSubsystems(m_Displays);
                foreach (var display in m_Displays)
                {
                    if (display.running)
                        return display;
                }

                return null;
            }
        }

        /// <summary>
        /// 锁定测量条件。注意立体渲染模式（SPI / Multi-pass）**无法在运行时切换** ——
        /// 它是 OpenXR 的构建期设置，这里只记录，不设置。要对比就得出两个包。
        /// </summary>
        public void Lock(float viewportScale, float foveationLevel)
        {
            LockedViewportScale = viewportScale;
            LockedFoveationLevel = foveationLevel;

            XRSettings.renderViewportScale = viewportScale;
            TrySetFoveation(foveationLevel);

            Poll();
            LockedRefreshHz = RefreshHz;
            ClearDrift();
        }

        public void SetViewportScale(float scale)
        {
            LockedViewportScale = scale;
            XRSettings.renderViewportScale = scale;
        }

        /// <summary>
        /// 注视点渲染从「锁定条件」改成了可调旋钮（推翻 design D2 的原判）。
        ///
        /// 原判理由是测量纯净度：FFR 浮动会让档间不可比。但真机数据出来后，88 万 splats
        /// 距 72fps 差 3.6 倍，FFR 那 15~30% 已经不是噪声而是必须动用的杠杆。
        /// 折中：它仍然逐帧回读、逐档写进 CSV，所以任何一行数据都能看出当时开到几级 ——
        /// 可比性由**记录**保证，而不是由**冻结**保证。
        /// </summary>
        public void SetFoveationLevel(float level)
        {
            LockedFoveationLevel = level;
            TrySetFoveation(level);
        }

        public void ClearDrift()
        {
            Drifted = false;
            DriftReason = string.Empty;
            for (var i = 0; i < k_SlotCount; ++i)
                m_StickySlots[i] = null;
        }

        /// <param name="displayIntervalMedianMs">显示帧间隔中位数，由 <see cref="BenchMetrics"/> 提供。</param>
        public void Poll(double displayIntervalMedianMs = 0)
        {
            var display = Display;
            XrActive = display != null;

            if (display != null && display.TryGetDisplayRefreshRate(out var hz) && hz > 0f)
                RefreshHz = hz;
            else
                RefreshHz = (float)Screen.currentResolution.refreshRateRatio.value;

            ViewportScale = XRSettings.renderViewportScale;
            EyeResolutionScale = XRSettings.eyeTextureResolutionScale;
            DynamicResScale = ScalableBufferManager.widthScaleFactor;
            StereoMode = XRSettings.enabled ? XRSettings.stereoRenderingMode.ToString() : "disabled";
            FoveationLevel = TryGetFoveation(display);

            DisplayIntervalRatio = RefreshHz > 0f && displayIntervalMedianMs > 0
                ? (float)(displayIntervalMedianMs / (1000.0 / RefreshHz))
                : 0f;

            DetectDrift();
        }

        void DetectDrift()
        {
            // 只在已经锁定过之后才判漂移。
            if (LockedRefreshHz <= 0f)
                return;

            // XR 没起来（编辑器平面模式）时，下面这些量全都读不到真值 ——
            // `XRSettings.renderViewportScale` 回读 0、注视点回读 0、刷新率退化成
            // 显示器刷新率。这种情况下逐项判漂移只会得到一串假警报，而且这一轮本来
            // 就不是有效的头显测量，所以整体标一条 xr-inactive 让人和 CSV 都看得见。
            if (!XrActive)
            {
                for (var i = 1; i < k_SlotCount; ++i)
                    m_StickySlots[i] = null;

                MarkDrift(Slot.XrInactive, true, "xr-inactive (flat mode; not a valid headset run)");
                RebuildDriftReason();
                return;
            }

            m_StickySlots[(int)Slot.XrInactive] = null;

            // 每帧覆写各自的槽位，不做字符串累加 —— 累加过的版本会让 DriftReason
            // 随帧数无限增长，HUD 文本涨到几 MB，TMP 每次重排就把帧率吃光。
            MarkDrift(Slot.Refresh,
                RefreshHz > 0f && !Mathf.Approximately(Mathf.Round(RefreshHz), Mathf.Round(LockedRefreshHz)),
                $"refresh {LockedRefreshHz:F0}→{RefreshHz:F0}Hz");

            MarkDrift(Slot.Viewport,
                Mathf.Abs(ViewportScale - LockedViewportScale) > 0.001f,
                $"viewportScale {LockedViewportScale:F2}→{ViewportScale:F2}");

            // 注视点等级由 provider 决定是否真的采纳，回读值和锁定值不一致本身就是要记录的事实。
            MarkDrift(Slot.Foveation,
                Mathf.Abs(FoveationLevel - LockedFoveationLevel) > 0.05f,
                $"foveation {LockedFoveationLevel:F2}→{FoveationLevel:F2}");

            // 动态分辨率应当跟随我们自己设的 viewport scale，而不是恒为 1 ——
            // `XRSettings.renderViewportScale` 本来就会带动 ScalableBufferManager。
            // 早前拿它跟 1 比，于是只要 viewScale 旋钮不是 1.0 就误报漂移，
            // sweep 的最后一档（+viewscale-0.7）会被无条件标成 invalid。
            MarkDrift(Slot.DynamicRes,
                DynamicResScale > 0f && Mathf.Abs(DynamicResScale - LockedViewportScale) > 0.02f,
                $"dynamicRes {DynamicResScale:F2} != viewScale {LockedViewportScale:F2}");

            MarkDrift(Slot.HalfRate,
                HalfRateSuspected,
                $"half-rate x{DisplayIntervalRatio:F2} (ASW/reprojection?)");

            RebuildDriftReason();
        }

        /// <param name="tripped">本帧该项是否越界。为 false 时清空槽位。</param>
        void MarkDrift(Slot slot, bool tripped, string reason)
        {
            if (!tripped)
                return;

            // 采样窗口内只要出现过一次就算脏，直到 ClearDrift 为止 —— 这是 sweep 判
            // 有效性要的语义。同一项重复越界只覆写自己的槽位，不追加。
            m_StickySlots[(int)slot] = reason;
        }

        void RebuildDriftReason()
        {
            m_ReasonBuilder.Clear();
            Drifted = false;

            for (var i = 0; i < k_SlotCount; ++i)
            {
                var reason = m_StickySlots[i];
                if (string.IsNullOrEmpty(reason))
                    continue;

                Drifted = true;
                if (m_ReasonBuilder.Length > 0)
                    m_ReasonBuilder.Append("; ");
                m_ReasonBuilder.Append(reason);
            }

            DriftReason = Drifted ? m_ReasonBuilder.ToString() : string.Empty;
        }

        static float TryGetFoveation(XRDisplaySubsystem display)
        {
            if (display == null)
                return 0f;

            // provider 未实现时会抛，捕获后按 0 报告 —— 这本身也是要显示给人看的事实。
            try
            {
                return display.foveatedRenderingLevel;
            }
            catch
            {
                return 0f;
            }
        }

        void TrySetFoveation(float level)
        {
            var display = Display;
            if (display == null)
                return;

            try
            {
                display.foveatedRenderingLevel = level;
            }
            catch
            {
                // 忽略：回读时会显示真实值，人能看见没生效。
            }
        }

        public void AppendTo(StringBuilder builder)
        {
            builder.Append("== locked conditions ==\n");
            builder.Append("  refresh   ").Append(RefreshHz.ToString("F0")).Append("Hz")
                .Append("  budget ").Append(BudgetMs.ToString("F1")).Append("ms")
                .Append("   locked ").Append(LockedRefreshHz.ToString("F0")).Append('\n');
            builder.Append("  stereo    ").Append(StereoMode)
                .Append("   (build-time, not switchable)\n");
            builder.Append("  viewScale ").Append(ViewportScale.ToString("F2"))
                .Append("  eyeRes ").Append(EyeResolutionScale.ToString("F2"))
                .Append("  dynRes ").Append(DynamicResScale.ToString("F2")).Append('\n');
            builder.Append("  foveation ").Append(FoveationLevel.ToString("F2"))
                .Append("   locked ").Append(LockedFoveationLevel.ToString("F2")).Append('\n');
            builder.Append("  frameIntervalRatio ").Append(DisplayIntervalRatio.ToString("F2"));
            if (HalfRateSuspected)
                builder.Append("  <-- HALF RATE");
            builder.Append('\n');

            if (Drifted)
                builder.Append("  !! DRIFT: ").Append(DriftReason).Append('\n');
        }
    }
}
