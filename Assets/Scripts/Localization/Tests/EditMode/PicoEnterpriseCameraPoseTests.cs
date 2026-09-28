using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 钉死 4U <c>frame.pose</c>（头位姿）进入 Unity 追踪系的转换、头→左相机外参的换算，
/// 以及三者与 Unity 相机系 marker 的合成。
///
/// marker 已由 <see cref="PlanarPoseSolver.ToUnityCameraSpace"/> 翻成 Y-up / Z-forward；
/// 父位姿必须同一套约定，否则盒子会出现在观察者背后。
/// 见 docs/superpowers/specs/2026-09-24-pico-camera-extrinsic-design.md。
/// </summary>
public class PicoEnterpriseCameraPoseTests
{
    // 2026-09-24 PICO 4U（PA9410MGL5140677G）启动日志里 GetCameraParametersNewfor4U 的原值。
    static readonly Vector3 MeasuredLPos = new Vector3(-0.03f, 0.00f, -0.07f);
    static readonly Quaternion MeasuredLRot = Normalized(new Quaternion(0.99999f, 0.00226f, 0.00031f, 0.00230f));

    [Test]
    public void ToUnityTrackingPose_NegatesZ_MatchingMeasuredUnityVsSensorSign()
    {
        var sensor = new Pose(new Vector3(0.10f, 0.30f, 0.186f), Quaternion.identity);

        Pose unity = PicoEnterpriseCameraPose.ToUnityTrackingPose(sensor);

        Assert.AreEqual(0.10f, unity.position.x, 0.0001f);
        Assert.AreEqual(0.30f, unity.position.y, 0.0001f);
        Assert.AreEqual(-0.186f, unity.position.z, 0.0001f,
            "真机同一时刻 unityCam z 与 sensorCam z 符号相反。");
        Assert.Less(Quaternion.Angle(unity.rotation, Quaternion.identity), 0.1f);
    }

    [Test]
    public void TryCreateHeadToCamera_MeasuredPico4U_LeftCameraSitsLeftAndForwardOfHead()
    {
        bool ok = PicoEnterpriseCameraPose.TryCreateHeadToCamera(MeasuredLPos, MeasuredLRot, out Pose headToCamera);

        Assert.IsTrue(ok);
        Assert.AreEqual(-0.03f, headToCamera.position.x, 0.0001f, "左相机在头中心左侧。");
        Assert.AreEqual(0.00f, headToCamera.position.y, 0.0001f);
        Assert.AreEqual(0.07f, headToCamera.position.z, 0.0001f,
            "SDK 头系 Z 朝后，-0.07 是前方 7 cm；离线盲拟合得到 +7.5 cm。");
        Assert.Less(Quaternion.Angle(headToCamera.rotation, Quaternion.identity), 1f,
            "l_rot 的绕 X 180° 只是 OpenCV 相机系与头系的约定差，换算后只剩标定残差。");
    }

    [Test]
    public void TryCreateHeadToCamera_SdkIdentityFallback_Rejected()
    {
        // GetCameraParametersNewfor4U 失败时 SDK 回填 identity()：l_pos=0、l_rot=identity，
        // 换算后等于相机朝后，照用会把码解到观察者背后。
        bool ok = PicoEnterpriseCameraPose.TryCreateHeadToCamera(Vector3.zero, Quaternion.identity, out _);

        Assert.IsFalse(ok);
    }

    [Test]
    public void TryCreateHeadToCamera_OffsetFartherThanHeadset_Rejected()
    {
        bool ok = PicoEnterpriseCameraPose.TryCreateHeadToCamera(new Vector3(0f, 0f, -0.5f), MeasuredLRot, out _);

        Assert.IsFalse(ok);
    }

    [Test]
    public void TryCreateHeadToCamera_ZeroQuaternion_Rejected()
    {
        bool ok = PicoEnterpriseCameraPose.TryCreateHeadToCamera(MeasuredLPos, new Quaternion(0f, 0f, 0f, 0f), out _);

        Assert.IsFalse(ok);
    }

    [Test]
    public void ComposeWorld_NegatesParentZ_ThenOffsetsAlongConvertedForward()
    {
        var framePose = new Pose(new Vector3(0.10f, 0.30f, 0.20f), Quaternion.identity);
        var markerInCamera = new Pose(new Vector3(0f, 0f, 1f), Quaternion.identity);

        Pose world = PicoEnterpriseCameraPose.ComposeWorld(framePose, Pose.identity, markerInCamera);

        Assert.AreEqual(0.10f, world.position.x, 0.0001f);
        Assert.AreEqual(0.30f, world.position.y, 0.0001f);
        Assert.AreEqual(0.80f, world.position.z, 0.0001f,
            "父位姿 Z 取反后再加相机系 +Z：-0.20 + 1.0 = 0.80。原样合成会得到 1.20，盒子在背后。");
        Assert.Less(Quaternion.Angle(world.rotation, Quaternion.identity), 0.1f);
    }

    [Test]
    public void ComposeWorld_Yaw90Sensor_MapsCameraForwardToWorldNegativeX()
    {
        var framePose = new Pose(Vector3.zero, Quaternion.Euler(0f, 90f, 0f));
        var markerInCamera = new Pose(new Vector3(0f, 0f, 1f), Quaternion.identity);

        Pose world = PicoEnterpriseCameraPose.ComposeWorld(framePose, Pose.identity, markerInCamera);

        Assert.AreEqual(-1f, world.position.x, 0.0001f);
        Assert.AreEqual(0f, world.position.y, 0.0001f);
        Assert.AreEqual(0f, world.position.z, 0.0001f);
    }

    [Test]
    public void ComposeWorld_HeadToCameraOffset_AddedInHeadFrame()
    {
        var headToCamera = new Pose(new Vector3(-0.03f, 0f, 0.07f), Quaternion.identity);
        var markerInCamera = new Pose(new Vector3(0f, 0f, 0.5f), Quaternion.identity);

        Pose world = PicoEnterpriseCameraPose.ComposeWorld(Pose.identity, headToCamera, markerInCamera);

        Assert.AreEqual(-0.03f, world.position.x, 0.0001f);
        Assert.AreEqual(0f, world.position.y, 0.0001f);
        Assert.AreEqual(0.57f, world.position.z, 0.0001f);
    }

    [Test]
    public void ComposeWorld_HeadToCameraOffset_RotatesWithHead()
    {
        // 偏移固定在头系里，随头转——这正是它不能用 PlatformOffsetConfig（码系常量）补的原因。
        // 传感器 yaw +90° 换进 Unity 是 yaw -90°：头右 → 世界 +Z，头前 → 世界 -X。
        var framePose = new Pose(Vector3.zero, Quaternion.Euler(0f, 90f, 0f));
        var headToCamera = new Pose(new Vector3(-0.03f, 0f, 0.07f), Quaternion.identity);
        var markerInCamera = new Pose(new Vector3(0f, 0f, 1f), Quaternion.identity);

        Pose world = PicoEnterpriseCameraPose.ComposeWorld(framePose, headToCamera, markerInCamera);

        Assert.AreEqual(-1.07f, world.position.x, 0.0001f);
        Assert.AreEqual(0f, world.position.y, 0.0001f);
        Assert.AreEqual(-0.03f, world.position.z, 0.0001f);
    }

    static Quaternion Normalized(Quaternion q)
    {
        float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
    }
}
