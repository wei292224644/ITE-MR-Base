using System;
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
        /// <summary>是否需要停用当前 Tour 并重新选一个。</summary>
        public bool ShouldReselect;

        /// <summary>
        /// 可供重选的 Tour（所在区域集合中的 <c>regionalTrigger</c>）。
        /// <see cref="ShouldReselect"/> 为 false 时为空。
        ///
        /// 刻意**只返回候选集，不替调用方挑**：源实现用
        /// <c>OrderBy(t =&gt; Guid.NewGuid())</c> 随机取一个，把随机性埋在 LINQ 链里，
        /// 既让决策不可测，也让「为什么是随机」无处说明。挑选交给调用方之后，
        /// 随机是一个显式的、可替换的选择。
        /// </summary>
        public IReadOnlyList<string> ReselectCandidates;

        public static RegionDecision None => new RegionDecision
        {
            ShouldReselect = false,
            ReselectCandidates = Array.Empty<string>(),
        };
    }

    /// <summary>
    /// 相机进出 Tour 触发体积的**纯决策**，分两半（ite-guide-state-machine D5）：
    /// <see cref="Apply"/> 把一次进出应用到「所在区域」集合上；<see cref="Decide"/> 在帧末按集合
    /// 相对上一帧末的净变化判一次要不要换 Tour。进出事件本身不再触发重选——同一帧里离开又
    /// 进入同一区域，净变化为零，不该切走在播的 Tour。
    /// </summary>
    public static class TourRegionPolicy
    {
        /// <summary>把一次进出应用到集合上，返回新集合。不改动传入集合；null 视为空集。</summary>
        public static IReadOnlyList<string> Apply(IReadOnlyList<string> current, string tourId, VolumeTransition transition)
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

        /// <summary>
        /// 帧末结算：<paramref name="state"/> 的所在区域集合相对 <paramref name="previousTourIds"/>
        /// （上一帧末）有净变化时，要不要停掉在播的 Tour、从哪些 Tour 里重选。
        ///
        /// - 只在 <see cref="GuideState.Anchored"/> 下判（I2）：摘下与等待扫码时区域不能唤醒 Tour。
        /// - 集合没变不判：刚锚定完的那一帧，集合仍是锚定前体积位置下的值，据此判断会切走刚扫的 Tour。
        /// - 在播 Tour 仍在集合内就不动；否则候选是集合中的 regionalTrigger。在播为空且没有候选时什么都不做。
        /// </summary>
        public static RegionDecision Decide(
            ScanState state, IReadOnlyList<TourDescriptor> tours, IReadOnlyList<string> previousTourIds)
        {
            if (state.State != GuideState.Anchored)
            {
                return RegionDecision.None;
            }

            if (TourIdLists.SameSet(previousTourIds, state.PendingTourIds))
            {
                return RegionDecision.None;
            }

            if (TourIdLists.Contains(state.PendingTourIds, state.ActiveTourId))
            {
                return RegionDecision.None;
            }

            var candidates = RegionalTriggerTours(tours, state.PendingTourIds);
            if (string.IsNullOrEmpty(state.ActiveTourId) && candidates.Count == 0)
            {
                return RegionDecision.None;
            }

            return new RegionDecision { ShouldReselect = true, ReselectCandidates = candidates };
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
                    && TourIdLists.Contains(pending, tours[i].TourId))
                {
                    candidates.Add(tours[i].TourId);
                }
            }

            return candidates;
        }
    }
}
