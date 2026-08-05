using System.Threading.Tasks;
using UnityEngine;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Components
{
    /// <summary>
    /// 实例化元素预制体并取出它的载体组件。
    ///
    /// 预制体没连线时报明确错误并返回 null——否则要等到某个实体实例化时才 NRE，
    /// 现场离原因很远（design D20）。
    /// </summary>
    internal static class ElementPrefabFactory
    {
        public static T Instantiate<T>(GameObject prefab, Core.Entity entity, string componentName)
            where T : MonoBehaviour
        {
            if (prefab == null)
            {
                Debug.LogError(
                    $"[ITE] {componentName} 的元素预制体未连线，检查 IteTourObject 预制体上的 ElementPrefabs");
                return null;
            }

            return Object.Instantiate(prefab, entity.Root.transform).GetComponent<T>();
        }
    }

    /// <summary>
    /// EMW 模型元素。实例化预制体到实体的 Root 下，再把 glb 装进去。
    /// </summary>
    public class EMWModelRenderUnityComponent : BaseElementComponent
    {
        /// <summary>glb 自带动画时才有。动作组件（PlayAnimationAction）从这里取。</summary>
        public LegacyAnimationController Animation { get; private set; }

        public override async Task Constructor(object data)
        {
            var element = ElementPrefabFactory.Instantiate<EMWModelRenderElement>(
                _iteTourObject.ElementPrefabs?.EmwModelRender, _entity, nameof(EMWModelRender));
            if (element == null)
            {
                return;
            }

            await element.Constructor((EMWModelRender)data);

            Animation = element.AnimationController;
            element.InjectTapEvent(() => OnElementTapped?.Invoke());
        }
    }

    /// <summary>富文本元素。</summary>
    public class RichTextUnityComponent : BaseElementComponent
    {
        public RichTextElement element { get; private set; }

        public override async Task Constructor(object data)
        {
            element = ElementPrefabFactory.Instantiate<RichTextElement>(
                _iteTourObject.ElementPrefabs?.RichText, _entity, nameof(RichText));
            if (element == null)
            {
                return;
            }

            await element.Constructor((RichText)data);
            element.InjectTapEvent(() => OnElementTapped?.Invoke());
        }

        public void ToggleAudio() => element?.ToggleAudio();

        public void PauseAudio() => element?.PauseAudio();

        public void PlayAudio() => element?.PlayAudio();
    }

    /// <summary>视频元素。</summary>
    public class VideoPlaneUnityComponent : BaseElementComponent
    {
        public VideoPlaneElement element { get; private set; }

        public override async Task Constructor(object data)
        {
            element = ElementPrefabFactory.Instantiate<VideoPlaneElement>(
                _iteTourObject.ElementPrefabs?.VideoPlane, _entity, nameof(VideoPlane));
            if (element == null)
            {
                return;
            }

            await element.Constructor((VideoPlane)data);
            element.InjectTapEvent(() => OnElementTapped?.Invoke());
        }

        public void ToggleVideo() => element?.ToggleVideo();

        public void PauseVideo() => element?.PauseVideo();

        public void PlayVideo() => element?.PlayVideo();
    }

    /// <summary>
    /// 基础几何体元素。不用预制体——直接 <c>GameObject.CreatePrimitive</c>。
    ///
    /// 注意它继承 <see cref="BaseComponent"/> 而非 <see cref="BaseElementComponent"/>，
    /// 与源实现一致：因此几何体上挂 <c>TapTrigger</c> 不会生效。已记入 TODO。
    /// </summary>
    public class PrimitiveModelRenderUnityComponent : BaseComponent
    {
        public override Task Constructor(object data)
        {
            var v = (PrimitiveModelRender)data;
            var shape = PrimitiveShapes.Resolve(v.PrimitiveType);

            if (shape == PrimitiveShape.Unsupported)
            {
                Debug.LogWarning($"[ITE] 不支持的 PrimitiveType: {v.PrimitiveType}");
            }

            var primitive = GameObject.CreatePrimitive(PrimitiveShapes.ToUnity(shape));
            primitive.transform.SetParent(transform, false);
            primitive.transform.localPosition = Vector3.zero;

            // 未知形状不设缩放，保持 (1,1,1)——与源实现一致（design D19）
            if (PrimitiveShapes.TryGetLocalScale(shape, v, out var scale))
            {
                primitive.transform.localScale = scale;
            }

            return Task.CompletedTask;
        }
    }
}
