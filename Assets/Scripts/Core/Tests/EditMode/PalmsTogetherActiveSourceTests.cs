using NUnit.Framework;

namespace MRBase.Core.Tests
{
    /// <summary>
    /// 门面活动源语义：IsHeld 跟随 ActiveSource；两边保持态可独立存在。
    /// （不启动 MonoBehaviour，直接测 HoldTracker + 选源规则。）
    /// </summary>
    public class PalmsTogetherActiveSourceTests
    {
        static bool HeldFor(PalmsTogetherGesture.Source active, bool heldA, bool heldB) =>
            active == PalmsTogetherGesture.Source.JointMath ? heldA : heldB;

        [Test]
        public void IsHeld_Follows_JointMath_When_Active_Is_A()
        {
            Assert.IsTrue(HeldFor(PalmsTogetherGesture.Source.JointMath, heldA: true, heldB: false));
            Assert.IsFalse(HeldFor(PalmsTogetherGesture.Source.JointMath, heldA: false, heldB: true));
        }

        [Test]
        public void IsHeld_Follows_HandPose_When_Active_Is_B()
        {
            Assert.IsTrue(HeldFor(PalmsTogetherGesture.Source.HandPoseComposite, heldA: false, heldB: true));
            Assert.IsFalse(HeldFor(PalmsTogetherGesture.Source.HandPoseComposite, heldA: true, heldB: false));
        }
    }
}
