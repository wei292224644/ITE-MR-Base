using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class MarkerProbeCoreTests
{
    [TestCase("0", "0")]
    [TestCase("250", "250")]
    public void MarkerIdParser_AcceptsCanonicalFixtureIds(string raw, string expected)
    {
        MarkerIdParseResult result = new StandardFixtureMarkerIdParser().Parse(raw);

        Assert.IsTrue(result.success);
        Assert.AreEqual(expected, result.markerId);
        Assert.AreEqual(MarkerIdParseFailure.None, result.failure);
    }

    [TestCase(null, MarkerIdParseFailure.NullPayload)]
    [TestCase("", MarkerIdParseFailure.EmptyPayload)]
    [TestCase("00", MarkerIdParseFailure.LeadingZeroNotCanonical)]
    [TestCase("0250", MarkerIdParseFailure.LeadingZeroNotCanonical)]
    [TestCase("2x0", MarkerIdParseFailure.NonDecimalPayload)]
    [TestCase("251", MarkerIdParseFailure.UnsupportedMarkerId)]
    public void MarkerIdParser_RejectsNonCanonicalPayloads(
        string raw,
        MarkerIdParseFailure expectedFailure)
    {
        MarkerIdParseResult result = new StandardFixtureMarkerIdParser().Parse(raw);

        Assert.IsFalse(result.success);
        Assert.AreEqual(expectedFailure, result.failure);
        Assert.IsNotEmpty(result.failureDetail);
    }

    [Test]
    public void RawPayload_DefaultIsHashed_ExplicitDevelopmentModeIncludesPlaintext()
    {
        MarkerIdParseResult privateResult = new StandardFixtureMarkerIdParser(false).Parse("250");
        Assert.IsFalse(privateResult.rawPayload.rawPayloadCaptured);
        Assert.IsNull(privateResult.rawPayload.plaintext);
        Assert.AreEqual(3, privateResult.rawPayload.utf8ByteLength);
        Assert.AreEqual(64, privateResult.rawPayload.sha256.Length);

        MarkerIdParseResult explicitResult = new StandardFixtureMarkerIdParser(true).Parse("250");
        Assert.AreEqual(Debug.isDebugBuild, explicitResult.rawPayload.rawPayloadCaptured);
        Assert.AreEqual(Debug.isDebugBuild ? "250" : null, explicitResult.rawPayload.plaintext);
    }

    [Test]
    public void Jsonl_EachLineParses_SequenceAndReplayOrderAreStable()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path;
            using (var writer = new MarkerProbeJsonlWriter("jsonl-order", directory))
            {
                path = writer.FilePath;
                writer.Write(Event("requested", 1d));
                writer.Write(Event("callback", 2d));
                writer.Write(Event("matched", 3d));
                writer.Flush();
            }

            string[] lines = File.ReadAllLines(path);
            Assert.AreEqual(3, lines.Length);
            MarkerProbeLogEvent[] events = lines
                .Select(JsonUtility.FromJson<MarkerProbeLogEvent>)
                .ToArray();
            CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, events.Select(value => value.sequence));
            CollectionAssert.AreEqual(
                new[] { "requested", "callback", "matched" },
                events.Select(value => value.eventType));
            Assert.IsTrue(events.All(value =>
                value.schemaVersion == "marker-probe-v1" &&
                value.sessionId == "jsonl-order" &&
                value.platform == "Fake" &&
                !string.IsNullOrEmpty(value.utcTimestamp) &&
                value.monotonicTimeSeconds > 0d &&
                value.threadId != 0));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void Jsonl_FlushSummaryAndClosedWriterAreDeterministic()
    {
        string directory = CreateTempDirectory();
        try
        {
            var writer = new MarkerProbeJsonlWriter("jsonl-close", directory);
            writer.Write(Event("sample", 1d));
            writer.Write(Event("sample", 4d));
            writer.Flush();
            Assert.AreEqual(2, File.ReadAllLines(writer.FilePath).Length);

            MarkerProbeSessionSummary summary = writer.CreateFinalSummary(
                2,
                MarkerProbeEndReason.UnrecoverableError,
                "forced test ending");
            Assert.AreEqual(3, summary.totalEventCount);
            Assert.AreEqual(2, summary.droppedEventCount);
            Assert.AreEqual(3000d, summary.maxSilenceMilliseconds, 0.01d);
            Assert.AreEqual("UnrecoverableError", summary.endReason);

            writer.Dispose();
            Assert.Throws<ObjectDisposedException>(() => writer.Write(Event("late", 5d)));
            Assert.Throws<ObjectDisposedException>(() => writer.Flush());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void PoseEvidence_PreservesSpacesAndComputesRelativeTransforms()
    {
        var a = new Pose(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 30f, 0f));
        var b = new Pose(new Vector3(2f, 2f, 3f), Quaternion.Euler(0f, 50f, 0f));
        Quaternion inverseA = Quaternion.Inverse(a.rotation);
        var relative = new Pose(inverseA * (b.position - a.position), inverseA * b.rotation);

        var evidence = new MarkerProbePoseEvidence
        {
            nativePose = MarkerProbePoseSerialization.FromDoubleComponents(
                1d, 2d, 3d, 0d, 0d, 0d, 1d, "native", "ArUco"),
            unityTrackingOriginPose = MarkerProbePoseSerialization.FromUnityPose(a, "origin", "ArUco"),
            unityPose = MarkerProbePoseSerialization.FromUnityPose(b, "world", "ArUco"),
            validation = MarkerProbePoseSerialization.ValidateUnityPose(a)
        };
        var origin = new MarkerProbeXrOriginSnapshot
        {
            available = true,
            trackingOriginMode = "Floor",
            originWorldPose = MarkerProbePoseSerialization.FromUnityPose(Pose.identity, "world", "origin"),
            originLossyScale = MarkerProbePoseSerialization.FromUnityVector(Vector3.one)
        };
        var offset = new MarkerProbeOffsetEvidence
        {
            status = "candidate",
            inputPose = evidence.unityPose,
            candidateOffset = MarkerProbePoseSerialization.FromUnityPose(Pose.identity, "local", "offset"),
            outputPose = MarkerProbePoseSerialization.FromUnityPose(b, "world", "output")
        };

        Assert.AreEqual("native", evidence.nativePose.coordinateSpace);
        Assert.AreEqual("origin", evidence.unityTrackingOriginPose.coordinateSpace);
        Assert.AreEqual("world", evidence.unityPose.coordinateSpace);
        Assert.IsTrue(origin.available);
        Assert.IsNotNull(offset.inputPose);
        Assert.AreEqual(1f, relative.position.magnitude, 0.0001f);
        Assert.AreEqual(20f, Quaternion.Angle(Quaternion.identity, relative.rotation), 0.001f);
    }

    [Test]
    public void ConsoleMirror_ThrottlesPoseButJsonlKeepsEverySample()
    {
        string directory = CreateTempDirectory();
        try
        {
            const string sessionId = "console-correlation";
            var mirror = new MarkerProbeConsoleMirror(sessionId, "test.jsonl", 1d);
            var first = Event("pose", 10d);
            first.markerId = "0";
            first.pose = new MarkerProbePoseEvidence
            {
                unityPose = MarkerProbePoseSerialization.FromUnityPose(Pose.identity, "world", "QR")
            };
            var second = Event("pose", 10.5d);
            second.markerId = "0";
            second.pose = first.pose;

            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(
                "\\[MarkerProbe\\].*session=console-correlation.*marker=0"));
            Assert.IsTrue(mirror.TryLogPose(first));
            Assert.IsFalse(mirror.TryLogPose(second));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "\\[MarkerProbe\\].*session=console-correlation.*errorCode=FAKE_ERROR"));
            mirror.LogError("fake_error", new MarkerProbeErrorContext
            {
                errorCode = "FAKE_ERROR",
                message = "correlation test"
            });

            using var writer = new MarkerProbeJsonlWriter(sessionId, directory);
            writer.Write(first);
            writer.Write(second);
            writer.Flush();
            Assert.AreEqual(2, File.ReadAllLines(writer.FilePath).Length);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static MarkerProbeLogEvent Event(string eventType, double monotonic)
    {
        return new MarkerProbeLogEvent
        {
            schemaVersion = "marker-probe-v1",
            eventType = eventType,
            platform = "Fake",
            utcTimestamp = DateTime.UtcNow.ToString("O"),
            monotonicTimeSeconds = monotonic,
            unityFrame = 1,
            threadId = 1,
            probeState = "Testing"
        };
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "MarkerProbeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
