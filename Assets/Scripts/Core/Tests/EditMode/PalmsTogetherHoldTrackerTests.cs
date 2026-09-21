using NUnit.Framework;

namespace MRBase.Core.Tests
{
    public class PalmsTogetherHoldTrackerTests
    {
        [Test]
        public void Short_Pulse_Does_Not_Perform()
        {
            var tracker = new PalmsTogetherHoldTracker();
            tracker.Tick(true, holdSeconds: 0.3f, deltaTime: 0.1f, out var edge);
            Assert.IsNull(edge);
            Assert.IsFalse(tracker.IsHeld);

            tracker.Tick(false, 0.3f, 0.1f, out edge);
            Assert.IsNull(edge);
            Assert.IsFalse(tracker.IsHeld);
        }

        [Test]
        public void Sustained_Match_Performs_Once()
        {
            var tracker = new PalmsTogetherHoldTracker();
            tracker.Tick(true, 0.3f, 0.2f, out _);
            tracker.Tick(true, 0.3f, 0.2f, out var edge);
            Assert.IsTrue(edge.HasValue && edge.Value);
            Assert.IsTrue(tracker.IsHeld);

            tracker.Tick(true, 0.3f, 0.2f, out edge);
            Assert.IsNull(edge);
            Assert.IsTrue(tracker.IsHeld);
        }

        [Test]
        public void Release_After_Held_Fires_Released_Edge()
        {
            var tracker = new PalmsTogetherHoldTracker();
            tracker.Tick(true, 0.1f, 0.2f, out _);
            Assert.IsTrue(tracker.IsHeld);

            tracker.Tick(false, 0.1f, 0.1f, out var edge);
            Assert.IsTrue(edge.HasValue && !edge.Value);
            Assert.IsFalse(tracker.IsHeld);
        }
    }
}
