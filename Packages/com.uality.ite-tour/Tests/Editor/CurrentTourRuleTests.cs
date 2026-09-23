using NUnit.Framework;
using Uality.IteTour.Core;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 当前 Tour（C）换成谁（ite-current-tour D3、D4）：只有人离开 C 的体积才换，换成队尾；C 为空时取队尾。
    /// 不认识展示类型——队尾是 normal 也照取，播不播由别的模块决定。
    /// </summary>
    public class CurrentTourRuleTests
    {
        private static string[] Q(params string[] ids) => ids;

        [Test]
        public void NoCurrent_TakesTail()
        {
            Assert.That(CurrentTourRule.Next(null, Q(), Q("a", "b")), Is.EqualTo("b"));
        }

        [Test]
        public void NoCurrent_EmptyQueue_StaysEmpty()
        {
            Assert.That(CurrentTourRule.Next(null, Q("a"), Q()), Is.Null);
        }

        [Test]
        public void EnteringOtherRegions_KeepsCurrent()
        {
            Assert.That(CurrentTourRule.Next("a", Q("a"), Q("a", "b")), Is.EqualTo("a"));
        }

        [Test]
        public void LeavingOtherRegions_KeepsCurrent()
        {
            Assert.That(CurrentTourRule.Next("a", Q("a", "b"), Q("a")), Is.EqualTo("a"));
        }

        [Test]
        public void CurrentLeft_TakesTail()
        {
            Assert.That(CurrentTourRule.Next("a", Q("a", "b", "c"), Q("b", "c")), Is.EqualTo("c"));
        }

        [Test]
        public void CurrentLeft_EmptyQueue_BecomesEmpty()
        {
            Assert.That(CurrentTourRule.Next("a", Q("a"), Q()), Is.Null);
        }

        [Test]
        public void CurrentLeftIntoNeighbourInOneStep_SwitchesDirectly()
        {
            Assert.That(CurrentTourRule.Next("a", Q("a"), Q("b")), Is.EqualTo("b"));
        }

        /// <summary>锚定后人不在 C 的体积里：离开、进入别的区域都不换，直到人走进 C 再走出来。</summary>
        [Test]
        public void CurrentNeverInside_IsKept_WhateverElseChanges()
        {
            Assert.That(CurrentTourRule.Next("a", Q("b"), Q("b", "c")), Is.EqualTo("a"));
            Assert.That(CurrentTourRule.Next("a", Q("b"), Q()), Is.EqualTo("a"));
        }

        [Test]
        public void CurrentEntered_IsKept()
        {
            Assert.That(CurrentTourRule.Next("a", Q("b"), Q("b", "a")), Is.EqualTo("a"));
        }

        [Test]
        public void ExitAndReenterWithinFrame_KeepsCurrent()
        {
            Assert.That(CurrentTourRule.Next("a", Q("a", "b"), Q("b", "a")), Is.EqualTo("a"));
        }

        [Test]
        public void NullLists_AreTreatedAsEmpty()
        {
            Assert.That(CurrentTourRule.Next(null, null, null), Is.Null);
            Assert.That(CurrentTourRule.Next("a", null, null), Is.EqualTo("a"));
        }
    }
}
