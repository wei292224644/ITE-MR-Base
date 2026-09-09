using System;
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
        public const string DefaultPayloadPattern = @"^\*{6}(.*?)\*{6}$";

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
        [Tooltip("用于识别谁进出触发体积，通常是 XR Origin 的主相机")]
        private Transform xrCamera;

        [Header("标记桥接")]
        [SerializeField]
        [Tooltip("从 RawPayload 剥出 tourId 的外壳正则。真机印制格式未核实时保持默认。")]
        private string payloadPattern = DefaultPayloadPattern;

        [SerializeField]
        [Tooltip("OnTourActivated 之后等待 OnTourSceneLoaded 的秒数。Enable() 是 fire-and-forget，超时才看得见失败。")]
        private float tourSceneLoadedTimeoutSeconds = 180f;

        [Header("行为")]
        [SerializeField]
        [Tooltip("关掉可用于离线调试：跳过全部下载与版本查询，直接读本地缓存")]
        private bool networkAvailable = true;

        private IteRuntime _ite;
        private HeadsetPresenceAdapter _headsetPresence;
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

            _ite = IteRuntime.Create(new IteBootstrap
            {
                Config = config,
                AnchorRoot = anchorRoot,
                TourRoot = tourRoot,
                Camera = xrCamera,
                IsNetworkAvailable = () => networkAvailable,
            });

            if (_ite == null)
            {
                return false;
            }

            HookRuntimeEvents();
            _headsetPresence = new HeadsetPresenceAdapter(_ite.SetHeadsetMounted);
            TryBindMarkerSession();

            if (_bridge == null)
            {
                Debug.Log("[ITE Host] 未接入标记源，扫码激活不可用");
            }

            return true;
        }

        private async void Start()
        {
            if (!TryCreateRuntime())
            {
                enabled = false;
                return;
            }

            await _ite.StartAsync();
        }

        private void Update()
        {
            _headsetPresence?.Poll();

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
            _awaitingSceneTourId = null;
        }

        private void TryBindMarkerSession()
        {
            if (_pendingSession == null || _ite == null)
            {
                return;
            }

            _bridge?.Dispose();
            var pattern = string.IsNullOrEmpty(payloadPattern) ? DefaultPayloadPattern : payloadPattern;
            _bridge = new IteMarkerBridge(_pendingSession, pattern, _ite.SubmitMarkerScan);
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
