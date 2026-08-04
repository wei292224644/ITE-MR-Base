using NUnit.Framework;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 源实现用裸 tourId 当 PlayerPrefs 键，直接占用宿主的全局键名空间。
    /// 包要与其它模块共存、要能移植进任意工程，就不能这么干（design D10）。
    /// 键名格式是跨版本的兼容契约，钉住它。
    /// </summary>
    public class TourVersionCacheTests
    {
        [Test]
        public void KeyFor_IsNamespacedAndContainsTourId()
        {
            Assert.That(TourVersionCache.KeyFor("abc"), Is.EqualTo("ite.tour.abc.version"));
        }

        [Test]
        public void KeyFor_DoesNotUseBareTourId()
        {
            Assert.That(TourVersionCache.KeyFor("abc"), Is.Not.EqualTo("abc"),
                "裸 tourId 会与宿主的键冲突");
        }

        [Test]
        public void SetThenGet_RoundTripsVersion()
        {
            const string tourId = "ite-tour-cache-test";
            try
            {
                TourVersionCache.Set(tourId, "v42");

                Assert.That(TourVersionCache.Get(tourId), Is.EqualTo("v42"));
            }
            finally
            {
                TourVersionCache.Clear(tourId);
            }
        }

        [Test]
        public void Get_ReturnsEmptyForUnknownTour()
        {
            Assert.That(TourVersionCache.Get("ite-tour-never-cached"), Is.Empty);
        }
    }
}
