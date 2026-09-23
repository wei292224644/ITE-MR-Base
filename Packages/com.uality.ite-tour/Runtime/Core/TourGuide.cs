using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// <see cref="TourGuide"/> 要求效果层执行的动作。字段可以同时出现：换 Tour 时既停用旧的、又激活新的。
    /// </summary>
    public struct GuideEffect
    {
        /// <summary>停用当前在播的 Tour。</summary>
        public bool Deactivate;

        /// <summary>要激活的 Tour；null 表示不激活。</summary>
        public string ActivateTourId;

        /// <summary>激活时的锚定位姿；null 表示沿用现有锚定（区域唤醒、直接激活）。</summary>
        public Pose? AnchorPose;

        /// <summary>只重新锚定、不重建内容的 Tour（regionalTrigger 二次锚定）；null 表示无。</summary>
        public string ReanchorTourId;

        public Pose ReanchorPose;

        /// <summary>重锚是否消耗该 Tour 的二次锚定许可。</summary>
        public bool ConsumesSecondAnchor;

        public static GuideEffect None => default;
    }

    /// <summary>
    /// 导览状态核心（ite-guide-state-machine D7）：状态、进入原因、在播 tourId、所在区域集合都在这里，
    /// 每个输入返回一个 <see cref="GuideEffect"/>，由 <see cref="TourDirector"/> 落到 IteTourObject 上。
    /// 不碰 GameObject，所以 spec §3.2 的每条转换都能在 EditMode 里测。
    ///
    /// 不变式：只有 <see cref="GuideState.Anchored"/> 下才可能有 Tour 在播（I1），也只有它允许区域
    /// 唤醒 Tour（I2）；所在区域集合在所有状态下都照常更新（I3）。
    /// </summary>
    public sealed class TourGuide
    {
        private readonly Func<IReadOnlyList<string>, string> _pick;

        // 集合只整体替换、从不原地修改，所以帧末快照可以直接持有同一个引用。
        private IReadOnlyList<string> _pendingTourIds = Array.Empty<string>();

        // 上一帧末的快照：区域重选（ite-guide-state-machine D5）与提示重算（ite-guide-state-machine D6）都以它为基准。
        private bool _hasFrame;
        private GuideState _frameState;
        private string _frameActiveTourId;
        private IReadOnlyList<string> _frameTourIds = Array.Empty<string>();

        /// <param name="pick">区域重选有多个候选时挑哪个。</param>
        public TourGuide(Func<IReadOnlyList<string>, string> pick)
        {
            _pick = pick ?? throw new ArgumentNullException(nameof(pick));
            State = GuideState.AwaitingScan;
            Reason = GuideStateReason.ColdStart;
        }

        public GuideState State { get; private set; }

        public GuideStateReason Reason { get; private set; }

        /// <summary>当前在播的 Tour；无则为 null。只有 Anchored 下可能非空（I1）。</summary>
        public string ActiveTourId { get; private set; }

        /// <summary>相机当前所在触发体积对应的 tourId 集合。</summary>
        public IReadOnlyList<string> PendingTourIds => _pendingTourIds;

        /// <summary>状态或进入原因变化时触发；两者都没变时不触发。</summary>
        public event Action<GuideState, GuideStateReason> StateChanged;

        public ScanState Snapshot() => new ScanState
        {
            State = State,

            // 过渡：旧模型里当前 Tour 就是在播的 Tour。ite-current-tour 实施中，TourGuide 重写时替换。
            CurrentTourId = ActiveTourId,
            ActiveTourId = ActiveTourId,
            PendingTourIds = _pendingTourIds,
        };

        /// <summary>摘下：停掉在播 Tour，进入 Suspended。戴上：只从 Suspended 进入 AwaitingScan，其他状态不变。</summary>
        public GuideEffect SetHeadsetMounted(bool mounted)
        {
            if (!mounted)
            {
                var effect = StopActive();
                Enter(GuideState.Suspended, GuideStateReason.HeadsetRemoved);
                return effect;
            }

            if (State == GuideState.Suspended)
            {
                Enter(GuideState.AwaitingScan, GuideStateReason.HeadsetMounted);
            }

            return GuideEffect.None;
        }

        /// <summary>
        /// 运行中直接进入等待扫码定位：停掉在播 Tour，与冷启动同一状态（ite-guide-state-machine D2）。
        /// Suspended 下不切换——摘下期间不能开始认扫码，戴上时本就会进入等待扫码（ite-guide-state-machine D3）。
        /// </summary>
        public GuideEffect RequireScan(GuideStateReason reason)
        {
            if (State == GuideState.Suspended)
            {
                return GuideEffect.None;
            }

            var effect = StopActive();
            Enter(GuideState.AwaitingScan, reason);
            return effect;
        }

        public GuideEffect SubmitScan(
            string markerId, Pose pose, IReadOnlyList<TourDescriptor> tours, out ScanDecision decision)
        {
            decision = TourScanPolicy.Decide(Snapshot(), tours, markerId);

            switch (decision.Action)
            {
                case ScanAction.Activate:
                    var effect = ActivateEffect(decision.TourId, pose);
                    if (State == GuideState.AwaitingScan)
                    {
                        Enter(GuideState.Anchored, GuideStateReason.Scanned);

                        // 刚锚定：把区域重选基准立刻跟上当前所在区域集合。不这么做的话，下一次
                        // 帧末结算会拿锚定前（本帧内、还没过帧末）的旧基准跟当前集合比，误判成
                        // 净变化，把刚扫到的 Tour 挤走（ite-guide-state-machine D5）。
                        _frameTourIds = _pendingTourIds;
                    }

                    return effect;

                case ScanAction.Reanchor:
                    return new GuideEffect
                    {
                        ReanchorTourId = decision.TourId,
                        ReanchorPose = pose,
                        ConsumesSecondAnchor = decision.ConsumesSecondAnchor,
                    };

                default:
                    return GuideEffect.None;
            }
        }

        /// <summary>相机进出某个 Tour 的触发体积：只更新集合（I3），要不要换 Tour 在帧末判（ite-guide-state-machine D5）。</summary>
        public void SubmitVolumeTransition(string tourId, VolumeTransition transition)
            => _pendingTourIds = TourRegionPolicy.Apply(_pendingTourIds, tourId, transition);

        /// <summary>不经扫码直接激活，沿用现有锚定。只在 Anchored 下可用（ite-guide-state-machine D4）。</summary>
        public bool TryActivateById(string tourId, out GuideEffect effect)
        {
            if (State != GuideState.Anchored)
            {
                effect = GuideEffect.None;
                return false;
            }

            effect = ActivateEffect(tourId, null);
            return true;
        }

        /// <summary>
        /// 帧末结算：集合有净变化且处于 Anchored 时做一次区域重选（ite-guide-state-machine D5）；
        /// <paramref name="changed"/> 报告（状态、在播、所在区域）相对上一帧末是否变化，供调用方决定
        /// 要不要重算提示（ite-guide-state-machine D6）。
        /// </summary>
        /// <param name="tours">只在真要判区域时才取，避免每帧都分配 Tour 描述列表。</param>
        public GuideEffect EndOfFrame(Func<IReadOnlyList<TourDescriptor>> tours, out bool changed)
        {
            if (tours == null)
            {
                throw new ArgumentNullException(nameof(tours));
            }

            var effect = GuideEffect.None;
            var previous = _hasFrame ? _frameTourIds : _pendingTourIds;

            if (State == GuideState.Anchored && !TourIdLists.SameSet(previous, _pendingTourIds))
            {
                var region = TourRegionPolicy.Decide(Snapshot(), tours(), previous);
                if (region.ShouldReselect)
                {
                    effect = StopActive();
                    if (region.ReselectCandidates.Count > 0)
                    {
                        var pick = _pick(region.ReselectCandidates);
                        effect.ActivateTourId = pick;
                        ActiveTourId = pick;
                    }
                }
            }

            changed = !_hasFrame
                      || State != _frameState
                      || ActiveTourId != _frameActiveTourId
                      || !TourIdLists.SameSet(_frameTourIds, _pendingTourIds);

            _hasFrame = true;
            _frameState = State;
            _frameActiveTourId = ActiveTourId;
            _frameTourIds = _pendingTourIds;

            return effect;
        }

        private GuideEffect StopActive()
        {
            if (ActiveTourId == null)
            {
                return GuideEffect.None;
            }

            ActiveTourId = null;
            return new GuideEffect { Deactivate = true };
        }

        /// <summary>换 Tour 时先停旧的；同一个 Tour 由效果层只重新锚定并确保已启用（与原 Activate 一致）。</summary>
        private GuideEffect ActivateEffect(string tourId, Pose? pose)
        {
            var effect = new GuideEffect
            {
                Deactivate = ActiveTourId != null && ActiveTourId != tourId,
                ActivateTourId = tourId,
                AnchorPose = pose,
            };

            ActiveTourId = tourId;
            return effect;
        }

        private void Enter(GuideState state, GuideStateReason reason)
        {
            if (State == state && Reason == reason)
            {
                return;
            }

            State = state;
            Reason = reason;
            StateChanged?.Invoke(state, reason);
        }
    }
}
