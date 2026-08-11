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

        /// <summary>左摇杆推满时绕世界 Y 轴的转速（度/秒）。推杆量线性映射，便于微调角度。</summary>
        const float k_RotateDegPerSec = 90f;

        /// <summary>旋转输入死区。低于此值视为回中 —— 否则摇杆的静态偏置会让模型一直缓慢漂移。</summary>
        const float k_RotateDeadzone = 0.15f;

        [Header("渲染配置")]
        [Tooltip("bench 专属 URP Asset。运行时切过去，绝不改动产品用的渲染配置。")]
        [SerializeField] UniversalRenderPipelineAsset benchPipeline;

        [Header("被测资产")]
        [Tooltip("要循环切换的 Gsplat 资产。用右手摇杆上下切。")]
        [SerializeField] GsplatAsset[] assets;

        [Tooltip("承载 GsplatRenderer 的模板对象。副本按其包围盒尺寸在 XZ 平面上排格子。")]
        [SerializeField] GsplatRenderer rendererTemplate;

        [Tooltip("裁掉离群 splat 的立方体。由 cutout 旋钮控制边长，关闭时组件被禁用。" +
                 "挂在 rendererTemplate 之下，Target=Parent。")]
        [SerializeField] GsplatCutout outlierCutout;

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
        InputAction m_RotateStick;

        int m_AssetIndex;
        int m_StickLatchX;
        int m_StickLatchY;
        float m_NextReportTime;
        float m_NextLogTime;
        bool m_Rotating;

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
            m_RotateStick.Enable();
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
            m_RotateStick.Disable();
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
            m_RotateStick?.Dispose();
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
            ApplyRotation();
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

            // 左摇杆横轴转模型。右摇杆已经被旋钮和切资产占满，左摇杆此前只用了「按下」。
            m_RotateStick = new InputAction("bench/rotate", InputActionType.Value, expectedControlType: "Axis");
            m_RotateStick.AddBinding("<XRController>{LeftHand}/thumbstick/x");
            m_RotateStick.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/q")
                .With("Positive", "<Keyboard>/e");
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

        /// <summary>
        /// 左摇杆横轴绕**世界** Y 轴转模型，转速与推杆量成正比。
        ///
        /// 绕各 renderer 自身原点自转（<c>Space.World</c> 只约束轴向，不改位置），
        /// 所以副本方阵的格子布局不会被搅乱 —— <see cref="LayoutRenderers"/> 只写位置。
        ///
        /// 转动期间强制重排序。包里的 <c>RefreshOnCameraMove</c> 只盯**相机**变换，
        /// 模型自己转不会触发重排 —— 于是在 sort 1/30 下转模型会看到深度序每 30 帧
        /// 跳一次。相机转和模型转对深度序的影响完全等价，只补一边是包的触发条件漏了一项，
        /// 不是一个值得保留下来测量的行为。代价是转动期间 sort 1/N 失效，
        /// 所以 HUD 会标 rotating，避免有人拿转动中的数字去和静止档比较。
        /// </summary>
        void ApplyRotation()
        {
            // 与手动调旋钮同一条纪律：sweep 期间不许动，否则改的是正在被采样的那一档。
            if (m_Sweep.Running)
            {
                m_Rotating = false;
                return;
            }

            var axis = m_RotateStick.ReadValue<float>();
            m_Rotating = Mathf.Abs(axis) >= k_RotateDeadzone;
            if (!m_Rotating)
                return;

            var delta = axis * k_RotateDegPerSec * Time.unscaledDeltaTime;

            foreach (var renderer in m_Renderers)
            {
                if (renderer == null)
                    continue;

                renderer.transform.Rotate(Vector3.up, delta, Space.World);
                renderer.ForceRefresh();
            }
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
            ApplyCutout();
        }

        /// <summary>
        /// 裁剪盒按世界尺寸设定，所以要先除掉 renderer 的缩放 —— 否则一个被缩到 0.01 的
        /// 扫描件上，"50 米的盒子" 实际只有半米。
        ///
        /// 关掉时禁用组件而不是把盒子放到无穷大：裁剪本身要跑一遍 compute prepass，
        /// 「不裁」必须是真的不跑，否则测出来的 off 档带着裁剪的固定开销。
        /// </summary>
        void ApplyCutout()
        {
            if (outlierCutout == null)
                return;

            var meters = Knobs.CutoutMeters;
            var enable = meters > 0f;
            if (outlierCutout.enabled != enable)
                outlierCutout.enabled = enable;

            if (!enable)
                return;

            var parentScale = outlierCutout.transform.parent != null
                ? outlierCutout.transform.parent.lossyScale
                : Vector3.one;

            outlierCutout.transform.localScale = new Vector3(
                meters / Mathf.Max(1e-6f, parentScale.x),
                meters / Mathf.Max(1e-6f, parentScale.y),
                meters / Mathf.Max(1e-6f, parentScale.z));

            // 以**相机**为中心，不是以资产包围盒中心 —— 包围盒本身就是被离群点撑歪的，
            // 拿它当锚点可能把主体裁掉。语义定为「只保留身边 N 米内的 splat」，
            // 这样无论内容在哪，看着的东西都还在。
            //
            // 只在旋钮变动时重定位，不每帧跟随：cutout 一动就要重跑 compute prepass，
            // 每帧跟随等于把装置自己的开销加进被测数字。
            var camera = Camera.main;
            if (camera != null)
                outlierCutout.transform.position = camera.transform.position;
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

        /// <summary>
        /// 输入自诊断。没有这一段，「旋钮没变」有两种完全不同的原因分不开：
        /// 人没按到，还是 binding 在这台设备上根本没解析出控件。
        /// 直接把「认到哪些设备」和「每个 action 当前绑到哪个控件」摆出来。
        /// </summary>
        void AppendInput(StringBuilder builder)
        {
            builder.Append("== input ==\n  devices ");
            var any = false;
            foreach (var device in InputSystem.devices)
            {
                if (device.layout.IndexOf("XR", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    device.layout.IndexOf("Controller", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                builder.Append(device.layout).Append(' ');
                any = true;
            }

            if (!any)
                builder.Append("(no XR controller layouts!)");
            builder.Append('\n');

            AppendAction(builder, "knob+", m_NextKnob);
            AppendAction(builder, "adjust", m_AdjustStick);
            AppendAction(builder, "rotate", m_RotateStick);
            AppendAction(builder, "sweep", m_ToggleSweep);
        }

        static void AppendAction(StringBuilder builder, string label, InputAction action)
        {
            var control = action.activeControl;
            builder.Append("  ").Append(label.PadRight(7))
                .Append(control != null ? control.path : "(idle)")
                .Append("  ctrls=").Append(action.controls.Count)
                .Append('\n');
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
            // 角度要报出来：调完参数换个角度再看，得能回到同一个角度才谈得上对比。
            m_Builder.Append("  yaw     ")
                .Append(rendererTemplate != null
                    ? rendererTemplate.transform.eulerAngles.y.ToString("F0")
                    : "n/a")
                .Append("deg  (L-stick X)");
            if (m_Rotating)
                m_Builder.Append("   <-- ROTATING (forced re-sort, sort 1/N bypassed)");
            m_Builder.Append('\n');
            // 报**用量**不报设备总量。GetAllocatedMemoryForGraphicsDriver 在 release 包里
            // 没有 profiler 计数器、恒返回 0，所以为 0 时直接标 n/a，不摆一个假零出来。
            var gfx = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576;
            m_Builder.Append("  mem     gfxDriver ")
                .Append(gfx > 0 ? gfx + "MB" : "n/a (release)")
                .Append("  reserved ")
                .Append(UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576).Append("MB\n");

            Knobs.AppendTo(m_Builder);
            AppendInput(m_Builder);
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
