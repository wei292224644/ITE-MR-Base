using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Management;

/// <summary>
/// 核心场景的启动闸门：查前置条件 → 报就绪，不满足则把原因摆到用户眼前。
///
/// 为什么需要它：这个项目的失败模式几乎全是「静默」的 —— loader 装错是黑屏无日志，
/// 手部子系统没起来是手不出现，构建 define 漏配是两端逻辑都不走。这些在真机上
/// 都表现为「应用起来了但什么都不对」，没有任何提示。所以前置检查必须有可见输出。
///
/// 这里不写 <c>#if MRBASE_*</c>：平台差异全部经由 <see cref="PlatformRuntime"/>，
/// 否则条件编译会随场景数扩散。
/// </summary>
public class MRBootstrap : MonoBehaviour
{
    [Tooltip("前置条件不满足时显示的提示。留空则只写日志。")]
    [SerializeField] GameObject statusRoot;
    [SerializeField] TMP_Text statusLabel;

    [Tooltip("等待手部子系统启动的秒数。XR 子系统的启动晚于 Awake。")]
    [SerializeField] float handSubsystemTimeout = 5f;

    /// <summary>全部前置条件通过。失败时应用继续运行并显示提示，不终止。</summary>
    public bool IsReady { get; private set; }

    void Start()
    {
        ShowStatus(null);
        StartCoroutine(Check());
    }

    IEnumerator Check()
    {
        if (PlatformRuntime.Name == null)
        {
            Fail("构建意图未配置。\n经 MRBase/Build/Quest 或 MRBase/Build/Pico 出包，" +
                 "不要直接用 Build Settings。");
            yield break;
        }

        var loader = XRGeneralSettings.Instance == null ? null : XRGeneralSettings.Instance.Manager?.activeLoader;
        if (loader == null)
        {
            Fail($"XR loader 未初始化（目标 {PlatformRuntime.Name}）。\n" +
                 "检查 Project Settings > XR Plug-in Management 的 Android 配置。");
            yield break;
        }

        if (MRContext.Instance == null || MRContext.Instance.Origin == null)
        {
            Fail("场景里没有 XR Origin，或 MRContext 未挂载。");
            yield break;
        }

        // 手部子系统的启动晚于第一帧,不能一次判死。
        var deadline = Time.time + handSubsystemTimeout;
        while (MRContext.Instance.Hands == null && Time.time < deadline)
            yield return null;

        if (MRContext.Instance.Hands == null)
        {
            Fail($"{handSubsystemTimeout} 秒内没等到手部子系统（loader {loader.name}）。\n" +
                 "检查头显系统设置里的手势追踪开关。");
            yield break;
        }

        IsReady = true;
        ShowStatus(null);
        Debug.Log($"[MRBootstrap] 就绪：平台 {PlatformRuntime.Name}，loader {loader.name}，" +
                  $"手部 provider {MRContext.Instance.Hands.subsystemDescriptor.id}。");
    }

    void Fail(string message)
    {
        IsReady = false;
        Debug.LogError("[MRBootstrap] " + message);
        ShowStatus(message);
    }

    void ShowStatus(string message)
    {
        if (statusLabel != null)
            statusLabel.text = message ?? string.Empty;
        if (statusRoot != null)
            statusRoot.SetActive(message != null);
    }
}
