using System.Collections.Generic;
using NUnit.Framework;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 「要不要提示用户去扫码、提示哪个 Tour」。
    ///
    /// 源实现是个每 0.75 秒轮询的协程，直接调 <c>ScanPreviewUI.Instance.Show()/Hide()</c>——
    /// **决策**是 ITE 业务，**渲染**不是（design D5）。这里只留决策，渲染交给宿主订阅广播。
    ///
    /// 已定位后只看当前 Tour（ite-current-tour D7）：当前 Tour 是 normal 且还没播，才提示扫它。
    /// </summary>
    public class ScanPromptPolicyTests
    {
        private const IteSpaceScene.Tour.DisplayType Normal = IteSpaceScene.Tour.DisplayType.normal;
        private const IteSpaceScene.Tour.DisplayType Regional = IteSpaceScene.Tour.DisplayType.regionalTrigger;

        private static TourDescriptor Tour(string id, IteSpaceScene.Tour.DisplayType type)
            => new TourDescriptor { TourId = id, DisplayType = type };

        private static List<TourDescriptor> Tours(params TourDescriptor[] tours) => new List<TourDescriptor>(tours);

        private static ScanState Anchored(string currentTourId, string activeTourId = null)
            => new ScanState { State = GuideState.Anchored, CurrentTourId = currentTourId, ActiveTourId = activeTourId };

        /// <summary>已经有 Tour 在放，提示无条件收起——这条优先级最高。</summary>
        [Test]
        public void Decide_WhileATourIsPlaying_Hides()
        {
            var prompt = ScanPromptPolicy.Decide(Anchored("n1", "n1"), Tours(Tour("n1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }

        /// <summary>等待扫码定位：提示不带 Tour 名，因为哪个都行。</summary>
        [Test]
        public void Decide_AwaitingScan_ShowsWithoutNamingTours()
        {
            var state = new ScanState { State = GuideState.AwaitingScan };

            var prompt = ScanPromptPolicy.Decide(state, Tours(Tour("n1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Visible));
            Assert.That(prompt.TourIds, Is.Empty);
        }

        /// <summary>头显摘下：没人在看，一律不提示。</summary>
        [Test]
        public void Decide_WhenSuspended_Hides()
        {
            var state = new ScanState { State = GuideState.Suspended };

            var prompt = ScanPromptPolicy.Decide(state, Tours(Tour("n1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }

        /// <summary>当前 Tour 是 normal、还没播：只提示它，别的码扫了也不认（ite-current-tour D6）。</summary>
        [Test]
        public void Decide_CurrentNormalNotPlaying_ShowsOnlyIt()
        {
            var prompt = ScanPromptPolicy.Decide(
                Anchored("n1"), Tours(Tour("n1", Normal), Tour("n2", Normal), Tour("r1", Regional)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Visible));
            Assert.That(prompt.TourIds, Is.EqualTo(new[] { "n1" }));
        }

        [Test]
        public void Decide_CurrentRegional_Hides()
        {
            var prompt = ScanPromptPolicy.Decide(Anchored("r1"), Tours(Tour("r1", Regional)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }

        /// <summary>
        /// 已定位、没有当前 Tour（人在所有区域之外）：扫什么都不认，提示就是在叫人做无效操作
        /// （沿用 ite-scan-region-gate D3 的理由）。
        /// </summary>
        [Test]
        public void Decide_Anchored_NoCurrent_Hides()
        {
            var prompt = ScanPromptPolicy.Decide(Anchored(null), Tours(Tour("n1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }

        [Test]
        public void Decide_CurrentUnknown_Hides()
        {
            var prompt = ScanPromptPolicy.Decide(Anchored("ghost"), Tours(Tour("n1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }
    }
}
