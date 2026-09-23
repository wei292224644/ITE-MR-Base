using System;
using System.Collections.Generic;
using UnityEngine;
using Uality.IteTour.Data;

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

        /// <summary>激活时的锚定位姿；null 表示沿用现有锚定（区域补位、直接激活）。</summary>
        public Pose? AnchorPose;

        /// <summary>只重新锚定、不换 Tour（regionalTrigger 二次锚定；等待扫码时扫到 alwaysDisplayed）；null 表示无。</summary>
        public string ReanchorTourId;

        public Pose ReanchorPose;

        /// <summary>重锚是否消耗该 Tour 的二次锚定许可。</summary>
        public bool ConsumesSecondAnchor;

        /// <summary>alwaysDisplayed 的显隐要变成什么；null 表示不变。只在进出 Anchored 时给出（ite-current-tour D8）。</summary>
        public bool? AlwaysDisplayedVisible;

        public static GuideEffect None => default;
    }

    /// <summary>
    /// 导览状态核心（ite-guide-state-machine D7；ite-current-tour D12）。持有导览状态、当前 Tour 与在播 Tour，
    /// 按顺序调用各规则模块，把结论合成一个 <see cref="GuideEffect"/> 交给 <see cref="TourDirector"/>。
    /// 规则本身不在这里：
    /// - 人在哪些区域：<see cref="RegionQueue"/>
    /// - 什么算人动了：<see cref="RegionBaseline"/>
    /// - 当前 Tour 换成谁：<see cref="CurrentTourRule"/>
    /// - 选中后播不播：<see cref="TourAssembly.PlaysOnSelect"/>
    /// - 扫码认不认：<see cref="TourScanPolicy"/>
    ///
    /// 不变式：I1 只有 Anchored 下才可能有 Tour 在播；I2 只有 Anchored 下区域才会换当前 Tour；I3 区域队列在
    /// 所有状态下都照常更新；I4 ActiveTourId 要么为空、要么等于 CurrentTourId；I5 当前 Tour 永远不是 alwaysDisplayed。
    /// 不碰 GameObject，所以每条规则的组合都能在 EditMode 里测。
    /// </summary>
    public sealed class TourGuide
    {
        private readonly Func<bool> _inPhysicsStep;
        private readonly RegionQueue _regions = new RegionQueue();
        private readonly RegionBaseline _baseline = new RegionBaseline();

        // 上一帧末的提示快照（ite-guide-state-machine D6）。提示不看区域队列（ite-current-tour D7），快照里也不放。
        private bool _hasFrame;
        private GuideState _frameState;
        private string _frameCurrentTourId;
        private string _frameActiveTourId;

        /// <param name="inPhysicsStep">
        /// 此刻是否在物理阶段内（效果层传 <c>() =&gt; Time.inFixedTimeStep</c>）。在物理阶段内锚定时，这一步的
        /// 模拟可能已经跑完，结算窗口要多等一步（ite-current-tour D9）。null 视为总是 false。
        /// </param>
        public TourGuide(Func<bool> inPhysicsStep = null)
        {
            _inPhysicsStep = inPhysicsStep ?? (() => false);
            State = GuideState.AwaitingScan;
            Reason = GuideStateReason.ColdStart;
        }

        public GuideState State { get; private set; }

        public GuideStateReason Reason { get; private set; }

        /// <summary>持有优先级的 Tour（ite-current-tour D3）；无则为 null。</summary>
        public string CurrentTourId { get; private set; }

        /// <summary>正在播的 Tour；无则为 null。只会是 null 或 <see cref="CurrentTourId"/>（I4）。</summary>
        public string ActiveTourId { get; private set; }

        /// <summary>相机所在区域，按进入先后排列（ite-current-tour D2）。</summary>
        public IReadOnlyList<string> PendingTourIds => _regions.TourIds;

        /// <summary>锚定结算窗口是否开着（ite-current-tour D9）。</summary>
        public bool IsSettling => _baseline.IsSettling;

        /// <summary>alwaysDisplayed 此刻该不该显示（ite-current-tour D8）。</summary>
        public bool AlwaysDisplayedVisible => ShowsAlwaysDisplayed(State);

        /// <summary>状态或进入原因变化时触发；两者都没变时不触发。</summary>
        public event Action<GuideState, GuideStateReason> StateChanged;

        public int RegionCountOf(string tourId) => _regions.CountOf(tourId);

        public ScanState Snapshot() => new ScanState
        {
            State = State,
            CurrentTourId = CurrentTourId,
            ActiveTourId = ActiveTourId,
        };

        /// <summary>摘下：停掉在播 Tour、清空当前 Tour，进入 Suspended。戴上：只从 Suspended 进入 AwaitingScan。</summary>
        public GuideEffect SetHeadsetMounted(bool mounted)
        {
            var before = State;
            var effect = GuideEffect.None;

            if (!mounted)
            {
                effect = ClearCurrent();
                Enter(GuideState.Suspended, GuideStateReason.HeadsetRemoved);
            }
            else if (State == GuideState.Suspended)
            {
                Enter(GuideState.AwaitingScan, GuideStateReason.HeadsetMounted);
            }

            return WithVisibility(effect, before);
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

            var before = State;
            var effect = ClearCurrent();
            Enter(GuideState.AwaitingScan, reason);
            return WithVisibility(effect, before);
        }

        public GuideEffect SubmitScan(
            string markerId, Pose pose, IReadOnlyList<TourDescriptor> tours, out ScanDecision decision)
        {
            decision = TourScanPolicy.Decide(Snapshot(), tours, markerId);

            GuideEffect effect;
            switch (decision.Action)
            {
                case ScanAction.Activate:
                    effect = Play(decision.TourId);
                    effect.AnchorPose = pose;
                    break;

                case ScanAction.Reanchor:
                    effect = new GuideEffect
                    {
                        ReanchorTourId = decision.TourId,
                        ReanchorPose = pose,
                        ConsumesSecondAnchor = decision.ConsumesSecondAnchor,
                    };
                    break;

                default:
                    return GuideEffect.None;
            }

            var before = State;
            if (State == GuideState.AwaitingScan)
            {
                Enter(GuideState.Anchored, GuideStateReason.Scanned);
            }

            // 两种动作都挪动了所有体积：接下来那一步物理里的进出是锚定造成的，不算人移动（ite-current-tour D9）。
            _baseline.BeginSettle(_inPhysicsStep() ? 2 : 1);

            return WithVisibility(effect, before);
        }

        /// <summary>
        /// 相机侧某个碰撞体进出某个 Tour 的体积：只更新区域队列（I3），当前 Tour 换不换在帧末判。
        /// </summary>
        /// <returns>false：没有对应进入的离开，被拒收（ite-current-tour D11）。</returns>
        public bool SubmitVolumeTransition(string tourId, VolumeTransition transition)
        {
            if (transition == VolumeTransition.Enter)
            {
                _regions.Enter(tourId);
                return true;
            }

            return _regions.Exit(tourId);
        }

        /// <summary>体积被停用：Unity 不发离开，计数直接清零（ite-current-tour D11）。</summary>
        public void ClearVolume(string tourId) => _regions.Clear(tourId);

        /// <summary>
        /// 不经扫码直接激活，沿用现有锚定。只在 Anchored 下可用（ite-guide-state-machine D4），
        /// alwaysDisplayed 不能激活（ite-current-tour D10）。
        /// </summary>
        public bool TryActivateById(string tourId, IteSpaceScene.Tour.DisplayType displayType, out GuideEffect effect)
        {
            if (State != GuideState.Anchored || !TourAssembly.CanBeCurrent(displayType))
            {
                effect = GuideEffect.None;
                return false;
            }

            effect = Play(tourId);
            return true;
        }

        /// <summary>一个物理步的触发回调全部到达之后调用。这一步关上结算窗口时返回 true（ite-current-tour D9）。</summary>
        public bool AfterPhysicsStep() => _baseline.AfterPhysicsStep(_regions.TourIds);

        /// <summary>
        /// 帧末结算：Anchored 且结算窗口已关时，按「基准 → 当前队列」判一次当前 Tour 换不换（ite-current-tour §5.2）；
        /// <paramref name="changed"/> 报告（状态、当前、在播）相对上一帧末是否变化，供调用方决定要不要重算提示
        /// （ite-guide-state-machine D6）。
        /// </summary>
        /// <param name="tours">只在当前 Tour 真的换了时才取，避免每帧都分配 Tour 描述列表。</param>
        public GuideEffect EndOfFrame(Func<IReadOnlyList<TourDescriptor>> tours, out bool changed)
        {
            if (tours == null)
            {
                throw new ArgumentNullException(nameof(tours));
            }

            var effect = GuideEffect.None;
            var queue = _regions.TourIds;

            if (State == GuideState.Anchored && !_baseline.IsSettling)
            {
                var next = CurrentTourRule.Next(CurrentTourId, _baseline.Baseline, queue);
                if (next != CurrentTourId)
                {
                    effect = ClearCurrent();
                    CurrentTourId = next;

                    if (next != null && PlaysOnSelect(tours(), next))
                    {
                        ActiveTourId = next;
                        effect.ActivateTourId = next;
                    }
                }
            }

            _baseline.Advance(queue);

            changed = !_hasFrame
                      || State != _frameState
                      || CurrentTourId != _frameCurrentTourId
                      || ActiveTourId != _frameActiveTourId;

            _hasFrame = true;
            _frameState = State;
            _frameCurrentTourId = CurrentTourId;
            _frameActiveTourId = ActiveTourId;

            return effect;
        }

        private static bool ShowsAlwaysDisplayed(GuideState state) => state == GuideState.Anchored;

        private static bool PlaysOnSelect(IReadOnlyList<TourDescriptor> tours, string tourId)
        {
            if (tours != null)
            {
                for (int i = 0; i < tours.Count; i++)
                {
                    if (tours[i].TourId == tourId)
                    {
                        return TourAssembly.PlaysOnSelect(tours[i].DisplayType);
                    }
                }
            }

            return false;
        }

        /// <summary>设为当前 Tour 并在播。换 Tour 时先停旧的；同一个 Tour 由效果层只重新锚定并确保已启用。</summary>
        private GuideEffect Play(string tourId)
        {
            var effect = new GuideEffect
            {
                Deactivate = ActiveTourId != null && ActiveTourId != tourId,
                ActivateTourId = tourId,
            };

            CurrentTourId = tourId;
            ActiveTourId = tourId;
            return effect;
        }

        /// <summary>清空当前 Tour；有在播的就停掉。</summary>
        private GuideEffect ClearCurrent()
        {
            CurrentTourId = null;

            if (ActiveTourId == null)
            {
                return GuideEffect.None;
            }

            ActiveTourId = null;
            return new GuideEffect { Deactivate = true };
        }

        /// <summary>进出 Anchored 时带上 alwaysDisplayed 的新显隐；没跨过这条线就不动（ite-current-tour D8）。</summary>
        private GuideEffect WithVisibility(GuideEffect effect, GuideState before)
        {
            if (ShowsAlwaysDisplayed(before) != AlwaysDisplayedVisible)
            {
                effect.AlwaysDisplayedVisible = AlwaysDisplayedVisible;
            }

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
