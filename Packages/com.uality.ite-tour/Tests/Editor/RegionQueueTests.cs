using NUnit.Framework;
using Uality.IteTour.Core;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 区域队列（ite-current-tour D1、D2、D11）。相机侧有两个碰撞体（Main Camera 上的球、XR Origin 上的
    /// CharacterController 胶囊），要全部离开才算离开——PICO 实测（2026-09-23）的成对进出在这里回放。
    /// </summary>
    public class RegionQueueTests
    {
        [Test]
        public void Enter_AppendsInEntryOrder()
        {
            var queue = new RegionQueue();

            queue.Enter("a");
            queue.Enter("b");

            Assert.That(queue.TourIds, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(queue.CountOf("a"), Is.EqualTo(1));
        }

        [Test]
        public void SecondColliderEnter_CountsWithoutReordering()
        {
            var queue = new RegionQueue();

            queue.Enter("a");
            queue.Enter("b");
            queue.Enter("a");

            Assert.That(queue.TourIds, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(queue.CountOf("a"), Is.EqualTo(2));
        }

        /// <summary>PICO 14:36:36–39：qtcljiro 两次 Enter、两次 Exit，第一次 Exit 之后还有一个碰撞体在里面。</summary>
        [Test]
        public void TwoColliders_LeavesOnlyWhenBothExit()
        {
            var queue = new RegionQueue();
            queue.Enter("qtcljiro_zrx");
            queue.Enter("qtcljiro_zrx");

            Assert.That(queue.Exit("qtcljiro_zrx"), Is.True);
            Assert.That(queue.TourIds, Is.EqualTo(new[] { "qtcljiro_zrx" }), "还有一个碰撞体在体积里");

            Assert.That(queue.Exit("qtcljiro_zrx"), Is.True);
            Assert.That(queue.TourIds, Is.Empty);
            Assert.That(queue.CountOf("qtcljiro_zrx"), Is.EqualTo(0));
        }

        [Test]
        public void ReenterAfterLeaving_MovesToTail()
        {
            var queue = new RegionQueue();
            queue.Enter("a");
            queue.Enter("b");

            queue.Exit("a");
            queue.Enter("a");

            Assert.That(queue.TourIds, Is.EqualTo(new[] { "b", "a" }));
        }

        [Test]
        public void ExitWithoutEnter_IsRejected_AndCountNeverNegative()
        {
            var queue = new RegionQueue();

            Assert.That(queue.Exit("a"), Is.False);
            Assert.That(queue.CountOf("a"), Is.EqualTo(0));

            queue.Enter("a");
            Assert.That(queue.TourIds, Is.EqualTo(new[] { "a" }), "被拒的 Exit 没有留下欠账");
            Assert.That(queue.CountOf("a"), Is.EqualTo(1));
        }

        [Test]
        public void Clear_DropsTourRegardlessOfCount()
        {
            var queue = new RegionQueue();
            queue.Enter("a");
            queue.Enter("a");
            queue.Enter("b");

            queue.Clear("a");

            Assert.That(queue.TourIds, Is.EqualTo(new[] { "b" }));
            Assert.That(queue.CountOf("a"), Is.EqualTo(0));
        }

        [Test]
        public void Clear_UnknownTour_IsNoOp()
        {
            var queue = new RegionQueue();
            queue.Enter("a");
            var before = queue.TourIds;

            queue.Clear("zzz");
            queue.Clear(null);

            Assert.That(queue.TourIds, Is.SameAs(before));
        }

        [TestCase(null)]
        [TestCase("")]
        public void Enter_EmptyId_IsIgnored(string tourId)
        {
            var queue = new RegionQueue();

            queue.Enter(tourId);

            Assert.That(queue.TourIds, Is.Empty);
        }

        [Test]
        public void TourIds_IsReplacedNotMutated()
        {
            var queue = new RegionQueue();
            queue.Enter("a");
            var before = queue.TourIds;

            queue.Enter("b");
            queue.Exit("a");

            Assert.That(before, Is.EqualTo(new[] { "a" }), "帧末基准持有旧引用，不能被原地改掉");
        }
    }
}
