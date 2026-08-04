using UnityEngine;

namespace Uality.IteTour.Internal
{
    /// <summary>
    /// ITE 内容以右手坐标系导出，进入 Unity 前需要转换。
    /// 注意：<c>Tour</c> 与 <c>Entity</c> 的调用组合在源实现中并不一致
    /// （Tour 只做 ConvertToLeftHanded，Entity 还额外 FlipRotY），
    /// 这是迁移前即存在的行为，本处原样保留，由快照测试锁定。
    /// </summary>
    public static class Matrix4x4Extensions
    {
        public static Matrix4x4 ConvertToLeftHanded(this Matrix4x4 matrix)
        {
            var flipZ = Matrix4x4.Scale(new Vector3(1, 1, -1));
            matrix = flipZ * matrix * flipZ;

            Vector3 position = matrix.GetColumn(3);
            Vector3 forward = matrix.GetColumn(2);
            Vector3 up = matrix.GetColumn(1);
            Quaternion rotation = Quaternion.LookRotation(forward, up);

            // 获取缩放
            Vector3 scale = new Vector3(
                matrix.GetColumn(0).magnitude,
                matrix.GetColumn(1).magnitude,
                matrix.GetColumn(2).magnitude
            );

            // 创建新的Matrix4x4
            Matrix4x4 leftHandedMatrix = Matrix4x4.TRS(position, rotation, scale);
            return leftHandedMatrix;
        }

        public static Matrix4x4 FlipRotY(this Matrix4x4 matrix)
        {
            var flipY = Matrix4x4.Rotate(Quaternion.Euler(0, 180f, 0));
            return matrix * flipY;
        }

        public static Matrix4x4 FlipScaleZ(this Matrix4x4 matrix)
        {
            var flipZ = Matrix4x4.Scale(new Vector3(1, 1, -1));
            return flipZ * matrix * flipZ;
        }
    }
}
