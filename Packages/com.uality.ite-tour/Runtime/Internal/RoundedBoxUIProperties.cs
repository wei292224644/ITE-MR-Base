using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Uality.IteTour.Internal
{
    /// <summary>
    /// 把每角圆角半径与矩形尺寸写进顶点属性，供 <c>Uality/IteTour/RoundedBoxUI</c> 着色器做 SDF 裁边。
    ///
    /// 原实现用的是 Meta SDK 示例目录里的同名组件（<c>com.meta.xr.sdk.interaction</c> 的
    /// <c>Runtime/Sample/</c>）。那条路走不通：Meta SDK 是 Quest 专用，本包依赖它就等于放弃 PICO；
    /// 该包也不在本工程里；且示例代码受 Oculus SDK 许可约束，不该进可移植包。
    /// 这里按相同的对外用法（<see cref="borderRadius"/> 一个 Vector4）重新实现。
    ///
    /// 顶点通道约定（与配套着色器一一对应）：
    ///   uv1 = float4(左上, 右上, 右下, 左下) 半径，单位与 RectTransform 一致
    ///   uv2 = float4(宽, 高, 矩形内归一化 x, 矩形内归一化 y)
    /// 归一化坐标单独存一份，是因为 uv0 在图集化 Sprite 上并不是矩形的 0..1。
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    [DisallowMultipleComponent]
    public class RoundedBoxUIProperties : UIBehaviour, IMeshModifier
    {
        [Tooltip("四角半径：x=左上 y=右上 z=右下 w=左下")]
        public Vector4 borderRadius;

        private Graphic _graphic;

        private Graphic Graphic => _graphic != null ? _graphic : (_graphic = GetComponent<Graphic>());

        protected override void OnEnable()
        {
            base.OnEnable();
            Graphic?.SetVerticesDirty();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Graphic?.SetVerticesDirty();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            Graphic?.SetVerticesDirty();
        }
#endif

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            Graphic?.SetVerticesDirty();
        }

        /// <summary>已废弃的旧接口，Unity 仍要求实现。</summary>
        public void ModifyMesh(Mesh mesh)
        {
            // 走 VertexHelper 重载，这里不需要做任何事。
        }

        public void ModifyMesh(VertexHelper verts)
        {
            if (!IsActive())
            {
                return;
            }

            Rect rect = ((RectTransform)transform).rect;
            var size = new Vector2(rect.width, rect.height);

            UIVertex vertex = default;
            for (int i = 0; i < verts.currentVertCount; i++)
            {
                verts.PopulateUIVertex(ref vertex, i);

                float normalizedX = Mathf.InverseLerp(rect.xMin, rect.xMax, vertex.position.x);
                float normalizedY = Mathf.InverseLerp(rect.yMin, rect.yMax, vertex.position.y);

                vertex.uv1 = borderRadius;
                vertex.uv2 = new Vector4(size.x, size.y, normalizedX, normalizedY);

                verts.SetUIVertex(vertex, i);
            }
        }
    }
}
