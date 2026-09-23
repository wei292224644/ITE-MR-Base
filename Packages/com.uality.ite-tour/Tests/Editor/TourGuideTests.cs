using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 导览状态核心（ite-guide-state-machine）。spec §3.2 的转换表逐条钉住，外加 PICO 实测
    /// 暴露的 bug：摘下头显期间区域仍能唤醒 Tour。
    /// </summary>
    public class TourGuideTests
    {
        private const IteSpaceScene.Tour.DisplayType Normal = IteSpaceScene.Tour.DisplayType.normal;
        private const IteSpaceScene.Tour.DisplayType Regional = IteSpaceScene.Tour.DisplayType.regionalTrigger;

        private static readonly Pose MarkerPose = new Pose(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f));

        private static TourDescriptor Tour(
            string id, IteSpaceScene.Tour.DisplayType type = Regional, bool secondAnchorAvailable = false)
            => new TourDescriptor { TourId = id, DisplayType = type, SecondAnchorAvailable = secondAnchorAvailable };

        private static List<TourDescriptor> Tours(params TourDescriptor[] tours) => new List<TourDescriptor>(tours);

        /// <summary>确定性挑选：总是第一个候选，让重选结果可断言。</summary>
        private static TourGuide NewGuide() => new TourGuide(ids => ids[0]);

        private static GuideEffect Frame(TourGuide guide, List<TourDescriptor> tours)
            => guide.EndOfFrame(() => tours, out _);

        /// <summary>相机在 tourId 的体积里扫它的码：走到 Anchored，tourId 在播。</summary>
        private static TourGuide AnchoredOn(string tourId, List<TourDescriptor> tours)
        {
            var guide = NewGuide();
            guide.SubmitVolumeTransition(tourId, VolumeTransition.Enter);
            Frame(guide, tours);
            guide.SubmitScan(tourId, MarkerPose, tours, out _);
            Frame(guide, tours);
            return guide;
        }

        // ---- 构造与扫码 ----

        [Test]
        public void Constructed_IsAwaitingScanForColdStart()
        {
            var guide = NewGuide();

            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.ColdStart));
            Assert.That(guide.ActiveTourId, Is.Null);
        }

        [Test]
        public void ScanWhileAwaiting_ActivatesWithPose_AndEntersAnchored()
        {
            var guide = NewGuide();

            var effect = guide.SubmitScan("t1", MarkerPose, Tours(Tour("t1")), out var decision);

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(effect.ActivateTourId, Is.EqualTo("t1"));
            Assert.That(effect.AnchorPose.HasValue, Is.True);
            Assert.That(effect.AnchorPose.Value, Is.EqualTo(MarkerPose));
            Assert.That(effect.Deactivate, Is.False);
            Assert.That(guide.State, Is.EqualTo(GuideState.Anchored));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.Scanned));
            Assert.That(guide.ActiveTourId, Is.EqualTo("t1"));
        }

        [Test]
        public void Anchored_RescanActiveRegional_ReanchorsWithoutRebuild()
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
        }

        // ---- 摘下 / 戴上 ----

        [Test]
        public void HeadsetRemoved_FromAnchored_StopsTour_AndSuspends()
        {
            var guide = AnchoredOn("t1", Tours(Tour("t1")));

            var effect = guide.SetHeadsetMounted(false);

            Assert.That(effect.Deactivate, Is.True);
            Assert.That(guide.State, Is.EqualTo(GuideState.Suspended));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.HeadsetRemoved));
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
            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.HeadsetMounted));
        }

        [Test]
        public void HeadsetMounted_WhenNotSuspended_ChangesNothing()
        {
            var guide = NewGuide();
            int raised = 0;
            guide.StateChanged += (state, reason) => raised++;

            guide.SetHeadsetMounted(true);

            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.ColdStart));
            Assert.That(raised, Is.EqualTo(0));
        }

        /// <summary>
        /// PICO 实测（2026-09-23）：摘下期间应用照常运行、头显仍在追踪，区域进出事件不停。
        /// 原实现摘下只置 _paused，区域策略不看它，于是摘下期间区域唤醒了 Tour。
        /// </summary>
        [Test]
        public void WhileSuspended_EnteringRegion_NeverActivates()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);
            guide.SetHeadsetMounted(false);

            guide.SubmitVolumeTransition("t1", VolumeTransition.Exit);
            guide.SubmitVolumeTransition("t2", VolumeTransition.Enter);
            var effect = Frame(guide, tours);

            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(effect.Deactivate, Is.False);
            Assert.That(guide.ActiveTourId, Is.Null);
            Assert.That(guide.PendingTourIds, Is.EquivalentTo(new[] { "t2" }), "所在区域集合照常更新（I3）");
        }

        [Test]
        public void AfterRemount_EnteringRegion_NeverActivates()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);
            guide.SetHeadsetMounted(false);
            guide.SetHeadsetMounted(true);

            guide.SubmitVolumeTransition("t2", VolumeTransition.Enter);
            var effect = Frame(guide, tours);

            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.Null);
            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
        }

        [Test]
        public void WhileSuspended_ScanIsIgnored()
        {
            var guide = NewGuide();
            guide.SetHeadsetMounted(false);

            var effect = guide.SubmitScan("t1", MarkerPose, Tours(Tour("t1")), out var decision);

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.State, Is.EqualTo(GuideState.Suspended));
        }

        /// <summary>
        /// spec §8.1 核心场景：摘下期间在 t1、t2 的体积之间走动（所在区域集合照常更新，I3），
        /// 戴上后扫 t1 的码——等待扫码不看区域（ite-scan-region-gate D2）——重新锚定 t1；
        /// 紧接着那一帧的区域重选不能把刚扫的 Tour 挤走：所在区域集合此刻仍是摘下前的旧值
        /// （要等物理步产生新的进出事件），据此判断会误切走刚扫的 Tour。
        /// </summary>
        [Test]
        public void AfterRemount_RegionsWalkedWhileSuspended_ScanThenNextFrameKeepsTour()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);

            guide.SetHeadsetMounted(false);
            guide.SubmitVolumeTransition("t1", VolumeTransition.Exit);
            guide.SubmitVolumeTransition("t2", VolumeTransition.Enter);
            Frame(guide, tours);

            guide.SetHeadsetMounted(true);
            Frame(guide, tours);

            var scanEffect = guide.SubmitScan("t1", MarkerPose, tours, out var decision);
            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate), "等待扫码不看区域，t1 不在集合里也能扫");
            Assert.That(scanEffect.ActivateTourId, Is.EqualTo("t1"));

            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.False);
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.EqualTo("t1"));
            Assert.That(guide.State, Is.EqualTo(GuideState.Anchored));
        }

        // ---- 运行中要求重扫 ----

        [Test]
        public void RequireScan_FromAnchored_StopsTour_AndAwaitsScan()
        {
            var guide = AnchoredOn("t1", Tours(Tour("t1")));

            var effect = guide.RequireScan(GuideStateReason.Recentered);

            Assert.That(effect.Deactivate, Is.True, "锚定作废后不能继续显示错位的内容（ite-guide-state-machine D2）");
            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.Recentered));
            Assert.That(guide.ActiveTourId, Is.Null);
        }

        [Test]
        public void RequireScan_WhileSuspended_StaysSuspended()
        {
            var guide = NewGuide();
            guide.SetHeadsetMounted(false);

            var effect = guide.RequireScan(GuideStateReason.Recentered);

            Assert.That(effect.Deactivate, Is.False);
            Assert.That(guide.State, Is.EqualTo(GuideState.Suspended), "摘下期间不能开始认扫码（ite-guide-state-machine D3）");
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.HeadsetRemoved));
        }

        [Test]
        public void RequireScan_WhileAwaiting_UpdatesReasonOnce()
        {
            var guide = NewGuide();
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
            var guide = NewGuide();

            Assert.That(guide.TryActivateById("t1", out var effect), Is.False, "等待扫码时冒出在播 Tour 会违反 I1（ite-guide-state-machine D4）");
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.Null);
        }

        [Test]
        public void TryActivateById_WhenAnchored_SwitchesWithoutAnchoring()
        {
            var guide = AnchoredOn("t1", Tours(Tour("t1"), Tour("t2")));

            Assert.That(guide.TryActivateById("t2", out var effect), Is.True);
            Assert.That(effect.Deactivate, Is.True);
            Assert.That(effect.ActivateTourId, Is.EqualTo("t2"));
            Assert.That(effect.AnchorPose.HasValue, Is.False, "沿用现有锚定");
            Assert.That(guide.ActiveTourId, Is.EqualTo("t2"));
        }

        // ---- 帧末区域重选（ite-guide-state-machine D5）----

        [Test]
        public void Anchored_LeavingActiveRegion_SwitchesToRegionalInRange()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);
            guide.SubmitVolumeTransition("t2", VolumeTransition.Enter);
            Assert.That(Frame(guide, tours).ActivateTourId, Is.Null, "t1 仍在范围内，不换");

            guide.SubmitVolumeTransition("t1", VolumeTransition.Exit);
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.True);
            Assert.That(effect.ActivateTourId, Is.EqualTo("t2"));
            Assert.That(effect.AnchorPose.HasValue, Is.False, "区域唤醒沿用现有锚定");
            Assert.That(guide.ActiveTourId, Is.EqualTo("t2"));
        }

        [Test]
        public void Anchored_ExitAndReenterSameFrame_KeepsTour()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);
            guide.SubmitVolumeTransition("t2", VolumeTransition.Enter);
            Frame(guide, tours);

            guide.SubmitVolumeTransition("t1", VolumeTransition.Exit);
            guide.SubmitVolumeTransition("t1", VolumeTransition.Enter);
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.False, "集合净变化为零，不该切走 t1");
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.EqualTo("t1"));
        }

        [Test]
        public void Anchored_NothingPlayingAndNoRegionalInRange_NoEffect()
        {
            var tours = Tours(Tour("n1", Normal), Tour("n2", Normal));
            var guide = AnchoredOn("n1", tours);
            guide.SubmitVolumeTransition("n2", VolumeTransition.Enter);
            Frame(guide, tours);
            guide.SubmitVolumeTransition("n1", VolumeTransition.Exit);
            Assert.That(Frame(guide, tours).Deactivate, Is.True, "离开在播 normal 的区域：停用，范围内没有 regionalTrigger 可换");

            guide.SubmitVolumeTransition("n2", VolumeTransition.Exit);
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.False);
            Assert.That(effect.ActivateTourId, Is.Null);
        }

        /// <summary>
        /// 刚锚定完的那一帧，集合还是锚定前体积位置下的值（要等物理步产生进出事件），
        /// 不能据此切走刚扫的 Tour。
        /// </summary>
        [Test]
        public void JustAnchored_UnchangedSet_DoesNotReselect()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = NewGuide();
            guide.SubmitVolumeTransition("t2", VolumeTransition.Enter);
            Frame(guide, tours);

            guide.SubmitScan("t1", MarkerPose, tours, out _); // 等待扫码不看区域：t1 不在集合里也激活
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.False);
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.EqualTo("t1"));
        }

        /// <summary>
        /// 扫码这一帧之前，区域集合已经发生了变化（进了某个体积，但还没过帧末）：扫码把
        /// AwaitingScan 切成 Anchored 之后，区域重选基准要立刻跟上当前集合，否则下一次帧末
        /// 结算会拿锚定前的旧基准跟新集合比，误判成净变化，把刚扫到的 Tour 挤走
        /// （ite-guide-state-machine D5；不同于 <see cref="JustAnchored_UnchangedSet_DoesNotReselect"/>——
        /// 那个用例的区域变化已经过了一次帧末，基准早就跟上了）。
        /// </summary>
        [Test]
        public void JustAnchored_RegionChangedBeforeScan_DoesNotReselect()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = NewGuide();
            Frame(guide, tours);

            guide.SubmitVolumeTransition("t2", VolumeTransition.Enter); // 本帧内变化，尚未过帧末

            var scanEffect = guide.SubmitScan("t1", MarkerPose, tours, out _);
            Assert.That(scanEffect.ActivateTourId, Is.EqualTo("t1"));

            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.False);
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.EqualTo("t1"));
        }

        // ---- 帧末快照（ite-guide-state-machine D6）与状态广播（ite-guide-state-machine D8）----

        [Test]
        public void EndOfFrame_ReportsChangeOnlyWhenSnapshotChanges()
        {
            var tours = Tours(Tour("t1"));
            var guide = NewGuide();

            guide.EndOfFrame(() => tours, out bool first);
            guide.EndOfFrame(() => tours, out bool idle);
            guide.SubmitVolumeTransition("t1", VolumeTransition.Enter);
            guide.EndOfFrame(() => tours, out bool afterEnter);
            guide.SetHeadsetMounted(false);
            guide.EndOfFrame(() => tours, out bool afterRemove);

            Assert.That(first, Is.True, "第一帧必须算一次提示");
            Assert.That(idle, Is.False);
            Assert.That(afterEnter, Is.True);
            Assert.That(afterRemove, Is.True);
        }

        [Test]
        public void StateChanged_CarriesReason()
        {
            var tours = Tours(Tour("t1"));
            var guide = NewGuide();
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
