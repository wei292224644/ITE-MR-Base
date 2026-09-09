using NUnit.Framework;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 与 <see cref="TourVersionCacheTests"/> 同构：键名格式是跨版本的兼容契约，钉住它。
    /// 场景包与 tour 包用各自的键名空间（design D4）。
    /// </summary>
    public class SpacePackageEtagCacheTests
    {
        [Test]
        public void KeyFor_IsNamespacedAndContainsSceneName()
        {
            Assert.That(SpacePackageEtagCache.KeyFor("thirdDemo"), Is.EqualTo("ite.space.thirdDemo.etag"));
        }

        [Test]
        public void KeyFor_DoesNotCollideWithTourVersionCache()
        {
            Assert.That(SpacePackageEtagCache.KeyFor("abc"), Is.Not.EqualTo(TourVersionCache.KeyFor("abc")),
                "两类包的键名空间必须分开，否则同名的 scene 与 tour 会互相覆盖");
        }

        [Test]
        public void Get_ReturnsEmptyForUnknownScene()
        {
            Assert.That(SpacePackageEtagCache.Get("ite-space-never-cached"), Is.Empty);
        }

        /// <summary>
        /// OSS 返回的 ETag 字面量带双引号（`"B6A4...-1"`）。存取两侧都来自同一个
        /// 响应头入口，形态一致，按原样存即可——不做去引号之类的规范化。
        /// </summary>
        [Test]
        public void SetThenGet_RoundTripsEtagIncludingQuotes()
        {
            const string sceneName = "ite-space-cache-test";
            try
            {
                SpacePackageEtagCache.Set(sceneName, "\"B6A43FCA26644FF44FA21BBADCC8D5B3-1\"");

                Assert.That(SpacePackageEtagCache.Get(sceneName),
                    Is.EqualTo("\"B6A43FCA26644FF44FA21BBADCC8D5B3-1\""));
            }
            finally
            {
                SpacePackageEtagCache.Clear(sceneName);
            }
        }

        [Test]
        public void Clear_RemovesTheRecord()
        {
            const string sceneName = "ite-space-clear-test";
            SpacePackageEtagCache.Set(sceneName, "etag-1");

            SpacePackageEtagCache.Clear(sceneName);

            Assert.That(SpacePackageEtagCache.Get(sceneName), Is.Empty);
        }
    }
}
