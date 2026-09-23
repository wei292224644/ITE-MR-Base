using System;
using System.Collections.Generic;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 人在哪些区域里、按什么先后（ite-current-tour D1、D2）。
    ///
    /// 每个 Tour 一个计数：相机侧每个碰撞体进入加 1、离开减 1，归零才算离开。相机侧不止一个碰撞体——
    /// Main Camera 上的球和 XR Origin 上的 CharacterController 胶囊都被 <see cref="SceneRoles.IsCamera"/>
    /// 认作相机，两者进出体积的时刻不同；只按第一次离开算，Tour 会在还有碰撞体在里面时就被移出（PICO 实测）。
    ///
    /// 列表按计数 0→1 的先后排列，离开后再进入排到队尾。不变式：计数 &gt; 0 ⟺ 在列表里。
    /// 不认识展示类型、导览状态与锚定。
    /// </summary>
    public sealed class RegionQueue
    {
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        // 只整体替换、从不原地修改：帧末基准（RegionBaseline）直接持有旧引用。
        private string[] _tourIds = Array.Empty<string>();

        public IReadOnlyList<string> TourIds => _tourIds;

        public int CountOf(string tourId)
            => tourId != null && _counts.TryGetValue(tourId, out var count) ? count : 0;

        public void Enter(string tourId)
        {
            if (string.IsNullOrEmpty(tourId))
            {
                return;
            }

            int count = CountOf(tourId);
            _counts[tourId] = count + 1;

            if (count == 0)
            {
                var next = new List<string>(_tourIds) { tourId };
                _tourIds = next.ToArray();
            }
        }

        /// <returns>
        /// false：计数已是 0，这次离开没有对应的进入，被拒收（ite-current-tour D11）。计数永远不会变负——
        /// 变负的计数会让下一次进入「抵消」掉，人明明在体积里，队列里却没有它。
        /// </returns>
        public bool Exit(string tourId)
        {
            int count = CountOf(tourId);
            if (count == 0)
            {
                return false;
            }

            if (count > 1)
            {
                _counts[tourId] = count - 1;
                return true;
            }

            Remove(tourId);
            return true;
        }

        /// <summary>体积被停用：Unity 不发离开，计数直接清零（ite-current-tour D11）。</summary>
        public void Clear(string tourId)
        {
            if (CountOf(tourId) > 0)
            {
                Remove(tourId);
            }
        }

        private void Remove(string tourId)
        {
            _counts.Remove(tourId);

            var next = new List<string>(_tourIds);
            next.Remove(tourId);
            _tourIds = next.ToArray();
        }
    }
}
