using UnityEngine;

/// <summary>
/// 把 AprilTag 解出的 Unity 相机系位姿接到 PICO 4U 帧位姿上。
///
/// <c>frame.pose</c> 是曝光时刻的<b>头</b>位姿（SDK 注释原文："The head Pose at the time of image
/// production"），不是左 RGB 相机光心。中间差的「头→左相机」外参来自
/// <c>GetCameraParametersNewfor4U</c> 的 <c>l_pos</c>/<c>l_rot</c>；漏乘它时码被定偏约 7.6 cm，
/// 且偏移固定在头系、随头转。
///
/// 三方坐标约定：
/// - SDK 头系：右手，X 右 / Y 上 / Z 朝后；换进 Unity 是 <c>(x, y, -z)</c> / <c>(x, y, -z, -w)</c>。
/// - SDK 相机系：OpenCV，X 右 / Y 下 / Z 朝前；<see cref="PlanarPoseSolver.ToUnityCameraSpace"/> 翻 Y 进 Unity。
/// - <c>markerInCamera</c> 已是 Unity 相机系（X 右 / Y 上 / Z 朝前）。
///
/// 见 docs/superpowers/specs/2026-09-24-pico-camera-extrinsic-design.md。
/// </summary>
public static class PicoEnterpriseCameraPose
{
    // 超出即判外参不可信：左相机不可能离头中心 15 cm 以上；约定翻转抵掉后标定残差实测约 0.4°。
    const float MaxHeadToCameraMeters = 0.15f;
    const float MaxResidualDegrees = 5f;

    public static Pose ToUnityTrackingPose(Pose framePose)
    {
        return new Pose(
            new Vector3(framePose.position.x, framePose.position.y, -framePose.position.z),
            new Quaternion(
                framePose.rotation.x,
                framePose.rotation.y,
                -framePose.rotation.z,
                -framePose.rotation.w));
    }

    /// <summary>
    /// 把 SDK 的 <c>l_pos</c>/<c>l_rot</c>（OpenCV 相机在右手头系里的位姿）换成
    /// Unity 头系里的 Unity 相机位姿。数值不可信时返回 false，调用方不得出位姿。
    /// </summary>
    public static bool TryCreateHeadToCamera(Vector3 lPos, Quaternion lRot, out Pose headToCamera)
    {
        headToCamera = Pose.identity;
        float norm = Mathf.Sqrt(lRot.x * lRot.x + lRot.y * lRot.y + lRot.z * lRot.z + lRot.w * lRot.w);
        if (Mathf.Abs(norm - 1f) > 0.01f) return false;

        // R_unity = S_H · R · S_C，S_H = diag(1,1,-1)，S_C = diag(1,-1,1) = S_H · Rx(180°)。
        // S_H·R·S_H 就是头位姿那套手性翻转，所以 R_unity = 翻转(l_rot) · Rx(180°)。
        Quaternion rotation =
            new Quaternion(lRot.x, lRot.y, -lRot.z, -lRot.w) * new Quaternion(1f, 0f, 0f, 0f);
        var position = new Vector3(lPos.x, lPos.y, -lPos.z);

        if (position.magnitude > MaxHeadToCameraMeters) return false;
        if (Quaternion.Angle(rotation, Quaternion.identity) > MaxResidualDegrees) return false;

        headToCamera = new Pose(position, rotation);
        return true;
    }

    public static Pose ComposeWorld(Pose framePose, Pose headToCamera, Pose markerInCamera)
    {
        Pose cameraPose = PoseMath.Compose(ToUnityTrackingPose(framePose), headToCamera);
        return PoseMath.Compose(cameraPose, markerInCamera);
    }
}
