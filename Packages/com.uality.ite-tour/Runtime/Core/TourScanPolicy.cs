using System.Collections.Generic;
using Uality.IteTour.Data;

namespace Uality.IteTour.Core
{
    /// <summary>一次标记扫描应当产生的动作。</summary>
    public enum ScanAction
    {
        /// <summary>什么都不做。</summary>
        Ignore,

        /// <summary>停用当前 Tour，激活目标 Tour，并按扫到的位姿锚定。</summary>
        Activate,

        /// <summary>
        /// 不换 Tour，仅按新位姿重新锚定（不销毁重建内容）。当前 Tour 在播时再扫它的码走这里（marker-rescan D4）；
        /// 等待扫码时扫到 alwaysDisplayed 也走这里：只锚定，不当当前 Tour（ite-current-tour D10）。
        /// </summary>
        Reanchor,
    }

    /// <summary>决策所需的 Tour 事实。刻意只带事实，不带 GameObject。</summary>
    public struct TourDescriptor
    {
        public string TourId;
        public IteSpaceScene.Tour.DisplayType DisplayType;
    }

    /// <summary>决策所需的导览状态快照。</summary>
    public struct ScanState
    {
        /// <summary>导览所处的状态（ite-guide-state-machine D1）。</summary>
        public GuideState State;

        /// <summary>
        /// 持有优先级的 Tour（ite-current-tour D3）。已定位后扫码只认它的码（ite-current-tour D6）。
        /// </summary>
        public string CurrentTourId;

        public string ActiveTourId;
    }

    public struct ScanDecision
    {
        public ScanAction Action;
        public string TourId;

        public static ScanDecision Ignore => new ScanDecision { Action = ScanAction.Ignore };
    }

    /// <summary>
    /// 标记扫描的**纯决策**。无副作用、不碰 GameObject、不依赖帧或 async 时序。
    ///
    /// 源实现把这段逻辑摊在三个订阅同一事件的处理器里，一次扫码的效果取决于处理器的执行顺序和 await 时序
    /// （design D14）；这里由一次决策明确规定。
    ///
    /// 只回答「这次扫到的码要怎么处理」：激活、定位还是忽略。「这是不是一次有意的扫描」不归这里——
    /// 底层保证同一张码每次出现只提交一次、移开视线够久再看回来才算新的一次（marker-rescan D1、D2），
    /// 所以这里不限次数（marker-rescan D4）。状态转换（等待扫码 → 已定位）不在这里，归 <see cref="TourGuide"/>。
    /// </summary>
    public static class TourScanPolicy
    {
        public static ScanDecision Decide(ScanState state, IReadOnlyList<TourDescriptor> tours, string markerId)
        {
            if (state.State == GuideState.Suspended || string.IsNullOrEmpty(markerId) || tours == null)
            {
                return ScanDecision.Ignore;
            }

            if (!TryFind(tours, markerId, out var tour))
            {
                return ScanDecision.Ignore;
            }

            if (state.State == GuideState.AwaitingScan)
            {
                // 等待扫码定位：唯一不受规则约束的入口——不看区域、不看当前 Tour，任何匹配的码都认
                // （ite-scan-region-gate D2）。
                //
                // alwaysDisplayed 只拿来锚定：它没有触发体积，当了当前 Tour 就永远离不开
                // （ite-current-tour D10）。
                return new ScanDecision
                {
                    Action = TourAssembly.CanBeCurrent(tour.DisplayType) ? ScanAction.Activate : ScanAction.Reanchor,
                    TourId = tour.TourId,
                };
            }

            // 已定位：只认当前 Tour 的码（ite-current-tour D6，取代 ite-scan-region-gate D1）。
            // 当前 Tour 优先级最高：人站在别的 Tour 的区域里、扫别的码，一律不认。
            // alwaysDisplayed 不会是当前 Tour（I5）；万一是，也不参与扫码切换。
            if (tour.TourId != state.CurrentTourId || !TourAssembly.CanBeCurrent(tour.DisplayType))
            {
                return ScanDecision.Ignore;
            }

            // 在播时再扫它的码 = 重新定位，不分展示类型、不限次数（marker-rescan D4）；没在播就开始播。
            return new ScanDecision
            {
                Action = tour.TourId == state.ActiveTourId ? ScanAction.Reanchor : ScanAction.Activate,
                TourId = tour.TourId,
            };
        }

        private static bool TryFind(IReadOnlyList<TourDescriptor> tours, string tourId, out TourDescriptor found)
        {
            for (int i = 0; i < tours.Count; i++)
            {
                if (tours[i].TourId == tourId)
                {
                    found = tours[i];
                    return true;
                }
            }

            found = default;
            return false;
        }
    }
}
