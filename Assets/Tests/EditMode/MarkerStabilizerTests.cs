using NUnit.Framework;
using UnityEngine;

public class MarkerStabilizerTests
{
    [Test]
    public void Feed_SamePoseRepeatedly_FiresStabilizedAfterThreshold()
    {
        var stabilizer = new MarkerStabilizer(positionThreshold: 0.05f, rotationThreshold: 1f, smoothTime: 0.001f, stableFrameThreshold: 3);
        int firedCount = 0;
        string firedId = null;
        stabilizer.Stabilized += (id, pose) => { firedCount++; firedId = id; };

        var pose = new Pose(new Vector3(1, 2, 3), Quaternion.identity);

        stabilizer.Feed("A", pose, 1f);
        Assert.AreEqual(0, firedCount);

        stabilizer.Feed("A", pose, 1f);
        Assert.AreEqual(0, firedCount);

        stabilizer.Feed("A", pose, 1f);
        Assert.AreEqual(1, firedCount);
        Assert.AreEqual("A", firedId);
    }

    [Test]
    public void Feed_PoseKeepsMoving_NeverFiresStabilized()
    {
        var stabilizer = new MarkerStabilizer(positionThreshold: 0.05f, rotationThreshold: 1f, smoothTime: 0.001f, stableFrameThreshold: 3);
        int firedCount = 0;
        stabilizer.Stabilized += (_, __) => firedCount++;

        for (int i = 0; i < 10; i++)
        {
            stabilizer.Feed("A", new Pose(new Vector3(i, 0, 0), Quaternion.identity), 1f);
        }

        Assert.AreEqual(0, firedCount);
    }

    [Test]
    public void Feed_FiresOnce_ThenRefiresOnlyAfterMovingAgain()
    {
        var stabilizer = new MarkerStabilizer(positionThreshold: 0.05f, rotationThreshold: 1f, smoothTime: 0.001f, stableFrameThreshold: 2);
        int firedCount = 0;
        stabilizer.Stabilized += (_, __) => firedCount++;

        var poseA = new Pose(new Vector3(1, 0, 0), Quaternion.identity);
        stabilizer.Feed("A", poseA, 1f);
        stabilizer.Feed("A", poseA, 1f); // 第2次达到阈值,触发
        stabilizer.Feed("A", poseA, 1f); // 仍稳定,不重复触发
        Assert.AreEqual(1, firedCount);

        var poseB = new Pose(new Vector3(5, 0, 0), Quaternion.identity);
        stabilizer.Feed("A", poseB, 1f); // 移动了,重新计数
        stabilizer.Feed("A", poseB, 1f); // 再次达到阈值,重新触发
        Assert.AreEqual(2, firedCount);
    }
}
