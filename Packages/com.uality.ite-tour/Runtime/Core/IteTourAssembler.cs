using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Uality.IteTour.Data;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 内容管线的**实例化半段**：把 <see cref="IteContentPipeline"/> 产出的数据变成
    /// 场景里的 <see cref="IteTourObject"/>，并持有存活列表。
    ///
    /// 与数据半段分开是因为两者的可测性天差地别：数据半段是纯数据变换，
    /// 这半段每一步都要 GameObject。分开之后前者可离机全测，后者只剩一层薄壳。
    ///
    /// 进度上报与事件广播都不在这里——那是编排层（<c>IteRuntime</c>）的职责。
    /// </summary>
    public class IteTourAssembler
    {
        private readonly GameObject _tourObjectPrefab;
        private readonly Transform _tourRoot;
        private readonly Transform _anchorRoot;
        private readonly Transform _camera;

        private readonly List<IteTourObject> _liveTours = new List<IteTourObject>();

        public IReadOnlyList<IteTourObject> LiveTours => _liveTours;

        /// <param name="tourObjectPrefab">包内的 Tour 预制体。</param>
        /// <param name="tourRoot">Tour 实例的父节点（原 tag <c>AnchorOffsetObject</c>）。</param>
        /// <param name="anchorRoot">锚定目标（原 tag <c>AnchorObject</c>）。</param>
        /// <param name="camera">用于识别谁进出触发体积（原 tag <c>ARCamera</c>）。</param>
        public IteTourAssembler(
            GameObject tourObjectPrefab, Transform tourRoot, Transform anchorRoot, Transform camera)
        {
            _camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));

            _tourObjectPrefab = tourObjectPrefab != null
                ? tourObjectPrefab
                : throw new ArgumentNullException(nameof(tourObjectPrefab));

            _tourRoot = tourRoot != null ? tourRoot : throw new ArgumentNullException(nameof(tourRoot));
            _anchorRoot = anchorRoot != null ? anchorRoot : throw new ArgumentNullException(nameof(anchorRoot));
        }

        /// <summary>
        /// 实例化一个 Tour 并完成其构建。返回 null 表示预制体上没有
        /// <see cref="IteTourObject"/>——装配错误，不该静默跳过。
        /// </summary>
        public async Task<IteTourObject> CreateAsync(IteSpaceScene.Tour tour, Data.IteTour tourData)
        {
            var instance = UnityEngine.Object.Instantiate(_tourObjectPrefab, _tourRoot);

            var tourObject = instance.GetComponent<IteTourObject>();
            if (tourObject == null)
            {
                Debug.LogError("[ITE] Tour 预制体上没有 IteTourObject 组件");
                UnityEngine.Object.Destroy(instance);
                return null;
            }

            tourObject.BindScene(_anchorRoot, _tourRoot, _camera);
            await tourObject.CreateTourObject(tour, tourData);

            _liveTours.Add(tourObject);
            return tourObject;
        }

        public IteTourObject Find(string tourId)
        {
            for (int i = 0; i < _liveTours.Count; i++)
            {
                if (_liveTours[i] != null && _liveTours[i].TourId == tourId)
                {
                    return _liveTours[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 触发体积的显隐由宿主决定（源实现在开场动画结束后统一打开）。
        /// </summary>
        public void SetAllVolumesActive(bool active)
        {
            foreach (var tour in _liveTours)
            {
                if (tour != null)
                {
                    tour.SetVolumeObjectActive(active);
                }
            }
        }

        public void DestroyAll()
        {
            foreach (var tour in _liveTours)
            {
                if (tour == null)
                {
                    continue;
                }

                tour.Destroy();
                UnityEngine.Object.Destroy(tour.gameObject);
            }

            _liveTours.Clear();
        }
    }
}
