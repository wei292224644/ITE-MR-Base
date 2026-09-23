using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 导览状态核心（ite-guide-state-machine；ite-current-tour）。状态转换逐条钉住；区域部分按 PICO 实测
    /// （2026-09-23）回放：扫码后被锚定引起的区域变化切走（问题 1）、双碰撞体成对进出（问题 2）。
    /// </summary>
    public class TourGuideTests
    {
        private const IteSpaceScene.Tour.DisplayType Normal = IteSpaceScene.Tour.DisplayType.normal;
        private const IteSpaceScene.Tour.DisplayType Regional = IteSpaceScene.Tour.DisplayType.regionalTrigger;
        private const IteSpaceScene.Tour.DisplayType Always = IteSpaceScene.Tour.DisplayType.alwaysDisplayed;

        private static readonly Pose MarkerPose = new Pose(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f));

        private static TourDescriptor Tour(
            string id, IteSpaceScene.Tour.DisplayType type = Regional, bool secondAnchorAvailable = false)
            => new TourDescriptor { TourId = id, DisplayType = type, SecondAnchorAvailable = secondAnchorAvailable };

        private static List<TourDescriptor> Tours(params TourDescriptor[] tours) => new List<TourDescriptor>(tours);

        private static GuideEffect Frame(TourGuide guide, List<TourDescriptor> tours)
            => guide.EndOfFrame(() => tours, out _);

        private static void Enter(TourGuide guide, params string[] tourIds)
        {
            foreach (var id in tourIds)
            {
                guide.SubmitVolumeTransition(id, VolumeTransition.Enter);
            }
        }

        private static void Exit(TourGuide guide, params string[] tourIds)
        {
            foreach (var id in tourIds)
            {
                guide.SubmitVolumeTransition(id, VolumeTransition.Exit);
            }
        }

        /// <summary>
        /// 站在 tourId 的体积里扫它的码，锚定后人仍在里面（那一步物理没有进出事件）：走到 Anchored，
        /// tourId 是当前 Tour 且在播，结算窗口已关。
        /// </summary>
        private static TourGuide AnchoredOn(string tourId, List<TourDescriptor> tours)
        {
            var guide = new TourGuide();
            Enter(guide, tourId);
            Frame(guide, tours);
            guide.SubmitScan(tourId, MarkerPose, tours, out _);
            Frame(guide, tours);
            guide.AfterPhysicsStep();
            Frame(guide, tours);
            return guide;
        }

        // ---- 构造与扫码 ----

        [Test]
        public void Constructed_IsAwaitingScanForColdStart()
        {
            var guide = new TourGuide();

            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.ColdStart));
            Assert.That(guide.CurrentTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.Null);
            Assert.That(guide.AlwaysDisplayedVisible, Is.False, "锚定前 alwaysDisplayed 不显示（ite-current-tour D8）");
        }

        [Test]
        public void ScanWhileAwaiting_ActivatesWithPose_EntersAnchored_OpensSettle()
        {
            var guide = new TourGuide();

            var effect = guide.SubmitScan("t1", MarkerPose, Tours(Tour("t1")), out var decision);

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(effect.ActivateTourId, Is.EqualTo("t1"));
            Assert.That(effect.AnchorPose.HasValue, Is.True);
            Assert.That(effect.AnchorPose.Value, Is.EqualTo(MarkerPose));
            Assert.That(effect.Deactivate, Is.False);
            Assert.That(effect.AlwaysDisplayedVisible, Is.EqualTo((bool?)true), "进入 Anchored 时显示 alwaysDisplayed");
            Assert.That(guide.State, Is.EqualTo(GuideState.Anchored));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.Scanned));
            Assert.That(guide.CurrentTourId, Is.EqualTo("t1"));
            Assert.That(guide.ActiveTourId, Is.EqualTo("t1"));
            Assert.That(guide.IsSettling, Is.True, "锚定挪动了体积（ite-current-tour D9）");
        }

        [Test]
        public void Anchored_RescanCurrentRegional_ReanchorsAndOpensSettle()
        {
            var tours = Tours(Tour("t1", Regional, secondAnchorAvailable: true));
            var guide = AnchoredOn("t1", tours);
            var newPose = new Pose(new Vector3(4f, 5f, 6f), Quaternion.identity);

            var effect = guide.SubmitScan("t1", newPose, tours, out _);

            Assert.That(effect.ReanchorTourId, Is.EqualTo("t1"));
            Assert.That(effect.ReanchorPose, Is.EqualTo(newPose));
            Assert.That(effect.ConsumesSecondAnchor, Is.True);
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(effect.Deactivate, Is.False);
            Assert.That(effect.AlwaysDisplayedVisible, Is.Null, "状态没变，显隐不动");
            Assert.That(guide.IsSettling, Is.True);
        }

        /// <summary>ite-current-tour D10：等待扫码时扫到 alwaysDisplayed，只锚定，当前 Tour 为空，之后取队尾。</summary>
        [Test]
        public void ScanAlwaysDisplayedWhileAwaiting_AnchorsOnly_ThenTailBecomesCurrent()
        {
            var tours = Tours(Tour("a1", Always), Tour("r1"));
            var guide = new TourGuide();

            var effect = guide.SubmitScan("a1", MarkerPose, tours, out _);

            Assert.That(effect.ReanchorTourId, Is.EqualTo("a1"));
            Assert.That(effect.ConsumesSecondAnchor, Is.False);
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.State, Is.EqualTo(GuideState.Anchored));
            Assert.That(guide.CurrentTourId, Is.Null, "alwaysDisplayed 不当当前 Tour（I5）");

            Enter(guide, "r1");
            guide.AfterPhysicsStep();
            var next = Frame(guide, tours);

            Assert.That(next.ActivateTourId, Is.EqualTo("r1"), "当前 Tour 为空时取队尾");
            Assert.That(guide.CurrentTourId, Is.EqualTo("r1"));
        }

        // ---- 摘下 / 戴上 ----

        [Test]
        public void HeadsetRemoved_FromAnchored_StopsTour_ClearsCurrent_HidesAlwaysDisplayed()
        {
            var guide = AnchoredOn("t1", Tours(Tour("t1")));

            var effect = guide.SetHeadsetMounted(false);

            Assert.That(effect.Deactivate, Is.True);
            Assert.That(effect.AlwaysDisplayedVisible, Is.EqualTo((bool?)false));
            Assert.That(guide.State, Is.EqualTo(GuideState.Suspended));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.HeadsetRemoved));
            Assert.That(guide.CurrentTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.Null);
        }

        [Test]
        public void HeadsetMounted_FromSuspended_AwaitsScan()
        {
            var guide = AnchoredOn("t1", Tours(Tour("t1")));
            guide.SetHeadsetMounted(false);

            var effect = guide.SetHeadsetMounted(true);

            Assert.That(effect.Deactivate, Is.False);
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(effect.AlwaysDisplayedVisible, Is.Null, "Suspended → AwaitingScan 都不显示，显隐不动");
            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.HeadsetMounted));
        }

        [Test]
        public void HeadsetMounted_WhenNotSuspended_ChangesNothing()
        {
            var guide = new TourGuide();
            int raised = 0;
            guide.StateChanged += (state, reason) => raised++;

            guide.SetHeadsetMounted(true);

            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.ColdStart));
            Assert.That(raised, Is.EqualTo(0));
        }

        /// <summary>PICO 实测（2026-09-23）：摘下期间头显仍在追踪，区域进出事件不停，但不能唤醒 Tour。</summary>
        [Test]
        public void WhileSuspended_EnteringRegion_NeverActivates()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);
            guide.SetHeadsetMounted(false);

            Exit(guide, "t1");
            Enter(guide, "t2");
            var effect = Frame(guide, tours);

            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(effect.Deactivate, Is.False);
            Assert.That(guide.ActiveTourId, Is.Null);
            Assert.That(guide.PendingTourIds, Is.EqualTo(new[] { "t2" }), "区域队列照常更新（I3）");
        }

        [Test]
        public void AfterRemount_EnteringRegion_NeverActivates()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);
            guide.SetHeadsetMounted(false);
            guide.SetHeadsetMounted(true);

            Enter(guide, "t2");
            var effect = Frame(guide, tours);

            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.Null);
            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
        }

        [Test]
        public void WhileSuspended_ScanIsIgnored()
        {
            var guide = new TourGuide();
            guide.SetHeadsetMounted(false);

            var effect = guide.SubmitScan("t1", MarkerPose, Tours(Tour("t1")), out var decision);

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.State, Is.EqualTo(GuideState.Suspended));
            Assert.That(guide.IsSettling, Is.False, "被忽略的扫码不挪体积");
        }

        // ---- 运行中要求重扫 ----

        [Test]
        public void RequireScan_FromAnchored_StopsTour_AndAwaitsScan()
        {
            var guide = AnchoredOn("t1", Tours(Tour("t1")));

            var effect = guide.RequireScan(GuideStateReason.Recentered);

            Assert.That(effect.Deactivate, Is.True, "锚定作废后不能继续显示错位的内容（ite-guide-state-machine D2）");
            Assert.That(effect.AlwaysDisplayedVisible, Is.EqualTo((bool?)false));
            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.Recentered));
            Assert.That(guide.CurrentTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.Null);
        }

        [Test]
        public void RequireScan_WhileSuspended_StaysSuspended()
        {
            var guide = new TourGuide();
            guide.SetHeadsetMounted(false);

            var effect = guide.RequireScan(GuideStateReason.Recentered);

            Assert.That(effect.Deactivate, Is.False);
            Assert.That(guide.State, Is.EqualTo(GuideState.Suspended), "摘下期间不能开始认扫码（ite-guide-state-machine D3）");
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.HeadsetRemoved));
        }

        [Test]
        public void RequireScan_WhileAwaiting_UpdatesReasonOnce()
        {
            var guide = new TourGuide();
            var raised = new List<GuideStateReason>();
            guide.StateChanged += (state, reason) => raised.Add(reason);

            guide.RequireScan(GuideStateReason.HostRequested);
            guide.RequireScan(GuideStateReason.HostRequested);

            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.HostRequested));
            Assert.That(raised, Is.EqualTo(new[] { GuideStateReason.HostRequested }), "状态与原因都没变时不重复广播");
        }

        // ---- 直接激活 ----

        [Test]
        public void TryActivateById_RefusedUnlessAnchored()
        {
            var guide = new TourGuide();

            Assert.That(guide.TryActivateById("t1", Regional, out var effect), Is.False,
                "等待扫码时冒出在播 Tour 会违反 I1（ite-guide-state-machine D4）");
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.Null);
        }

        [Test]
        public void TryActivateById_RefusesAlwaysDisplayed()
        {
            var guide = AnchoredOn("t1", Tours(Tour("t1"), Tour("a1", Always)));

            Assert.That(guide.TryActivateById("a1", Always, out _), Is.False, "I5（ite-current-tour D10）");
            Assert.That(guide.CurrentTourId, Is.EqualTo("t1"));
        }

        [Test]
        public void TryActivateById_WhenAnchored_SwitchesCurrentWithoutAnchoring()
        {
            var guide = AnchoredOn("t1", Tours(Tour("t1"), Tour("t2")));

            Assert.That(guide.TryActivateById("t2", Regional, out var effect), Is.True);
            Assert.That(effect.Deactivate, Is.True);
            Assert.That(effect.ActivateTourId, Is.EqualTo("t2"));
            Assert.That(effect.AnchorPose.HasValue, Is.False, "沿用现有锚定");
            Assert.That(guide.CurrentTourId, Is.EqualTo("t2"));
            Assert.That(guide.ActiveTourId, Is.EqualTo("t2"));
            Assert.That(guide.IsSettling, Is.False, "不挪体积，不开窗");
        }

        // ---- 真机回放 ----

        /// <summary>
        /// PICO 14:36:31（问题 1）：站在锚定前 ujf5 体积的位置扫 ujf5 的码（两个碰撞体都在里面）。锚定把体积
        /// 挪到真实位置，下一步物理里 ujf5 的两个碰撞体都离开、hncx 进入——这是锚定造成的，不能把刚扫的
        /// ujf5 切走。之后人真的走进 ujf5 再走出来，才换成队尾的 hncx。
        /// </summary>
        [Test]
        public void Replay_AnchorMovesVolumes_KeepsScannedTour_UntilPersonEntersAndLeavesIt()
        {
            var tours = Tours(Tour("ujf5bo31_frb"), Tour("hncxtzfe_p4d"));
            var guide = new TourGuide();
            Enter(guide, "ujf5bo31_frb", "ujf5bo31_frb");
            Frame(guide, tours);

            guide.SubmitScan("ujf5bo31_frb", MarkerPose, tours, out _);
            Assert.That(Frame(guide, tours).ActivateTourId, Is.Null, "扫码同一帧");

            Exit(guide, "ujf5bo31_frb", "ujf5bo31_frb");
            Enter(guide, "hncxtzfe_p4d");
            Assert.That(guide.AfterPhysicsStep(), Is.True, "锚定后第一步物理：关窗");
            var afterSettle = Frame(guide, tours);

            Assert.That(afterSettle.Deactivate, Is.False);
            Assert.That(afterSettle.ActivateTourId, Is.Null);
            Assert.That(guide.CurrentTourId, Is.EqualTo("ujf5bo31_frb"));
            Assert.That(guide.ActiveTourId, Is.EqualTo("ujf5bo31_frb"));

            Enter(guide, "ujf5bo31_frb");
            Assert.That(Frame(guide, tours).ActivateTourId, Is.Null, "走进当前 Tour：不变");

            Exit(guide, "ujf5bo31_frb");
            var leave = Frame(guide, tours);

            Assert.That(leave.Deactivate, Is.True);
            Assert.That(leave.ActivateTourId, Is.EqualTo("hncxtzfe_p4d"));
            Assert.That(leave.AnchorPose.HasValue, Is.False, "区域补位沿用现有锚定");
            Assert.That(guide.CurrentTourId, Is.EqualTo("hncxtzfe_p4d"));
        }

        /// <summary>
        /// PICO 14:36:36–39（问题 2）：qtcljiro 的两个碰撞体先后进入、先后离开。第一次离开后还有一个
        /// 碰撞体在里面，当前 Tour 不能被释放。
        /// </summary>
        [Test]
        public void Replay_TwoCameraColliders_CurrentReleasedOnlyWhenBothLeave()
        {
            var tours = Tours(Tour("qtcljiro_zrx"), Tour("r2"));
            var guide = AnchoredOn("qtcljiro_zrx", tours);
            Enter(guide, "qtcljiro_zrx", "r2");
            Frame(guide, tours);

            Exit(guide, "qtcljiro_zrx");
            var first = Frame(guide, tours);

            Assert.That(first.Deactivate, Is.False, "还有一个碰撞体在体积里");
            Assert.That(guide.CurrentTourId, Is.EqualTo("qtcljiro_zrx"));

            Exit(guide, "qtcljiro_zrx");
            var second = Frame(guide, tours);

            Assert.That(second.Deactivate, Is.True);
            Assert.That(second.ActivateTourId, Is.EqualTo("r2"));
        }

        // ---- 当前 Tour 优先（ite-current-tour D3、D4、D5）----

        [Test]
        public void EnteringOtherRegions_NeverInterruptsCurrent()
        {
            var tours = Tours(Tour("t1"), Tour("t2"), Tour("t3"));
            var guide = AnchoredOn("t1", tours);

            Enter(guide, "t2");
            Assert.That(Frame(guide, tours).ActivateTourId, Is.Null);
            Enter(guide, "t3");
            Assert.That(Frame(guide, tours).ActivateTourId, Is.Null);

            Assert.That(guide.CurrentTourId, Is.EqualTo("t1"));
            Assert.That(guide.ActiveTourId, Is.EqualTo("t1"));
        }

        [Test]
        public void LeavingCurrent_TailIsNormal_SelectsItWithoutPlaying()
        {
            var tours = Tours(Tour("r1"), Tour("r2"), Tour("n1", Normal));
            var guide = AnchoredOn("r1", tours);
            Enter(guide, "r2", "n1");
            Frame(guide, tours);

            Exit(guide, "r1");
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.True);
            Assert.That(effect.ActivateTourId, Is.Null, "碰撞永远不激活 normal（ite-current-tour D5）");
            Assert.That(guide.CurrentTourId, Is.EqualTo("n1"), "取队尾，不跳过 normal（ite-current-tour D4）");
            Assert.That(guide.ActiveTourId, Is.Null);
        }

        [Test]
        public void CurrentNormalAwaitingScan_OtherRegionAndOtherCode_DoNotTakeOver_OwnCodeActivates()
        {
            var tours = Tours(Tour("r1"), Tour("r2"), Tour("n1", Normal));
            var guide = AnchoredOn("r1", tours);
            Enter(guide, "n1");
            Frame(guide, tours);
            Exit(guide, "r1");
            Frame(guide, tours);
            Assert.That(guide.CurrentTourId, Is.EqualTo("n1"));

            Enter(guide, "r2");
            Assert.That(Frame(guide, tours).ActivateTourId, Is.Null, "当前 Tour 优先级最高");

            guide.SubmitScan("r2", MarkerPose, tours, out var other);
            Assert.That(other.Action, Is.EqualTo(ScanAction.Ignore), "只认当前 Tour 的码（ite-current-tour D6）");

            var effect = guide.SubmitScan("n1", MarkerPose, tours, out var own);

            Assert.That(own.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(effect.ActivateTourId, Is.EqualTo("n1"));
            Assert.That(effect.AnchorPose.HasValue, Is.True);
            Assert.That(guide.ActiveTourId, Is.EqualTo("n1"));
            Assert.That(guide.IsSettling, Is.True, "扫 normal 会重新锚定，开窗");
        }

        [Test]
        public void NoCurrent_TailNormal_IsSelectedButNeverPlayedByRegion()
        {
            var tours = Tours(Tour("t1"), Tour("n1", Normal));
            var guide = AnchoredOn("t1", tours);
            Exit(guide, "t1");
            Frame(guide, tours);
            Assert.That(guide.CurrentTourId, Is.Null);

            Enter(guide, "n1");
            var effect = Frame(guide, tours);

            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.CurrentTourId, Is.EqualTo("n1"));
            Assert.That(guide.ActiveTourId, Is.Null);
        }

        [Test]
        public void LeavingCurrentIntoNothing_Stops_ThenEnteringRegionPicksIt()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);

            Exit(guide, "t1");
            var stop = Frame(guide, tours);

            Assert.That(stop.Deactivate, Is.True);
            Assert.That(stop.ActivateTourId, Is.Null);
            Assert.That(guide.CurrentTourId, Is.Null);

            Enter(guide, "t2");
            var start = Frame(guide, tours);

            Assert.That(start.ActivateTourId, Is.EqualTo("t2"));
            Assert.That(guide.CurrentTourId, Is.EqualTo("t2"));
        }

        [Test]
        public void ExitAndReenterCurrentWithinFrame_KeepsIt()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);
            Enter(guide, "t2");
            Frame(guide, tours);

            Exit(guide, "t1");
            Enter(guide, "t1");
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.False);
            Assert.That(guide.CurrentTourId, Is.EqualTo("t1"));
            Assert.That(guide.PendingTourIds, Is.EqualTo(new[] { "t2", "t1" }), "重新进入排到队尾");
        }

        [Test]
        public void MovingFromCurrentIntoNeighbourInOneStep_SwitchesDirectly()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);

            Exit(guide, "t1");
            Enter(guide, "t2");
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.True);
            Assert.That(effect.ActivateTourId, Is.EqualTo("t2"));
        }

        // ---- 结算窗口与防御（ite-current-tour D9、D11）----

        /// <summary>在物理阶段内锚定（宿主在物理回调里扫码）：这一步的模拟可能已跑完，窗口多等一步。</summary>
        [Test]
        public void ScanInsidePhysicsStep_WaitsOneMoreStep_BeforeCurrentCanMove()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = new TourGuide(() => true);
            Enter(guide, "t1");
            Frame(guide, tours);
            guide.SubmitScan("t1", MarkerPose, tours, out _);

            Assert.That(guide.AfterPhysicsStep(), Is.False, "第一步可能在锚定之前就模拟完了");
            Exit(guide, "t1");
            Enter(guide, "t2");
            Assert.That(Frame(guide, tours).Deactivate, Is.False, "窗口还开着");

            Assert.That(guide.AfterPhysicsStep(), Is.True);
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.False, "锚定造成的离开并入基准");
            Assert.That(guide.CurrentTourId, Is.EqualTo("t1"));
        }

        [Test]
        public void OrphanExit_IsRejected_AndDoesNotReleaseCurrent()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);

            Assert.That(guide.SubmitVolumeTransition("t2", VolumeTransition.Exit), Is.False);
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.False);
            Assert.That(guide.CurrentTourId, Is.EqualTo("t1"));
            Assert.That(guide.PendingTourIds, Is.EqualTo(new[] { "t1" }));
        }

        /// <summary>体积被停用时 Unity 不发 Exit，由 ClearVolume 清零；人在里面时等同于离开。</summary>
        [Test]
        public void ClearVolume_WhileInsideCurrent_ReleasesIt()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);
            Enter(guide, "t1", "t2");
            Frame(guide, tours);

            guide.ClearVolume("t1");
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.True);
            Assert.That(effect.ActivateTourId, Is.EqualTo("t2"));
            Assert.That(guide.RegionCountOf("t1"), Is.EqualTo(0));
        }

        // ---- 帧末快照（ite-guide-state-machine D6）与状态广播（ite-guide-state-machine D8）----

        [Test]
        public void EndOfFrame_ReportsChangeOnlyWhenPromptSnapshotChanges()
        {
            var tours = Tours(Tour("t1"));
            var guide = new TourGuide();

            guide.EndOfFrame(() => tours, out bool first);
            guide.EndOfFrame(() => tours, out bool idle);
            Enter(guide, "t1");
            guide.EndOfFrame(() => tours, out bool afterEnter);
            guide.SetHeadsetMounted(false);
            guide.EndOfFrame(() => tours, out bool afterRemove);

            Assert.That(first, Is.True, "第一帧必须算一次提示");
            Assert.That(idle, Is.False);
            Assert.That(afterEnter, Is.False, "提示不再看区域队列（ite-current-tour D7）");
            Assert.That(afterRemove, Is.True);
        }

        [Test]
        public void StateChanged_CarriesReason()
        {
            var tours = Tours(Tour("t1"));
            var guide = new TourGuide();
            var raised = new List<(GuideState, GuideStateReason)>();
            guide.StateChanged += (state, reason) => raised.Add((state, reason));

            guide.SetHeadsetMounted(false);
            guide.SetHeadsetMounted(true);
            guide.SubmitScan("t1", MarkerPose, tours, out _);

            Assert.That(raised, Is.EqualTo(new[]
            {
                (GuideState.Suspended, GuideStateReason.HeadsetRemoved),
                (GuideState.AwaitingScan, GuideStateReason.HeadsetMounted),
                (GuideState.Anchored, GuideStateReason.Scanned),
            }));
        }
    }
}
