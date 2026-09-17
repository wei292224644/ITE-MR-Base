using NUnit.Framework;
using Uality.IteTour.Core;

namespace Uality.IteTour.Tests
{
    public class TourSceneLifecycleTests
    {
        [Test]
        public void TryBeginBuild_FromEmpty_IssuesAGenerationAndEntersBuilding()
        {
            var life = new TourSceneLifecycle();

            Assert.That(life.TryBeginBuild(out var generation), Is.True);
            Assert.That(generation, Is.EqualTo(1));
            Assert.That(life.Current, Is.EqualTo(TourSceneLifecycle.State.Building));
            Assert.That(life.IsCurrentBuild(generation), Is.True);
        }

        [Test]
        public void TryBeginBuild_WhileBuildingOrReady_Refuses()
        {
            var life = new TourSceneLifecycle();
            life.TryBeginBuild(out var first);

            Assert.That(life.TryBeginBuild(out _), Is.False);

            Assert.That(life.TryMarkReady(first), Is.True);
            Assert.That(life.TryBeginBuild(out _), Is.False);
            Assert.That(life.IsReady, Is.True);
        }

        [Test]
        public void TryMarkReady_WrongGeneration_LeavesStateUnchanged()
        {
            var life = new TourSceneLifecycle();
            life.TryBeginBuild(out var generation);

            Assert.That(life.TryMarkReady(generation + 1), Is.False);
            Assert.That(life.Current, Is.EqualTo(TourSceneLifecycle.State.Building));
        }

        [Test]
        public void TearDown_InvalidatesInFlightBuild()
        {
            var life = new TourSceneLifecycle();
            life.TryBeginBuild(out var generation);
            life.TearDown();

            Assert.That(life.Current, Is.EqualTo(TourSceneLifecycle.State.Empty));
            Assert.That(life.IsCurrentBuild(generation), Is.False);
            Assert.That(life.TryMarkReady(generation), Is.False);
        }

        [Test]
        public void TearDown_ThenBeginBuild_UsesANewGeneration()
        {
            var life = new TourSceneLifecycle();
            life.TryBeginBuild(out var first);
            life.TearDown();

            Assert.That(life.TryBeginBuild(out var second), Is.True);
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(life.TryMarkReady(first), Is.False);
            Assert.That(life.TryMarkReady(second), Is.True);
        }

        [Test]
        public void Abandon_OnlyAffectsMatchingBuildingGeneration()
        {
            var life = new TourSceneLifecycle();
            life.TryBeginBuild(out var generation);
            life.Abandon(generation + 1);
            Assert.That(life.Current, Is.EqualTo(TourSceneLifecycle.State.Building));

            life.Abandon(generation);
            Assert.That(life.Current, Is.EqualTo(TourSceneLifecycle.State.Empty));
        }

        [Test]
        public void Destroy_BlocksFurtherBuilds()
        {
            var life = new TourSceneLifecycle();
            life.TryBeginBuild(out var generation);
            life.Destroy();

            Assert.That(life.IsDestroyed, Is.True);
            Assert.That(life.TryBeginBuild(out _), Is.False);
            Assert.That(life.TryMarkReady(generation), Is.False);

            life.TearDown();
            Assert.That(life.IsDestroyed, Is.True);
        }
    }
}
