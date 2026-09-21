using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class MarkerTrackingSessionTests
{
    private static MarkerObservation Observation(string rawPayload, Vector3 position = default)
        => new MarkerObservation(MarkerPlatform.Quest, rawPayload, new Pose(position, Quaternion.identity));

    [Test]
    public void Tick_MarkerVisibleAcrossMultipleTicks_DispatchesObservedEveryTick()
    {
        var source = new MockObservationSource();
        var session = new MarkerTrackingSession(source, lostAfterSeconds: 1.0f);
        var observedPositions = new List<Vector3>();
        session.MarkerObserved += obs => observedPositions.Add(obs.Pose.position);

        source.SetNextPoll(new[] { Observation("250", new Vector3(0, 0, 0)) });
        session.Tick(0.1f);
        source.SetNextPoll(new[] { Observation("250", new Vector3(1, 0, 0)) });
        session.Tick(0.1f);
        source.SetNextPoll(new[] { Observation("250", new Vector3(2, 0, 0)) });
        session.Tick(0.1f);

        Assert.AreEqual(3, observedPositions.Count, "不做跨平台限流:每个 Tick 都应派发,不因为已经发过一次而跳过");
        Assert.AreEqual(new Vector3(2, 0, 0), observedPositions[2], "每次都应携带最新位姿");
    }

    [Test]
    public void Tick_AbsentForLessThanHysteresis_ThenReappears_DoesNotFireLost()
    {
        var source = new MockObservationSource();
        var session = new MarkerTrackingSession(source, lostAfterSeconds: 1.0f);
        int lostCount = 0;
        session.MarkerLost += (_, __) => lostCount++;

        source.SetNextPoll(new[] { Observation("250") });
        session.Tick(0.1f);

        source.SetNextPollEmpty();
        session.Tick(0.5f); // 累计缺席 0.5s,< 1.0s

        source.SetNextPoll(new[] { Observation("250") });
        session.Tick(0.1f);

        Assert.AreEqual(0, lostCount);
    }

    [Test]
    public void Tick_AbsentPastHysteresis_FiresLostExactlyOnce()
    {
        var source = new MockObservationSource();
        var session = new MarkerTrackingSession(source, lostAfterSeconds: 1.0f);
        int lostCount = 0;
        session.MarkerLost += (_, __) => lostCount++;

        source.SetNextPoll(new[] { Observation("250") });
        session.Tick(0.1f);

        source.SetNextPollEmpty();
        session.Tick(0.6f); // 累计 0.6s
        Assert.AreEqual(0, lostCount, "未到滞回阈值,不应触发");

        session.Tick(0.6f); // 累计 1.2s,超过 1.0s 阈值
        Assert.AreEqual(1, lostCount, "超过滞回阈值,应触发且仅触发一次");

        session.Tick(0.6f); // 持续缺席
        Assert.AreEqual(1, lostCount, "持续缺席不应重复触发");
    }

    [Test]
    public void Tick_PlatformRemovalWithinHysteresis_DoesNotFireLost()
    {
        // Quest 的 TrackableRemoved 没有专门 API:效果就是 Poll() 不再返回该标记,
        // 与其他缺席原因(遮挡、单帧漏检)走同一条滞回判定(design D5)。
        var source = new MockObservationSource();
        var session = new MarkerTrackingSession(source, lostAfterSeconds: 1.0f);
        int lostCount = 0;
        session.MarkerLost += (_, __) => lostCount++;

        source.SetNextPoll(new[] { Observation("250") });
        session.Tick(0.1f);

        source.SetNextPollEmpty(); // 模拟 TrackableRemoved 之后 Poll() 不再包含该标记
        session.Tick(0.3f);

        source.SetNextPoll(new[] { Observation("250") });
        session.Tick(0.1f);

        Assert.AreEqual(0, lostCount);
    }

    [Test]
    public void Tick_ReappearsAfterLost_DispatchesObservedAndResetsAbsence()
    {
        var source = new MockObservationSource();
        var session = new MarkerTrackingSession(source, lostAfterSeconds: 1.0f);
        int lostCount = 0;
        int observedCount = 0;
        session.MarkerLost += (_, __) => lostCount++;
        session.MarkerObserved += _ => observedCount++;

        source.SetNextPoll(new[] { Observation("250") });
        session.Tick(0.1f);

        source.SetNextPollEmpty();
        session.Tick(1.5f); // 超过滞回阈值,触发一次丢失
        Assert.AreEqual(1, lostCount);

        source.SetNextPoll(new[] { Observation("250") });
        session.Tick(0.1f); // 重新出现,按新一轮处理
        Assert.AreEqual(2, observedCount);

        // 再次经历一轮完整的缺席→丢失,验证缺席计时确实被重置而不是延续上一轮。
        source.SetNextPollEmpty();
        session.Tick(0.6f);
        Assert.AreEqual(1, lostCount, "刚重新出现,不应立即算作继续缺席");
        session.Tick(0.6f);
        Assert.AreEqual(2, lostCount, "缺席计时重置后,应能再次独立触发一次丢失");
    }

    [Test]
    public void Tick_WhilePaused_DispatchesNothingAndDoesNotAccumulateAbsence()
    {
        var source = new MockObservationSource();
        var session = new MarkerTrackingSession(source, lostAfterSeconds: 1.0f);
        int lostCount = 0;
        int observedCount = 0;
        session.MarkerLost += (_, __) => lostCount++;
        session.MarkerObserved += _ => observedCount++;

        source.SetNextPoll(new[] { Observation("250") });
        session.Tick(0.1f);
        Assert.AreEqual(1, observedCount);

        session.Pause();
        Assert.IsTrue(source.IsPaused);

        source.SetNextPollEmpty();
        session.Tick(2.0f); // 暂停期间即使"缺席"超过滞回阈值,也不应计入或触发
        Assert.AreEqual(1, observedCount, "暂停期间不应派发任何观测");
        Assert.AreEqual(0, lostCount, "暂停期间不应派发任何丢失");

        session.Resume();
        Assert.IsFalse(source.IsPaused);

        source.SetNextPoll(new[] { Observation("250") });
        session.Tick(0.1f);
        Assert.AreEqual(0, lostCount, "暂停超过滞回时长后恢复,标记始终在视野内,不应被误判为丢失");
    }

    [Test]
    public void Tick_SameElapsedTimeSplitDifferently_ProducesSameHysteresisConclusion()
    {
        int lostCountFewLargeTicks = RunAbsenceScenario(deltaTimeStep: 0.5f, tickCount: 3); // 1.5s 总计
        int lostCountManySmallTicks = RunAbsenceScenario(deltaTimeStep: 0.1f, tickCount: 15); // 1.5s 总计

        Assert.AreEqual(1, lostCountFewLargeTicks);
        Assert.AreEqual(1, lostCountManySmallTicks);
        Assert.AreEqual(lostCountFewLargeTicks, lostCountManySmallTicks);
    }

    private static int RunAbsenceScenario(float deltaTimeStep, int tickCount)
    {
        var source = new MockObservationSource();
        var session = new MarkerTrackingSession(source, lostAfterSeconds: 1.0f);
        int lostCount = 0;
        session.MarkerLost += (_, __) => lostCount++;

        source.SetNextPoll(new[] { Observation("250") });
        session.Tick(0.1f);

        source.SetNextPollEmpty();
        for (int i = 0; i < tickCount; i++)
        {
            session.Tick(deltaTimeStep);
        }

        return lostCount;
    }
}
