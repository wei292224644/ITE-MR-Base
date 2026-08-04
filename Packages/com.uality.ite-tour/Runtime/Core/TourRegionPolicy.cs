using System.Collections.Generic;
using Uality.IteTour.Data;

namespace Uality.IteTour.Core
{
    /// <summary>相机与 Tour 触发体积的进出。</summary>
    public enum VolumeTransition
    {
        Enter,
        Exit,
    }

    public struct RegionDecision
    {
        /// <summary>更新后的待扫描 Tour 集合。调用方用它替换旧集合。</summary>
        public IReadOnlyList<string> PendingTourIds;

        /// <summary>是否需要停用当前 Tour 并重新选一个。</summary>
        public bool ShouldReselect;

        /// <summary>
        /// 可供重选的 Tour（待扫描集合中的 <c>regionalTrigger</c>）。
        /// <see cref="ShouldReselect"/> 为 false 时为空。
        ///
        /// 刻意**只返回候选集，不替调用方挑**：源实现用
        /// <c>OrderBy(t =&gt; Guid.NewGuid())</c> 随机取一个，把随机性埋在 LINQ 链里，
        /// 既让决策不可测，也让「为什么是随机」无处说明。挑选交给效果层之后，
        /// 随机是一个显式的、可替换的选择。
        /// </summary>
        public IReadOnlyList<string> ReselectCandidates;
    }

    /// <summary>
    /// 相机进出 Tour 触发体积时的**纯决策**。无副作用，不改动传入集合。
    ///
    /// 不负责去抖：源实现用一个 0.01 秒的协程把同一帧内的多次进出合并掉，
    /// 那是时序问题，归效果层。
    /// </summary>
    public static class TourRegionPolicy
    {
        public static RegionDecision Decide(
            ScanState state,
            IReadOnlyList<TourDescriptor> tours,
            string tourId,
            VolumeTransition transition)
        {
            var pending = UpdatePending(state.PendingTourIds, tourId, transition);

            var decision = new RegionDecision
            {
                PendingTourIds = pending,
                ShouldReselect = false,
                ReselectCandidates = System.Array.Empty<string>(),
            };

            // 当前 Tour 仍在待扫描范围内，或还没扫过第一次码：不动
            if (state.ForcedScanPending || Contains(pending, state.ActiveTourId))
            {
                return decision;
            }

            // 进入自己所在的 Tour、或离开一个本就不是当前激活的 Tour：都不构成变化
            if (transition == VolumeTransition.Enter && state.ActiveTourId == tourId)
            {
                return decision;
            }

            if (transition == VolumeTransition.Exit && state.ActiveTourId != tourId)
            {
                return decision;
            }

            decision.ShouldReselect = true;
            decision.ReselectCandidates = RegionalTriggerTours(tours, pending);
            return decision;
        }

        private static List<string> UpdatePending(
            IReadOnlyList<string> current, string tourId, VolumeTransition transition)
        {
            var pending = current == null ? new List<string>() : new List<string>(current);

            if (transition == VolumeTransition.Enter)
            {
                if (!string.IsNullOrEmpty(tourId) && !pending.Contains(tourId))
                {
                    pending.Add(tourId);
                }
            }
            else
            {
                pending.Remove(tourId);
            }

            return pending;
        }

        private static List<string> RegionalTriggerTours(
            IReadOnlyList<TourDescriptor> tours, IReadOnlyList<string> pending)
        {
            var candidates = new List<string>();
            if (tours == null)
            {
                return candidates;
            }

            for (int i = 0; i < tours.Count; i++)
            {
                if (tours[i].DisplayType == IteSpaceScene.Tour.DisplayType.regionalTrigger
                    && Contains(pending, tours[i].TourId))
                {
                    candidates.Add(tours[i].TourId);
                }
            }

            return candidates;
        }

        private static bool Contains(IReadOnlyList<string> ids, string id)
        {
            if (ids == null || string.IsNullOrEmpty(id))
            {
                return false;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
