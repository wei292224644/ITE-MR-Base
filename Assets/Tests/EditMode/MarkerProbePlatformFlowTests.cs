using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class MarkerProbePlatformFlowTests
{
    private GameObject host;
    private MarkerProbeEntry entry;
    private string logPath;

    [SetUp]
    public void SetUp()
    {
        host = new GameObject("MarkerProbePlatformFlowTest");
        entry = host.AddComponent<MarkerProbeEntry>();
        PropertyInfo running = typeof(MarkerProbeEntry).GetProperty(nameof(MarkerProbeEntry.IsProbeRunning));
        running.SetValue(entry, true);
        Assert.IsTrue(entry.StartSession());
        logPath = entry.CurrentLogPath;
    }

    [TearDown]
    public void TearDown()
    {
        if (entry != null && entry.IsSessionActive)
        {
            entry.EndSession();
        }

        UnityEngine.Object.DestroyImmediate(host);
        if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
        {
            File.Delete(logPath);
        }

    }

    [Test]
    public void FakeQuest_AddedUpdatedRemovedInvalidPoseAndLateEventAreIsolated()
    {
        var fake = new FakeQuestObservation(entry, entry.CurrentSessionGeneration);
        var firstPose = new Pose(new Vector3(1f, 2f, 3f), Quaternion.identity);
        var updatedPose = new Pose(new Vector3(2f, 2f, 3f), Quaternion.Euler(0f, 10f, 0f));

        Assert.IsTrue(fake.Emit("quest_trackable_added", firstPose, true));
        Assert.IsTrue(fake.Emit("quest_trackable_updated_equivalent", updatedPose, true));
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
            "\\[MarkerProbe\\].*event=quest_trackable_invalid_pose.*errorCode=QUEST_INVALID_POSE"));
        Assert.IsTrue(fake.EmitInvalidPose());
        Assert.IsTrue(fake.Emit("quest_trackable_removed", updatedPose, false));

        entry.EndSession();
        Assert.IsFalse(fake.Emit("quest_trackable_late", firstPose, true));

        MarkerProbeLogEvent[] events = ReadEvents(logPath);
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "quest_trackable_added",
                "quest_trackable_updated_equivalent",
                "quest_trackable_invalid_pose",
                "quest_trackable_removed"
            },
            events.Select(value => value.eventType));
        Assert.IsFalse(events.Any(value => value.eventType == "quest_trackable_late"));
        MarkerProbeLogEvent[] trackableEvents = events
            .Where(value => value.trackableIdentityAvailable)
            .ToArray();
        Assert.IsTrue(trackableEvents.All(value => value.trackableInstanceId == 42));
        Assert.AreEqual(2d, events
            .Single(value => value.eventType == "quest_trackable_updated_equivalent")
            .pose.unityPose.position.x);
        Assert.IsFalse(events
            .Single(value => value.eventType == "quest_trackable_invalid_pose")
            .pose.validation.valid);
    }

    [Test]
    public void FakePico_QrAndMarkerStateMachineCoversSuccessEmptyWatchdogAndLateResult()
    {
        Assert.IsTrue(entry.StartNextRun());
        long generation = entry.CurrentSessionGeneration;
        string firstRunId = entry.CurrentRun.runId;
        IMarkerIdParser parser = entry.CreateMarkerIdParser();

        Assert.IsTrue(entry.RecordPicoQrScanResult(
            generation,
            1,
            firstRunId,
            parser.Parse("0"),
            false,
            DateTime.UtcNow.ToString("O"),
            1d,
            7,
            120d));
        Assert.AreEqual(MarkerProbeState.AwaitingMatchingMarker, entry.CurrentState);

        entry.RecordPicoMarkerSample(generation, MarkerSample("250", 1));
        Assert.AreEqual(MarkerProbeState.AwaitingMatchingMarker, entry.CurrentState);
        entry.RecordPicoMarkerSample(generation, MarkerSample("0", 0));
        Assert.AreEqual(MarkerProbeState.AwaitingMatchingMarker, entry.CurrentState);
        entry.RecordPicoMarkerSample(generation, MarkerSample("0", 1));
        Assert.AreEqual(MarkerProbeState.MatchingMarkerObserved, entry.CurrentState);
        entry.EndCurrentRun();

        Assert.IsTrue(entry.StartNextRun());
        string emptyRunId = entry.CurrentRun.runId;
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
            "\\[MarkerProbe\\].*event=pico_qr_scan_result.*errorCode=PICO_QR_NULLPAYLOAD"));
        Assert.IsTrue(entry.RecordPicoQrScanResult(
            generation,
            2,
            emptyRunId,
            parser.Parse(null),
            false,
            DateTime.UtcNow.ToString("O"),
            2d,
            8,
            80d));
        Assert.AreEqual(MarkerProbeState.RunEnded, entry.CurrentRun.state);
        Assert.AreEqual(MarkerProbeEndReason.InvalidPayload, entry.CurrentRun.endReason);

        Assert.IsFalse(entry.RecordPicoQrScanResult(
            generation,
            1,
            firstRunId,
            parser.Parse("0"),
            false,
            DateTime.UtcNow.ToString("O"),
            3d,
            9,
            2000d));

        Assert.IsTrue(entry.StartNextRun());
        string watchdogRunId = entry.CurrentRun.runId;
        Assert.IsTrue(entry.RecordPicoQrScanTerminalEvent(
            generation,
            3,
            watchdogRunId,
            "pico_qr_scan_watchdog_timeout",
            MarkerProbeEndReason.WatchdogTimeout,
            "fake no-callback timeout"));
        Assert.AreEqual(MarkerProbeState.RunEnded, entry.CurrentRun.state);
        Assert.AreEqual(MarkerProbeEndReason.WatchdogTimeout, entry.CurrentRun.endReason);

        entry.EndSession();
        MarkerProbeLogEvent[] events = ReadEvents(logPath);
        Assert.IsTrue(events.Any(value => value.eventType == "pico_qr_scan_result"));
        Assert.IsTrue(events.Any(value => value.eventType == "pico_marker_id_mismatch"));
        Assert.IsTrue(events.Any(value => value.eventType == "pico_marker_invalid_sample"));
        Assert.IsTrue(events.Any(value => value.eventType == "pico_matching_marker_observed"));
        Assert.IsTrue(events.Any(value => value.eventType == "pico_qr_scan_watchdog_timeout"));
        Assert.IsFalse(events.Any(value =>
            value.runId == firstRunId &&
            value.monotonicTimeSeconds == 3d));
    }

    private static MarkerProbeLogEvent MarkerSample(string markerId, int validFlag)
    {
        return new MarkerProbeLogEvent
        {
            nativeEventKind = "fake_pico_marker_callback",
            markerId = markerId,
            validFlagAvailable = true,
            validFlag = validFlag,
            pose = new MarkerProbePoseEvidence
            {
                unityTrackingOriginPose = MarkerProbePoseSerialization.FromUnityPose(
                    Pose.identity,
                    "origin",
                    "ArUco"),
                unityPose = MarkerProbePoseSerialization.FromUnityPose(Pose.identity, "world", "ArUco"),
                validation = MarkerProbePoseSerialization.ValidateUnityPose(Pose.identity)
            }
        };
    }

    private static MarkerProbeLogEvent[] ReadEvents(string path)
    {
        return File.ReadAllLines(path)
            .Select(JsonUtility.FromJson<MarkerProbeLogEvent>)
            .ToArray();
    }

    private sealed class FakeQuestObservation
    {
        private readonly MarkerProbeEntry entry;
        private readonly long generation;

        public FakeQuestObservation(MarkerProbeEntry entry, long generation)
        {
            this.entry = entry;
            this.generation = generation;
        }

        public bool Emit(string eventType, Pose pose, bool tracked)
        {
            return entry.TryRecordPlatformEvent(generation, new MarkerProbeLogEvent
            {
                eventType = eventType,
                nativeEventKind = "fake_mruk",
                trackableIdentityAvailable = true,
                trackableInstanceId = 42,
                trackableAnchorUuid = "00000000-0000-0000-0000-000000000042",
                questIsTrackedAvailable = true,
                questIsTracked = tracked,
                markerId = "0",
                pose = new MarkerProbePoseEvidence
                {
                    unityPose = MarkerProbePoseSerialization.FromUnityPose(pose, "Unity World", "QR"),
                    validation = MarkerProbePoseSerialization.ValidateUnityPose(pose)
                }
            });
        }

        public bool EmitInvalidPose()
        {
            var invalid = new Pose(Vector3.zero, new Quaternion(0f, 0f, 0f, 0f));
            return entry.TryRecordPlatformEvent(generation, new MarkerProbeLogEvent
            {
                eventType = "quest_trackable_invalid_pose",
                nativeEventKind = "fake_mruk",
                trackableIdentityAvailable = true,
                trackableInstanceId = 42,
                pose = new MarkerProbePoseEvidence
                {
                    validation = MarkerProbePoseSerialization.ValidateUnityPose(invalid)
                },
                error = new MarkerProbeErrorContext
                {
                    category = "pose",
                    errorCode = "QUEST_INVALID_POSE",
                    message = "fake invalid pose"
                }
            });
        }
    }
}
