using UnityEngine;
using UnityEngine.SceneManagement;
#if MRBASE_QUEST
using UnityEngine.XR.ARFoundation;
#endif
#if MRBASE_PICO && MRBASE_HAS_PICO_SDK
using Unity.XR.PXR;
#endif

/// <summary>
/// 两端 passthrough 的唯一开关点。
///
/// 存在的理由：两端的开法不对称，而这个不对称原本落在场景里 ——
/// - Quest 走 AR Foundation：场景中的 <c>ARCameraManager</c> 组件本身就是开关。
/// - PICO 在 PXR_Loader 模式下与 AR Foundation 无关：<c>PXR_Manager.EnableVideoSeeThrough</c>
///   是静态属性，默认 false（<c>PXR_ProjectSetting.videoSeeThrough</c> 只决定 manifest 声明，
///   不会自动打开它）。少这一行，PICO 上就是纯黑背景。
///
/// 场景里的差异 `#if` 管不住：每新增一个场景就要重摆一套平台专属对象，成本随场景数线性涨。
/// 挪进代码后场景不带任何平台专属对象，新场景零成本。
///
/// 相机的 clearFlags = SolidColor、背景 alpha = 0 两端都要，是通用设置，仍留在场景里。
/// </summary>
public static class PlatformRuntime
{
    /// <summary>
    /// 本次构建的目标平台，供别处判断而不必自己写 <c>#if MRBASE_*</c>。
    /// 构建意图未配置时为 null —— 调用方据此报错，而不是默默按某一端跑。
    /// </summary>
    public static string Name =>
#if MRBASE_QUEST
        "Quest";
#elif MRBASE_PICO && MRBASE_HAS_PICO_SDK
        "PICO";
#elif MRBASE_PICO
        null;   // 构建意图是 PICO 但 com.unity.xr.picoxr 没装,等同于未配置
#else
        null;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SilenceMetaGlobalHookOnPico();

        // AfterSceneLoad 只对启动场景触发一次，后续场景要靠 sceneLoaded。
        EnablePassthrough();
        SceneManager.sceneLoaded += (_, __) => EnablePassthrough();
    }

    /// <summary>
    /// PICO 包里停掉 MRUK 的进程级 hook 对象。
    ///
    /// MRUK 包自带 <c>MRUKGlobalContext.CreateInstance</c>（<c>[RuntimeInitializeOnLoadMethod</c>
    /// <c>(BeforeSceneLoad)]</c>），无条件建一个 DontDestroyOnLoad 对象，每帧调
    /// <c>MRUK.UpdateGlobalContext()</c> → <c>OVRPlugin</c>。而 PICO 包按 <c>BuildScript</c> 的
    /// <c>excludePluginRoot</c> 剔掉了 Meta 的原生插件（两端 <c>.so</c> 重名，Gradle 会失败），
    /// 托管代码却照样进包——于是每帧一次 <c>DllNotFoundException</c>。
    ///
    /// 2026-09-17 PICO 实测：30 秒 14655 行日志里 8712 行是这一条，logcat 环形缓冲被冲爆，
    /// 真机上再也读不到自己的日志；每帧抛异常本身也不便宜。
    ///
    /// 停用而不销毁：它的 <c>OnDestroy</c> 会「好心」重建自己并报一条 LogError，销毁等于
    /// 换一种刷屏。停用后 <c>Update</c> 不再跑，<c>OnDestroy</c> 不触发。
    ///
    /// 按名字找而不按类型：<c>MRUKGlobalContext</c> 是 Meta 程序集的 <c>internal</c> 类型，
    /// 宿主程序集拿不到它。名字来自 <c>nameof</c>，与类名同生同死。
    /// </summary>
    static void SilenceMetaGlobalHookOnPico()
    {
#if MRBASE_PICO
        // hideFlags = HideInHierarchy，只影响 Inspector 显示，Find 仍然找得到。
        var hook = GameObject.Find("MRUKGlobalContext");
        if (hook == null)
        {
            return;
        }

        hook.SetActive(false);
        Debug.Log("[PlatformRuntime] PICO 包无 Meta 原生插件，已停用 MRUKGlobalContext（否则每帧抛 OVRPlugin 未找到）。");
#endif
    }

    /// <summary>幂等：重复调用不会重复装配。</summary>
    public static void EnablePassthrough()
    {
#if MRBASE_QUEST
        // AR Session 必须活过场景切换。
        //
        // 它原本建在「当前激活场景」里，而核心装配（含挂着 ARCameraManager 的相机）
        // 进了 DontDestroyOnLoad —— 于是每次 LoadSceneMode.Single 切场景，session 随旧场景
        // 被销毁，相机管理器却还在。相机子系统随之停掉，passthrough 关闭，背景变成不透明黑。
        // 症状很有欺骗性：日志照常打「已装配」（组件确实加上了），XR、手部追踪、帧率全都正常。
        if (Object.FindFirstObjectByType<ARSession>() == null)
        {
            var session = new GameObject("AR Session");
            session.AddComponent<ARSession>();
            // 与 sceneLoaded 钩子同一个道理：这是进程级设施，不属于任何一个场景。
            Object.DontDestroyOnLoad(session);
        }

        var camera = Camera.main;
        if (camera == null)
        {
            // 多场景组合下，内容场景先于核心装配加载时会走到这里。sceneLoaded 会再触发一次，
            // 那时相机已就位 —— 所以这是可恢复状态，不是错误，打成 LogError 会淹掉真错误。
            Debug.LogWarning("[PlatformRuntime] 暂无 MainCamera，passthrough 推迟到下次场景加载后装配。");
            return;
        }

        if (camera.GetComponent<ARCameraManager>() == null)
            camera.gameObject.AddComponent<ARCameraManager>();

        Debug.Log("[PlatformRuntime] Quest passthrough 已装配。");
#elif MRBASE_PICO && MRBASE_HAS_PICO_SDK
        PXR_Manager.EnableVideoSeeThrough = true;
        // PXR_Manager 组件本身没有放进场景（同一个"零场景专属对象"理由），它 Awake() 里下发的
        // usePremultipliedAlpha 配置从没生效，合成器停在自己的启动默认值上。世界空间半透明物体
        // （如控制器射线的渐隐渐变）用的是 Unity 标准直通 alpha 混合，与合成器默认值不一致时，
        // 在 passthrough 底上会被合成成近乎全透明——UI 走 Overlay 不经过这条路径，不受影响。
        // 真机验证过 false（跟渲染侧假设的直通 alpha 对齐）不解决问题，说明合成器默认反而是
        // 直通、Unity 侧对 XR 交换链做了预乘转换——改传 true 让合成器按预乘处理。
        PXR_Plugin.Render.UPxr_EnablePremultipliedAlpha(true);
        Debug.Log("[PlatformRuntime] PICO video see-through 已开启。");
#elif MRBASE_PICO
        Debug.LogError("[PlatformRuntime] 构建意图为 PICO，但未安装 com.unity.xr.picoxr，passthrough 未开启。");
#else
        Debug.LogWarning("[PlatformRuntime] 未设置 MRBASE_QUEST / MRBASE_PICO，跳过 passthrough 装配。");
#endif
    }

    /// <summary>
    /// 系统重定位（recenter）发生了。两端的事件源不同，订阅方不必知道是哪一端。
    ///
    /// - PICO：<c>PXR_Plugin.System.RecenterSuccess</c>。
    /// - 其余（Quest / OpenXR）：<c>XRInputSubsystem.trackingOriginUpdated</c>，
    ///   并额外关掉「世界原点跟随系统重定位」——关掉之后这条基本不再触发，语义仍成立。
    ///
    /// 放在这里而不是订阅方自己 <c>#if</c>：平台分叉只在这一个文件里（见类注释），
    /// 加第三个平台时要改的是本文件，不是每一个关心重定位的组件。
    /// </summary>
    public static event System.Action Recentered;

    private static bool _recenterHooked;

    /// <summary>幂等：重复调用只装一次。</summary>
    public static void HookRecenter()
    {
        if (_recenterHooked)
        {
            return;
        }

#if MRBASE_PICO && MRBASE_HAS_PICO_SDK
        PXR_Plugin.System.RecenterSuccess += RaiseRecentered;
        _recenterHooked = true;
#else
        var subsystem = GetInputSubsystem();
        if (subsystem != null)
        {
            subsystem.trackingOriginUpdated += HandleTrackingOriginUpdated;
            _recenterHooked = true;
        }

        UnityEngine.XR.OpenXR.OpenXRSettings.SetAllowRecentering(false);
#endif
    }

    public static void UnhookRecenter()
    {
        if (!_recenterHooked)
        {
            return;
        }

#if MRBASE_PICO && MRBASE_HAS_PICO_SDK
        PXR_Plugin.System.RecenterSuccess -= RaiseRecentered;
#else
        var subsystem = GetInputSubsystem();
        if (subsystem != null)
        {
            subsystem.trackingOriginUpdated -= HandleTrackingOriginUpdated;
        }
#endif
        _recenterHooked = false;
    }

    private static void HandleTrackingOriginUpdated(UnityEngine.XR.XRInputSubsystem _) => RaiseRecentered();

    private static void RaiseRecentered() => Recentered?.Invoke();

    private static UnityEngine.XR.XRInputSubsystem GetInputSubsystem()
    {
        var subsystems = new System.Collections.Generic.List<UnityEngine.XR.XRInputSubsystem>();
        SubsystemManager.GetSubsystems(subsystems);
        return subsystems.Count > 0 ? subsystems[0] : null;
    }
}
