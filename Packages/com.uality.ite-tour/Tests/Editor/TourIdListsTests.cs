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

        [Test]
        public void SameSet_IgnoresOrder()
        {
            Assert.That(TourIdLists.SameSet(new[] { "a", "b" }, new[] { "b", "a" }), Is.True);
        }

        [Test]
        public void SameSet_DifferentMembers_IsFalse()
        {
            Assert.That(TourIdLists.SameSet(new[] { "a" }, new[] { "a", "b" }), Is.False);
            Assert.That(TourIdLists.SameSet(new[] { "a", "c" }, new[] { "a", "b" }), Is.False);
        }

        [Test]
        public void SameSet_NullEqualsEmpty()
        {
            Assert.That(TourIdLists.SameSet(null, new string[0]), Is.True);
            Assert.That(TourIdLists.SameSet(null, new[] { "a" }), Is.False);
        }
    }
}
