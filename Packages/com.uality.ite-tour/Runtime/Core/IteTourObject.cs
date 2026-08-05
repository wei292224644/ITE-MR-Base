using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
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

        [SerializeField] private Components.IteTourElementPrefabs _elementPrefabs;

        /// <summary>元素组件要实例化的预制体。运行时挂上去的组件拿不到序列化引用，从这里要（D20）。</summary>
        public Components.IteTourElementPrefabs ElementPrefabs => _elementPrefabs;

        private Transform _anchorObject;
        private Transform _tourOffsetObject;

        private bool _isDestroyed;
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

        /// <summary>注入锚点与偏移 Transform。装配时调用一次。</summary>
        public void BindAnchors(Transform anchorObject, Transform tourOffsetObject)
        {
            _anchorObject = anchorObject;
            _tourOffsetObject = tourOffsetObject;
        }

        public async Task CreateTourObject(IteSpaceScene.Tour tour, Data.IteTour tourData)
        {
            if (_isDestroyed) return;

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

            // 源实现在此处取半值。触发体积因此只有描述尺寸的一半，是既有行为，原样保留。
            boxCollider.size = new Vector3(
                tour.triggerVolume.width / 2, tour.triggerVolume.height / 2, tour.triggerVolume.depth / 2);

            await LoadAssets(tourData.Assets);
            ChangeDisplayType(tour.displayType);

            if (tour.displayType != IteSpaceScene.Tour.DisplayType.alwaysDisplayed)
            {
                Disable();
            }
        }

        public void ChangeTourObjectTransform(Vector3 t, Quaternion r)
        {
            if (_anchorObject == null || _tourOffsetObject == null)
            {
                Debug.LogError("[ITE] BindAnchors 未调用，无法锚定 Tour: " + _tourId);
                return;
            }

            var localMatrix = (transform.parent.localToWorldMatrix.inverse * transform.localToWorldMatrix).inverse;

            _tourOffsetObject.SetLocalPositionAndRotation(localMatrix.GetPosition(), localMatrix.rotation);
            _anchorObject.SetLocalPositionAndRotation(t, r);
        }

        public void SetVolumeObjectActive(bool isActive)
        {
            if (_isDestroyed) return;
            _volumeObject.SetActive(isActive);
        }

        public async Task Enable()
        {
            if (_isDestroyed) return;
            gameObject.SetActive(true);
            await CreateTourScene();
            OnTourSceneLoaded?.Invoke();

            _canAnchor = true;
        }

        public void Disable()
        {
            if (_isDestroyed) return;
            DestroyTourScene();

            // 源实现此处先 ResetSecondAnchor()（置 true）再置 false，前者是死调用（D14）。
            // 连同只有它一个调用方的 ResetSecondAnchor 一并删除。
            _canAnchor = false;
        }

        public void Destroy()
        {
            Disable();
            _isDestroyed = true;
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

            // 一直显示的 Tour 不需要触发体积
            if (displayType == IteSpaceScene.Tour.DisplayType.alwaysDisplayed)
            {
                _volumeObject.SetActive(false);
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
            if (_isDestroyed || assets == null) return;

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

        // TODO(5.2c) 组件实例化：依赖第 6 节的 ComponentsUtils 与 BaseComponent
        private Task CreateTourScene() => Task.CompletedTask;

        private void DestroyTourScene()
        {
            if (_isDestroyed) return;

            foreach (Transform child in _mainGroupObject.transform)
            {
                Destroy(child.gameObject);
            }
        }
    }
}
