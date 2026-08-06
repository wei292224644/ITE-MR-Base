using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reuses the Diagnostics scene's tracked-device UI button as a development-only
/// world-space control surface. This keeps the probe operable on a headset where
/// IMGUI cannot be targeted reliably by an XR ray.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MarkerProbeEntry))]
public sealed class MarkerProbeXrControls : MonoBehaviour
{
    private MarkerProbeEntry entry;
    private Button actionButton;
    private Button staticButton;
    private Button dynamicButton;
    private Button dualButton;
    private Button stopButton;
    private TMP_Text statusText;
    [SerializeField] private float refreshIntervalSeconds = 0.2f;
    private float nextRefreshTime;

    private void Start()
    {
        if (!Debug.isDebugBuild)
        {
            enabled = false;
            return;
        }

        entry = GetComponent<MarkerProbeEntry>();
        GameObject templateObject = GameObject.Find("RayTargetButton");
        Button template = templateObject != null ? templateObject.GetComponent<Button>() : null;
        if (template == null)
        {
            Debug.LogError("[MarkerProbe] XR controls require the Diagnostics RayTargetButton template.", this);
            enabled = false;
            return;
        }

        RectTransform canvasTransform = template.transform.parent as RectTransform;
        if (canvasTransform != null)
        {
            canvasTransform.sizeDelta = new Vector2(900f, 720f);
        }

        actionButton = ConfigureButton(template, "MarkerProbeAction", new Vector2(0f, 245f), new Vector2(800f, 110f));
        staticButton = CloneButton(template, "MarkerProbeStatic", new Vector2(-270f, 90f), new Vector2(250f, 100f));
        dynamicButton = CloneButton(template, "MarkerProbeDynamic", new Vector2(0f, 90f), new Vector2(250f, 100f));
        dualButton = CloneButton(template, "MarkerProbeDual", new Vector2(270f, 90f), new Vector2(250f, 100f));
        stopButton = CloneButton(template, "MarkerProbeStop", new Vector2(0f, -65f), new Vector2(800f, 100f));

        actionButton.onClick.AddListener(Advance);
        staticButton.onClick.AddListener(entry.SelectStaticFixture);
        dynamicButton.onClick.AddListener(entry.SelectDynamicFixture);
        dualButton.onClick.AddListener(entry.SelectDualFixture);
        stopButton.onClick.AddListener(StopCurrentBoundary);

        TMP_Text templateLabel = GetLabel(template);
        if (templateLabel != null && canvasTransform != null)
        {
            statusText = Instantiate(templateLabel, canvasTransform);
            statusText.name = "MarkerProbeStatus";
            statusText.raycastTarget = false;
            statusText.alignment = TextAlignmentOptions.TopLeft;
            statusText.fontSize = 28f;
            RectTransform statusTransform = statusText.rectTransform;
            statusTransform.anchorMin = statusTransform.anchorMax = new Vector2(0.5f, 0.5f);
            statusTransform.anchoredPosition = new Vector2(0f, -245f);
            statusTransform.sizeDelta = new Vector2(800f, 230f);
        }

        Debug.Log("[MarkerProbe] XR diagnostic controls ready; use the tracked-device ray to operate the probe.", this);
        RefreshLabels();
    }

    private void Update()
    {
        if (entry != null && Time.unscaledTime >= nextRefreshTime)
        {
            nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshIntervalSeconds);
            RefreshLabels();
        }
    }

    private static Button ConfigureButton(Button button, string objectName, Vector2 position, Vector2 size)
    {
        button.name = objectName;
        button.onClick = new Button.ButtonClickedEvent();
        RectTransform transform = button.transform as RectTransform;
        if (transform != null)
        {
            transform.anchorMin = transform.anchorMax = new Vector2(0.5f, 0.5f);
            transform.anchoredPosition = position;
            transform.sizeDelta = size;
        }

        return button;
    }

    private static Button CloneButton(Button template, string objectName, Vector2 position, Vector2 size)
    {
        Button clone = Instantiate(template, template.transform.parent);
        return ConfigureButton(clone, objectName, position, size);
    }

    private void Advance()
    {
        if (!entry.IsProbeRunning)
        {
            entry.TryStartProbe();
        }
        else if (!entry.IsSessionActive)
        {
            entry.StartSession();
        }
        else if (entry.CurrentRun == null || entry.CurrentRun.state == MarkerProbeState.RunEnded)
        {
            entry.StartNextRun();
        }
        else if (entry.IsDualMarkerRun && !entry.IsDualPairingComplete)
        {
            entry.ContinueDualPairing();
        }
        else
        {
            entry.EndCurrentRun();
        }
    }

    private void StopCurrentBoundary()
    {
        if (entry.CurrentRun != null && entry.CurrentRun.state != MarkerProbeState.RunEnded)
        {
            entry.EndCurrentRun();
        }
        else if (entry.IsSessionActive)
        {
            entry.EndSession();
        }
        else if (entry.IsProbeRunning)
        {
            entry.StopProbe();
        }
    }

    private void RefreshLabels()
    {
        SetLabel(actionButton, ResolveActionLabel());
        SetLabel(staticButton, entry.SelectedFixture == MarkerProbeFixtureKind.StaticId0 ? "STATIC 0  [selected]" : "STATIC 0");
        SetLabel(dynamicButton, entry.SelectedFixture == MarkerProbeFixtureKind.DynamicId250 ? "DYNAMIC 250  [selected]" : "DYNAMIC 250");
        SetLabel(dualButton, entry.SelectedFixture == MarkerProbeFixtureKind.DualMarker0And250 ? "DUAL 0 + 250  [selected]" : "DUAL 0 + 250");
        SetLabel(stopButton, ResolveStopLabel());

        if (statusText != null)
        {
            statusText.text =
                $"State: {entry.CurrentState}\n" +
                $"Session: {entry.CurrentSession?.sessionId ?? "-"}\n" +
                $"Run: {entry.CurrentRun?.runId ?? "-"}\n" +
                $"Log: {entry.CurrentLogPath ?? "created when session starts"}";
        }
    }

    private string ResolveActionLabel()
    {
        if (!entry.IsProbeRunning) return "1. START PROBE";
        if (!entry.IsSessionActive) return "2. START SESSION";
        if (entry.CurrentRun == null || entry.CurrentRun.state == MarkerProbeState.RunEnded) return "3. START NEXT RUN";
        if (entry.IsDualMarkerRun && !entry.IsDualPairingComplete) return "4. SCAN SECOND DUAL QR";
        return "END CURRENT RUN";
    }

    private string ResolveStopLabel()
    {
        if (entry.CurrentRun != null && entry.CurrentRun.state != MarkerProbeState.RunEnded) return "END RUN";
        if (entry.IsSessionActive) return "END SESSION + FLUSH LOG";
        if (entry.IsProbeRunning) return "STOP PROBE";
        return "NOTHING TO STOP";
    }

    private static TMP_Text GetLabel(Button button)
    {
        return button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
    }

    private static void SetLabel(Button button, string text)
    {
        TMP_Text label = GetLabel(button);
        if (label != null)
        {
            label.text = text;
        }
    }
}
