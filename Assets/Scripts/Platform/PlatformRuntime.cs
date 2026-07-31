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
        if (Object.FindFirstObjectByType<ARSession>() == null)
            new GameObject("AR Session").AddComponent<ARSession>();

        var camera = Camera.main;
        if (camera == null)
        {
            Debug.LogError("[PlatformRuntime] 场景里没有 MainCamera，passthrough 未开启。");
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
