using System.Collections.Generic;
using NUnit.Framework;
using Uality.IteTour.Core;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// payload → tourId 的解析归包（design D5）。这里穷举 spec `ite-marker-identity`
    /// 的全部 scenario：两端各自的 payload 形状、解析不出、解析出但不在场景中。
    /// </summary>
    public class MarkerIdentityTests
    {
        private static readonly IReadOnlyList<MarkerBinding> Bindings = new[]
        {
            new MarkerBinding("wm0l5qcn_ibd", 250),
            new MarkerBinding("earyserh_i5x", 0),
            new MarkerBinding("4kvhqwvp_12f", null),
        };

        [Test]
        public void QrText_ResolvesTourId()
        {
            var outcome = MarkerIdentity.Resolve(
                MarkerKind.QrText, "******wm0l5qcn_ibd******", Bindings, out string tourId);

            Assert.That(outcome, Is.EqualTo(MarkerResolution.Resolved));
            Assert.That(tourId, Is.EqualTo("wm0l5qcn_ibd"));
        }

        [Test]
        public void AprilTagId_ResolvesViaBinding()
        {
            var outcome = MarkerIdentity.Resolve(
                MarkerKind.AprilTagId, "250", Bindings, out string tourId);

            Assert.That(outcome, Is.EqualTo(MarkerResolution.Resolved));
            Assert.That(tourId, Is.EqualTo("wm0l5qcn_ibd"));
        }

        /// <summary>tag 0 是合法 ID,不能被当成「没绑定」。</summary>
        [Test]
        public void AprilTagZero_IsResolvable()
        {
            var outcome = MarkerIdentity.Resolve(
                MarkerKind.AprilTagId, "0", Bindings, out string tourId);

            Assert.That(outcome, Is.EqualTo(MarkerResolution.Resolved));
            Assert.That(tourId, Is.EqualTo("earyserh_i5x"));
        }

        [Test]
        public void QrText_NotMatchingShell_IsUnparsable()
        {
            var outcome = MarkerIdentity.Resolve(
                MarkerKind.QrText, "250", Bindings, out string tourId);

            Assert.That(outcome, Is.EqualTo(MarkerResolution.Unparsable));
            Assert.That(tourId, Is.Null);
        }

        [Test]
        public void AprilTagId_NotANumber_IsUnparsable()
        {
            var outcome = MarkerIdentity.Resolve(
                MarkerKind.AprilTagId, "******wm0l5qcn_ibd******", Bindings, out string tourId);

            Assert.That(outcome, Is.EqualTo(MarkerResolution.Unparsable));
            Assert.That(tourId, Is.Null);
        }

        [Test]
        public void QrText_ParsedButNotInScene_IsUnknownTour()
        {
            var outcome = MarkerIdentity.Resolve(
                MarkerKind.QrText, "******nosuchtour_xxx******", Bindings, out string tourId);

            Assert.That(outcome, Is.EqualTo(MarkerResolution.UnknownTour));
            Assert.That(tourId, Is.EqualTo("nosuchtour_xxx"),
                "解析成功但不在场景中时仍要带出 tourId，日志才能说清是谁");
        }

        [Test]
        public void AprilTagId_WithNoBinding_IsUnknownTour()
        {
            var outcome = MarkerIdentity.Resolve(
                MarkerKind.AprilTagId, "64", Bindings, out string tourId);

            Assert.That(outcome, Is.EqualTo(MarkerResolution.UnknownTour));
            Assert.That(tourId, Is.Null, "tag 64 没有对应的 tourId，带不出来");
        }

        /// <summary>没写 aprilTagID 的 tour 不参与反查，且不因此影响别人。</summary>
        [Test]
        public void TourWithoutBinding_NeverMatchesAnyTag()
        {
            for (int tag = 0; tag < 300; tag++)
            {
                MarkerIdentity.Resolve(
                    MarkerKind.AprilTagId, tag.ToString(), Bindings, out string tourId);
                Assert.That(tourId, Is.Not.EqualTo("4kvhqwvp_12f"));
            }
        }

        [Test]
        public void EmptyPayload_IsUnparsable()
        {
            Assert.That(
                MarkerIdentity.Resolve(MarkerKind.QrText, "", Bindings, out _),
                Is.EqualTo(MarkerResolution.Unparsable));
            Assert.That(
                MarkerIdentity.Resolve(MarkerKind.AprilTagId, null, Bindings, out _),
                Is.EqualTo(MarkerResolution.Unparsable));
        }

        /// <summary>
        /// 外壳格式是「会变」的那一处（design D8）。这条钉住的是**替换点存在**，
        /// 不是外壳本身——`QrPayloadFormat` 换实现时，上面那些用例跟着换，本条不动。
        /// </summary>
        [Test]
        public void QrShellParsing_IsIsolatedInOneEntryPoint()
        {
            Assert.That(QrPayloadFormat.TryParseTourId("******abc******", out string tourId), Is.True);
            Assert.That(tourId, Is.EqualTo("abc"));
            Assert.That(QrPayloadFormat.TryParseTourId("abc", out _), Is.False);
        }
    }
}
