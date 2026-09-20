// 桌面验收脚手架：只在 Editor 里存在，不进设备包。
//
// 为什么是 #if UNITY_EDITOR 而不是 Editor-only 的 asmdef：Unity 不允许把 Editor 程序集里的
// MonoBehaviour 挂到 GameObject 上（AddComponent 直接返回 null，场景里的引用变成 Missing）。
// 条件编译能达到同样的目的——类型在播放器构建里根本不存在——而场景与预制体的引用不受影响。
#if UNITY_EDITOR
using UnityEngine;

namespace MRBase.Ite.Host
{
    /// <summary>
    /// 假扫码位姿与 payload：贴在相机前方、正对观察者（design D6）。
    /// </summary>
    public static class EditorFakeScan
    {
        public const float DefaultDistance = 1.5f;

        public static Pose PoseInFront(
            Vector3 cameraPosition,
            Quaternion cameraRotation,
            float distance = DefaultDistance)
        {
            var forward = cameraRotation * Vector3.forward;
            var position = cameraPosition + forward * distance;
            var rotation = Quaternion.LookRotation(-forward, cameraRotation * Vector3.up);
            return new Pose(position, rotation);
        }

        public static string WrapPayload(string tourId) => "******" + tourId + "******";
    }
}
#endif
