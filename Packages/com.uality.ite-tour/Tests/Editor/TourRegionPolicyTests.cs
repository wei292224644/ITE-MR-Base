using System.Collections.Generic;
using NUnit.Framework;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 相机进出触发体积之后要不要换 Tour（ite-guide-state-machine D5）。集合增删（Apply）与
    /// 帧末重选判断（Decide）分开：进出事件只改集合，帧末按集合相对上一帧末的净变化判一次，
    /// 且只在 Anchored 下判。挑选（随机）归调用方。
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

        private static ScanState Anchored(string activeTourId, params string[] pending)
            => new ScanState { State = GuideState.Anchored, ActiveTourId = activeTourId, PendingTourIds = Pending(pending) };

        // ---- 集合增删 ----

        [Test]
        public void Apply_OnEnter_AddsTour()
        {
            Assert.That(TourRegionPolicy.Apply(Pending(), "t1", VolumeTransition.Enter), Is.EquivalentTo(new[] { "t1" }));
        }

        [Test]
        public void Apply_OnExit_RemovesTour()
        {
            Assert.That(
                TourRegionPolicy.Apply(Pending("t1", "t2"), "t1", VolumeTransition.Exit),
                Is.EquivalentTo(new[] { "t2" }));
        }

        [Test]
        public void Apply_OnRepeatedEnter_DoesNotDuplicate()
        {
            Assert.That(TourRegionPolicy.Apply(Pending("t1"), "t1", VolumeTransition.Enter), Is.EquivalentTo(new[] { "t1" }));
        }

        [Test]
        public void Apply_DoesNotMutateTheIncomingSet()
        {
            var original = Pending("t1");

            TourRegionPolicy.Apply(original, "t2", VolumeTransition.Enter);

            Assert.That(original, Is.EquivalentTo(new[] { "t1" }), "纯函数不得改动传入集合");
        }

        [Test]
        public void Apply_NullSet_IsTreatedAsEmpty()
        {
            Assert.That(TourRegionPolicy.Apply(null, "t1", VolumeTransition.Enter), Is.EquivalentTo(new[] { "t1" }));
        }

        // ---- 帧末重选 ----

        [TestCase(GuideState.Suspended)]
        [TestCase(GuideState.AwaitingScan)]
        public void Decide_WhenNotAnchored_NeverReselects(GuideState notAnchored)
        {
            var state = new ScanState { State = notAnchored, PendingTourIds = Pending("t1") };

            var decision = TourRegionPolicy.Decide(state, Tours(Tour("t1", Regional)), Pending());

            Assert.That(decision.ShouldReselect, Is.False, "只有已定位才允许区域唤醒 Tour（I2）");
        }

        [Test]
        public void Decide_WhenSetUnchanged_DoesNotReselect()
        {
            var decision = TourRegionPolicy.Decide(
                Anchored(null, "t1", "t2"), Tours(Tour("t1", Regional), Tour("t2", Regional)), Pending("t2", "t1"));

            Assert.That(decision.ShouldReselect, Is.False, "按集合比较，顺序不同不算变化");
        }

        [Test]
        public void Decide_WhenActiveTourStillInRange_DoesNotReselect()
        {
            var decision = TourRegionPolicy.Decide(
                Anchored("t1", "t1", "t2"), Tours(Tour("t1", Regional), Tour("t2", Regional)), Pending("t1"));

            Assert.That(decision.ShouldReselect, Is.False, "当前 Tour 仍在范围内就不该被打断");
        }

        [Test]
        public void Decide_WhenActiveTourLeftRange_ReselectsAmongRegionalInRange()
        {
            var tours = Tours(
                Tour("r1", Regional),
                Tour("r2", Regional),   // 在播，刚离开
                Tour("n1", Normal),     // 类型不符
                Tour("a1", Always));    // 类型不符

            var decision = TourRegionPolicy.Decide(
                Anchored("r2", "r1", "n1", "a1"), tours, Pending("r1", "r2", "n1", "a1"));

            Assert.That(decision.ShouldReselect, Is.True);
            Assert.That(decision.ReselectCandidates, Is.EquivalentTo(new[] { "r1" }), "只有集合内的 regionalTrigger 才是候选");
        }

        [Test]
        public void Decide_WhenActiveTourLeftRange_AndNoRegionalInRange_ReselectsWithEmptyCandidates()
        {
            // 源实现的语义：当前 Tour 照样停用，只是没有新的可激活
            var decision = TourRegionPolicy.Decide(
                Anchored("t1", "n1"), Tours(Tour("t1", Regional), Tour("n1", Normal)), Pending("t1", "n1"));

            Assert.That(decision.ShouldReselect, Is.True);
            Assert.That(decision.ReselectCandidates, Is.Empty);
        }

        [Test]
        public void Decide_WhenNothingPlaying_AndNoRegionalInRange_DoesNothing()
        {
            var decision = TourRegionPolicy.Decide(Anchored(null, "n1"), Tours(Tour("n1", Normal)), Pending());

            Assert.That(decision.ShouldReselect, Is.False);
        }

        [Test]
        public void Decide_WhenNothingPlaying_AndRegionalInRange_Reselects()
        {
            var decision = TourRegionPolicy.Decide(Anchored(null, "r1"), Tours(Tour("r1", Regional)), Pending());

            Assert.That(decision.ShouldReselect, Is.True);
            Assert.That(decision.ReselectCandidates, Is.EquivalentTo(new[] { "r1" }));
        }

        /// <summary>
        /// 策略只给候选集，不替调用方挑。源实现用 OrderBy(Guid.NewGuid()) 随机取一个，
        /// 把随机性埋在 LINQ 链里——决策因此不可测，「为什么是随机」也无处说明。
        /// </summary>
        [Test]
        public void Decide_IsDeterministic_CandidateOrderFollowsTourOrder()
        {
            var tours = Tours(
                Tour("r0", Regional), Tour("r1", Regional), Tour("r2", Regional), Tour("r3", Regional));
            var state = Anchored("r0", "r3", "r1", "r2");
            var previous = Pending("r0", "r1", "r2", "r3");

            var first = TourRegionPolicy.Decide(state, tours, previous);
            var second = TourRegionPolicy.Decide(state, tours, previous);

            Assert.That(first.ReselectCandidates, Is.EqualTo(new[] { "r1", "r2", "r3" }));
            Assert.That(second.ReselectCandidates, Is.EqualTo(first.ReselectCandidates),
                "相同输入必须给出相同候选集；随机挑选是调用方的显式选择");
        }
    }
}
