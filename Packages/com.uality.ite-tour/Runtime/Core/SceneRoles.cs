using UnityEngine;

namespace Uality.IteTour.Core
{
    /// <summary>场景角色识别。纯函数，不碰 tag、不碰 layer。</summary>
    public static class SceneRoles
    {
        /// <summary>
        /// 给定的 Transform 是不是那台相机（或与它同一条父子链上的碰撞体）。
        ///
        /// 源实现用 <c>CompareTag("ARCamera")</c>。tag 是**工程级全局配置**，包移植到
        /// 别的工程时那边没有这个 tag，判定永远为假、区域触发整体失效，且不报任何错
        /// （design D26）。
        ///
        /// 上下都认：碰撞体既可能挂在相机的子物体上，也可能挂在整个 rig 上。
        /// </summary>
        public static bool IsCamera(Transform camera, Transform candidate)
        {
            if (camera == null || candidate == null)
            {
                return false;
            }

            return candidate == camera || candidate.IsChildOf(camera) || camera.IsChildOf(candidate);
        }
    }
}
