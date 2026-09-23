using System.Text;
using TMPro;
using UnityEngine;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace MRBase.Ite.Host
{
    /// <summary>
    /// 头显里的导览状态面板（design D12）。
    ///
    /// 存在的理由很直接：包广播 `OnLoadProgress` / `OnScanPromptChanged`，宿主此前只
    /// `Debug.Log`；而真机首跑要下载空间包与 5 个 tour 包，头显里全程零反馈，
    /// 与「卡死」不可区分。日志在头显里看不见。
    ///
    /// 世界空间 —— 屏幕空间 Overlay 在 VR 里根本不渲染。
    /// 归宿主而不进包：包对 UI 零涉入，加进去等于把宿主 UX 焊进可移植包。
    /// 也不挂在 MRCore 上：那是 MR 基座，不承载 ITE 的内容状态。
    /// </summary>
    [AddComponentMenu("ITE/HMD Panel")]
    [DisallowMultipleComponent]
    public sealed class IteHmdPanel : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("装配点。留空则在本场景里找")]
        private IteHostBootstrap host;

        [Header("摆位")]
        [SerializeField]
        [Tooltip("面板与相机的距离（米）")]
        private float distance = 1.6f;

        [SerializeField]
        [Tooltip("面板相对视线中心的下移量（米），免得挡住内容")]
        private float verticalOffset = -0.35f;

        [SerializeField]
        [Tooltip("跟随视线的平滑时间常数（秒）。0 表示硬跟随")]
        private float followSmoothTime = 0.25f;

        [Header("行为")]
        [SerializeField]
        [Tooltip("装配完成且无失败时，面板在这么多秒后自动收起。0 表示不收")]
        private float hideAfterInitializedSeconds = 4f;

        private Canvas _canvas;
        private TMP_Text _text;
        private Transform _camera;

        private float _loadProgress;
        private string _sceneName;
        private ScanPrompt _prompt = ScanPrompt.Hidden;
        private string _failure;
        private bool _initialized;
        private float _initializedAt;
        private bool _recenterPending;

        private IteRuntime _hookedRuntime;
        private IteHostBootstrap _hookedHost;

        private void Awake()
        {
            if (host == null)
            {
                host = FindFirstObjectByType<IteHostBootstrap>();
            }

            BuildCanvas();
        }

        private void Update()
        {
            TryHook();
            Follow();
            Refresh();
        }

        private void TryHook()
        {
            if (host == null)
            {
                return;
            }

            if (_hookedHost != host)
            {
                if (_hookedHost != null)
                {
                    _hookedHost.Failed -= HandleFailed;
                }

                _hookedHost = host;
                _hookedHost.Failed += HandleFailed;
            }

            var runtime = host.Runtime;
            if (runtime == null || ReferenceEquals(_hookedRuntime, runtime))
            {
                return;
            }

            Unhook();
            _hookedRuntime = runtime;
            _hookedRuntime.OnLoadProgress += HandleLoadProgress;
            _hookedRuntime.OnSpaceSceneLoaded += HandleSpaceSceneLoaded;
            _hookedRuntime.OnScanPromptChanged += HandleScanPromptChanged;
            _hookedRuntime.OnInitialized += HandleInitialized;
            _hookedRuntime.OnGuideStateChanged += HandleGuideStateChanged;

            // 挂钩可能晚于首次广播（构造后第一帧就发），先同步一次当前提示与状态
            _prompt = runtime.ScanPrompt;
            HandleGuideStateChanged(runtime.GuideState, runtime.GuideStateReason);
        }

        private void Unhook()
        {
            if (_hookedRuntime == null)
            {
                return;
            }

            _hookedRuntime.OnLoadProgress -= HandleLoadProgress;
            _hookedRuntime.OnSpaceSceneLoaded -= HandleSpaceSceneLoaded;
            _hookedRuntime.OnScanPromptChanged -= HandleScanPromptChanged;
            _hookedRuntime.OnInitialized -= HandleInitialized;
            _hookedRuntime.OnGuideStateChanged -= HandleGuideStateChanged;
            _hookedRuntime = null;
        }

        private void HandleLoadProgress(float progress) => _loadProgress = progress;

        private void HandleSpaceSceneLoaded(IteSpaceScene scene)
            => _sceneName = scene != null ? scene.name : null;

        private void HandleScanPromptChanged(ScanPrompt prompt) => _prompt = prompt;

        /// <summary>
        /// 重定位黄条表达的是一个状态：「因为重定位，正在等待扫码」（ite-guide-state-machine D8）。
        /// 跟着状态走：扫码进入 Anchored（或摘下）即清掉，不会残留。
        /// </summary>
        public static bool ShowsRecenterBanner(GuideState state, GuideStateReason reason)
            => state == GuideState.AwaitingScan && reason == GuideStateReason.Recentered;

        private void HandleGuideStateChanged(GuideState state, GuideStateReason reason)
        {
            bool show = ShowsRecenterBanner(state, reason);
            if (show != _recenterPending)
            {
                Debug.Log("[ITE Host] 重定位横幅：" + (show ? "显示" : "清除"));
            }

            _recenterPending = show;
        }

        private void HandleInitialized()
        {
            _initialized = true;
            _initializedAt = Time.unscaledTime;
        }

        /// <summary>失败**不自动收起**：没人看见的失败等于没报。</summary>
        private void HandleFailed(string message) => _failure = message;

        private void Follow()
        {
            if (_camera == null)
            {
                var context = MRContext.Instance;
                _camera = context != null && context.Camera != null ? context.Camera.transform : null;
                if (_camera == null)
                {
                    return;
                }
            }

            var target = _camera.position
                         + _camera.forward * distance
                         + Vector3.up * verticalOffset;

            if (followSmoothTime <= 0f)
            {
                transform.SetPositionAndRotation(target, Quaternion.LookRotation(_camera.forward, Vector3.up));
                return;
            }

            float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime / followSmoothTime);
            transform.position = Vector3.Lerp(transform.position, target, t);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, Quaternion.LookRotation(_camera.forward, Vector3.up), t);
        }

        private void Refresh()
        {
            if (_canvas == null || _text == null)
            {
                return;
            }

            bool shouldShow = ShouldShow();
            if (_canvas.enabled != shouldShow)
            {
                _canvas.enabled = shouldShow;
            }

            if (!shouldShow)
            {
                return;
            }

            _text.text = Compose();
        }

        private bool ShouldShow()
        {
            if (_failure != null || _recenterPending)
            {
                return true;
            }

            if (!_initialized)
            {
                return true;
            }

            if (_prompt.State == ScanPromptState.Visible)
            {
                return true;
            }

            return hideAfterInitializedSeconds > 0f
                   && Time.unscaledTime - _initializedAt < hideAfterInitializedSeconds;
        }

        private string Compose()
        {
            var builder = new StringBuilder();

            if (_failure != null)
            {
                builder.Append("<color=#FF6B6B>").Append(_failure).Append("</color>\n");
            }

            if (_recenterPending)
            {
                builder.Append("<color=#FFD166>视角已重定位，请重新扫码</color>\n");
            }

            if (!_initialized)
            {
                builder.Append(string.IsNullOrEmpty(_sceneName) ? "正在连接内容服务…" : _sceneName)
                    .Append('\n')
                    .Append("加载中 ")
                    .Append(Mathf.RoundToInt(_loadProgress * 100f))
                    .Append('%');
                return builder.ToString();
            }

            if (_prompt.State == ScanPromptState.Visible)
            {
                builder.Append("请扫描展位上的标记");
                if (_prompt.TourIds != null && _prompt.TourIds.Count > 0)
                {
                    builder.Append('\n').Append(string.Join("、", _prompt.TourIds));
                }

                return builder.ToString();
            }

            builder.Append(string.IsNullOrEmpty(_sceneName) ? "导览已就绪" : _sceneName + " 已就绪");
            return builder.ToString();
        }

        private void BuildCanvas()
        {
            var canvasObject = new GameObject("IteHmdPanelCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);

            _canvas = canvasObject.AddComponent<Canvas>();
            // 世界空间：屏幕空间 Overlay 在头显里不渲染。
            _canvas.renderMode = RenderMode.WorldSpace;

            var rect = (RectTransform)canvasObject.transform;
            rect.sizeDelta = new Vector2(800f, 320f);
            rect.localScale = Vector3.one * 0.001f;

            var textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(canvasObject.transform, false);
            var textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 16f);
            textRect.offsetMax = new Vector2(-16f, -16f);

            _text = textObject.AddComponent<TextMeshProUGUI>();
            _text.alignment = TextAlignmentOptions.Center;
            _text.fontSize = 48f;
            _text.color = Color.white;
            _text.text = string.Empty;
        }

        private void OnDestroy()
        {
            Unhook();
            if (_hookedHost != null)
            {
                _hookedHost.Failed -= HandleFailed;
                _hookedHost = null;
            }
        }
    }
}
