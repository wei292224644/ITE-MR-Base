using UnityEngine;
#if MRBASE_PICO && MRBASE_HAS_PICO_SDK
using Unity.XR.PXR;
#endif

/// <summary>
/// PICO 侧点亮 passthrough。
///
/// 两端的开法不对称，这是本组件存在的唯一理由：
/// - Quest 走 AR Foundation —— 场景里的 <c>ARCameraManager</c> 组件本身就是开关，无需代码。
/// - PICO 在 PXR_Loader 模式下与 AR Foundation 无关。<c>PXR_Manager.EnableVideoSeeThrough</c>
///   是个静态属性，默认 false；<c>PXR_ProjectSetting.videoSeeThrough</c> 只决定 manifest 声明,
///   不会自动打开它。没有这一行,PICO 上就是纯黑背景。
///
/// ponytail: 组 9 建起 MRBootstrap 后,这段应并入平台装配层,本文件删除。
/// </summary>
public class PicoPassthrough : MonoBehaviour
{
    private void Start()
    {
#if MRBASE_PICO && MRBASE_HAS_PICO_SDK
        PXR_Manager.EnableVideoSeeThrough = true;
        Debug.Log("[PicoPassthrough] 已开启 video see-through。");
#endif
    }
}
