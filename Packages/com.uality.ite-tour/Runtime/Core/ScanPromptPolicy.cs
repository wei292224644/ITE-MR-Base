using System;
using System.Collections.Generic;
using Uality.IteTour.Data;

namespace Uality.IteTour.Core
{
    public enum ScanPromptState
    {
        Hidden,
        Visible,
    }

    public struct ScanPrompt
    {
        public ScanPromptState State;

        /// <summary>
        /// 需要用户去扫的 Tour。为空表示「随便扫哪个都行」——只出现在等待扫码定位
        /// （<see cref="GuideState.AwaitingScan"/>）时。
        /// </summary>
        public IReadOnlyList<string> TourIds;

        public static ScanPrompt Hidden => new ScanPrompt
        {
            State = ScanPromptState.Hidden,
            TourIds = Array.Empty<string>(),
        };

        public static ScanPrompt Visible(IReadOnlyList<string> tourIds) => new ScanPrompt
        {
            State = ScanPromptState.Visible,
            TourIds = tourIds ?? Array.Empty<string>(),
        };
    }

    /// <summary>
    /// 「要不要提示用户去扫码、提示哪几个 Tour」的**纯决策**。
    ///
    /// 源实现是个每 0.75 秒轮询的协程，直接调 <c>ScanPreviewUI.Instance.Show()/Hide()</c>。
    /// 决策是 ITE 业务，渲染不是——这里只留决策，渲染由宿主订阅广播自行处理（design D5）。
    /// </summary>
    public static class ScanPromptPolicy
    {
        public static ScanPrompt Decide(ScanState state, IReadOnlyList<TourDescriptor> tours)
        {
            // 已经有 Tour 在放，提示无条件收起
            if (!string.IsNullOrEmpty(state.ActiveTourId))
            {
                return ScanPrompt.Hidden;
            }

            switch (state.State)
            {
                case GuideState.Suspended:
                    // 头显摘下：没人在看
                    return ScanPrompt.Hidden;

                case GuideState.AwaitingScan:
                    // 等待扫码定位（冷启动 / 重新戴上 / 追踪原点重置 / 宿主要求）：随便扫哪个都行，不报名字
                    return ScanPrompt.Visible(Array.Empty<string>());
            }

            // 定位过之后体积外扫码不生效，不提示（ite-scan-region-gate D3）
            if (state.PendingTourIds == null || state.PendingTourIds.Count == 0)
            {
                return ScanPrompt.Hidden;
            }

            // 范围内有 regionalTrigger：它会自动激活，不必提示
            if (HasPending(tours, state.PendingTourIds, IteSpaceScene.Tour.DisplayType.regionalTrigger))
            {
                return ScanPrompt.Hidden;
            }

            var normalTourIds = PendingIds(tours, state.PendingTourIds, IteSpaceScene.Tour.DisplayType.normal);

            return normalTourIds.Count > 0
                ? ScanPrompt.Visible(normalTourIds)
                : ScanPrompt.Hidden;
        }

        private static bool HasPending(
            IReadOnlyList<TourDescriptor> tours,
            IReadOnlyList<string> pending,
            IteSpaceScene.Tour.DisplayType displayType)
            => PendingIds(tours, pending, displayType).Count > 0;

        private static List<string> PendingIds(
            IReadOnlyList<TourDescriptor> tours,
            IReadOnlyList<string> pending,
            IteSpaceScene.Tour.DisplayType displayType)
        {
            var ids = new List<string>();
            if (tours == null)
            {
                return ids;
            }

            for (int i = 0; i < tours.Count; i++)
            {
                if (tours[i].DisplayType == displayType && TourIdLists.Contains(pending, tours[i].TourId))
                {
                    ids.Add(tours[i].TourId);
                }
            }

            return ids;
        }
    }
}
