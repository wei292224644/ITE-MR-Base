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
        // AfterSceneLoad 只对启动场景触发一次，后续场景要靠 sceneLoaded。
        EnablePassthrough();
        SceneManager.sceneLoaded += (_, __) => EnablePassthrough();
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
        Debug.Log("[PlatformRuntime] PICO video see-through 已开启。");
#elif MRBASE_PICO
        Debug.LogError("[PlatformRuntime] 构建意图为 PICO，但未安装 com.unity.xr.picoxr，passthrough 未开启。");
#else
        Debug.LogWarning("[PlatformRuntime] 未设置 MRBASE_QUEST / MRBASE_PICO，跳过 passthrough 装配。");
#endif
    }
}
