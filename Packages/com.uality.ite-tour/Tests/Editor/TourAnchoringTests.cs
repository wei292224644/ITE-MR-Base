using NUnit.Framework;
using UnityEngine;
using Uality.IteTour.Core;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 锚定链的不变式。取值照搬现场那份空间场景描述（thirdDemo）：五个 tour 沿 **+Z**
    /// 每 2 米一个，Y 全为 0，旋转只绕 Y——所以期望值都能用世界基向量直接写死，
    /// 不经被测函数推导（D30 的教训：拿被测约定算期望值，整体转 180° 也是绿的）。
    /// </summary>
    public class TourAnchoringTests
    {
        private const float Tolerance = 1e-3f;

        private static Matrix4x4 TourAt(float z, float yawDegrees = 0f)
            => Matrix4x4.TRS(new Vector3(0f, 0f, z), Quaternion.Euler(0f, yawDegrees, 0f), Vector3.one);

        [Test]
        public void ScannedTour_LandsExactlyOnTheMarker()
        {
            var anchor = new Pose(new Vector3(1.5f, 1.2f, -3f), Quaternion.Euler(0f, 37f, 0f));
            var scanned = TourAt(4f);

            var world = TourAnchoring.ResolveWorld(anchor, scanned, scanned);

            Assert.That(Vector3.Distance(world.position, anchor.position), Is.LessThan(Tolerance),
                "被扫中的 Tour 必须落在标记上——整条锚定链就是为这一条存在的");
            Assert.That(Quaternion.Angle(world.rotation, anchor.rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void OtherTours_KeepTheirSpacingFromTheScannedOne()
        {
            var anchor = new Pose(Vector3.zero, Quaternion.identity);

            // 扫中间那个（z=4），首尾两个应落在它的前后各 4 米
            var world0 = TourAnchoring.ResolveWorld(anchor, TourAt(4f), TourAt(0f));
            var world8 = TourAnchoring.ResolveWorld(anchor, TourAt(4f), TourAt(8f));

            Assert.That(world0.position, Is.EqualTo(new Vector3(0f, 0f, -4f)).Using(Vector3Comparer));
            Assert.That(world8.position, Is.EqualTo(new Vector3(0f, 0f, 4f)).Using(Vector3Comparer));
        }

        /// <summary>
        /// 这条是轴向守卫：场景描述里那一排 tour 沿 +Z 排，锚定后它们必须沿**锚点的 +Z**
        /// 排开。锚点朝哪，那一排就朝哪——中间任何一处多转了 90°，这里就红。
        /// </summary>
        [Test]
        public void RowOfTours_RunsAlongTheAnchorsForwardAxis()
        {
            // 锚点绕 Y 转 90°：它的 +Z 指向世界 +X（东）
            var anchor = new Pose(Vector3.zero, Quaternion.Euler(0f, 90f, 0f));

            var next = TourAnchoring.ResolveWorld(anchor, TourAt(0f), TourAt(2f));

            Assert.That(next.position, Is.EqualTo(new Vector3(2f, 0f, 0f)).Using(Vector3Comparer),
                "锚点 +Z 指东时，下一个 tour 必须落在东边 2 米");
        }

        [Test]
        public void ScannedTourYaw_IsCancelledOut()
        {
            var anchor = new Pose(Vector3.zero, Quaternion.identity);

            // 扫中那个自身绕 Y 转了 90°（thirdDemo 里 hkdaowxy_0hu 就是这样），
            // 锚定后它自己必须与标记同向——它的自转被 TourRoot 的逆抵消掉。
            var world = TourAnchoring.ResolveWorld(anchor, TourAt(8f, 90f), TourAt(8f, 90f));

            Assert.That(Quaternion.Angle(world.rotation, Quaternion.identity), Is.LessThan(0.01f));
        }

        [Test]
        public void TourRootLocal_IsTheInverseOfTheScannedTour()
        {
            var scanned = TourAt(6f, 45f);

            var rootLocal = TourAnchoring.TourRootLocal(scanned);
            var composed = Matrix4x4.TRS(rootLocal.position, rootLocal.rotation, Vector3.one) * scanned;

            Assert.That(composed.GetPosition(), Is.EqualTo(Vector3.zero).Using(Vector3Comparer));
            Assert.That(Quaternion.Angle(composed.rotation, Quaternion.identity), Is.LessThan(0.01f));
        }

        [Test]
        public void HasUnitScale_FlagsScaledDescriptions()
        {
            Assert.IsTrue(TourAnchoring.HasUnitScale(TourAt(2f, 30f)));
            Assert.IsFalse(TourAnchoring.HasUnitScale(
                Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1f, 1f, 2f))),
                "带缩放的描述会在锚定时被静默丢掉，必须能被识别出来");
        }

        private static readonly System.Collections.IComparer Vector3Comparer =
            new Vector3EqualityComparer(Tolerance);

        private class Vector3EqualityComparer : System.Collections.IComparer
        {
            private readonly float _tolerance;

            public Vector3EqualityComparer(float tolerance) => _tolerance = tolerance;

            public int Compare(object x, object y)
                => Vector3.Distance((Vector3)x, (Vector3)y) < _tolerance ? 0 : 1;
        }
    }
}
