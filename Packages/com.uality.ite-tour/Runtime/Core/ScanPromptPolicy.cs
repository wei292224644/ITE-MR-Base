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
    /// 「要不要提示用户去扫码、提示哪个 Tour」的**纯决策**。已定位后只看当前 Tour，不读区域队列
    /// （ite-current-tour D7）。
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

            // 已定位：只有当前 Tour 是 normal、还没播时才提示扫它（ite-current-tour D7，取代
            // ite-scan-region-gate D3）。别的码扫了也不认（ite-current-tour D6），提示别的就是在叫人做无效操作。
            return IsNormal(tours, state.CurrentTourId)
                ? ScanPrompt.Visible(new[] { state.CurrentTourId })
                : ScanPrompt.Hidden;
        }

        private static bool IsNormal(IReadOnlyList<TourDescriptor> tours, string tourId)
        {
            if (tours == null || string.IsNullOrEmpty(tourId))
            {
                return false;
            }

            for (int i = 0; i < tours.Count; i++)
            {
                if (tours[i].TourId == tourId)
                {
                    return tours[i].DisplayType == IteSpaceScene.Tour.DisplayType.normal;
                }
            }

            return false;
        }
    }
}
