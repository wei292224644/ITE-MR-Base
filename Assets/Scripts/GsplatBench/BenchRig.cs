using System.Collections;
using System.Collections.Generic;
using System.Text;
using Gsplat;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace MRBase.GsplatBench
{
    /// <summary>
    /// 3DGS 性能实测装置的唯一 MonoBehaviour：拥有旋钮、指标、条件锁定与 sweep，
    /// 负责输入、HUD 文本与 renderer 副本管理。
    ///
    /// 这套东西刻意与主工程解耦（独立 asmdef、独立场景、独立 URP 资产），
    /// 是可整体删除的一次性探针，不是产品代码。
    /// </summary>
    public sealed class BenchRig : MonoBehaviour
    {
        public const string HarnessVersion = "1.0.0";

        /// <summary>摇杆推到多少才算一次「拨动」，以及回中到多少才允许下一次。</summary>
        const float k_StickOn = 0.7f;
        const float k_StickOff = 0.3f;

        /// <summary>HUD 文本硬上限。正常报告约 1.2k 字符，留足余量后仍然有界。</summary>
        const int k_MaxReportChars = 4000;

        [Header("渲染配置")]
        [Tooltip("bench 专属 URP Asset。运行时切过去，绝不改动产品用的渲染配置。")]
        [SerializeField] UniversalRenderPipelineAsset benchPipeline;

        [Header("被测资产")]
        [Tooltip("要循环切换的 Gsplat 资产。用右手摇杆上下切。")]
        [SerializeField] GsplatAsset[] assets;

        [Tooltip("承载 GsplatRenderer 的模板对象。副本按其包围盒尺寸在 XZ 平面上排格子。")]
        [SerializeField] GsplatRenderer rendererTemplate;

        [Tooltip("大于 0 时把资产按包围盒缩放到这个米数再摆到相机前方。扫描件的包围盒常被离群 " +
                 "splat 撑大，自动缩放会把主体缩没 —— 所以默认关闭（0），需要时再开。")]
        [SerializeField] float fitToMeters;

        [Tooltip("fitToMeters 生效时，资产摆在相机前方多少米。")]
        [SerializeField] float fitDistanceMeters = 3f;

        [Header("HUD")]
        [SerializeField] TMP_Text hudText;

        [Tooltip("HUD 文本重建间隔。TMP 每帧 rebuild 在移动端是可测量的开销，必须限频。")]
        [SerializeField] float reportIntervalSeconds = 0.2f;

        [Tooltip("同一份报告写进 .log 文件的间隔；0 表示不写。不走 Console —— 见 BenchLog。")]
        [SerializeField] float logIntervalSeconds = 2f;

        [Header("测量条件（启动时锁定）")]
        [SerializeField] float lockedViewportScale = 1f;

        [Tooltip("注视点渲染等级，0=关。锁定后不参与 sweep —— 它是条件，不是旋钮。")]
        [Range(0f, 1f)]
        [SerializeField] float lockedFoveationLevel;

        [Header("采样时长")]
        [SerializeField] float warmupSeconds = 2f;
        [SerializeField] float sampleSeconds = 6f;

        readonly List<GsplatRenderer> m_Renderers = new();
        readonly StringBuilder m_Builder = new();

        BenchSweep m_Sweep;

        InputAction m_NextKnob;
        InputAction m_ResetKnobs;
        InputAction m_AdjustStick;
        InputAction m_ToggleHud;
        InputAction m_ToggleSweep;
        InputAction m_SingleVariableSweep;
        InputAction m_NextAsset;

        int m_AssetIndex;
        int m_StickLatchX;
        int m_StickLatchY;
        float m_NextReportTime;
        float m_NextLogTime;

        public BenchKnobs Knobs { get; } = new();
        public BenchMetrics Metrics { get; } = new();
        public BenchConditions Conditions { get; } = new();

        public float WarmupSeconds => warmupSeconds;
        public float SampleSeconds => sampleSeconds;
        public bool HudEnabled { get; private set; } = true;

        public int RendererCount => m_Renderers.Count;

        public uint SplatTotal
        {
            get
            {
                uint total = 0;
                foreach (var renderer in m_Renderers)
                {
                    if (renderer != null)
                        total += renderer.SplatCount;
                }

                return total;
            }
        }

        public GsplatAsset CurrentAsset =>
            assets != null && assets.Length > 0 ? assets[Mathf.Clamp(m_AssetIndex, 0, assets.Length - 1)] : null;

        public string AssetLabel => CurrentAsset != null ? CurrentAsset.name : "(none)";
        public uint AssetSplatCount => CurrentAsset != null ? CurrentAsset.SplatCount : 0;

        void Awake()
        {
            // 切到 bench 专属渲染配置。放在 Awake 是因为必须早于第一次渲染，
            // 而这样做就不用去动 QualitySettings.asset 里任何一个产品档位。
            if (benchPipeline != null)
                QualitySettings.renderPipeline = benchPipeline;

            m_Sweep = new BenchSweep(this);
            CreateInputActions();
        }

        void OnEnable()
        {
            m_NextKnob.Enable();
            m_ResetKnobs.Enable();
            m_AdjustStick.Enable();
            m_ToggleHud.Enable();
            m_ToggleSweep.Enable();
            m_SingleVariableSweep.Enable();
            m_NextAsset.Enable();
        }

        void OnDisable()
        {
            m_NextKnob.Disable();
            m_ResetKnobs.Disable();
            m_AdjustStick.Disable();
            m_ToggleHud.Disable();
            m_ToggleSweep.Disable();
            m_SingleVariableSweep.Disable();
            m_NextAsset.Disable();
        }

        void OnDestroy()
        {
            m_NextKnob?.Dispose();
            m_ResetKnobs?.Dispose();
            m_AdjustStick?.Dispose();
            m_ToggleHud?.Dispose();
            m_ToggleSweep?.Dispose();
            m_SingleVariableSweep?.Dispose();
            m_NextAsset?.Dispose();
        }

        IEnumerator Start()
        {
            SyncRendererCopies();
            ApplyAssetToRenderers();

            // XR display subsystem 要几帧才起得来，太早锁会锁到默认值。
            yield return null;
            yield return null;

            Conditions.Lock(lockedViewportScale, lockedFoveationLevel);
            ApplyKnobsNow();

            BenchLog.Milestone($"ready  asset={AssetLabel} splats={AssetSplatCount} " +
                               $"stereo={Conditions.StereoMode} xrActive={Conditions.XrActive} " +
                               $"refresh={Conditions.RefreshHz:F0}Hz");
            BenchLog.Milestone("log -> " + BenchLog.Path);
        }

        void Update()
        {
            Metrics.Sample();
            Conditions.Poll(Metrics.DisplayIntervalMedianMs);

            HandleInput();
            Report();
        }

        // —— 输入 ——

        void CreateInputActions()
        {
            // 代码内建 action，不依赖任何 .inputactions 资产 —— 装置要能被整体删除。
            m_NextKnob = new InputAction("bench/nextKnob", InputActionType.Button);
            m_NextKnob.AddBinding("<XRController>{RightHand}/primaryButton");
            m_NextKnob.AddBinding("<Keyboard>/tab");

            m_ResetKnobs = new InputAction("bench/reset", InputActionType.Button);
            m_ResetKnobs.AddBinding("<XRController>{RightHand}/secondaryButton");
            m_ResetKnobs.AddBinding("<Keyboard>/r");

            m_AdjustStick = new InputAction("bench/adjust", InputActionType.Value, expectedControlType: "Vector2");
            m_AdjustStick.AddBinding("<XRController>{RightHand}/thumbstick");
            m_AdjustStick.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            m_ToggleHud = new InputAction("bench/hud", InputActionType.Button);
            m_ToggleHud.AddBinding("<XRController>{LeftHand}/primaryButton");
            m_ToggleHud.AddBinding("<Keyboard>/h");

            m_ToggleSweep = new InputAction("bench/sweep", InputActionType.Button);
            m_ToggleSweep.AddBinding("<XRController>{LeftHand}/secondaryButton");
            m_ToggleSweep.AddBinding("<Keyboard>/y");

            m_SingleVariableSweep = new InputAction("bench/sweepSingle", InputActionType.Button);
            m_SingleVariableSweep.AddBinding("<XRController>{LeftHand}/thumbstickClicked");
            m_SingleVariableSweep.AddBinding("<Keyboard>/u");

            // 切资产除了右摇杆上下，再给一个独立按钮 —— 摇杆在戴着头显调参时容易误触到别的档。
            m_NextAsset = new InputAction("bench/nextAsset", InputActionType.Button);
            m_NextAsset.AddBinding("<XRController>{RightHand}/thumbstickClicked");
            m_NextAsset.AddBinding("<Keyboard>/n");
        }

        void HandleInput()
        {
            if (m_NextKnob.WasPressedThisFrame())
                Knobs.SelectNext();

            if (m_ResetKnobs.WasPressedThisFrame())
            {
                Knobs.ResetToBaseline();
                ApplyKnobsNow();
            }

            if (m_ToggleHud.WasPressedThisFrame())
                SetHudEnabled(!HudEnabled);

            if (m_ToggleSweep.WasPressedThisFrame())
                ToggleSweep(BenchSweep.Mode.Cumulative);

            if (m_SingleVariableSweep.WasPressedThisFrame())
                ToggleSweep(BenchSweep.Mode.SingleVariable);

            if (m_NextAsset.WasPressedThisFrame())
                NextAsset();

            HandleStick();
        }

        void HandleStick()
        {
            // sweep 期间锁住手动调节，否则改的是正在被采样的那一档。
            if (m_Sweep.Running)
                return;

            var stick = m_AdjustStick.ReadValue<Vector2>();

            var x = Latch(stick.x, ref m_StickLatchX);
            if (x != 0)
            {
                Knobs.AdjustSelected(x);
                ApplyKnobsNow();
            }

            var y = Latch(stick.y, ref m_StickLatchY);
            if (y != 0)
                CycleAsset(y);
        }

        /// <summary>摇杆推到阈值算一次事件，回中之前不再触发 —— 否则一推就连跳十几档。</summary>
        static int Latch(float value, ref int latch)
        {
            if (latch != 0)
            {
                if (Mathf.Abs(value) < k_StickOff)
                    latch = 0;
                return 0;
            }

            if (value > k_StickOn)
            {
                latch = 1;
                return 1;
            }

            if (value < -k_StickOn)
            {
                latch = -1;
                return -1;
            }

            return 0;
        }

        // —— 状态变更 ——

        void ToggleSweep(BenchSweep.Mode mode)
        {
            if (m_Sweep.Running)
            {
                m_Sweep.RequestStop();
                return;
            }

            StartCoroutine(m_Sweep.Run(mode));
        }

        void SetHudEnabled(bool enabled)
        {
            HudEnabled = enabled;
            if (hudText != null)
                hudText.enabled = enabled;
        }

        /// <summary>供 HUD 上的 UI 按钮直接绑定（headset 里没有射线交互器，按钮只在编辑器 Game 视图可点）。</summary>
        public void NextAsset() => CycleAsset(1);

        public void PreviousAsset() => CycleAsset(-1);

        void CycleAsset(int direction)
        {
            if (assets == null || assets.Length == 0)
                return;

            m_AssetIndex = (m_AssetIndex + direction + assets.Length) % assets.Length;
            ApplyAssetToRenderers();
            Metrics.ResetWindow();
        }

        /// <summary>把当前旋钮档位写进渲染状态，并同步 renderer 副本数量。</summary>
        public void ApplyKnobsNow()
        {
            SyncRendererCopies();
            Knobs.Apply(UniversalRenderPipeline.asset, m_Renderers, Conditions);
        }

        void SyncRendererCopies()
        {
            if (rendererTemplate == null)
                return;

            if (m_Renderers.Count == 0)
                m_Renderers.Add(rendererTemplate);

            var wanted = Knobs.RendererCopies;

            while (m_Renderers.Count > wanted)
            {
                var last = m_Renderers[^1];
                m_Renderers.RemoveAt(m_Renderers.Count - 1);
                if (last != null && last != rendererTemplate)
                    Destroy(last.gameObject);
            }

            while (m_Renderers.Count < wanted)
            {
                var clone = Instantiate(rendererTemplate, rendererTemplate.transform.parent);
                clone.name = $"{rendererTemplate.name}_copy{m_Renderers.Count}";
                clone.GsplatAsset = CurrentAsset;
                m_Renderers.Add(clone);
            }

            LayoutRenderers();
        }

        /// <summary>
        /// 副本在 XZ 平面上排方阵，间距取包围盒尺寸 —— 副本堆在原地会变成纯 overdraw 放大器，
        /// 那测的是「同一块屏幕叠 N 层」，不是「场景里有 N 倍 splat」。
        /// </summary>
        void LayoutRenderers()
        {
            if (m_Renderers.Count <= 1)
                return;

            var asset = CurrentAsset;
            var local = asset != null ? asset.Bounds.size : Vector3.one;
            var scale = rendererTemplate.transform.lossyScale;
            var size = new Vector3(local.x * scale.x, local.y * scale.y, local.z * scale.z);

            // 扫描件的包围盒常被离群 splat 撑到成千上万米，直接拿来当间距会把副本
            // 甩到视野外，那测的就不是「更多 splat 在眼前」了。夹到房间尺度。
            var spacing = Mathf.Clamp(Mathf.Max(size.x, size.z), 0.5f, 30f);
            var side = Mathf.CeilToInt(Mathf.Sqrt(m_Renderers.Count));
            var origin = rendererTemplate.transform.position;

            for (var i = 0; i < m_Renderers.Count; ++i)
            {
                var renderer = m_Renderers[i];
                if (renderer == null || renderer == rendererTemplate)
                    continue;

                var col = i % side;
                var row = i / side;
                renderer.transform.position = origin + new Vector3(
                    (col - (side - 1) * 0.5f) * spacing,
                    0f,
                    (row - (side - 1) * 0.5f) * spacing);
            }
        }

        void ApplyAssetToRenderers()
        {
            foreach (var renderer in m_Renderers)
            {
                if (renderer != null)
                    renderer.GsplatAsset = CurrentAsset;
            }

            FitTemplate();
            LayoutRenderers();

            var asset = CurrentAsset;
            if (asset != null)
            {
                BenchLog.Write($"asset {asset.name} splats={asset.SplatCount} SH={asset.SHBands} " +
                               $"bounds={asset.Bounds.size} center={asset.Bounds.center}");
            }
        }

        void FitTemplate()
        {
            if (fitToMeters <= 0f || rendererTemplate == null || CurrentAsset == null)
                return;

            var size = CurrentAsset.Bounds.size;
            var largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (largest <= Mathf.Epsilon)
                return;

            var scale = fitToMeters / largest;
            rendererTemplate.transform.localScale = Vector3.one * scale;

            var camera = Camera.main;
            var origin = camera != null
                ? camera.transform.position + camera.transform.forward * fitDistanceMeters
                : Vector3.forward * fitDistanceMeters;
            rendererTemplate.transform.position = origin - CurrentAsset.Bounds.center * scale;
        }

        // —— HUD ——

        void Report()
        {
            if (Time.unscaledTime < m_NextReportTime)
                return;

            m_NextReportTime = Time.unscaledTime + Mathf.Max(0.2f, reportIntervalSeconds);

            // HUD 关掉时连字符串都不要建 —— 关 HUD 的目的就是让它彻底不参与开销。
            if (!HudEnabled && logIntervalSeconds <= 0f)
                return;

            var report = BuildReport();

            if (HudEnabled && hudText != null)
                hudText.text = report;

            if (logIntervalSeconds > 0f && Time.unscaledTime >= m_NextLogTime)
            {
                m_NextLogTime = Time.unscaledTime + Mathf.Max(2f, logIntervalSeconds);
                BenchLog.Write("snapshot\n" + report);
            }
        }

        string BuildReport()
        {
            m_Builder.Clear();
            m_Builder.Append("GSPLAT BENCH ").Append(HarnessVersion).Append('\n');

            Metrics.AppendTo(m_Builder, Conditions.BudgetMs);

            m_Builder.Append("== scene ==\n");
            m_Builder.Append("  asset   ").Append(AssetLabel)
                .Append("  (").Append(AssetSplatCount).Append(" splats, SH")
                .Append(CurrentAsset != null ? CurrentAsset.SHBands : 0).Append(")\n");
            m_Builder.Append("  live    ").Append(SplatTotal).Append(" splats in ")
                .Append(RendererCount).Append(" renderer(s)\n");
            m_Builder.Append("  mem     sys ").Append(SystemInfo.systemMemorySize).Append("MB")
                .Append("  gfx ").Append(SystemInfo.graphicsMemorySize).Append("MB\n");

            Knobs.AppendTo(m_Builder);
            Conditions.AppendTo(m_Builder);
            m_Sweep.AppendTo(m_Builder);

            // 硬上限。测量装置把被测对象拖垮是这类工具最典型的翻车方式，
            // 已经踩过一次（漂移原因逐帧拼接把 HUD 文本涨到失控）。
            if (m_Builder.Length > k_MaxReportChars)
            {
                m_Builder.Length = k_MaxReportChars;
                m_Builder.Append("\n...[truncated]");
            }

            return m_Builder.ToString();
        }
    }
}
