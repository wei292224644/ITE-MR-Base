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

        ShowOrUpdateVisual(key, observation);
    }

    private void HandleLost(MarkerPlatform platform, string rawPayload)
    {
        string key = Key(platform, rawPayload);
        LostCount++;
        Debug.Log($"{LogPrefix} Lost platform={platform} rawPayload={rawPayload}", this);

        lastObservationByKey.Remove(key);
        RemoveVisual(key);
    }

    private void ShowOrUpdateVisual(string key, MarkerObservation observation)
    {
        Pose pose = observation.Pose;
        if (!activeVisuals.TryGetValue(key, out MarkerVisual visual))
        {
            visual = CreateVisual(key);
            activeVisuals[key] = visual;
        }

        visual.Box.transform.SetPositionAndRotation(pose.position, pose.rotation);

        // 这是平台原始四元数的 Euler,不是镜像后坐标轴的——镜像系没有对应的四元数。
        Vector3 euler = pose.rotation.eulerAngles;
        visual.Label.text =
            $"{observation.Platform}\n{observation.RawPayload}\n" +
            $"raw X{euler.x:F0} Y{euler.y:F0} Z{euler.z:F0}";

        Vector3 labelPosition = pose.position + Vector3.up * (boxSize * 1.5f);
        visual.Label.transform.position = labelPosition;
    }

    // 画的是契约本身:MarkerObservation.Pose 的 Unity 坐标轴,两端应一致为
    // X=印刷左、Y=印刷上、Z=出纸面。换算到 ITE 内容锚点是 ITE 包的事(MarkerFrame),这里不掺。
    private MarkerVisual CreateVisual(string key)
    {
        GameObject root = new GameObject($"MarkerHookVisual_{key}");
        root.transform.SetParent(transform, worldPositionStays: true);

        AxisGizmo.Create(root.transform, boxSize * 3f, Quaternion.identity, authoringHanded: false);

        var labelObject = new GameObject($"MarkerHookLabel_{key}");
        labelObject.transform.SetParent(root.transform, worldPositionStays: true);
        // TMP 的 fontSize 是世界单位,4 在 8cm 的盒子边上等于字符几米高,糊满整个视野——
        // 之前几轮截图读不出数字就是被这个盖住了,不是截图分辨率问题。用 localScale 兜底缩小,
        // 不靠猜 fontSize 的换算系数。
        labelObject.transform.localScale = Vector3.one * 0.02f;
        TextMeshPro label = labelObject.AddComponent<TextMeshPro>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 4f;
        label.rectTransform.sizeDelta = new Vector2(1f, 0.3f);

        return new MarkerVisual { Box = root, Label = label };
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
