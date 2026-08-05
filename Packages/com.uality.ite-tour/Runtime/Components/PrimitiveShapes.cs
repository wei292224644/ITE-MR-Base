using UnityEngine;

namespace Uality.IteTour.Components
{
    /// <summary>几何体元素支持的形状。</summary>
    public enum PrimitiveShape
    {
        Unsupported,
        Box,
        Plane,
        Sphere,
    }

    /// <summary>
    /// 形状解析与尺寸投影。纯函数。
    ///
    /// 源实现把「什么形状」查了两次，且两处对未知类型的口径不同：
    /// <c>GetPrimitiveType()</c> 兜底成 Cube，设置缩放的 switch 则只打警告不设缩放。
    /// 这里收敛成一次 <see cref="Resolve"/> 加两个投影，行为与源实现逐点一致
    /// （未知类型 → Cube 且不改缩放），但决策只有一处（design D19）。
    /// </summary>
    public static class PrimitiveShapes
    {
        public static PrimitiveShape Resolve(string type)
        {
            if (string.IsNullOrEmpty(type))
            {
                return PrimitiveShape.Unsupported;
            }

            switch (type.ToLowerInvariant())
            {
                case "box": return PrimitiveShape.Box;
                case "plane": return PrimitiveShape.Plane;
                case "sphere": return PrimitiveShape.Sphere;
                default: return PrimitiveShape.Unsupported;
            }
        }

        /// <summary>未知形状兜底成 Cube，与源实现的 <c>GetPrimitiveType</c> 一致。</summary>
        public static PrimitiveType ToUnity(PrimitiveShape shape)
        {
            switch (shape)
            {
                case PrimitiveShape.Plane: return PrimitiveType.Plane;
                case PrimitiveShape.Sphere: return PrimitiveType.Sphere;
                default: return PrimitiveType.Cube;
            }
        }

        /// <summary>
        /// 未知形状返回 false，调用方保持默认缩放——与源实现「只打警告不设缩放」一致。
        /// </summary>
        public static bool TryGetLocalScale(PrimitiveShape shape, PrimitiveModelRender data, out Vector3 scale)
        {
            switch (shape)
            {
                case PrimitiveShape.Box:
                    scale = new Vector3(data.boxWidth, data.boxHeight, data.boxDepth);
                    return true;

                // Unity 的 Plane 躺在 XZ 面上，所以「高」映射到 Z
                case PrimitiveShape.Plane:
                    scale = new Vector3(data.planeWidth, 1, data.planeHeight);
                    return true;

                case PrimitiveShape.Sphere:
                    scale = new Vector3(data.sphereRadius, data.sphereRadius, data.sphereRadius);
                    return true;

                default:
                    scale = Vector3.one;
                    return false;
            }
        }
    }
}
