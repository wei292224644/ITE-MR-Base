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
