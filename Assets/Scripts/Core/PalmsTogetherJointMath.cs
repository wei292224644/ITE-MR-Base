using System;
using UnityEngine;

/// <summary>
/// 双手合十路径 A：纯关节几何与弯曲判据（不碰引擎帧循环，可离机测）。
///
/// 轴约定抄自 XR Hands 内部的 XRHandOrientationUtility.GetHandAxisDirection：
///   掌心方向     = rootPose.rotation * (0, -1, 0)
///   手指伸展方向 = rootPose.rotation * (0,  0, 1)
/// </summary>
public static class PalmsTogetherJointMath
{
    /// <summary>一组阈值。抽成结构体是为了让「松开」用一份放宽过的副本做迟滞。</summary>
    [Serializable]
    public struct Tuning
    {
        [Tooltip("两腕之间的距离上限，米。")]
        public float maxWristGap;

        [Tooltip("两只手掌心方向要多接近「正对」，度。")]
        public float palmOpposeTolerance;

        [Tooltip("两只手指尖朝向要多接近「一致」，度。挡住一只手朝上一只手朝下的姿势。")]
        public float fingerAlignTolerance;

        [Tooltip("中指弯曲上限，0 = 完全伸直，1 = 完全握拳。挡住两个拳头对撞。")]
        public float maxCurl;

        public static Tuning Default => new Tuning
        {
            maxWristGap = 0.10f,
            palmOpposeTolerance = 40f,
            fingerAlignTolerance = 35f,
            maxCurl = 0.35f,
        };

        /// <summary>按比例放宽的一份副本，用作松开阈值。</summary>
        public Tuning Relaxed(float slack) => new Tuning
        {
            maxWristGap = maxWristGap * slack,
            palmOpposeTolerance = Mathf.Min(180f, palmOpposeTolerance * slack),
            fingerAlignTolerance = Mathf.Min(180f, fingerAlignTolerance * slack),
            maxCurl = Mathf.Min(1f, maxCurl * slack),
        };
    }

    /// <summary>
    /// 纯判据。姿态取腕关节，弯曲度取中指（0 = 伸直，1 = 握拳）。
    /// </summary>
    public static bool Matches(Pose leftWrist, Pose rightWrist,
                               float leftCurl, float rightCurl, in Tuning t)
    {
        if (leftCurl > t.maxCurl || rightCurl > t.maxCurl)
            return false;

        Vector3 leftToRight = rightWrist.position - leftWrist.position;
        if (leftToRight.sqrMagnitude > t.maxWristGap * t.maxWristGap)
            return false;

        Vector3 leftPalm = leftWrist.rotation * Vector3.down;
        Vector3 rightPalm = rightWrist.rotation * Vector3.down;

        // 两个掌心反向平行。注意这一条**同时**被合十和「手背贴手背」满足，所以
        // 下面还得判一次朝向的正负号。
        if (Vector3.Angle(leftPalm, -rightPalm) > t.palmOpposeTolerance)
            return false;

        // 掌心得朝着对方，不是背对。
        if (Vector3.Dot(leftPalm, leftToRight) <= 0f)
            return false;

        Vector3 leftFingers = leftWrist.rotation * Vector3.forward;
        Vector3 rightFingers = rightWrist.rotation * Vector3.forward;
        return Vector3.Angle(leftFingers, rightFingers) <= t.fingerAlignTolerance;
    }

    /// <summary>仅腕距门。路径 B 在 Pose 条件之外还要过这一关；可离机测。</summary>
    public static bool WristGapOk(Pose leftWrist, Pose rightWrist, float maxWristGap)
    {
        Vector3 delta = rightWrist.position - leftWrist.position;
        return delta.sqrMagnitude <= maxWristGap * maxWristGap;
    }
}
