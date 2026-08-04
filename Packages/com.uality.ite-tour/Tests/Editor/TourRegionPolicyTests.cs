using System.Collections.Generic;
using NUnit.Framework;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 相机进出触发体积后要不要换 Tour。源实现把「更新集合」「判断要不要重选」
    /// 「随机挑一个」「0.01 秒去抖协程」揉在一起，本处只保留前两件——
    /// 挑选与去抖归效果层。
    /// </summary>
    public class TourRegionPolicyTests
    {
        private const IteSpaceScene.Tour.DisplayType Normal = IteSpaceScene.Tour.DisplayType.normal;
        private const IteSpaceScene.Tour.DisplayType Regional = IteSpaceScene.Tour.DisplayType.regionalTrigger;
        private const IteSpaceScene.Tour.DisplayType Always = IteSpaceScene.Tour.DisplayType.alwaysDisplayed;

        private static TourDescriptor Tour(string id, IteSpaceScene.Tour.DisplayType type)
            => new TourDescriptor { TourId = id, DisplayType = type };

        private static List<TourDescriptor> Tours(params TourDescriptor[] tours) => new List<TourDescriptor>(tours);

        private static List<string> Pending(params string[] ids) => new List<string>(ids);

        // ---- 集合增删 ----

        [Test]
        public void Decide_OnEnter_AddsTourToPendingSet()
        {
            var state = new ScanState { PendingTourIds = Pending() };

            var decision = TourRegionPolicy.Decide(state, Tours(Tour("t1", Normal)), "t1", VolumeTransition.Enter);

            Assert.That(decision.PendingTourIds, Is.EquivalentTo(new[] { "t1" }));
        }

        [Test]
        public void Decide_OnExit_RemovesTourFromPendingSet()
        {
            var state = new ScanState { PendingTourIds = Pending("t1", "t2") };

            var decision = TourRegionPolicy.Decide(state, Tours(Tour("t1", Normal)), "t1", VolumeTransition.Exit);

            Assert.That(decision.PendingTourIds, Is.EquivalentTo(new[] { "t2" }));
        }

        [Test]
        public void Decide_OnRepeatedEnter_DoesNotDuplicate()
        {
            var state = new ScanState { PendingTourIds = Pending("t1") };

            var decision = TourRegionPolicy.Decide(state, Tours(Tour("t1", Normal)), "t1", VolumeTransition.Enter);

            Assert.That(decision.PendingTourIds, Is.EquivalentTo(new[] { "t1" }));
        }

        [Test]
        public void Decide_DoesNotMutateTheIncomingSet()
        {
            var original = Pending("t1");
            var state = new ScanState { PendingTourIds = original };

            TourRegionPolicy.Decide(state, Tours(Tour("t2", Normal)), "t2", VolumeTransition.Enter);

            Assert.That(original, Is.EquivalentTo(new[] { "t1" }), "纯函数不得改动传入集合");
        }

        // ---- 是否重选 ----

        [Test]
        public void Decide_WhileForcedScanPending_NeverReselects()
        {
            var state = new ScanState { ForcedScanPending = true, PendingTourIds = Pending() };

            var decision = TourRegionPolicy.Decide(state, Tours(Tour("t1", Regional)), "t1", VolumeTransition.Enter);

            Assert.That(decision.ShouldReselect, Is.False, "还没扫过第一次码，不能自行切 Tour");
            Assert.That(decision.PendingTourIds, Is.EquivalentTo(new[] { "t1" }), "但集合照常更新");
        }

        [Test]
        public void Decide_WhenActiveTourStillInRange_DoesNotReselect()
        {
            var state = new ScanState { ActiveTourId = "t1", PendingTourIds = Pending("t1") };

            var decision = TourRegionPolicy.Decide(state, Tours(Tour("t2", Regional)), "t2", VolumeTransition.Enter);

            Assert.That(decision.ShouldReselect, Is.False, "当前 Tour 仍在范围内就不该被打断");
        }

        [Test]
        public void Decide_OnEnteringOwnActiveTour_DoesNotReselect()
        {
            var state = new ScanState { ActiveTourId = "t1", PendingTourIds = Pending() };

            var decision = TourRegionPolicy.Decide(state, Tours(Tour("t1", Regional)), "t1", VolumeTransition.Enter);

            Assert.That(decision.ShouldReselect, Is.False);
        }

        [Test]
        public void Decide_OnExitingSomeoneElsesTour_DoesNotReselect()
        {
            var state = new ScanState { ActiveTourId = "t1", PendingTourIds = Pending("t2") };

            var decision = TourRegionPolicy.Decide(state, Tours(Tour("t2", Regional)), "t2", VolumeTransition.Exit);

            Assert.That(decision.ShouldReselect, Is.False);
        }

        [Test]
        public void Decide_OnExitingTheActiveTour_Reselects()
        {
            var state = new ScanState { ActiveTourId = "t1", PendingTourIds = Pending("t1", "t2") };

            var decision = TourRegionPolicy.Decide(
                state, Tours(Tour("t1", Regional), Tour("t2", Regional)), "t1", VolumeTransition.Exit);

            Assert.That(decision.ShouldReselect, Is.True);
            Assert.That(decision.ReselectCandidates, Is.EquivalentTo(new[] { "t2" }));
        }

        // ---- 候选集 ----

        [Test]
        public void Decide_CandidatesAreRegionalTriggerToursInsidePendingSet()
        {
            // 当前激活的是 r2，相机刚离开它的触发体积
            var state = new ScanState { ActiveTourId = "r2", PendingTourIds = Pending("r1", "n1", "a1") };
            var tours = Tours(
                Tour("r1", Regional),
                Tour("r2", Regional),   // 已离开，不在待扫描集合内
                Tour("n1", Normal),     // 类型不符
                Tour("a1", Always));    // 类型不符

            var decision = TourRegionPolicy.Decide(state, tours, "r2", VolumeTransition.Exit);

            Assert.That(decision.ShouldReselect, Is.True);
            Assert.That(decision.ReselectCandidates, Is.EquivalentTo(new[] { "r1" }),
                "只有待扫描集合内的 regionalTrigger 才是候选");
        }

        [Test]
        public void Decide_WhenNoRegionalTriggerInRange_ReselectsWithEmptyCandidates()
        {
            // 源实现的语义：当前 Tour 照样停用，只是没有新的可激活
            var state = new ScanState { ActiveTourId = "t1", PendingTourIds = Pending("n1") };
            var tours = Tours(Tour("t1", Regional), Tour("n1", Normal));

            var decision = TourRegionPolicy.Decide(state, tours, "t1", VolumeTransition.Exit);

            Assert.That(decision.ShouldReselect, Is.True);
            Assert.That(decision.ReselectCandidates, Is.Empty);
        }

        /// <summary>
        /// 策略只给候选集，不替调用方挑。源实现用 OrderBy(Guid.NewGuid()) 随机取一个，
        /// 把随机性埋在 LINQ 链里——决策因此不可测，「为什么是随机」也无处说明。
        /// </summary>
        [Test]
        public void Decide_IsDeterministic_CandidateOrderFollowsTourOrder()
        {
            // 当前激活的是 r0，相机刚离开它；r1/r2/r3 都还在范围内
            var state = new ScanState { ActiveTourId = "r0", PendingTourIds = Pending("r1", "r2", "r3") };
            var tours = Tours(
                Tour("r0", Regional), Tour("r1", Regional), Tour("r2", Regional), Tour("r3", Regional));

            var first = TourRegionPolicy.Decide(state, tours, "r0", VolumeTransition.Exit);
            var second = TourRegionPolicy.Decide(state, tours, "r0", VolumeTransition.Exit);

            Assert.That(first.ReselectCandidates, Is.EqualTo(new[] { "r1", "r2", "r3" }));
            Assert.That(second.ReselectCandidates, Is.EqualTo(first.ReselectCandidates),
                "相同输入必须给出相同候选集；随机挑选是效果层的显式选择");
        }
    }
}
