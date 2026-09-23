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
        /// 不换 Tour，仅按新位姿重新锚定（不销毁重建内容）。等待扫码时扫到 alwaysDisplayed 也走这里：
        /// 只锚定，不当当前 Tour（ite-current-tour D10）。
        /// </summary>
        Reanchor,
    }

    /// <summary>决策所需的 Tour 事实。刻意只带事实，不带 GameObject。</summary>
    public struct TourDescriptor
    {
        public string TourId;
        public IteSpaceScene.Tour.DisplayType DisplayType;

        /// <summary>该 Tour 当前是否还允许一次二次锚定。</summary>
        public bool SecondAnchorAvailable;
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

        /// <summary>本次是否消耗目标 Tour 的二次锚定许可。</summary>
        public bool ConsumesSecondAnchor;

        public static ScanDecision Ignore => new ScanDecision { Action = ScanAction.Ignore };
    }

    /// <summary>
    /// 标记扫描的**纯决策**。无副作用、不碰 GameObject、不依赖帧或 async 时序。
    ///
    /// 源实现把这段逻辑摊在三个订阅同一事件的处理器里，其中第一个会清掉
    /// 「必须扫码」标志，导致第二个在同一次扫码中也会执行；而第二个是否生效
    /// 又取决于 <c>IteTourObject.Enable()</c> 里 <c>_canAnchor = true</c> 有没有
    /// 在 <c>await CreateTourScene()</c> 之后跑到——一个内容为空的 Tour 会同步
    /// 走完，于是走出另一套语义。详见 design D14。
    ///
    /// 本实现采用**内容异步加载路径**下的行为作为规范语义（真机上的常规情况），
    /// 把原先的偶然行为固化成契约。状态转换（等待扫码 → 已定位）不在这里，归
    /// <see cref="TourGuide"/>。
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
                if (!TourAssembly.CanBeCurrent(tour.DisplayType))
                {
                    return new ScanDecision
                    {
                        Action = ScanAction.Reanchor,
                        TourId = tour.TourId,
                        ConsumesSecondAnchor = false,
                    };
                }

                // ConsumesSecondAnchor 为 false 是 D14 的所选语义：源实现中第二个
                // 处理器此刻会去查 CanSecondAnchor()，而 Enable() 尚未完成、
                // _canAnchor 仍为 false，因此不消耗。
                return new ScanDecision
                {
                    Action = ScanAction.Activate,
                    TourId = tour.TourId,
                    ConsumesSecondAnchor = false,
                };
            }

            // 已定位：只认当前 Tour 的码（ite-current-tour D6，取代 ite-scan-region-gate D1）。
            // 当前 Tour 优先级最高：人站在别的 Tour 的区域里、扫别的码，一律不认。
            if (tour.TourId != state.CurrentTourId)
            {
                return ScanDecision.Ignore;
            }

            switch (tour.DisplayType)
            {
                case IteSpaceScene.Tour.DisplayType.normal:
                    // 当前 Tour 是 normal：还没播就扫它的码开始播；已在播则忽略
                    return tour.TourId == state.ActiveTourId
                        ? ScanDecision.Ignore
                        : new ScanDecision { Action = ScanAction.Activate, TourId = tour.TourId };

                case IteSpaceScene.Tour.DisplayType.regionalTrigger:
                    if (tour.TourId == state.ActiveTourId)
                    {
                        // 重锚路径不触发 Enable()，所以这里消耗的许可不会被覆盖回来 —— 真正生效。
                        return tour.SecondAnchorAvailable
                            ? new ScanDecision
                            {
                                Action = ScanAction.Reanchor,
                                TourId = tour.TourId,
                                ConsumesSecondAnchor = true,
                            }
                            : ScanDecision.Ignore;
                    }

                    // 按 D14 所选语义，激活不消耗二次锚定许可（源实现此处的 SecondAnchored()
                    // 会被 Enable() 续体里的 _canAnchor = true 覆盖掉，是死代码）。
                    return new ScanDecision
                    {
                        Action = ScanAction.Activate,
                        TourId = tour.TourId,
                        ConsumesSecondAnchor = false,
                    };

                // alwaysDisplayed 不会是当前 Tour（I5）；万一是，也不参与扫码切换
                default:
                    return ScanDecision.Ignore;
            }
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
