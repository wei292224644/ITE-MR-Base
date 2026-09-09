using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Uality.IteTour.Core;

namespace MRBase.Ite.Host
{
    /// <summary>
    /// 编辑器调试 HUD：摊开加载与激活状态。屏幕空间 Overlay，可整块停用。
    /// </summary>
    [AddComponentMenu("ITE/Editor HUD")]
    public sealed class IteEditorHud : MonoBehaviour
    {
        [SerializeField] IteHostBootstrap host;
        [SerializeField] float refreshIntervalSeconds = 0.2f;

        private TMP_Text _text;
        private IteRuntime _hookedRuntime;
        private float _nextRefresh;
        private float _loadProgress;
        private string _sceneName;
        private ScanPrompt _prompt;

        private void Start()
        {
            BuildCanvas();
            TryHook();
        }

        private void Update()
        {
            TryHook();
            if (Time.unscaledTime < _nextRefresh)
            {
                return;
            }

            _nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, refreshIntervalSeconds);
            Refresh();
        }

        private void TryHook()
        {
            if (host == null || host.Runtime == null || _hookedRuntime == host.Runtime)
            {
                return;
            }

            Unhook();
            _hookedRuntime = host.Runtime;
            _hookedRuntime.OnLoadProgress += HandleLoadProgress;
            _hookedRuntime.OnSpaceSceneLoaded += HandleSpaceSceneLoaded;
            _hookedRuntime.OnScanPromptChanged += HandleScanPromptChanged;
        }

        private void HandleLoadProgress(float progress) => _loadProgress = progress;

        private void HandleSpaceSceneLoaded(Uality.IteTour.Data.IteSpaceScene scene)
            => _sceneName = scene != null ? scene.name : null;

        private void HandleScanPromptChanged(ScanPrompt prompt) => _prompt = prompt;

        private void Refresh()
        {
            if (_text == null || host == null)
            {
                return;
            }

            var runtime = host.Runtime;
            var bridge = host.MarkerBridge;
            _text.text = IteEditorHudText.Format(
                _loadProgress,
                _sceneName,
                runtime != null ? runtime.AssembledTourIds : null,
                runtime != null ? runtime.ActiveTourId : null,
                _prompt,
                runtime != null ? runtime.PendingTourIds : null,
                bridge != null ? bridge.LastObservedRawPayload : null,
                bridge != null ? bridge.LastLostRawPayload : null);
        }

        private void BuildCanvas()
        {
            var canvasObject = new GameObject("IteEditorHudCanvas");
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObject.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasObject.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(16f, -16f);
            panelRect.sizeDelta = new Vector2(560f, 320f);
            var image = panel.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.65f);

            var textObject = new GameObject("Status", typeof(RectTransform));
            textObject.transform.SetParent(panel.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 8f);
            textRect.offsetMax = new Vector2(-12f, -8f);

            _text = textObject.AddComponent<TextMeshProUGUI>();
            _text.fontSize = 22f;
            _text.color = Color.white;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.enableWordWrapping = true;
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
            _hookedRuntime = null;
        }

        private void OnDestroy() => Unhook();
    }
}
