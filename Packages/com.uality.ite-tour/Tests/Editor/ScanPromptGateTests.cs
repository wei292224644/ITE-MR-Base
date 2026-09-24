using System.Collections.Generic;
using NUnit.Framework;
using Uality.IteTour.Core;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// marker-rescan D8：加载完成前不对外发扫码提示。那时 ITE 还不收扫码（ite-guide-state-machine D9），
    /// 「请扫码」是假的——宿主据此放行，码判稳后被丢弃，加载完成后就扫不上了。
    /// </summary>
    public class ScanPromptGateTests
    {
        private static readonly ScanPrompt Anything = ScanPrompt.Visible(new string[0]);

        [Test]
        public void BeforeOpen_OffersAreNotForwarded_AndCurrentIsHidden()
        {
            var gate = new ScanPromptGate();
            var forwarded = new List<ScanPrompt>();
            gate.Changed += forwarded.Add;

            gate.Offer(Anything);

            Assert.That(forwarded, Is.Empty);
            Assert.That(gate.Current.State, Is.EqualTo(ScanPromptState.Hidden));
        }

        [Test]
        public void Open_ReplaysTheLatestVisiblePrompt()
        {
            var gate = new ScanPromptGate();
            var forwarded = new List<ScanPrompt>();
            gate.Changed += forwarded.Add;
            gate.Offer(ScanPrompt.Visible(new[] { "t1" }));

            gate.Open();

            Assert.That(forwarded.Count, Is.EqualTo(1), "加载完成时补发一次");
            Assert.That(forwarded[0].State, Is.EqualTo(ScanPromptState.Visible));
            Assert.That(forwarded[0].TourIds, Is.EqualTo(new[] { "t1" }));
            Assert.That(gate.Current.State, Is.EqualTo(ScanPromptState.Visible));
        }

        [Test]
        public void Open_WithLatestHidden_DoesNotBroadcast()
        {
            var gate = new ScanPromptGate();
            var forwarded = new List<ScanPrompt>();
            gate.Changed += forwarded.Add;
            gate.Offer(Anything);
            gate.Offer(ScanPrompt.Hidden);

            gate.Open();

            Assert.That(forwarded, Is.Empty, "对外一直是隐藏，没有变化就不广播");
        }

        [Test]
        public void AfterOpen_OffersAreForwardedAsIs()
        {
            var gate = new ScanPromptGate();
            var forwarded = new List<ScanPrompt>();
            gate.Changed += forwarded.Add;
            gate.Open();

            gate.Offer(Anything);
            gate.Offer(ScanPrompt.Hidden);

            Assert.That(forwarded.Count, Is.EqualTo(2));
            Assert.That(forwarded[1].State, Is.EqualTo(ScanPromptState.Hidden));
            Assert.That(gate.Current.State, Is.EqualTo(ScanPromptState.Hidden));
        }

        [Test]
        public void OpenTwice_ReplaysOnlyOnce()
        {
            var gate = new ScanPromptGate();
            var forwarded = new List<ScanPrompt>();
            gate.Changed += forwarded.Add;
            gate.Offer(Anything);

            gate.Open();
            gate.Open();

            Assert.That(forwarded.Count, Is.EqualTo(1));
        }
    }
}
