using System.Collections.Generic;
using NUnit.Framework;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 扫描决策是这个包里最容易在 PICO 上出问题、又最难在真机上复现的一段。
    /// D14 把它从「三个订阅者的相互作用 + `_canAnchor` 的 await 竞态」抽成纯函数，
    /// 就是为了让这组测试能够存在。
    /// </summary>
    public class TourScanPolicyTests
    {
        private const IteSpaceScene.Tour.DisplayType Normal = IteSpaceScene.Tour.DisplayType.normal;
        private const IteSpaceScene.Tour.DisplayType Regional = IteSpaceScene.Tour.DisplayType.regionalTrigger;
        private const IteSpaceScene.Tour.DisplayType Always = IteSpaceScene.Tour.DisplayType.alwaysDisplayed;

        private static TourDescriptor Tour(
            string id,
            IteSpaceScene.Tour.DisplayType type = Normal,
            bool secondAnchorAvailable = false)
            => new TourDescriptor
            {
                TourId = id,
                DisplayType = type,
                SecondAnchorAvailable = secondAnchorAvailable,
            };

        private static List<TourDescriptor> Tours(params TourDescriptor[] tours) => new List<TourDescriptor>(tours);

        /// <summary>相机当前所在的触发体积对应的 tourId。</summary>
        private static List<string> InVolumes(params string[] tourIds) => new List<string>(tourIds);

        // ---- 前置门禁 ----

        [Test]
        public void Decide_WhenPaused_Ignores()
        {
            var state = new ScanState { Paused = true, ForcedScanPending = true };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1")), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
        }

        [TestCase(null)]
        [TestCase("")]
        public void Decide_WithoutMarkerId_Ignores(string markerId)
        {
            var state = new ScanState { ForcedScanPending = true };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1")), markerId);

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
        }

        [Test]
        public void Decide_WhenMarkerMatchesNoTour_Ignores()
        {
            var state = new ScanState { ForcedScanPending = true };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1")), "somethingElse");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
        }

        // ---- 强制扫码 ----

        [TestCase(Normal)]
        [TestCase(Regional)]
        [TestCase(Always)]
        public void Decide_ForcedScan_ActivatesAnyDisplayTypeAndClearsTheFlag(IteSpaceScene.Tour.DisplayType type)
        {
            var state = new ScanState { ForcedScanPending = true };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1", type)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(decision.TourId, Is.EqualTo("t1"));
            Assert.That(decision.ClearsForcedScan, Is.True);
        }

        [Test]
        public void Decide_ForcedScan_IgnoresPendingTourFilter()
        {
            // 强制扫码先于区域过滤：刚戴上头显时相机可能不在任何触发体积内
            var state = new ScanState
            {
                ForcedScanPending = true,
                PendingTourIds = new List<string> { "other" },
            };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1")), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
        }

        /// <summary>
        /// D14 所选语义，单独钉住：强制扫码激活 regionalTrigger 后，本次扫码
        /// **不**消耗其二次锚定许可。
        ///
        /// 源实现中第二个处理器此刻会去查 CanSecondAnchor()，而 Enable() 尚未完成、
        /// _canAnchor 仍为 false，因此不消耗。内容为空的 Tour 会同步走完 Enable()
        /// 从而走出另一分支——那个偶然分支本次被规范掉了。
        /// </summary>
        [Test]
        public void Decide_ForcedScanOnRegionalTrigger_DoesNotConsumeSecondAnchor_D14()
        {
            var state = new ScanState { ForcedScanPending = true };

            var decision = TourScanPolicy.Decide(
                state, Tours(Tour("t1", Regional, secondAnchorAvailable: true)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(decision.ConsumesSecondAnchor, Is.False);
        }

        /// <summary>
        /// D36：冷启动、重新戴上、追踪原点重置三者都落到强制扫码，此刻体积位置还没（重新）
        /// 锚定过，站在哪都不算数——相机在所有体积外也必须能扫。
        /// </summary>
        [Test]
        public void Decide_ForcedScan_ActivatesOutsideAllVolumes_D36()
        {
            var state = new ScanState { ForcedScanPending = true, PendingTourIds = InVolumes() };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1")), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
        }

        // ---- 区域门禁（D36）：定位过之后，只认相机所在体积的码 ----

        [Test]
        public void Decide_WhenMarkerOutsidePendingTours_Ignores()
        {
            var state = new ScanState { PendingTourIds = InVolumes("t2") };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1"), Tour("t2")), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
        }

        [Test]
        public void Decide_AfterAnchoring_OutsideAllVolumes_Ignores_D36()
        {
            var state = new ScanState { PendingTourIds = InVolumes() };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1")), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore),
                "源实现在体积外不设限；D36 起定位过之后必须走进该 tour 的体积");
        }

        [Test]
        public void Decide_AfterAnchoring_WithoutVolumeSet_Ignores_D36()
        {
            var state = new ScanState { PendingTourIds = null };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1")), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
        }

        // ---- normal ----

        [Test]
        public void Decide_NormalTourNotActive_Activates()
        {
            var state = new ScanState { ActiveTourId = "other", PendingTourIds = InVolumes("t1") };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1", Normal)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(decision.TourId, Is.EqualTo("t1"));
        }

        [Test]
        public void Decide_NormalTourAlreadyActive_Ignores()
        {
            var state = new ScanState { ActiveTourId = "t1", PendingTourIds = InVolumes("t1") };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1", Normal)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
        }

        // ---- regionalTrigger ----

        [Test]
        public void Decide_RegionalTourNotActive_ActivatesWithoutConsumingSecondAnchor()
        {
            var state = new ScanState { ActiveTourId = "other", PendingTourIds = InVolumes("t1") };

            var decision = TourScanPolicy.Decide(
                state, Tours(Tour("t1", Regional, secondAnchorAvailable: true)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(decision.ConsumesSecondAnchor, Is.False,
                "源实现此处的 SecondAnchored() 会被 Enable() 续体覆盖，是死代码（D14）");
        }

        [Test]
        public void Decide_RegionalTourActiveWithAllowance_ReanchorsAndConsumesIt()
        {
            var state = new ScanState { ActiveTourId = "t1", PendingTourIds = InVolumes("t1") };

            var decision = TourScanPolicy.Decide(
                state, Tours(Tour("t1", Regional, secondAnchorAvailable: true)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Reanchor));
            Assert.That(decision.TourId, Is.EqualTo("t1"));
            Assert.That(decision.ConsumesSecondAnchor, Is.True,
                "重锚路径不触发 Enable()，所以这次消耗真正生效");
        }

        [Test]
        public void Decide_RegionalTourActiveWithoutAllowance_Ignores()
        {
            var state = new ScanState { ActiveTourId = "t1", PendingTourIds = InVolumes("t1") };

            var decision = TourScanPolicy.Decide(
                state, Tours(Tour("t1", Regional, secondAnchorAvailable: false)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
        }

        // ---- alwaysDisplayed ----

        [Test]
        public void Decide_AlwaysDisplayedTour_IgnoredOutsideForcedScan()
        {
            var state = new ScanState { ActiveTourId = "other", PendingTourIds = InVolumes("t1") };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1", Always)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore),
                "alwaysDisplayed 始终显示，不参与扫码切换");
        }

        // ---- 重复推入 ----

        /// <summary>
        /// ITE 需要的是**可重复触发的原始事件流**，不是去重后的（design D13）。
        /// 同一个码连扫两次，第二次必须照常求值。
        /// </summary>
        [Test]
        public void Decide_IsPurelyStateDriven_SameMarkerTwiceIsNotDeduplicated()
        {
            var tours = Tours(Tour("t1", Regional, secondAnchorAvailable: true));
            var afterActivation = new ScanState { ActiveTourId = "t1", PendingTourIds = InVolumes("t1") };

            var first = TourScanPolicy.Decide(afterActivation, tours, "t1");
            var second = TourScanPolicy.Decide(afterActivation, tours, "t1");

            Assert.That(first.Action, Is.EqualTo(ScanAction.Reanchor));
            Assert.That(second.Action, Is.EqualTo(ScanAction.Reanchor),
                "相同状态下相同输入必须给出相同决策——去重是效果层的事，不是决策的事");
        }
    }
}
