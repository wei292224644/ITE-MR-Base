using NUnit.Framework;
using Uality.IteTour.Core;

namespace Uality.IteTour.Tests
{
    public class TourIdListsTests
    {
        [Test]
        public void Contains_FindsExactId()
        {
            Assert.That(TourIdLists.Contains(new[] { "a", "b" }, "b"), Is.True);
            Assert.That(TourIdLists.Contains(new[] { "a", "b" }, "c"), Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        public void Contains_EmptyId_IsFalse(string id)
        {
            Assert.That(TourIdLists.Contains(new[] { "a" }, id), Is.False);
        }

        [Test]
        public void Contains_NullList_IsFalse()
        {
            Assert.That(TourIdLists.Contains(null, "a"), Is.False);
        }
    }
}
