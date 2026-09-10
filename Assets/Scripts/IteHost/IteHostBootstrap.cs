using System;
using System.Threading.Tasks;
using UnityEngine;
using Uality.IteTour.Config;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace MRBase.Ite.Host
{
    /// <summary>
    /// ITE 导览在本工程里的装配点：把场景里的东西交给包，把包推出来的事件接回来。
    ///
    /// 这是**唯一**知道 <c>Uality.IteTour</c> 与 <c>MRBase.*</c> 两边的类型。
    /// 依赖方向单向：包对宿主零知识（design D2）。
    ///
    /// <see cref="MarkerTrackingSession"/> 只能由外部注入，装配点不构造会话、
    /// 不持有任何 <c>IMarkerObservationSource</c> 字段（design D3）。
    /// </summary>
    public class IteHostBootstrap : MonoBehaviour
    {
        [Header("包配置")]
        [SerializeField] private IteRuntimeConfig config;

        [Header("场景装配")]
        [SerializeField]
        [Tooltip("锚定目标，扫码后被移动到标记位姿")]
        private Transform anchorRoot;

        [SerializeField]
        [Tooltip("Tour 实例的父节点")]
        private Transform tourRoot;

        [SerializeField]
        [Tooltip("用于识别谁进出触发体积。留空则运行时解析 MRContext 的 XR 相机——" +
                 "内容场景是加性加载的，引用不到 MRCore 里的相机")]
        private Transform xrCamera;

        [Header("标记桥接")]
        [SerializeField]
        [Tooltip("防抖参数，两端各一套。留空则用出厂参数，扫码仍可工作")]
        private MarkerStabilizerProfile stabilizerProfile;

        [SerializeField]
        [Tooltip("标记局部坐标系到内容锚点的固定偏移，两端各一套。留空按 identity 处理")]
        private PlatformOffsetConfig platformOffsets;

        [SerializeField]
        [Tooltip("OnTourActivated 之后等待 OnTourSceneLoaded 的秒数。Enable() 是 fire-and-forget，超时才看得见失败。")]
        private float tourSceneLoadedTimeoutSeconds = 180f;

        [Header("行为")]
        [SerializeField]
        [Tooltip("强制离线调试：跳过全部下载与版本查询，直接读本地缓存。" +
                 "不勾则按运行时网络可达性判定")]
        private bool forceOffline;

        [SerializeField]
        [Tooltip("勾选则 Start 里自跑加载链（编辑器验收场景）。真机由设备 rig 在就绪后触发")]
        private bool startOnAwake = true;

        private IteRuntime _ite;
        private HeadsetPresenceAdapter _headsetPresence;
        private PicoHeadsetPresence _picoPresence;
        private MarkerTrackingSession _pendingSession;
        private IteMarkerBridge _bridge;
        private bool _eventsHooked;

        private string _awaitingSceneTourId;
        private float _awaitingSceneSince;

        /// <summary>装配好的运行时。启动失败时为 null。</summary>
        public IteRuntime Runtime => _ite;

        /// <summary>已接上的标记桥。未注入会话时为 null。</summary>
        public IteMarkerBridge MarkerBridge => _bridge;

        /// <summary>
        /// 注入标记会话。可在运行时就绪前或后调用：未就绪则暂存，就绪后补接。
        /// </summary>
        public void AttachMarkerSession(MarkerTrackingSession session)
        {
            _pendingSession = session ?? throw new ArgumentNullException(nameof(session));
            TryBindMarkerSession();
        }

        /// <summary>
        /// 创建运行时、转发事件、接上已注入的会话。不跑加载链——那是 <c>Start</c> 里的
        /// <see cref="IteRuntime.StartAsync"/>。EditMode 测试走这条，避免一装配就下载。
        /// </summary>
        public bool TryCreateRuntime()
        {
            if (_ite != null)
            {
                TryBindMarkerSession();
                return true;
            }

            var camera = ResolveCamera();
            if (camera == null)
            {
                return false;
            }

            _ite = IteRuntime.Create(new IteBootstrap
            {
                Config = config,
                AnchorRoot = anchorRoot,
                TourRoot = tourRoot,
                Camera = camera,
                IsNetworkAvailable = IsNetworkAvailable,
            });

            if (_ite == null)
            {
                return false;
            }

            HookRuntimeEvents();

            // PICO 有专门的佩戴状态通道，读到就用它；读不到（含 Quest）退回通用输入设备。
            // 这样调用点不需要平台分支，也不必赌某一端恰好上报（design D19）。
            _picoPresence = new PicoHeadsetPresence();
            _headsetPresence = new HeadsetPresenceAdapter(
                _ite.SetHeadsetMounted,
                () => _picoPresence.Read() ?? HeadsetPresenceAdapter.ReadUserPresence());
            TryBindMarkerSession();

            if (_bridge == null)
            {
                Debug.Log("[ITE Host] 未接入标记源，扫码激活不可用");
            }

            return true;
        }

        /// <summary>
        /// 相机按「序列化覆盖 → 运行时解析 → 报错」三段取得（design D2）。
        ///
        /// 不能只靠序列化引用：内容场景以加性方式加载在 MRCore 之上，
        /// 而 Unity 不支持跨场景序列化引用，XR 相机在设备场景里根本指不上。
        /// 也不重试等待——重试会把「还在等」和「配错了」混成同一个现象。
        /// </summary>
        private Transform ResolveCamera()
        {
            if (xrCamera != null)
            {
                return xrCamera;
            }

            var context = MRContext.Instance;
            if (context != null && context.Camera != null)
            {
                return context.Camera.transform;
            }

            Debug.LogError(
                "[ITE Host] 取不到相机：序列化字段为空，且 MRContext 尚无 XR 相机。" +
                "编辑器场景请在字段里指定桌面相机；真机场景请等 XR 就绪后再启动装配点。", this);
            return null;
        }

        /// <summary>
        /// 联网与否按**运行时**可达性判定（design D13）。
        /// 序列化开关等于「打包时决定现场有没有网」，那会让离线自愈路径在现场断网时根本走不进去。
        /// </summary>
        private bool IsNetworkAvailable()
        {
            if (forceOffline)
            {
                return false;
            }

            return _networkProbe != null
                ? _networkProbe()
                : Application.internetReachability != NetworkReachability.NotReachable;
        }

        /// <summary>替换可达性判定，供测试与特殊部署使用。</summary>
        public void SetNetworkProbe(Func<bool> probe) => _networkProbe = probe;

        private Func<bool> _networkProbe;

        /// <summary>
        /// 由外部触发加载链（design D15）。真机上设备 rig 在相机可解析、观测源可打开之后调它。
        /// 重复调用无害：<see cref="IteRuntime.StartAsync"/> 自己只跑一次。
        /// </summary>
        public async Task StartRuntimeAsync()
        {
            if (!TryCreateRuntime())
            {
                RaiseFailure("装配不完整，导览未启动。详见上一条错误日志。");
                enabled = false;
                return;
            }

            try
            {
                await _ite.StartAsync();
            }
            catch (Exception e)
            {
                // 不接住的话这里是 async void 调用链的尽头，异常变成一条没人看的
                // UnobservedTaskException，头显里只是永远停在加载中。
                Debug.LogError("[ITE Host] 加载链失败：" + e, this);
                RaiseFailure("内容加载失败：" + e.Message);
            }
        }

        /// <summary>
        /// 面向人的失败信号。日志在头显里看不见，失败必须有第二条出口（design D12）。
        /// </summary>
        public event Action<string> Failed;

        private void RaiseFailure(string message) => Failed?.Invoke(message);

        private async void Start()
        {
            if (!startOnAwake)
            {
                return;
            }

            await StartRuntimeAsync();
        }

        private void Update()
        {
            _headsetPresence?.Poll();

            // 会话唯一的推进点：输入层只负责建源与注入，不自己 Tick，
            // 否则一帧推两次，滞回与稳定窗口都会走快一倍。
            _bridge?.Tick(Time.deltaTime);

            if (_awaitingSceneTourId == null || tourSceneLoadedTimeoutSeconds <= 0f)
            {
                return;
            }

            if (Time.unscaledTime - _awaitingSceneSince <= tourSceneLoadedTimeoutSeconds)
            {
                return;
            }

            Debug.LogError(
                "[ITE Host] OnTourSceneLoaded 超时：" + _awaitingSceneTourId +
                " 超过 " + tourSceneLoadedTimeoutSeconds + "s 仍未建树（Enable 是 fire-and-forget）");
            RaiseFailure("内容构建超时：" + _awaitingSceneTourId);
            _awaitingSceneTourId = null;
        }

        private void TryBindMarkerSession()
        {
            if (_pendingSession == null || _ite == null)
            {
                return;
            }

            _bridge?.Dispose();
            _bridge = new IteMarkerBridge(
                _pendingSession, stabilizerProfile, platformOffsets, _ite.SubmitMarkerScan);
            _pendingSession = null;
        }

        private void HookRuntimeEvents()
        {
            if (_eventsHooked)
            {
                return;
            }

            _eventsHooked = true;
            _ite.OnLoadProgress += HandleLoadProgress;
            _ite.OnSpaceSceneLoaded += HandleSpaceSceneLoaded;
            _ite.OnSpaceSceneAssetsLoaded += HandleSpaceSceneAssetsLoaded;
            _ite.OnInitialized += HandleInitialized;
            _ite.OnTourActivated += HandleTourActivated;
            _ite.OnTourDeactivated += HandleTourDeactivated;
            _ite.OnTourSceneLoaded += HandleTourSceneLoaded;
            _ite.OnScanPromptChanged += HandleScanPromptChanged;
        }

        private void UnhookRuntimeEvents()
        {
            if (_ite == null || !_eventsHooked)
            {
                return;
            }

            _ite.OnLoadProgress -= HandleLoadProgress;
            _ite.OnSpaceSceneLoaded -= HandleSpaceSceneLoaded;
            _ite.OnSpaceSceneAssetsLoaded -= HandleSpaceSceneAssetsLoaded;
            _ite.OnInitialized -= HandleInitialized;
            _ite.OnTourActivated -= HandleTourActivated;
            _ite.OnTourDeactivated -= HandleTourDeactivated;
            _ite.OnTourSceneLoaded -= HandleTourSceneLoaded;
            _ite.OnScanPromptChanged -= HandleScanPromptChanged;
            _eventsHooked = false;
        }

        private static void HandleLoadProgress(float progress)
            => Debug.Log("[ITE Host] OnLoadProgress " + progress.ToString("F2"));

        private static void HandleSpaceSceneLoaded(IteSpaceScene scene)
            => Debug.Log("[ITE Host] OnSpaceSceneLoaded " + (scene != null ? scene.name : "null"));

        private static void HandleSpaceSceneAssetsLoaded(IteSpaceScene scene)
            => Debug.Log("[ITE Host] OnSpaceSceneAssetsLoaded " + (scene != null ? scene.name : "null"));

        private static void HandleInitialized()
            => Debug.Log("[ITE Host] OnInitialized");

        private void HandleTourActivated(string tourId)
        {
            Debug.Log("[ITE Host] OnTourActivated " + tourId);
            _awaitingSceneTourId = tourId;
            _awaitingSceneSince = Time.unscaledTime;
        }

        private static void HandleTourDeactivated(string tourId)
            => Debug.Log("[ITE Host] OnTourDeactivated " + tourId);

        private void HandleTourSceneLoaded(string tourId)
        {
            Debug.Log("[ITE Host] OnTourSceneLoaded " + tourId);
            if (tourId == _awaitingSceneTourId)
            {
                _awaitingSceneTourId = null;
            }
        }

        private static void HandleScanPromptChanged(ScanPrompt prompt)
            => Debug.Log("[ITE Host] OnScanPromptChanged " + prompt.State);

        private void OnDestroy()
        {
            UnhookRuntimeEvents();
            _picoPresence?.Dispose();
            _picoPresence = null;
            _bridge?.Dispose();
            _bridge = null;
            _pendingSession = null;

            if (_ite != null)
            {
                _ite.Shutdown();
                _ite = null;
            }
        }
    }
}
