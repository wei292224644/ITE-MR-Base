using NUnit.Framework;
using Uality.IteTour.Core;

namespace MRBase.Ite.Host.Tests
{
    /// <summary>
    /// 重定位黄条表达的是一个状态：「因为重定位，正在等待扫码」（ite-guide-state-machine D8）。
    /// 旧实现挂在扫码提示的变化上，只有收到一次 Visible 才清，于是会残留。
    /// </summary>
    public class RecenterBannerTests
    {
        [Test]
        public void Shows_WhileAwaitingScanBecauseOfRecenter()
        {
            Assert.That(IteHmdPanel.ShowsRecenterBanner(GuideState.AwaitingScan, GuideStateReason.Recentered), Is.True);
        }

        [TestCase(GuideState.Anchored, GuideStateReason.Scanned)]
        [TestCase(GuideState.Suspended, GuideStateReason.HeadsetRemoved)]
        [TestCase(GuideState.AwaitingScan, GuideStateReason.HeadsetMounted)]
        [TestCase(GuideState.AwaitingScan, GuideStateReason.ColdStart)]
        public void Hidden_Otherwise(GuideState state, GuideStateReason reason)
        {
            Assert.That(IteHmdPanel.ShowsRecenterBanner(state, reason), Is.False);
        }
    }
}
