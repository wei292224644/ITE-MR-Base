using NUnit.Framework;
using UnityEngine;

namespace MRBase.Transitions.Tests
{
    public class IceSpriteTeleportTests
    {
        static readonly Vector3 From = new Vector3(0f, 1f, 0f);
        static readonly Vector3 To = new Vector3(4f, 1f, 0f);

        const float D = 0.5f;   // dissolveDuration
        const float F = 0.4f;   // flightDuration

        // ---- DissolveReform：消散完才重组，中间没有飞行段 ----

        [TestCase(0f, IceSpriteTeleportPhase.Vanishing)]
        [TestCase(0.49f, IceSpriteTeleportPhase.Vanishing)]
        [TestCase(0.5f, IceSpriteTeleportPhase.Appearing)]
        [TestCase(0.99f, IceSpriteTeleportPhase.Appearing)]
        [TestCase(1f, IceSpriteTeleportPhase.Done)]
        [TestCase(99f, IceSpriteTeleportPhase.Done)]
        public void DissolveReform_PhaseBoundaries(float elapsed, IceSpriteTeleportPhase expected)
        {
            Assert.That(
                IceSpriteTeleport.PhaseAt(IceSpriteTeleportStyle.DissolveReform, elapsed, D, F),
                Is.EqualTo(expected));
        }

        [Test]
        public void DissolveReform_StaysAtSourceWhileVanishing()
        {
            Vector3 p = IceSpriteTeleport.PositionAt(
                IceSpriteTeleportStyle.DissolveReform, From, To, 0.25f, D, F);

            Assert.That(p, Is.EqualTo(From));
        }

        [Test]
        public void DissolveReform_SnapsToTargetOnceVanished()
        {
            Vector3 p = IceSpriteTeleport.PositionAt(
                IceSpriteTeleportStyle.DissolveReform, From, To, 0.5f, D, F);

            Assert.That(p, Is.EqualTo(To));
        }

        [Test]
        public void DissolveReform_TotalIsTwoDissolves()
        {
            Assert.That(
                IceSpriteTeleport.TotalDuration(IceSpriteTeleportStyle.DissolveReform, D, F),
                Is.EqualTo(1f).Within(0.0001f));
        }

        // ---- TrailFlight：中间多一段飞行 ----

        [TestCase(0f, IceSpriteTeleportPhase.Vanishing)]
        [TestCase(0.5f, IceSpriteTeleportPhase.InTransit)]
        [TestCase(0.89f, IceSpriteTeleportPhase.InTransit)]
        [TestCase(0.9f, IceSpriteTeleportPhase.Appearing)]
        [TestCase(1.4f, IceSpriteTeleportPhase.Done)]
        public void TrailFlight_PhaseBoundaries(float elapsed, IceSpriteTeleportPhase expected)
        {
            Assert.That(
                IceSpriteTeleport.PhaseAt(IceSpriteTeleportStyle.TrailFlight, elapsed, D, F),
                Is.EqualTo(expected));
        }

        [Test]
        public void TrailFlight_MidTransitIsHalfway()
        {
            // elapsed 0.7 → 飞行段过了 (0.7-0.5)/0.4 = 0.5
            Vector3 p = IceSpriteTeleport.PositionAt(
                IceSpriteTeleportStyle.TrailFlight, From, To, 0.7f, D, F);

            Assert.That(p.x, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void TrailFlight_TotalIncludesFlight()
        {
            Assert.That(
                IceSpriteTeleport.TotalDuration(IceSpriteTeleportStyle.TrailFlight, D, F),
                Is.EqualTo(1.4f).Within(0.0001f));
        }

        [Test]
        public void TrailFlight_ZeroFlightDegradesToDissolveReform()
        {
            // 防除零：f = 0 时不能崩，也不能卡在 InTransit
            Assert.That(
                IceSpriteTeleport.PhaseAt(IceSpriteTeleportStyle.TrailFlight, 0.5f, D, 0f),
                Is.EqualTo(IceSpriteTeleportPhase.Appearing));

            Assert.That(
                IceSpriteTeleport.TotalDuration(IceSpriteTeleportStyle.TrailFlight, D, 0f),
                Is.EqualTo(1f).Within(0.0001f));
        }

        // ---- Afterimage：本体不消失，直接在目标点 ----

        [TestCase(0f)]
        [TestCase(0.25f)]
        public void Afterimage_BodyIsAtTargetImmediately(float elapsed)
        {
            Assert.That(
                IceSpriteTeleport.PositionAt(IceSpriteTeleportStyle.Afterimage, From, To, elapsed, D, F),
                Is.EqualTo(To));

            Assert.That(
                IceSpriteTeleport.PhaseAt(IceSpriteTeleportStyle.Afterimage, elapsed, D, F),
                Is.EqualTo(IceSpriteTeleportPhase.Done));
        }

        [Test]
        public void Afterimage_TotalIsGhostFade()
        {
            Assert.That(
                IceSpriteTeleport.TotalDuration(IceSpriteTeleportStyle.Afterimage, D, F),
                Is.EqualTo(0.5f).Within(0.0001f));
        }

        // ---- 边界 ----

        [Test]
        public void NegativeElapsedIsTreatedAsStart()
        {
            Assert.That(
                IceSpriteTeleport.PhaseAt(IceSpriteTeleportStyle.DissolveReform, -1f, D, F),
                Is.EqualTo(IceSpriteTeleportPhase.Vanishing));
        }
    }
}
