using NUnit.Framework;
using UnityEngine;
using Uality.IteTour.Core;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 期望值只用世界基向量写（东/北/上），不经被测函数推导——D30 那次的教训：
    /// 拿被测约定算期望值，整体转 180° 也是绿的。
    /// 场景：观察者站在南边朝北看，码的印刷上边朝北（平放）或朝上（贴墙）。
    /// </summary>
    public class MarkerFrameTests
    {
        private const float Tolerance = 1e-4f;

        private static readonly Vector3 East = Vector3.right;
        private static readonly Vector3 North = Vector3.forward;
        private static readonly Vector3 South = Vector3.back;
        private static readonly Vector3 West = Vector3.left;
        private static readonly Vector3 Up = Vector3.up;

        // 宿主约定：X = 印刷左、Y = 印刷上、Z = 出纸面。
        private static Pose HostMarker(Vector3 printTop, Vector3 outOfFace)
            => new Pose(Vector3.zero, Quaternion.LookRotation(outOfFace, printTop));

        [Test]
        public void HostConvention_FlatMarker_MatchesMeasuredRawFrame()
        {
            var marker = HostMarker(printTop: North, outOfFace: Up);

            AssertDirection(marker.rotation * Vector3.right, West, "X 应为印刷左");
            AssertDirection(marker.rotation * Vector3.up, North, "Y 应为印刷上");
            AssertDirection(marker.rotation * Vector3.forward, Up, "Z 应为出纸面");
        }

        [Test]
        public void ToContentAnchorPose_FlatMarker_YIsNormalAndXIsPrintRight()
        {
            var anchor = MarkerFrame.ToContentAnchorPose(HostMarker(printTop: North, outOfFace: Up));

            AssertDirection(anchor.rotation * Vector3.right, East, "X 应为印刷右");
            AssertDirection(anchor.rotation * Vector3.up, Up, "Y 应垂直纸面朝外");
            AssertDirection(anchor.rotation * Vector3.forward, North, "Z 应为印刷上（翻 Z 前的印刷下）");
        }

        [Test]
        public void ToContentAnchorPose_WallMarker_YIsNormalAndXIsPrintRight()
        {
            var anchor = MarkerFrame.ToContentAnchorPose(HostMarker(printTop: Up, outOfFace: South));

            AssertDirection(anchor.rotation * Vector3.right, East, "X 应为印刷右");
            AssertDirection(anchor.rotation * Vector3.up, South, "Y 应垂直纸面朝外");
            AssertDirection(anchor.rotation * Vector3.forward, Up, "Z 应为印刷上");
        }

        // 端到端语义：ITE 编辑器里摆在「印刷下边」方向（右手系 +Z）的内容，
        // 经 ConvertToLeftHanded 进 Unity 再挂到锚点下，应落在码的印刷下边。
        [Test]
        public void AuthoredPrintBottomOffset_LandsTowardPrintBottom()
        {
            var anchor = MarkerFrame.ToContentAnchorPose(HostMarker(printTop: North, outOfFace: Up));
            var authored = Matrix4x4.TRS(new Vector3(0f, 0f, 1f), Quaternion.identity, Vector3.one);
            var local = authored.ConvertToLeftHanded().GetPosition();

            AssertDirection(anchor.rotation * local, South, "印刷下边方向的内容应落在南侧");
        }

        private static void AssertDirection(Vector3 actual, Vector3 expected, string message)
        {
            Assert.That((actual - expected).magnitude, Is.LessThan(Tolerance),
                $"{message}：实际 {actual}，期望 {expected}");
        }
    }
}
