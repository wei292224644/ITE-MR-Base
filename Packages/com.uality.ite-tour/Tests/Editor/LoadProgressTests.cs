using NUnit.Framework;
using Uality.IteTour.Core;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 加载进度的取值曲线。源实现把 <c>0.2f + 0.8f * (count / total)</c> 直接写在
    /// 循环体里，两个魔数没有名字，且 <c>total</c> 为 0 时是除零。
    ///
    /// 期望值取自源工程 <c>IteSpaceManagerAssets.FetchIteSpaceScene</c>。
    /// </summary>
    public class LoadProgressTests
    {
        [Test]
        public void SceneParsed_IsTwentyPercent()
        {
            Assert.That(LoadProgress.SceneParsed, Is.EqualTo(0.2f));
        }

        /// <summary>前 20% 留给场景描述，剩下 80% 按 Tour 数均分。</summary>
        [TestCase(0, 4, 0.2f)]
        [TestCase(1, 4, 0.4f)]
        [TestCase(2, 4, 0.6f)]
        [TestCase(4, 4, 1.0f)]
        public void ForTours_SplitsTheRemainingEightyPercentEvenly(int completed, int total, float expected)
        {
            Assert.That(LoadProgress.ForTours(completed, total), Is.EqualTo(expected).Within(1e-5f));
        }

        /// <summary>没有 Tour 时不能除零——直接算作已完成。</summary>
        [Test]
        public void ForTours_WithNoToursReportsComplete()
        {
            Assert.That(LoadProgress.ForTours(0, 0), Is.EqualTo(1f));
        }
    }
}
