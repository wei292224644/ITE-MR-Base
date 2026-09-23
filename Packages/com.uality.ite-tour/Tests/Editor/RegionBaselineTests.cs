using System;
using NUnit.Framework;
using Uality.IteTour.Core;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 这次区域变化是人动了，还是锚定把体积挪了（ite-current-tour D9）。锚定后第一次物理步里到达的进出
    /// 都算锚定造成的：关窗时并入基准，不算人移动。
    /// </summary>
    public class RegionBaselineTests
    {
        private static readonly string[] Empty = new string[0];

        [Test]
        public void Fresh_IsNotSettling_AndBaselineIsEmpty()
        {
            var baseline = new RegionBaseline();

            Assert.That(baseline.IsSettling, Is.False);
            Assert.That(baseline.Baseline, Is.Empty);
        }

        [Test]
        public void Advance_MovesBaseline()
        {
            var baseline = new RegionBaseline();

            baseline.Advance(new[] { "a" });

            Assert.That(baseline.Baseline, Is.EqualTo(new[] { "a" }));
        }

        /// <summary>PICO 14:36:31：锚定前在 ujf5 里，锚定后那一步物理里 ujf5 离开、hncx 进入。</summary>
        [Test]
        public void BeginSettle_OneStep_ClosesAfterFirstPhysicsStep_AndRebases()
        {
            var baseline = new RegionBaseline();
            baseline.Advance(new[] { "ujf5bo31_frb" });

            baseline.BeginSettle(1);
            Assert.That(baseline.IsSettling, Is.True);

            Assert.That(baseline.AfterPhysicsStep(new[] { "hncxtzfe_p4d" }), Is.True, "这一步关窗");
            Assert.That(baseline.IsSettling, Is.False);
            Assert.That(baseline.Baseline, Is.EqualTo(new[] { "hncxtzfe_p4d" }), "锚定造成的变化并入基准");
        }

        /// <summary>锚定发生在物理阶段内：这一步的模拟可能早于锚定就跑完了，要再等一步。</summary>
        [Test]
        public void BeginSettle_TwoSteps_FirstStepDoesNotClose()
        {
            var baseline = new RegionBaseline();

            baseline.BeginSettle(2);

            Assert.That(baseline.AfterPhysicsStep(new[] { "a" }), Is.False);
            Assert.That(baseline.IsSettling, Is.True);
            Assert.That(baseline.AfterPhysicsStep(new[] { "b" }), Is.True);
            Assert.That(baseline.Baseline, Is.EqualTo(new[] { "b" }));
        }

        [Test]
        public void AfterPhysicsStep_WhenNotSettling_LeavesBaselineAlone()
        {
            var baseline = new RegionBaseline();
            baseline.Advance(new[] { "a" });

            Assert.That(baseline.AfterPhysicsStep(new[] { "b" }), Is.False);
            Assert.That(baseline.Baseline, Is.EqualTo(new[] { "a" }), "窗口外基准只在帧末前移");
        }

        [Test]
        public void BeginSettle_WhileSettling_KeepsTheLongerWindow()
        {
            var baseline = new RegionBaseline();

            baseline.BeginSettle(2);
            baseline.BeginSettle(1);

            Assert.That(baseline.AfterPhysicsStep(Empty), Is.False);
            Assert.That(baseline.AfterPhysicsStep(Empty), Is.True);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void BeginSettle_NonPositiveSteps_Throws(int steps)
        {
            var baseline = new RegionBaseline();

            Assert.Throws<ArgumentOutOfRangeException>(() => baseline.BeginSettle(steps));
        }
    }
}
