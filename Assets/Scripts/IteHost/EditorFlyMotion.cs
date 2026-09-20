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
    /// 编辑器走位的纯变换：WASD 平移 + 鼠标视角，无重力/碰撞/蹲跳。
    /// </summary>
    public static class EditorFlyMotion
    {
        public static void Apply(
            Transform transform,
            Vector3 move,
            Vector2 lookDelta,
            float moveSpeed,
            float lookSensitivity,
            float deltaTime,
            ref float pitch)
        {
            if (transform == null)
            {
                return;
            }

            pitch = Mathf.Clamp(pitch - lookDelta.y * lookSensitivity, -89f, 89f);
            var yaw = transform.eulerAngles.y + lookDelta.x * lookSensitivity;
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            var direction = transform.right * move.x + transform.forward * move.z;
            if (direction.sqrMagnitude > 1e-8f)
            {
                direction.Normalize();
            }

            transform.position += direction * (moveSpeed * deltaTime);
        }
    }
}
#endif
