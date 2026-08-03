using NUnit.Framework;
using UnityEngine;

namespace MRBase.Core.Tests
{
    public class PalmsTogetherJointMathTests
    {
        static readonly PalmsTogetherJointMath.Tuning Tuning = PalmsTogetherJointMath.Tuning.Default;

        // XR Hands 的轴约定：掌心 = rotation * (0,-1,0)，指尖 = rotation * (0,0,1)。
        // LookRotation(forward, up) 把局部 +Z 摆到 forward、+Y 摆到 up，所以掌心是 -up。
        static Pose Wrist(Vector3 position, Vector3 fingers, Vector3 palm) =>
            new Pose(position, Quaternion.LookRotation(fingers, -palm));

        static Pose LeftSealed => Wrist(new Vector3(-0.02f, 1f, 0f), Vector3.up, Vector3.right);
        static Pose RightSealed => Wrist(new Vector3(0.02f, 1f, 0f), Vector3.up, Vector3.left);

        static bool Match(Pose left, Pose right, float leftCurl = 0f, float rightCurl = 0f) =>
            PalmsTogetherJointMath.Matches(left, right, leftCurl, rightCurl, Tuning);

        [Test]
        public void Palms_Together_Matches()
        {
            Assert.IsTrue(Match(LeftSealed, RightSealed));
        }

        [Test]
        public void Hands_Too_Far_Apart_Do_Not_Match()
        {
            Pose right = RightSealed;
            right.position += new Vector3(0.3f, 0f, 0f);
            Assert.IsFalse(Match(LeftSealed, right), "距离没管住");
        }

        [Test]
        public void Back_To_Back_Does_Not_Match()
        {
            Pose left = Wrist(new Vector3(-0.02f, 1f, 0f), Vector3.up, Vector3.left);
            Pose right = Wrist(new Vector3(0.02f, 1f, 0f), Vector3.up, Vector3.right);
            Assert.IsFalse(Match(left, right), "掌心背对也被当成合十");
        }

        [Test]
        public void One_Hand_Upside_Down_Do_Not_Match()
        {
            Pose right = Wrist(new Vector3(0.02f, 1f, 0f), Vector3.down, Vector3.left);
            Assert.IsFalse(Match(LeftSealed, right), "指尖反向也被当成合十");
        }

        [Test]
        public void Two_Fists_Do_Not_Match()
        {
            Assert.IsFalse(Match(LeftSealed, RightSealed, leftCurl: 0.9f, rightCurl: 0.9f),
                           "握拳对撞也被当成合十");
        }

        [Test]
        public void Wrist_Gap_Ok_When_Close()
        {
            Assert.IsTrue(PalmsTogetherJointMath.WristGapOk(LeftSealed, RightSealed, 0.10f));
        }

        [Test]
        public void Wrist_Gap_Fails_When_Far()
        {
            Pose right = RightSealed;
            right.position += new Vector3(0.3f, 0f, 0f);
            Assert.IsFalse(PalmsTogetherJointMath.WristGapOk(LeftSealed, right, 0.10f));
        }
    }
}
