using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 钉死 4U <c>frame.pose</c> 进入 Unity 追踪系的转换，再与 Unity 相机系的 marker 合成。
///
/// 样例给 <c>FrameTarget</c> 赋原样，那是预览物体，不是这条合成链。
/// marker 已由 <see cref="PlanarPoseSolver.ToUnityCameraSpace"/> 翻成 Y-up / Z-forward；
/// 父位姿必须同一套约定，否则盒子会出现在观察者背后（design D14）。
/// </summary>
public class PicoEnterpriseCameraPoseTests
{
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
    public void ComposeWorld_NegatesParentZ_ThenOffsetsAlongConvertedForward()
    {
        var framePose = new Pose(new Vector3(0.10f, 0.30f, 0.20f), Quaternion.identity);
        var markerInCamera = new Pose(new Vector3(0f, 0f, 1f), Quaternion.identity);

        Pose world = PicoEnterpriseCameraPose.ComposeWorld(framePose, markerInCamera);

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

        Pose world = PicoEnterpriseCameraPose.ComposeWorld(framePose, markerInCamera);

        Assert.AreEqual(-1f, world.position.x, 0.0001f);
        Assert.AreEqual(0f, world.position.y, 0.0001f);
        Assert.AreEqual(0f, world.position.z, 0.0001f);
    }
}
