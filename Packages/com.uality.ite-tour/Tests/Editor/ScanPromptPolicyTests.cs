using System.Collections.Generic;
using NUnit.Framework;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 「要不要提示用户去扫码、提示哪几个 Tour」。
    ///
    /// 源实现是个每 0.75 秒轮询的协程，直接调 <c>ScanPreviewUI.Instance.Show()/Hide()</c>——
    /// **决策**是 ITE 业务，**渲染**不是（design D5）。这里只留决策，渲染交给宿主订阅广播。
    ///
    /// 期望值取自源工程 <c>NeedsToShowAnchorPreviewUI</c> 的分支顺序。
    /// 唯一有意偏离：定位后在所有触发体积外不再提示（ite-scan-region-gate D3）。
    /// </summary>
    public class ScanPromptPolicyTests
    {
        private const IteSpaceScene.Tour.DisplayType Normal = IteSpaceScene.Tour.DisplayType.normal;
        private const IteSpaceScene.Tour.DisplayType Regional = IteSpaceScene.Tour.DisplayType.regionalTrigger;

        private static TourDescriptor Tour(string id, IteSpaceScene.Tour.DisplayType type)
            => new TourDescriptor { TourId = id, DisplayType = type };

        private static List<TourDescriptor> Tours(params TourDescriptor[] tours) => new List<TourDescriptor>(tours);

        private static List<string> Pending(params string[] ids) => new List<string>(ids);

        /// <summary>已经有 Tour 在放，提示无条件收起——这条优先级最高。</summary>
        [Test]
        public void Decide_WhileATourIsActive_HidesEvenWhenAScanIsRequired()
        {
            var state = new ScanState
            {
                ActiveTourId = "t1",
                State = GuideState.AwaitingScan,
                PendingTourIds = Pending(),
            };

            var prompt = ScanPromptPolicy.Decide(state, Tours(Tour("t1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }

        /// <summary>还没扫过第一次码：提示不带 Tour 名，因为哪个都行。</summary>
        [Test]
        public void Decide_WhenAScanIsRequired_ShowsWithoutNamingTours()
        {
            var state = new ScanState { State = GuideState.AwaitingScan, PendingTourIds = Pending("t1") };

            var prompt = ScanPromptPolicy.Decide(state, Tours(Tour("t1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Visible));
            Assert.That(prompt.TourIds, Is.Empty);
        }

        /// <summary>
        /// 定位过之后相机不在任何触发体积内：体积外扫码一律不生效（ite-scan-region-gate D3），
        /// 提示「随便扫哪个」就是在叫人做无效操作。
        /// </summary>
        [Test]
        public void Decide_AfterAnchoring_NotInsideAnyVolume_Hides()
        {
            var state = new ScanState { State = GuideState.Anchored, PendingTourIds = Pending() };

            var prompt = ScanPromptPolicy.Decide(state, Tours(Tour("t1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }

        [Test]
        public void Decide_AfterAnchoring_WithoutVolumeSet_Hides()
        {
            var state = new ScanState { State = GuideState.Anchored, PendingTourIds = null };

            var prompt = ScanPromptPolicy.Decide(state, Tours(Tour("t1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }

        /// <summary>等待扫码在所有体积外仍提示「随便扫哪个」——此刻体积位置还没锚定。</summary>
        [Test]
        public void Decide_WhenAScanIsRequiredOutsideAllVolumes_ShowsWithoutNamingTours()
        {
            var state = new ScanState { State = GuideState.AwaitingScan, PendingTourIds = Pending() };

            var prompt = ScanPromptPolicy.Decide(state, Tours(Tour("t1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Visible));
            Assert.That(prompt.TourIds, Is.Empty);
        }

        /// <summary>头显摘下：没人在看，一律不提示——即使区域里有只能扫码进入的 normal Tour。</summary>
        [Test]
        public void Decide_WhenSuspended_Hides()
        {
            var state = new ScanState { State = GuideState.Suspended, PendingTourIds = Pending("n1") };

            var prompt = ScanPromptPolicy.Decide(state, Tours(Tour("n1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }

        /// <summary>
        /// 范围内有 regionalTrigger：它会自动激活，不必提示扫码。
        /// </summary>
        [Test]
        public void Decide_WhenARegionalTriggerIsInRange_Hides()
        {
            var state = new ScanState { State = GuideState.Anchored, PendingTourIds = Pending("r1", "n1") };

            var prompt = ScanPromptPolicy.Decide(
                state, Tours(Tour("r1", Regional), Tour("n1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }

        /// <summary>范围内只有 normal：这些必须扫码才能进，逐个报出名字。</summary>
        [Test]
        public void Decide_WhenOnlyNormalToursAreInRange_ShowsTheirIds()
        {
            var state = new ScanState { State = GuideState.Anchored, PendingTourIds = Pending("n1", "n2") };

            var prompt = ScanPromptPolicy.Decide(
                state, Tours(Tour("n1", Normal), Tour("n2", Normal), Tour("r1", Regional)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Visible));
            Assert.That(prompt.TourIds, Is.EqualTo(new[] { "n1", "n2" }));
        }

        /// <summary>范围内既没有 regionalTrigger 也没有 normal（例如只有 alwaysDisplayed）。</summary>
        [Test]
        public void Decide_WhenNothingActionableIsInRange_Hides()
        {
            var state = new ScanState { State = GuideState.Anchored, PendingTourIds = Pending("a1") };

            var prompt = ScanPromptPolicy.Decide(
                state, Tours(Tour("a1", IteSpaceScene.Tour.DisplayType.alwaysDisplayed)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }
    }
}
