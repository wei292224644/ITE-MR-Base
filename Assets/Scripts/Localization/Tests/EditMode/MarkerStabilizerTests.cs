using NUnit.Framework;
using UnityEngine;

public class MarkerStabilizerTests
{
    [Test]
    public void Feed_SamePoseRepeatedly_FiresStabilizedAfterWindow()
    {
        var stabilizer = new MarkerStabilizer(positionThreshold: 0.05f, rotationThreshold: 1f, smoothTime: 0.001f, stableSeconds: 3f);
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
        var stabilizer = new MarkerStabilizer(positionThreshold: 0.05f, rotationThreshold: 1f, smoothTime: 0.001f, stableSeconds: 3f);
        int firedCount = 0;
        stabilizer.Stabilized += (_, __) => firedCount++;

        for (int i = 0; i < 10; i++)
        {
            stabilizer.Feed("A", new Pose(new Vector3(i, 0, 0), Quaternion.identity), 1f);
        }

        Assert.AreEqual(0, firedCount);
    }

    /// <summary>
    /// 每次出现只提交一次（marker-rescan D1）：判稳之后位姿再移动、再稳定，也不重发。
    /// 码固定贴在场地里，持续观测期间的移动只来自识别噪声——PICO 上旧规则约 4 秒误触发一次重扫。
    /// </summary>
    [Test]
    public void Feed_FiresOncePerAppearance_EvenAfterMovingAndSettlingAgain()
    {
        var stabilizer = new MarkerStabilizer(positionThreshold: 0.05f, rotationThreshold: 1f, smoothTime: 0.001f, stableSeconds: 2f);
        int firedCount = 0;
        stabilizer.Stabilized += (_, __) => firedCount++;

        var poseA = new Pose(new Vector3(1, 0, 0), Quaternion.identity);
        stabilizer.Feed("A", poseA, 1f);
        stabilizer.Feed("A", poseA, 1f); // 累计到窗口,触发
        Assert.AreEqual(1, firedCount);

        var poseB = new Pose(new Vector3(5, 0, 0), Quaternion.identity);
        for (int i = 0; i < 5; i++)
        {
            stabilizer.Feed("A", poseB, 1f); // 移动后再稳定,远超窗口
        }

        Assert.AreEqual(1, firedCount, "同一次出现只提交一次");
    }

    /// <summary>Reset 之后是新的一次出现：重新判稳、再提交一次（丢失时由桥接调用）。</summary>
    [Test]
    public void Reset_ThenStableAgain_FiresAgain()
    {
        var stabilizer = new MarkerStabilizer(positionThreshold: 0.05f, rotationThreshold: 1f, smoothTime: 0.001f, stableSeconds: 2f);
        int firedCount = 0;
        stabilizer.Stabilized += (_, __) => firedCount++;

        var pose = new Pose(new Vector3(1, 0, 0), Quaternion.identity);
        stabilizer.Feed("A", pose, 1f);
        stabilizer.Feed("A", pose, 1f);
        Assert.AreEqual(1, firedCount);

        stabilizer.Reset("A");
        stabilizer.Feed("A", pose, 1f);
        Assert.AreEqual(1, firedCount, "重新判稳要走满窗口");
        stabilizer.Feed("A", pose, 1f);
        Assert.AreEqual(2, firedCount);
    }

    /// <summary>
    /// 窗口是时间不是次数(design D22)。两端派发速率实测差 12.5 倍
    /// (PICO 5.6 Hz / Quest 70 Hz),同一个「次数」阈值在两端是完全不同的等待时长:
    /// 帧计数制下 30 次在 Quest 上是 0.43 s、在 PICO 上是 5.4 s。
    /// </summary>
    [Test]
    public void StableWindow_IsEquivalentAcrossFeedRates()
    {
        var pose = new Pose(new Vector3(1, 0, 0), Quaternion.identity);

        float FirstFireTime(float hz)
        {
            var stabilizer = new MarkerStabilizer(
                positionThreshold: 0.05f, rotationThreshold: 1f, smoothTime: 0.2f, stableSeconds: 0.5f);
            float firedAt = -1f;
            float elapsed = 0f;
            stabilizer.Stabilized += (_, __) => { if (firedAt < 0f) firedAt = elapsed; };

            float dt = 1f / hz;
            for (int i = 0; i < Mathf.CeilToInt(hz * 3f); i++)
            {
                elapsed += dt;
                stabilizer.Feed("A", pose, dt);
            }

            return firedAt;
        }

        float slow = FirstFireTime(5.6f);   // PICO 实测速率
        float fast = FirstFireTime(70f);    // Quest 实测速率

        Assert.That(slow, Is.GreaterThan(0f), "慢速率下也必须触发");
        Assert.That(fast, Is.GreaterThan(0f));
        Assert.That(slow, Is.EqualTo(fast).Within(1f / 5.6f),
            "两端的首次触发时刻应当只差一个慢端的帧间隔,而不是差一个数量级");
    }

    /// <summary>
    /// smoothTime 必须真的平滑(design D9)。旧实现用 <c>Clamp01(deltaTime / smoothTime)</c>,
    /// 默认 smoothTime=0.01 而帧间隔约 0.014 → t 恒为 1,平滑位姿每帧跳到目标,
    /// 于是「平滑位姿 vs 目标位姿」的差就是相邻两帧的原始抖动,阈值永远过不去。
    /// </summary>
    [Test]
    public void Smoothing_WithTimeConstantLargerThanFrame_DoesNotSnap()
    {
        var stabilizer = new MarkerStabilizer(
            positionThreshold: 0f, rotationThreshold: 0f, smoothTime: 0.2f, stableSeconds: 999f);

        stabilizer.Feed("A", new Pose(Vector3.zero, Quaternion.identity), 0.014f);
        stabilizer.Feed("A", new Pose(new Vector3(10f, 0f, 0f), Quaternion.identity), 0.014f);

        Assert.That(stabilizer.SmoothedPose("A").position.x, Is.LessThan(9f),
            "一帧就跳到目标说明平滑没生效");
        Assert.That(stabilizer.SmoothedPose("A").position.x, Is.GreaterThan(0f));
    }

    /// <summary>抖动大于阈值时永不判稳——这正是 PICO 上「扫不到」的机理，必须可复现。</summary>
    [Test]
    public void Jitter_AboveRotationThreshold_NeverStabilizes()
    {
        var stabilizer = new MarkerStabilizer(
            positionThreshold: 0.05f, rotationThreshold: 1f, smoothTime: 0.2f, stableSeconds: 0.5f);
        int firedCount = 0;
        stabilizer.Stabilized += (_, __) => firedCount++;

        // 相邻两次抖 3 度,落在 PICO 实测的 2-5 度区间内
        for (int i = 0; i < 60; i++)
        {
            var rotation = Quaternion.Euler(0f, (i % 2) * 3f, 0f);
            stabilizer.Feed("A", new Pose(Vector3.zero, rotation), 1f / 5.6f);
        }

        Assert.AreEqual(0, firedCount, "阈值 1 度而抖动 3 度 → 永不判稳,与真机现象一致");
    }
}
