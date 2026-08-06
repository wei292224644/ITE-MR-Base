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
    public void VisualAnchors_TwoQrCodesRemainIndependentAndFollowTheirOwnPose()
    {
        MarkerProbeVisualAnchorManager visuals = host.AddComponent<MarkerProbeVisualAnchorManager>();
        var firstPose = new Pose(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 10f, 0f));
        var secondPose = new Pose(new Vector3(-1f, 1f, 4f), Quaternion.Euler(0f, -20f, 0f));
        var firstMovedPose = new Pose(new Vector3(2f, 2.5f, 3.5f), Quaternion.Euler(5f, 30f, 0f));

        visuals.ShowOrUpdate(100, "0", "0", firstPose);
        visuals.ShowOrUpdate(250, "250", "250", secondPose);

        Assert.AreEqual(2, visuals.ActiveAnchorCount);
        Assert.IsTrue(visuals.TryGetAnchorState(100, out MarkerProbeVisualAnchorState first));
        Assert.IsTrue(visuals.TryGetAnchorState(250, out MarkerProbeVisualAnchorState second));
        StringAssert.Contains("QR: 0", first.LabelText);
        StringAssert.Contains("MarkerID: 0", first.LabelText);
        StringAssert.Contains("QR: 250", second.LabelText);
        StringAssert.Contains("MarkerID: 250", second.LabelText);

        visuals.ShowOrUpdate(100, "0", "0", firstMovedPose);

        Assert.IsTrue(visuals.TryGetAnchorState(100, out first));
        Assert.IsTrue(visuals.TryGetAnchorState(250, out second));
        Assert.AreEqual(firstMovedPose.position, first.MarkerPose.position);
        Assert.AreEqual(firstMovedPose.rotation, first.MarkerPose.rotation);
        Assert.AreEqual(secondPose.position, second.MarkerPose.position);
        Assert.AreEqual(secondPose.rotation, second.MarkerPose.rotation);
    }

    [Test]
    public void VisualAnchor_CubeBottomTouchesQrPlaneAlongPositiveNormal()
    {
        MarkerProbeVisualAnchorManager visuals = host.AddComponent<MarkerProbeVisualAnchorManager>();
        var qrPlane = new Rect(-0.055f, -0.095f, 0.16f, 0.16f);

        visuals.ShowOrUpdate(100, "0", "0", Pose.identity, qrPlane);

        Transform cube = visuals.GetComponentsInChildren<MeshFilter>(true).Single().transform;
        float cubeHalfSize = cube.localScale.z * 0.5f;
        Assert.That(cube.localPosition.x, Is.EqualTo(qrPlane.center.x).Within(0.0001f));
        Assert.That(cube.localPosition.y, Is.EqualTo(qrPlane.center.y).Within(0.0001f));
        Assert.That(cube.localPosition.z, Is.EqualTo(cubeHalfSize).Within(0.0001f));
        Assert.That(cube.localPosition.z - cubeHalfSize, Is.EqualTo(0f).Within(0.0001f));
    }

    [Test]
    public void VisualAnchors_HidingOneQrLeavesTheOtherVisible()
    {
        MarkerProbeVisualAnchorManager visuals = host.AddComponent<MarkerProbeVisualAnchorManager>();
        visuals.ShowOrUpdate(100, "0", "0", new Pose(Vector3.left, Quaternion.identity));
        visuals.ShowOrUpdate(250, "250", "250", new Pose(Vector3.right, Quaternion.identity));

        visuals.Hide(100);

        Assert.AreEqual(1, visuals.ActiveAnchorCount);
        Assert.IsTrue(visuals.TryGetAnchorState(100, out MarkerProbeVisualAnchorState first));
        Assert.IsTrue(visuals.TryGetAnchorState(250, out MarkerProbeVisualAnchorState second));
        Assert.IsFalse(first.Visible);
        Assert.IsTrue(second.Visible);
    }

    [Test]
    public void DiagnosticVisualAnchors_EndSessionClearsAllAnchors()
    {
        entry.ShowOrUpdateDiagnosticAnchor(100, "0", "0", Pose.identity);
        entry.ShowOrUpdateDiagnosticAnchor(
            250,
            "250",
            "250",
            new Pose(Vector3.right, Quaternion.identity));

        Assert.AreEqual(2, entry.DiagnosticVisualAnchorCount);

        entry.EndSession();

        Assert.AreEqual(0, entry.DiagnosticVisualAnchorCount);
    }

    [Test]
    public void FakeQuest_AddedUpdatedRemovedInvalidPoseAndLateEventAreIsolated()
    {
        Assert.IsTrue(entry.StartNextRun());
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
        MarkerProbeLogEvent exactMatch = events
            .Single(value => value.eventType == "quest_trackable_added");
        Assert.IsTrue(exactMatch.markerMatchesCurrentRun);
        Assert.AreEqual("valid_exact_match", exactMatch.markerSampleClassification);
        Assert.AreEqual("not_tracked", events
            .Single(value => value.eventType == "quest_trackable_removed")
            .markerSampleClassification);
    }

    [Test]
    public void FakePico_MarkerRegistryResolvesKnownIdsAndRecordsUnknownIds()
    {
        Assert.IsTrue(entry.StartNextRun());
        long generation = entry.CurrentSessionGeneration;
        string firstRunId = entry.CurrentRun.runId;
        Assert.AreEqual(MarkerProbeState.TrackingRequested, entry.CurrentState);
        entry.RecordPicoMarkerSample(generation, MarkerSample("0", 1));
        Assert.AreEqual(MarkerProbeState.RegistryResolved, entry.CurrentState);
        entry.RecordPicoMarkerSample(generation, MarkerSample("999", 1));
        Assert.AreEqual(MarkerProbeState.RegistryMiss, entry.CurrentState);
        entry.RecordPicoMarkerSample(generation, MarkerSample("0", 0));
        Assert.AreEqual(MarkerProbeState.RegistryMiss, entry.CurrentState);
        entry.EndCurrentRun();
        entry.EndSession();
        MarkerProbeLogEvent[] events = ReadEvents(logPath);
        MarkerProbeLogEvent firstRunStarted = events.First(value =>
            value.eventType == "run_started" && value.runId == firstRunId);
        Assert.AreEqual("0", firstRunStarted.expectedMarkerId);
        CollectionAssert.AreEqual(new[] { "0" }, firstRunStarted.expectedMarkerIds);
        Assert.IsTrue(events.Any(value => value.eventType == "pico_marker_registry_resolved"));
        Assert.IsTrue(events.Any(value => value.eventType == "pico_marker_registry_miss"));
        Assert.IsTrue(events.Any(value => value.eventType == "pico_marker_invalid_sample"));
        Assert.IsFalse(events.Any(value => value.eventType == "pico_qr_scan_requested"));
        Assert.IsFalse(events.Any(value =>
            value.runId == firstRunId &&
            value.eventType == "pico_qr_scan_requested"));
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
            var logEvent = new MarkerProbeLogEvent
            {
                eventType = eventType,
                nativeEventKind = "fake_mruk",
                trackableIdentityAvailable = true,
                trackableInstanceId = 42,
                trackableAnchorUuid = "00000000-0000-0000-0000-000000000042",
                questIsTrackedAvailable = true,
                questIsTracked = tracked,
                markerIdParseAttempted = true,
                markerIdParseSuccess = true,
                markerId = "0",
                pose = new MarkerProbePoseEvidence
                {
                    unityPose = MarkerProbePoseSerialization.FromUnityPose(pose, "Unity World", "QR"),
                    validation = MarkerProbePoseSerialization.ValidateUnityPose(pose)
                }
            };
            entry.ClassifyQuestObservation(logEvent, tracked, logEvent.pose.validation.valid);
            return entry.TryRecordPlatformEvent(generation, logEvent);
        }

        public bool EmitInvalidPose()
        {
            var invalid = new Pose(Vector3.zero, new Quaternion(0f, 0f, 0f, 0f));
            var logEvent = new MarkerProbeLogEvent
            {
                eventType = "quest_trackable_invalid_pose",
                nativeEventKind = "fake_mruk",
                trackableIdentityAvailable = true,
                trackableInstanceId = 42,
                questIsTrackedAvailable = true,
                questIsTracked = true,
                markerIdParseAttempted = true,
                markerIdParseSuccess = true,
                markerId = "0",
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
            };
            entry.ClassifyQuestObservation(logEvent, true, false);
            return entry.TryRecordPlatformEvent(generation, logEvent);
        }
    }
}
