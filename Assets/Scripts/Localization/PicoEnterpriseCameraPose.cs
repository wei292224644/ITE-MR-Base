using UnityEngine;

/// <summary>
/// 把 AprilTag 解出的 Unity 相机系位姿接到 PICO 4U 帧位姿上。
///
/// <c>frame.pose</c> 是传感器系（真机同一时刻与 <c>Camera.main</c> 的 Z 符号相反）。
/// <c>markerInCamera</c> 已经过 <see cref="PlanarPoseSolver.ToUnityCameraSpace"/>，是 Unity 相机系。
/// 两边必须先对齐再 <see cref="PoseMath.Compose"/>，否则盒子会出现在观察者背后（design D14）。
///
/// 转换抄自官方 CameraRendering 样例里被注释掉、但与真机符号吻合的那两行：
/// <c>(x, y, -z)</c> / <c>(x, y, -z, -w)</c>。
/// 样例给 <c>FrameTarget</c> 赋原样，那是预览物体，不是这条合成链。
/// 不乘 <c>GetCameraExtrinsicsfor4U</c>。
/// </summary>
public static class PicoEnterpriseCameraPose
{
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

    public static Pose ComposeWorld(Pose framePose, Pose markerInCamera)
    {
        return PoseMath.Compose(ToUnityTrackingPose(framePose), markerInCamera);
    }
}
