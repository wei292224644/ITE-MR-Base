using MRBase.Transitions;
using NUnit.Framework;

namespace MRBase.Transitions.Tests
{
    public class GroundUpRevealControllerTests
    {
        [Test]
        public void ZeroProgress_StartsBelowFloorByNoiseAndEdgePadding()
        {
            float height = GroundUpRevealController.CalculateRevealHeight(
                floorHeight: 1.2f,
                verticalExtent: 6f,
                frontNoiseStrength: 0.2f,
                frontEdgeWidth: 0.15f,
                normalizedProgress: 0f);

            Assert.That(height, Is.EqualTo(0.85f).Within(0.0001f));
        }

        [Test]
        public void FullProgress_EndsAboveHighestPointByNoiseAndEdgePadding()
        {
            float height = GroundUpRevealController.CalculateRevealHeight(
                floorHeight: -0.25f,
                verticalExtent: 5f,
                frontNoiseStrength: 0.3f,
                frontEdgeWidth: 0.2f,
                normalizedProgress: 1f);

            Assert.That(height, Is.EqualTo(5.25f).Within(0.0001f));
        }

        [TestCase(-2f, 1f)]
        [TestCase(3f, 5f)]
        public void ProgressIsClamped(float progress, float expectedHeight)
        {
            float height = GroundUpRevealController.CalculateRevealHeight(
                floorHeight: 1f,
                verticalExtent: 4f,
                frontNoiseStrength: 0f,
                frontEdgeWidth: 0f,
                normalizedProgress: progress);

            Assert.That(height, Is.EqualTo(expectedHeight).Within(0.0001f));
        }
    }
}
