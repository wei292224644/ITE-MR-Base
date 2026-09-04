using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// <c>MarkerHookTest</c> 场景的装配根(design D1 / D8,task 6.2-6.5)。
///
/// 按 <c>#if MRBASE_QUEST / MRBASE_PICO</c> 建对应观测源,装配 <see cref="MarkerTrackingSession"/>,
/// 在 <see cref="Update"/> 里用 <c>Time.deltaTime</c> 驱动 <c>Tick</c>。<see cref="MarkerTrackingSession.MarkerObserved"/>
/// 喂进 <see cref="MarkerStabilizer"/>(design D8,复用而非重新发明);只有
/// <see cref="MarkerStabilizer.Stabilized"/> 触发时才在稳定位姿处显示盒子 + 世界空间标签,
/// 避免可视化跟着未滤波的原始位姿抖动。
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
    private MarkerStabilizer stabilizer;
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
#if MRBASE_QUEST
        source = new QuestObservationSource();
#elif MRBASE_PICO && MRBASE_HAS_PICO_SDK
        source = gameObject.AddComponent<PicoFiducialObservationSource>();
#elif MRBASE_PICO
        // 构建意图是 PICO,但 com.unity.xr.picoxr 不在工程里。两条 define 轴是独立的,
        // 分开报错才能一眼看出是"包没装"而不是"平台没配"(沿用 MarkerTrackingBootstrapper 的模式)。
        Debug.LogError($"{LogPrefix} 构建意图为 PICO,但未安装 com.unity.xr.picoxr。", this);
        enabled = false;
        return;
#else
        Debug.LogError($"{LogPrefix} 未识别到 MRBASE_QUEST 或 MRBASE_PICO 平台定义,部署配置错误。", this);
        enabled = false;
        return;
#endif
        stabilizer = new MarkerStabilizer();
        stabilizer.Stabilized += HandleStabilized;

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

        stabilizer.Feed(key, observation.Pose, Time.deltaTime);
    }

    private void HandleLost(MarkerPlatform platform, string rawPayload)
    {
        string key = Key(platform, rawPayload);
        LostCount++;
        Debug.Log($"{LogPrefix} Lost platform={platform} rawPayload={rawPayload}", this);

        stabilizer.Reset(key);
        lastObservationByKey.Remove(key);
        RemoveVisual(key);
    }

    private void HandleStabilized(string key, Pose stablePose)
    {
        ShowOrUpdateVisual(key, stablePose);
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

        if (stabilizer != null)
        {
            stabilizer.Stabilized -= HandleStabilized;
        }
    }
}
