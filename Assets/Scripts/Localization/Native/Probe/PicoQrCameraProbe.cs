#if MRBASE_HAS_PICO_SDK
using UnityEngine;
using Pose = UnityEngine.Pose;

/// <summary>
/// PICO 相机流上的 fiducial marker 度量台架的可视化外壳。
///
/// 名字还叫 QrCameraProbe 是历史遗留——检测器已由 ZXing/QR 换成 AprilTag（change
/// pico-camera-fiducial-tracking）。改名要连 .cs、类名和场景引用一起动，等管线在真机上跑通再做，
/// 免得把重命名的风险和换检测器的风险混在一起查。
///
/// 相机会话、采样节流与检测循环已整体搬进 <see cref="PicoFiducialObservationSource"/>
/// （design D9）。这里只保留遥测 OnGUI 与 markerBox 自检逻辑，不再持有任何相机/检测状态
/// （task 3.5）。
/// </summary>
[AddComponentMenu("MR Base/Probe/PICO Fiducial Camera Probe")]
[DisallowMultipleComponent]
[RequireComponent(typeof(PicoFiducialObservationSource))]
public sealed class PicoQrCameraProbe : MonoBehaviour
{
    [Header("Feedback")]
    [Tooltip("在相机正前方挂一个红色方块。它与识别无关，只用来确认渲染本身是通的。")]
    public bool showSelfTestBox = true;
    [Tooltip("识别到 marker 后，在其位置显示的方块边长（米）。")]
    [Range(0.02f, 0.30f)] public float markerBoxSize = 0.08f;
    [Tooltip("超过这个时长没再检出就把方块隐藏，避免停在旧位置误导判断。")]
    [Range(0.2f, 5f)] public float markerBoxHoldSeconds = 1f;

    private PicoFiducialObservationSource source;
    private GameObject markerBox;
    private float markerBoxHideTime;
    private string lastMarkerPayload = "(未检出)";

    private void Start()
    {
        source = GetComponent<PicoFiducialObservationSource>();
        source.Open();
        if (showSelfTestBox) CreateSelfTestBox();
    }

    private void Update()
    {
        if (markerBox != null && markerBox.activeSelf && Time.unscaledTime > markerBoxHideTime)
        {
            markerBox.SetActive(false);
        }

        var observations = source.Poll();
        for (int i = 0; i < observations.Count; i++)
        {
            MarkerObservation observation = observations[i];
            lastMarkerPayload = observation.RawPayload;
            ShowMarkerBox(observation.Pose);
        }
    }

    private void CreateSelfTestBox()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            Debug.LogWarning("[PicoFiducialProbe] 没有 Camera.main，跳过自检方块");
            return;
        }

        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "PicoFiducialProbe SelfTestBox";
        Destroy(box.GetComponent<Collider>());
        ApplyBoxMaterial(box, new Color(0.9f, 0.15f, 0.15f));
        box.transform.SetParent(camera.transform, false);
        box.transform.localPosition = new Vector3(0f, -0.15f, 0.5f);
        box.transform.localScale = Vector3.one * 0.05f;
        Debug.Log("[PicoFiducialProbe] 自检方块已挂到 Camera.main 前方 0.5 m");
    }

    private void ApplyBoxMaterial(GameObject box, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Universal Render Pipeline/Lit");
        var renderer = box.GetComponent<Renderer>();
        if (shader != null)
        {
            renderer.material = new Material(shader);
        }
        else
        {
            Debug.LogWarning("[PicoFiducialProbe] 找不到 URP shader，方块会是默认材质");
        }

        renderer.material.color = color;
    }

    private void ShowMarkerBox(Pose pose)
    {
        if (markerBox == null)
        {
            markerBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            markerBox.name = "PicoFiducialProbe MarkerBox";
            Destroy(markerBox.GetComponent<Collider>());
            // 内置默认材质在 URP 下是洋红色。
            ApplyBoxMaterial(markerBox, new Color(0.1f, 0.9f, 0.3f));
        }

        // 方块贴在板面上而不是嵌进去一半：沿法线抬高半个边长。
        markerBox.transform.SetPositionAndRotation(
            pose.position + pose.forward * (markerBoxSize * 0.5f),
            pose.rotation);
        markerBox.transform.localScale = Vector3.one * markerBoxSize;
        markerBox.SetActive(true);
        markerBoxHideTime = Time.unscaledTime + markerBoxHoldSeconds;
    }

    private void OnGUI()
    {
        const int pad = 24;
        GUI.Label(new Rect(pad, pad, 900, 28), "PICO Fiducial Camera Probe (AprilTag)");
        GUI.Label(new Rect(pad, pad + 32, 1100, 24),
            $"camera={source.IsCameraOpen} bound={source.IsServiceBound} callbacks={source.CallbackFrames} " +
            $"frame={source.FrameWidth}x{source.FrameHeight} status={source.FrameStatus}");
        GUI.Label(new Rect(pad, pad + 58, 1100, 24),
            $"sampleHz={source.sampleHz} sampled={source.SampledFrames} dropped={source.DroppedFrames} " +
            $"copyMs={source.LastCopyMs:F2} detectMs={source.LastDetectMs:F1}");
        GUI.Label(new Rect(pad, pad + 84, 1100, 24),
            $"detector: {source.DetectorStatus}  last: {source.LastDetection}  " +
            $"hit={source.DetectSuccesses}/{source.DetectAttempts}");
        GUI.Label(new Rect(pad, pad + 110, 1100, 24), $"frameTimestamp(ns): {source.LastFrameTimestamp}");
        GUI.Label(new Rect(pad, pad + 136, 1100, 24),
            $"pose: {source.PoseStatus}  lastPayload={lastMarkerPayload}  " +
            $"box={(markerBox != null && markerBox.activeSelf ? markerBox.transform.position.ToString("F3") : "隐藏")}");
    }

    private void OnDestroy()
    {
        source?.Close();
    }
}
#endif
