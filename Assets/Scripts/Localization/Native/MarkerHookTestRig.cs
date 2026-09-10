using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// <c>MarkerHookTest</c> 场景的装配根(design D1 / D8,task 6.2-6.5)。
///
/// 经 <see cref="MarkerSourceFactory"/> 取得对应平台的观测源,装配 <see cref="MarkerTrackingSession"/>,
/// 在 <see cref="Update"/> 里用 <c>Time.deltaTime</c> 驱动 <c>Tick</c>。
///
/// 盒子**直接跟每次 <see cref="MarkerTrackingSession.MarkerObserved"/> 更新**,不经过
/// <see cref="MarkerStabilizer"/>(design D8)。本场景要验的是 hook 本身发没发、发得对不对;
/// 插一层稳定器会把"hook 没发"和"稳定器没判稳"混成同一个现象——真机已经踩过一次:
/// 单应解出的位姿相邻两次抖 2-5 度,而稳定器的 rotationThreshold 是 1 度,计数器反复清零,
/// Stabilized 一次都没触发,外部看到的就是"扫不到"。
/// </summary>
[AddComponentMenu("MR Base/Diagnostics/Marker Hook Test Rig")]
public sealed class MarkerHookTestRig : MonoBehaviour
{
    private const string LogPrefix = "[MarkerHook]";

    [SerializeField] private float lostAfterSeconds = 1.0f;
    [SerializeField] private float boxSize = 0.08f;

    private sealed class MarkerVisual
    {
        public GameObject Box;
        public TextMeshPro Label;
    }

    private IMarkerObservationSource source;
    private MarkerTrackingSession session;
    private Camera mainCamera;

    private readonly Dictionary<string, MarkerObservation> lastObservationByKey =
        new Dictionary<string, MarkerObservation>();
    private readonly Dictionary<string, MarkerVisual> activeVisuals = new Dictionary<string, MarkerVisual>();

    public int ObservedCount { get; private set; }
    public int LostCount { get; private set; }
    public bool IsPaused { get; private set; }
    public IReadOnlyCollection<string> ActiveMarkerKeys => activeVisuals.Keys;

    private void Awake()
    {
        // 平台分支全在工厂里（design D4）。本组件与 ITE 的设备输入层共用同一份，
        // 否则 MRUK 装配、SDK 缺失、平台未配置这三条路径要各维护一份。
        source = MarkerSourceFactory.Create(gameObject, out _, out string detail);

        if (source == null)
        {
            Debug.LogError($"{LogPrefix} {detail}", this);
            enabled = false;
            return;
        }

        if (!string.IsNullOrEmpty(detail))
        {
            Debug.LogError($"{LogPrefix} {detail}", this);
        }

        session = new MarkerTrackingSession(source, lostAfterSeconds);
        session.MarkerObserved += HandleObserved;
        session.MarkerLost += HandleLost;
        session.Open();
    }

    private void Update()
    {
        session.Tick(Time.deltaTime);

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera != null)
        {
            foreach (MarkerVisual visual in activeVisuals.Values)
            {
                BillboardTowardsCamera(visual.Label.transform, mainCamera.transform);
            }
        }
    }

    public void Pause()
    {
        if (IsPaused)
        {
            return;
        }

        IsPaused = true;
        session.Pause();
        Debug.Log($"{LogPrefix} Paused", this);
    }

    public void Resume()
    {
        if (!IsPaused)
        {
            return;
        }

        IsPaused = false;
        session.Resume();
        Debug.Log($"{LogPrefix} Resumed", this);
    }

    private void HandleObserved(MarkerObservation observation)
    {
        string key = Key(observation.Platform, observation.RawPayload);
        lastObservationByKey[key] = observation;
        ObservedCount++;
        Debug.Log(
            $"{LogPrefix} Observed platform={observation.Platform} rawPayload={observation.RawPayload} " +
            $"pos={observation.Pose.position:F3}", this);

        ShowOrUpdateVisual(key, observation.Pose);
    }

    private void HandleLost(MarkerPlatform platform, string rawPayload)
    {
        string key = Key(platform, rawPayload);
        LostCount++;
        Debug.Log($"{LogPrefix} Lost platform={platform} rawPayload={rawPayload}", this);

        lastObservationByKey.Remove(key);
        RemoveVisual(key);
    }

    private void ShowOrUpdateVisual(string key, Pose pose)
    {
        if (!activeVisuals.TryGetValue(key, out MarkerVisual visual))
        {
            visual = CreateVisual(key);
            activeVisuals[key] = visual;
        }

        visual.Box.transform.SetPositionAndRotation(pose.position, pose.rotation);

        if (lastObservationByKey.TryGetValue(key, out MarkerObservation observation))
        {
            visual.Label.text = $"{observation.Platform}\n{observation.RawPayload}";
        }

        Vector3 labelPosition = pose.position + Vector3.up * (boxSize * 1.5f);
        visual.Label.transform.position = labelPosition;
    }

    private MarkerVisual CreateVisual(string key)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = $"MarkerHookVisual_{key}";
        Destroy(box.GetComponent<Collider>());
        box.transform.localScale = Vector3.one * boxSize;
        ApplyBoxMaterial(box, new Color(0.1f, 0.9f, 0.3f));
        box.transform.SetParent(transform, worldPositionStays: true);

        var labelObject = new GameObject($"MarkerHookLabel_{key}");
        labelObject.transform.SetParent(box.transform, worldPositionStays: true);
        TextMeshPro label = labelObject.AddComponent<TextMeshPro>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 4f;
        label.rectTransform.sizeDelta = new Vector2(1f, 0.3f);

        return new MarkerVisual { Box = box, Label = label };
    }

    private void RemoveVisual(string key)
    {
        if (activeVisuals.TryGetValue(key, out MarkerVisual visual))
        {
            if (visual.Box != null)
            {
                Destroy(visual.Box);
            }

            activeVisuals.Remove(key);
        }
    }

    private static void BillboardTowardsCamera(Transform label, Transform camera)
    {
        Vector3 direction = label.position - camera.position;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        label.rotation = Quaternion.LookRotation(direction);
    }

    private static void ApplyBoxMaterial(GameObject box, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit");
        var renderer = box.GetComponent<Renderer>();
        if (shader != null)
        {
            renderer.material = new Material(shader);
        }

        renderer.material.color = color;
    }

    private static string Key(MarkerPlatform platform, string rawPayload) => $"{platform}:{rawPayload}";

    private void OnDestroy()
    {
        if (session != null)
        {
            session.MarkerObserved -= HandleObserved;
            session.MarkerLost -= HandleLost;
            session.Close();
        }
    }
}
