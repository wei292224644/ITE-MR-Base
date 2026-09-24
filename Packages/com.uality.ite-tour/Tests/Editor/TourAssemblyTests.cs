using Newtonsoft.Json;
using NUnit.Framework;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace Uality.IteTour.Tests
{
    public class TourAssemblyTests
    {
        [Test]
        public void ShouldAssemble_NullTour_IsFalse()
        {
            Assert.That(TourAssembly.ShouldAssemble(null), Is.False);
        }

        [Test]
        public void ShouldAssemble_ExplicitlyDisabled_IsFalse()
        {
            Assert.That(TourAssembly.ShouldAssemble(new IteSpaceScene.Tour
            {
                tourID = "off",
                isEnabled = false,
            }), Is.False);
        }

        [Test]
        public void ShouldAssemble_DefaultAndEnabled_AreTrue()
        {
            Assert.That(TourAssembly.ShouldAssemble(new IteSpaceScene.Tour { tourID = "fresh" }), Is.True);
            Assert.That(TourAssembly.ShouldAssemble(new IteSpaceScene.Tour
            {
                tourID = "on",
                isEnabled = true,
            }), Is.True);
        }

        [Test]
        public void MissingIsEnabledInJson_DefaultsToEnabled()
        {
            var tour = JsonConvert.DeserializeObject<IteSpaceScene.Tour>(@"{""tourID"":""t1""}");

            Assert.That(tour.isEnabled, Is.True);
            Assert.That(TourAssembly.ShouldAssemble(tour), Is.True);
        }

        [Test]
        public void EnabledTours_SkipsNullAndDisabled()
        {
            var tours = new[]
            {
                null,
                new IteSpaceScene.Tour { tourID = "a", isEnabled = true },
                new IteSpaceScene.Tour { tourID = "b", isEnabled = false },
                new IteSpaceScene.Tour { tourID = "c" },
            };

            var enabled = TourAssembly.EnabledTours(tours);

            Assert.That(enabled.Count, Is.EqualTo(2));
            Assert.That(enabled[0].tourID, Is.EqualTo("a"));
            Assert.That(enabled[1].tourID, Is.EqualTo("c"));
        }

        [Test]
        public void EnabledTours_NullArray_IsEmpty()
        {
            Assert.That(TourAssembly.EnabledTours(null), Is.Empty);
        }

        [Test]
        public void AllowsTriggerVolume_OnlyAlwaysDisplayedIsExcluded()
        {
            Assert.That(TourAssembly.AllowsTriggerVolume(IteSpaceScene.Tour.DisplayType.normal), Is.True);
            Assert.That(
                TourAssembly.AllowsTriggerVolume(IteSpaceScene.Tour.DisplayType.regionalTrigger),
                Is.True);
            Assert.That(
                TourAssembly.AllowsTriggerVolume(IteSpaceScene.Tour.DisplayType.alwaysDisplayed),
                Is.False);
        }

        [TestCase(IteSpaceScene.Tour.DisplayType.regionalTrigger, true)]
        [TestCase(IteSpaceScene.Tour.DisplayType.normal, false)]
        [TestCase(IteSpaceScene.Tour.DisplayType.alwaysDisplayed, false)]
        public void PlaysOnSelect_OnlyRegionalTrigger(IteSpaceScene.Tour.DisplayType displayType, bool expected)
        {
            Assert.That(TourAssembly.PlaysOnSelect(displayType), Is.EqualTo(expected),
                "碰撞永远不激活 normal（ite-current-tour D5）");
        }

        [TestCase(IteSpaceScene.Tour.DisplayType.regionalTrigger, true)]
        [TestCase(IteSpaceScene.Tour.DisplayType.normal, true)]
        [TestCase(IteSpaceScene.Tour.DisplayType.alwaysDisplayed, false)]
        public void CanBeCurrent_OnlyToursWithAVolume(IteSpaceScene.Tour.DisplayType displayType, bool expected)
        {
            Assert.That(TourAssembly.CanBeCurrent(displayType), Is.EqualTo(expected),
                "没有体积就永远离不开，不能当当前 Tour（ite-current-tour D10、I5）");
        }
    }
}
