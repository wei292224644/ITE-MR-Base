using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Uality.IteTour.Components;
using Uality.IteTour.Data;
using Uality.IteTour.Data.Assets;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 一个 Tour 在场景中的载体：持有描述与资源、按需构建/销毁内容、维护二次锚定许可。
    ///
    /// 与源实现的差异：
    /// - 删掉了 <c>CanAnchor</c> 属性。它在源工程里**无人调用**，却是 <c>IteTourObject</c>
    ///   对 <c>IteSpaceManager.Instance</c> 单例的唯一依赖。删掉之后 Tour 对象不再反向
    ///   依赖管理器，「谁是当前 Tour」只由 <see cref="TourScanPolicy"/> 判定。
    /// - 锚点与偏移 Transform 由 <see cref="BindAnchors"/> 注入，不再 <c>FindGameObjectWithTag</c>。
    /// - 触发体积的进出回调不在这里判 tag，见任务 5.5。
    /// </summary>
    public class IteTourObject : MonoBehaviour
    {
        [SerializeField] private GameObject _volumeObject;
        [SerializeField] private GameObject _mainGroupObject;

        [SerializeField] private IteTourElementPrefabs _elementPrefabs;

        /// <summary>元素组件要实例化的预制体。运行时挂上去的组件拿不到序列化引用，从这里要（D20）。</summary>
        public IteTourElementPrefabs ElementPrefabs => _elementPrefabs;

        private Transform _anchorObject;
        private Transform _tourOffsetObject;
        private Transform _camera;

        /// <summary>
        /// 相机进出本 Tour 的触发体积：(tourId, 进/出, 碰撞体名字)。由编排层转给 <see cref="TourDirector"/>。
        /// 碰撞体名字只用于日志：相机侧不止一个碰撞体（Main Camera 的球、XR Origin 的 CharacterController），
        /// 真机上靠它分辨是谁在进出（ite-current-tour §8）。
        /// </summary>
        public Action<string, VolumeTransition, string> OnCameraVolumeTransition;

        /// <summary>触发体积被停用。Unity 停用碰撞体时不发 OnTriggerExit，计数要另行清零（ite-current-tour D11）。</summary>
        public Action<string> OnVolumeCleared;

        private TourSceneLifecycle _scene;
        private Task _building;
        private IteSpaceScene.Tour.DisplayType _displayType;
        private string _tourId;
        private Data.IteTour _tour;

        /// <summary>内容构建完成。触发器组件（LoadTrigger）据此派发首屏动作。</summary>
        public Action OnTourSceneLoaded;

        /// <summary>本 Tour 当前是否还允许一次二次锚定。</summary>
        private bool _canAnchor;

        public string TourId => _tourId;

        public IteSpaceScene.Tour.DisplayType DisplayType => _displayType;

        public Data.IteTour Tour => _tour;

        /// <summary>内容树已建好。再次 <see cref="Enable"/> 不会重建，也不再派发 <see cref="OnTourSceneLoaded"/>。</summary>
        public bool IsSceneReady => _scene.IsReady;

        /// <summary>
        /// 注入锚点、偏移与相机 Transform。装配时调用一次。
        ///
        /// 相机用于识别谁进出了触发体积——源实现用 tag <c>ARCamera</c>，那是工程级
        /// 全局配置，包移植后会静默失效（design D26）。
        /// </summary>
        public void BindScene(Transform anchorObject, Transform tourOffsetObject, Transform camera)
        {
            _anchorObject = anchorObject;
            _tourOffsetObject = tourOffsetObject;
            _camera = camera;
        }

        /// <summary>由触发体积上的 <see cref="TourVolumeTrigger"/> 调用。</summary>
        internal void NotifyVolumeTransition(Collider other, VolumeTransition transition)
        {
            if (_scene.IsDestroyed
                || !TourAssembly.AllowsTriggerVolume(_displayType)
                || !SceneRoles.IsCamera(_camera, other != null ? other.transform : null))
            {
                return;
            }

            OnCameraVolumeTransition?.Invoke(_tourId, transition, other.name);
        }

        public async Task CreateTourObject(IteSpaceScene.Tour tour, Data.IteTour tourData)
        {
            if (_scene.IsDestroyed) return;

            _tourId = tour.tourID;
            _tour = tourData;
            name = tour.tourID;

            var label = GetComponentInChildren<TextMesh>();
            if (label != null)
            {
                label.text = tour.tourID;
            }

            transform.SetLocalPositionAndRotation(tour.Matrix4X4.GetPosition(), tour.Matrix4X4.rotation);

            // 先激活再改碰撞体：SetActive(false) 的物体上 AddComponent 不会走 Awake
            SetVolumeObjectActive(true);
            var boxCollider = _volumeObject.GetComponent<BoxCollider>();
            _volumeObject.transform.SetLocalPositionAndRotation(
                tour.triggerVolume.Matrix4X4.GetPosition(), tour.triggerVolume.Matrix4X4.rotation);
            _volumeObject.AddComponent<BoxColliderWireframeDrawer>();
            SetVolumeObjectActive(false);

            // 必须是 trigger（design D31）：实心盒子会挡住宿主的物理射线（XRI 远距射线），
            // 站在体积外指向里面的图片/视频时射线停在盒壁上。源实现用的 Meta ISDK 射线不走物理，
            // 所以当时实心也没事。区域触发不受影响——宿主相机侧带 trigger 碰撞体 + kinematic 刚体。
            boxCollider.isTrigger = true;

            // 源实现在此处取半值。触发体积因此只有描述尺寸的一半，是既有行为，原样保留。
            boxCollider.size = new Vector3(
                tour.triggerVolume.width / 2, tour.triggerVolume.height / 2, tour.triggerVolume.depth / 2);

            await LoadAssets(tourData.Assets);
            ChangeDisplayType(tour.displayType);

            // 装配只准备资源、不建树，所有展示类型一样。什么时候建由编排层决定：普通 Tour 在被激活时，
            // alwaysDisplayed 在进入已定位时（ite-current-tour D13）。design D29 曾让 alwaysDisplayed
            // 装配即建树（源实现对它既不 Enable 也不 Disable，内容永远是空的），但那样首屏效果会在
            // 锚定前、看不见的时候就放完。
            Disable();
        }

        public void ChangeTourObjectTransform(Vector3 t, Quaternion r)
        {
            if (_anchorObject == null || _tourOffsetObject == null)
            {
                Debug.LogError("[ITE] BindAnchors 未调用，无法锚定 Tour: " + _tourId);
                return;
            }

            var tourLocal = transform.parent.localToWorldMatrix.inverse * transform.localToWorldMatrix;

            // 缩放会被 SetLocalPositionAndRotation 丢掉（既有行为）。丢掉本身不改，
            // 但不能一声不吭：内容整体大小不对时，没有日志就只能靠目测猜。
            if (!TourAnchoring.HasUnitScale(tourLocal))
            {
                Debug.LogWarning(
                    "[ITE] Tour " + _tourId + " 的场景描述带了非单位缩放 " + tourLocal.lossyScale +
                    "，锚定只写位置与旋转，缩放会被丢掉。", this);
            }

            var rootLocal = TourAnchoring.TourRootLocal(tourLocal);

            _tourOffsetObject.SetLocalPositionAndRotation(rootLocal.position, rootLocal.rotation);
            _anchorObject.SetLocalPositionAndRotation(t, r);
        }

        public void SetVolumeObjectActive(bool isActive)
        {
            if (_scene.IsDestroyed) return;

            // alwaysDisplayed 不参与区域触发。决策收在 Tour 自己，避免
            // SetAllVolumesActive 把 ChangeDisplayType 刚关掉的体积重新打开。
            bool active = isActive && TourAssembly.AllowsTriggerVolume(_displayType);
            bool wasActive = _volumeObject.activeSelf;

            _volumeObject.SetActive(active);

            // Unity 停用碰撞体时不发 OnTriggerExit：人站在里面时，这个 Tour 会永远留在区域队列里
            // （ite-current-tour D11）。重新启用时，Unity 会对仍在重叠的碰撞体补发 Enter。
            if (wasActive && !active)
            {
                OnVolumeCleared?.Invoke(_tourId);
            }
        }

        public async Task Enable()
        {
            if (_scene.IsDestroyed) return;

            gameObject.SetActive(true);

            if (_scene.IsReady)
            {
                return;
            }

            if (_building != null)
            {
                await _building;
                if (_scene.IsDestroyed) return;
                if (_scene.IsReady) return;
            }

            if (!_scene.TryBeginBuild(out var generation))
            {
                if (_building != null)
                {
                    await _building;
                }

                return;
            }

            var build = BuildSceneAsync(generation);
            _building = build;
            try
            {
                await build;
            }
            finally
            {
                if (ReferenceEquals(_building, build))
                {
                    _building = null;
                }
            }
        }

        public void Disable()
        {
            TearDownScene();
        }

        public void Destroy()
        {
            TearDownScene();
            _scene.Destroy();
            ReleaseAssets();
        }

        /// <summary>
        /// 释放本 Tour 运行时加载出来的资源。
        ///
        /// 这批资源不属于任何场景：<c>GltfImport</c> 自己持有导入产生的 Mesh/Texture/
        /// Material/AnimationClip，Sprite 与 AudioClip 是 UnityWebRequest 当场造出来的。
        /// 销毁 GameObject 只断引用，对象本身留在内存里，连卸场景都不回收——换一次 tour
        /// 就涨一次，直到 OOM。
        ///
        /// 只在 <see cref="Destroy"/> 走这条：<see cref="Disable"/> 之后还可能 Enable
        /// 回来，内容树要靠这批资源重建。
        /// </summary>
        private void ReleaseAssets()
        {
            if (_tour?.Assets == null)
            {
                return;
            }

            foreach (var asset in _tour.Assets.Values)
            {
                switch (asset)
                {
                    case EMWModelAsset model:
                        model.GltfImport?.Dispose();
                        model.GltfImport = null;
                        if (model.Json?.audio != null)
                        {
                            foreach (var audio in model.Json.audio)
                            {
                                DestroyAsset(audio.audioClip);
                                audio.audioClip = null;
                            }
                        }

                        break;

                    case RichTextAsset richText:
                        if (richText.spriteInstance != null)
                        {
                            // Sprite 与它的 Texture2D 是两个对象，只销毁 Sprite 会漏掉大头。
                            DestroyAsset(richText.spriteInstance.texture);
                            DestroyAsset(richText.spriteInstance);
                            richText.spriteInstance = null;
                        }

                        DestroyAsset(richText.audioInstance);
                        richText.audioInstance = null;
                        break;
                }
            }
        }

        private static void DestroyAsset(UnityEngine.Object asset)
        {
            if (asset == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(asset);
            }
            else
            {
                DestroyImmediate(asset);
            }
        }

        private void TearDownScene()
        {
            if (_scene.IsDestroyed) return;

            _scene.TearDown();
            DestroyTourScene();

            // 源实现此处先 ResetSecondAnchor()（置 true）再置 false，前者是死调用（D14）。
            // 连同只有它一个调用方的 ResetSecondAnchor 一并删除。
            _canAnchor = false;
        }

        /// <summary>本 Tour 当前是否还允许一次二次锚定。</summary>
        public bool CanSecondAnchor()
            => _displayType == IteSpaceScene.Tour.DisplayType.regionalTrigger && _canAnchor;

        public void SecondAnchored() => _canAnchor = false;

        public Asset GetAsset(string assetId)
        {
            if (_tour != null && _tour.Assets != null && _tour.Assets.TryGetValue(assetId, out var asset))
            {
                return asset;
            }

            return null;
        }

        private void ChangeDisplayType(IteSpaceScene.Tour.DisplayType displayType)
        {
            _displayType = displayType;

            if (!TourAssembly.AllowsTriggerVolume(displayType))
            {
                SetVolumeObjectActive(false);
            }
        }

        /// <summary>
        /// 把 Tour 描述里的资源引用变成可用的运行时对象（glb / 音频 / 位图 / 视频路径），
        /// 结果写回 <see cref="Asset"/> 子类上的运行时字段。
        ///
        /// 缺资源不抛异常：<see cref="ContentAssetLoader"/> 静默返回 null，对应字段留空。
        /// </summary>
        private async Task LoadAssets(Dictionary<string, Asset> assets)
        {
            if (_scene.IsDestroyed || assets == null) return;

            foreach (var pair in assets)
            {
                switch (pair.Value.Type)
                {
                    case AssetType.EMWModel:
                        await LoadEmwModelAsset(pair.Value as EMWModelAsset);
                        break;

                    case AssetType.RichText:
                        await LoadRichTextAsset(pair.Value as RichTextAsset);
                        break;

                    case AssetType.Video:
                        var video = pair.Value as VideoAsset;
                        video.videoAbsoluteUrl = ContentAssetLoader.Resolve(
                            TourAssetPaths.Video(_tourId, video.Id));
                        break;
                }
            }
        }

        private async Task LoadEmwModelAsset(EMWModelAsset model)
        {
            var json = await ContentAssetLoader.LoadJsonAsync<EMWModelAsset.PublishJson>(
                TourAssetPaths.EmwModelPublishJson(_tourId, model.Id, model.Version));

            model.Json = json;
            if (json == null)
            {
                return;
            }

            model.GltfImport = await ContentAssetLoader.LoadGlbAsync(
                TourAssetPaths.EmwModelGlb(_tourId, model.Id, model.Version, json.id));

            if (json.audio == null)
            {
                return;
            }

            foreach (var audioItem in json.audio)
            {
                audioItem.audioClip = await ContentAssetLoader.LoadAudioClipAsync(
                    TourAssetPaths.EmwModelAudio(_tourId, model.Id, audioItem.path));
            }
        }

        private async Task LoadRichTextAsset(RichTextAsset richText)
        {
            richText.audioInstance = await ContentAssetLoader.LoadAudioClipAsync(
                TourAssetPaths.RichTextAudio(_tourId, richText.Id));

            richText.spriteInstance = await ContentAssetLoader.LoadSpriteAsync(
                TourAssetPaths.RichTextImage(_tourId, richText.Id));
        }

        private async Task BuildSceneAsync(int generation)
        {
            try
            {
                await CreateTourScene(generation);
                if (!_scene.TryMarkReady(generation))
                {
                    return;
                }

                _canAnchor = true;
                OnTourSceneLoaded?.Invoke();
            }
            catch
            {
                _scene.Abandon(generation);
                throw;
            }
        }

        /// <summary>
        /// 构建 Tour 的第一个场景：逐实体建 GameObject，按 <see cref="ComponentLoadOrder"/>
        /// 挂组件并依次 await 各自的 <c>Constructor</c>。
        /// </summary>
        private async Task CreateTourScene(int generation)
        {
            if (!_scene.IsCurrentBuild(generation) || !TryGetFirstScene(out var scene))
            {
                return;
            }

            foreach (var entityData in scene.Entities.Values)
            {
                if (!_scene.IsCurrentBuild(generation))
                {
                    return;
                }

                await CreateEntity(entityData, generation);
            }
        }

        private bool TryGetFirstScene(out Scene scene)
        {
            scene = null;

            if (_tour?.ScenesOrder == null || _tour.ScenesOrder.Length == 0)
            {
                Debug.LogError("[ITE] Tour 里没有任何场景: " + _tourId);
                return false;
            }

            var firstSceneId = _tour.ScenesOrder[0];
            if (_tour.Scenes == null || !_tour.Scenes.TryGetValue(firstSceneId, out scene))
            {
                Debug.LogError($"[ITE] 找不到场景 {firstSceneId}（tour {_tourId}）");
                return false;
            }

            return true;
        }

        private async Task CreateEntity(Data.Entity entityData, int generation)
        {
            var go = new GameObject(entityData.Id, typeof(EventEmitter), typeof(Entity));
            go.transform.SetParent(_mainGroupObject.transform, false);

            go.transform.SetLocalPositionAndRotation(
                entityData.Matrix4X4.GetPosition(), entityData.Matrix4X4.rotation);
            go.transform.localScale = entityData.Matrix4X4.lossyScale;

            foreach (var componentData in ComponentLoadOrder.Sort(entityData.Components))
            {
                if (!_scene.IsCurrentBuild(generation))
                {
                    Destroy(go);
                    return;
                }

                var componentType = ComponentRegistry.Resolve(componentData.ComponentType);
                if (componentType == null)
                {
                    continue;
                }

                // 逐个 await：触发器与动作要在自己的 Constructor 里 GetComponent 拿元素，
                // 元素必须已经构建完
                if (go.AddComponent(componentType) is BaseComponent component)
                {
                    await component.Constructor(componentData);
                }
            }

            if (!_scene.IsCurrentBuild(generation))
            {
                Destroy(go);
                return;
            }

            // alwaysDisplayed 的 Tour 忽略实体自身的初始显隐，全部显示
            if (_displayType != IteSpaceScene.Tour.DisplayType.alwaysDisplayed)
            {
                go.GetComponent<Entity>().SetActive(entityData.enable);
            }
        }

        private void DestroyTourScene()
        {
            if (_scene.IsDestroyed) return;

            foreach (Transform child in _mainGroupObject.transform)
            {
                Destroy(child.gameObject);
            }
        }
    }
}
