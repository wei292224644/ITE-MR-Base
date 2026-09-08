using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 常驻 HUD(task 6.6-6.7):列出活跃标记、Observed/Lost 累计次数,并提供 Pause/Resume 按钮
/// 驱动 <see cref="MarkerHookTestRig"/>。用 UGUI + TMP(不是 IMGUI OnGUI)——沿用
/// TMP + UGUI + 射线交互,假定 PICO/Quest 侧 XR 射线可点击 UGUI Button。
/// 若真机验收发现点不动,单独排查交互层,不在本任务内解决。
/// </summary>
[AddComponentMenu("MR Base/Diagnostics/Marker Hook Test HUD")]
[RequireComponent(typeof(MarkerHookTestRig))]
public sealed class MarkerHookTestHud : MonoBehaviour
{
    [SerializeField] private float refreshIntervalSeconds = 0.2f;

    private MarkerHookTestRig rig;
    private TMP_Text statusText;
    private TMP_Text pauseButtonLabel;
    private Button pauseButton;
    private float nextRefreshTime;

    private void Start()
    {
        rig = GetComponent<MarkerHookTestRig>();
        BuildCanvas();
        RefreshLabels();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshIntervalSeconds);
        RefreshLabels();
    }

    private void BuildCanvas()
    {
        var canvasObject = new GameObject("MarkerHookTestHudCanvas");
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            canvasObject.transform.SetParent(mainCamera.transform, worldPositionStays: false);
            canvasObject.transform.localPosition = new Vector3(0.5f, 0.2f, 1.2f);
            canvasObject.transform.localRotation = Quaternion.identity;
        }

        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        canvasObject.AddComponent<GraphicRaycaster>();

        var rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(500f, 320f);
        canvasObject.transform.localScale = Vector3.one * 0.001f;

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(canvasObject.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        var panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.6f);

        statusText = CreateText(panel.transform, "StatusText", new Vector2(0f, 40f), new Vector2(460f, 200f), 20f);
        statusText.alignment = TextAlignmentOptions.TopLeft;

        pauseButton = CreateButton(panel.transform, "PauseResumeButton", new Vector2(0f, -120f), new Vector2(300f, 60f));
        pauseButton.onClick.AddListener(TogglePause);
        pauseButtonLabel = pauseButton.GetComponentInChildren<TMP_Text>();
    }

    private static TMP_Text CreateText(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, float fontSize)
    {
        var textObject = new GameObject(name, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        var rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.color = Color.white;
        return text;
    }

    private static Button CreateButton(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
    {
        var buttonObject = new GameObject(name, typeof(RectTransform));
        buttonObject.transform.SetParent(parent, false);
        var rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        var image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.2f, 0.4f, 0.9f, 0.9f);
        Button button = buttonObject.AddComponent<Button>();

        TMP_Text label = CreateText(buttonObject.transform, "Label", Vector2.zero, size, 22f);
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;

        return button;
    }

    private void TogglePause()
    {
        if (rig.IsPaused)
        {
            rig.Resume();
        }
        else
        {
            rig.Pause();
        }

        RefreshLabels();
    }

    private void RefreshLabels()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"State: {(rig.IsPaused ? "Paused" : "Running")}");
        builder.AppendLine($"Observed: {rig.ObservedCount}  Lost: {rig.LostCount}");
        builder.AppendLine("Active markers:");
        foreach (string key in rig.ActiveMarkerKeys.OrderBy(k => k))
        {
            builder.AppendLine($"  {key}");
        }

        if (statusText != null)
        {
            statusText.text = builder.ToString();
        }

        if (pauseButtonLabel != null)
        {
            pauseButtonLabel.text = rig.IsPaused ? "RESUME" : "PAUSE";
        }
    }
}
