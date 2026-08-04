using UnityEngine;

namespace Uality.IteTour.Internal
{
    public static class HierarchyBoundsCalculator
    {
        /// <summary>
        /// 计算 Hierarchy 中所有 Renderer 组成的 Bounds（在 root 的本地空间）
        /// </summary>
        public static Bounds CalculateLocalBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.zero);

            var worldToLocal = root.transform.worldToLocalMatrix;

            Bounds bounds = TransformBounds(worldToLocal, renderers[0].bounds);
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(TransformBounds(worldToLocal, renderers[i].bounds));
            }

            return bounds;
        }

        /// <summary>
        /// 计算 Hierarchy 中所有 Renderer 组成的 Bounds（在世界空间）
        /// </summary>
        public static Bounds CalculateWorldBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.zero);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        /// <summary>
        /// 将世界空间 Bounds 转换为局部空间 Bounds（通过转换 8 个角点）
        /// </summary>
        private static Bounds TransformBounds(Matrix4x4 toLocal, Bounds worldBounds)
        {
            Vector3 center = worldBounds.center;
            Vector3 extents = worldBounds.extents;

            Vector3[] corners = new Vector3[8]
            {
                center + new Vector3( extents.x,  extents.y,  extents.z),
                center + new Vector3( extents.x,  extents.y, -extents.z),
                center + new Vector3( extents.x, -extents.y,  extents.z),
                center + new Vector3( extents.x, -extents.y, -extents.z),
                center + new Vector3(-extents.x,  extents.y,  extents.z),
                center + new Vector3(-extents.x,  extents.y, -extents.z),
                center + new Vector3(-extents.x, -extents.y,  extents.z),
                center + new Vector3(-extents.x, -extents.y, -extents.z),
            };

            Bounds localBounds = new Bounds(toLocal.MultiplyPoint3x4(corners[0]), Vector3.zero);
            for (int i = 1; i < 8; i++)
            {
                localBounds.Encapsulate(toLocal.MultiplyPoint3x4(corners[i]));
            }

            return localBounds;
        }

        /// <summary>
        /// Gizmo 调试用：在 Scene 中绘制 Bounds（local 空间）
        /// </summary>
        public static void DrawLocalBoundsGizmo(GameObject root, Color color)
        {
#if UNITY_EDITOR
            Bounds localBounds = CalculateLocalBounds(root);
            Gizmos.color = color;
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = root.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(localBounds.center, localBounds.size);
            Gizmos.matrix = oldMatrix;
#endif
        }
    }
}
