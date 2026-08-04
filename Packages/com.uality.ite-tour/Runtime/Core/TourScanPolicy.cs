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

        /// <summary>保持当前 Tour，仅按新位姿重新锚定（不销毁重建内容）。</summary>
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
        /// <summary>摘下头显期间为 true，此时忽略一切扫描。</summary>
        public bool Paused;

        /// <summary>「必须先扫码」状态，戴上头显或冷启动后为 true。</summary>
        public bool ForcedScanPending;

        public string ActiveTourId;

        /// <summary>相机当前所在触发体积对应的 Tour 集合；为空表示不设限。</summary>
        public IReadOnlyList<string> PendingTourIds;
    }

    public struct ScanDecision
    {
        public ScanAction Action;
        public string TourId;

        /// <summary>本次是否消费掉「必须先扫码」状态。</summary>
        public bool ClearsForcedScan;

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
    /// 把原先的偶然行为固化成契约。
    /// </summary>
    public static class TourScanPolicy
    {
        public static ScanDecision Decide(ScanState state, IReadOnlyList<TourDescriptor> tours, string markerId)
        {
            if (state.Paused || string.IsNullOrEmpty(markerId) || tours == null)
            {
                return ScanDecision.Ignore;
            }

            if (!TryFind(tours, markerId, out var tour))
            {
                return ScanDecision.Ignore;
            }

            if (state.ForcedScanPending)
            {
                // 强制扫码不看展示类型，任何匹配的 Tour 都会被激活（与源实现一致）。
                //
                // ConsumesSecondAnchor 为 false 是 D14 的所选语义：源实现中第二个
                // 处理器此刻会去查 CanSecondAnchor()，而 Enable() 尚未完成、
                // _canAnchor 仍为 false，因此不消耗。
                return new ScanDecision
                {
                    Action = ScanAction.Activate,
                    TourId = tour.TourId,
                    ClearsForcedScan = true,
                    ConsumesSecondAnchor = false,
                };
            }

            // 相机在某些触发体积内时，只认这些 Tour 的码；集合为空表示不设限。
            if (state.PendingTourIds != null
                && state.PendingTourIds.Count > 0
                && !Contains(state.PendingTourIds, markerId))
            {
                return ScanDecision.Ignore;
            }

            switch (tour.DisplayType)
            {
                case IteSpaceScene.Tour.DisplayType.normal:
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

                    // 源实现此处是 ChangeTour(tour) 之后紧跟 tour.SecondAnchored()，
                    // 但那次置位会被 Enable() 续体里的 _canAnchor = true 覆盖掉——
                    // 是死代码。按 D14 所选语义，激活不消耗二次锚定许可。
                    return new ScanDecision
                    {
                        Action = ScanAction.Activate,
                        TourId = tour.TourId,
                        ConsumesSecondAnchor = false,
                    };

                // alwaysDisplayed 始终显示，不参与扫码切换（源实现两个分支都不匹配）
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

        private static bool Contains(IReadOnlyList<string> ids, string id)
        {
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
