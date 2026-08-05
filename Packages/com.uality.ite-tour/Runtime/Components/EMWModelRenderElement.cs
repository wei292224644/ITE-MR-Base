using System;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;
using Uality.IteTour.Core;
using Uality.IteTour.Data.Assets;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Components
{
    /// <summary>
    /// EMW 模型元素的载体（挂在 prefab 上）：把 glb 实例化进来、接上动画与配音、
    /// 按内容尺寸调整碰撞体。
    /// </summary>
    public class EMWModelRenderElement : MonoBehaviour
    {
        private Action _onTap;
        private IteTourObject _iteTourObject;

        /// <summary>
        /// glb 自带动画时才有。源实现用 <c>InjectAnimation(out ...)</c> 这个只写不读的
        /// out 参数当取值器，这里改成属性——同一件事，少一层绕。
        /// </summary>
        public LegacyAnimationController AnimationController { get; private set; }

        private void Awake()
        {
            _iteTourObject = GetComponentInParent<IteTourObject>();
        }

        public async Task Constructor(EMWModelRender data)
        {
            var asset = _iteTourObject.GetAsset(data.Asset) as EMWModelAsset;
            if (asset?.GltfImport == null)
            {
                Debug.LogWarning("[ITE] EMW 模型资源缺失或未加载: " + data.Asset);
                return;
            }

            // GLTFast.ComponentType 必须写全：本命名空间下另有一个组件类型键 ComponentType（D9）
            var settings = new InstantiationSettings
            {
                Mask = ~GLTFast.ComponentType.Camera & ~GLTFast.ComponentType.Light,
            };

            var instantiator = new GameObjectInstantiator(asset.GltfImport, transform, null, settings);
            if (!await asset.GltfImport.InstantiateMainSceneAsync(instantiator))
            {
                return;
            }

            SetUpAnimation(asset);
            FitColliderToContent();
        }

        private void SetUpAnimation(EMWModelAsset asset)
        {
            // gltfast 只在 glb 自带动画时挂 Animation
            var animation = GetComponent<Animation>();
            if (animation == null)
            {
                return;
            }

            animation.playAutomatically = false;
            animation.Stop();

            AnimationController = gameObject.AddComponent<LegacyAnimationController>();
            var audioController = gameObject.AddComponent<AnimationAudioController>();

            audioController.AnimationAudioClips =
                AnimationAudioMap.Build(asset.Json, asset.GltfImport.GetAnimationClips());
        }

        /// <summary>
        /// 让碰撞体包住模型实际内容。
        ///
        /// 先脱离父节点再算：包围盒按本地空间求，挂在父节点下时父级的缩放会算进去。
        /// 算完立刻挂回原位（<c>worldPositionStays: false</c>，保持局部变换）。
        /// </summary>
        private void FitColliderToContent()
        {
            var collider = GetComponent<BoxCollider>();
            if (collider == null)
            {
                return;
            }

            var parent = transform.parent;
            transform.SetParent(null, false);

            var bounds = HierarchyBoundsCalculator.CalculateLocalBounds(gameObject);
            collider.center = bounds.center;
            collider.size = bounds.size;

            transform.SetParent(parent, false);
        }

        /// <summary>
        /// 注意：<c>_onTap</c> 存下来了，但本类**没有任何地方触发它**——模型上没有
        /// Button，也没有射线命中回调。所以挂在 EMW 模型上的 <c>TapTrigger</c> 实际不会
        /// 生效。这是迁移前既有的缺陷，原样保留，已记入 TODO。
        /// </summary>
        public void InjectTapEvent(Action onTap) => _onTap = onTap;
    }
}
