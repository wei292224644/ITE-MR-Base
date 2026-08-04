using UnityEngine;

namespace Uality.IteTour.Internal
{
    /// <summary>
    /// 把 Tour 触发体积画成线框，便于现场调试锚定与区域范围。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class BoxColliderWireframeDrawer : MonoBehaviour
    {
        private LineRenderer lineRenderer;
        private BoxCollider boxCollider;

        private static readonly int[] LineIndices = new int[]
        {
            0, 1, 1, 2, 2, 3, 3, 0, // bottom
            4, 5, 5, 6, 6, 7, 7, 4, // top
            0, 4, 1, 5, 2, 6, 3, 7  // sides
        };

        void Awake()
        {
            boxCollider = GetComponent<BoxCollider>();
            lineRenderer = GetComponent<LineRenderer>();

            if (lineRenderer == null)
            {
                lineRenderer = gameObject.AddComponent<LineRenderer>();
                InitLineRenderer();
            }
        }

        void InitLineRenderer()
        {
            lineRenderer.positionCount = LineIndices.Length;
            lineRenderer.useWorldSpace = true;
            lineRenderer.loop = false;
            lineRenderer.widthMultiplier = 0.02f;

            // 设置基础材质
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));

            // 随机颜色
            Color randomColor = UnityEngine.Random.ColorHSV(0f, 1f, 0.8f, 1f, 0.9f, 1f); // 明亮饱和的颜色
            lineRenderer.startColor = lineRenderer.endColor = randomColor;
        }

        void Update()
        {
            if (lineRenderer == null || boxCollider == null) return;
            UpdateWireframe();
        }

        void UpdateWireframe()
        {
            Vector3 size = boxCollider.size;
            Vector3 center = boxCollider.center;

            Vector3[] localCorners = new Vector3[8];
            Vector3 halfSize = size * 0.5f;

            // Bottom
            localCorners[0] = center + new Vector3(-halfSize.x, -halfSize.y, -halfSize.z);
            localCorners[1] = center + new Vector3(halfSize.x, -halfSize.y, -halfSize.z);
            localCorners[2] = center + new Vector3(halfSize.x, -halfSize.y, halfSize.z);
            localCorners[3] = center + new Vector3(-halfSize.x, -halfSize.y, halfSize.z);
            // Top
            localCorners[4] = center + new Vector3(-halfSize.x, halfSize.y, -halfSize.z);
            localCorners[5] = center + new Vector3(halfSize.x, halfSize.y, -halfSize.z);
            localCorners[6] = center + new Vector3(halfSize.x, halfSize.y, halfSize.z);
            localCorners[7] = center + new Vector3(-halfSize.x, halfSize.y, halfSize.z);

            // 转世界坐标
            Vector3[] worldCorners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                worldCorners[i] = transform.TransformPoint(localCorners[i]);
            }

            for (int i = 0; i < LineIndices.Length; i++)
            {
                lineRenderer.SetPosition(i, worldCorners[LineIndices[i]]);
            }
        }
    }
}
