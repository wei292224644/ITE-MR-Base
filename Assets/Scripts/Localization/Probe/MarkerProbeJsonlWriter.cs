using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class MarkerProbeLogEvent
{
    public string schemaVersion;
    public string sessionId;
    public long sequence;
    public string eventType;
    public string platform;
    public string runId;
    public string fixtureId;
    public string utcTimestamp;
    public double monotonicTimeSeconds;
    public bool nativeTimestampAvailable;
    public double nativeTimestamp;
    public string nativeTimestampUnit;
    public bool callbackIntervalAvailable;
    public double callbackIntervalMilliseconds;
    public bool requestLatencyAvailable;
    public double requestLatencyMilliseconds;
    public long scanGeneration;
    public int unityFrame;
    public int threadId;
    public string probeState;
    public MarkerProbeErrorContext error;
    public string nativeEventKind;
    public string observationSemantics;
    public bool trackableIdentityAvailable;
    public int trackableInstanceId;
    public string trackableAnchorUuid;
    public string trackableObjectName;
    public bool questIsTrackedAvailable;
    public bool questIsTracked;
    public bool markerIdParseAttempted;
    public bool markerIdParseSuccess;
    public string markerIdParseFailure;
    public string markerIdParseFailureDetail;
    public string markerId;
    public RawPayloadSummary rawPayload;
    public bool validFlagAvailable;
    public int validFlag;
    public bool markerTypeAvailable;
    public int markerType;
    public long markerSnapshotSequence;
    public bool markerSnapshotWasNull;
    public int markerSnapshotEntryIndex;
    public int markerSnapshotEntryCount;
    public int markerIdOccurrenceInSnapshot;
    public bool duplicateMarkerIdInSnapshot;
    public string expectedMarkerId;
    public string[] expectedMarkerIds;
    public bool markerMatchesCurrentRun;
    public string markerSampleClassification;
    public MarkerProbePicoRawPoseSnapshot picoRawPose;
    public MarkerProbeDualMarkerEvidence dualMarker;
    public MarkerProbePoseEvidence pose;
    public MarkerProbeXrOriginSnapshot xrOrigin;
    public MarkerProbeOffsetEvidence candidateOffset;
    public MarkerProbeSdkResult sdkResult;
    public MarkerProbeQuestPreflightSnapshot questPreflight;
    public MarkerProbeSessionModel session;
    public MarkerProbeSessionSummary sessionSummary;
}

[Serializable]
public sealed class MarkerProbeErrorContext
{
    public string category;
    public string errorCode;
    public string exceptionType;
    public string message;
    public string stackTrace;
}

[Serializable]
public sealed class MarkerProbeEventTypeCount
{
    public string eventType;
    public long count;
}

[Serializable]
public sealed class MarkerProbeSessionSummary
{
    public long finalSequence;
    public long totalEventCount;
    public long droppedEventCount;
    public double maxSilenceMilliseconds;
    public string endReason;
    public string endDetail;
    public MarkerProbeEventTypeCount[] eventTypeCounts;
}

/// <summary>
/// Append-only JSONL writer for one marker probe session.
/// Sequence assignment and writes are serialized so every physical line is one complete event.
/// </summary>
public sealed class MarkerProbeJsonlWriter : IDisposable
{
    private readonly object writeGate = new object();
    private readonly StreamWriter streamWriter;
    private readonly string sessionId;
    private readonly Dictionary<string, long> eventTypeCounts = new Dictionary<string, long>();
    private long lastSequence;
    private long eventCount;
    private double lastEventMonotonicSeconds;
    private double maxSilenceMilliseconds;
    private bool disposed;

    public MarkerProbeJsonlWriter(string sessionId, string rootDirectory = null)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            throw new ArgumentException("Session ID must not be empty.", nameof(sessionId));
        }

        if (Path.GetFileName(sessionId) != sessionId)
        {
            throw new ArgumentException("Session ID must be a file-name-safe value, not a path.", nameof(sessionId));
        }

        this.sessionId = sessionId;
        string directory = rootDirectory ?? Path.Combine(Application.persistentDataPath, "MarkerProbe");
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, sessionId + ".jsonl");

        var fileStream = new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        streamWriter = new StreamWriter(fileStream, new UTF8Encoding(false))
        {
            AutoFlush = false,
            NewLine = "\n"
        };
    }

    public string FilePath { get; }

    public long LastSequence
    {
        get
        {
            lock (writeGate)
            {
                return lastSequence;
            }
        }
    }

    public long Write(MarkerProbeLogEvent logEvent)
    {
        if (logEvent == null)
        {
            throw new ArgumentNullException(nameof(logEvent));
        }

        lock (writeGate)
        {
            ThrowIfDisposed();

            logEvent.sessionId = sessionId;
            logEvent.sequence = ++lastSequence;
            string json = JsonUtility.ToJson(logEvent, false);

            // JsonUtility escapes CR/LF inside string fields. This assertion prevents a future
            // serializer change from silently corrupting the one-event-per-line contract.
            if (json.IndexOf('\n') >= 0 || json.IndexOf('\r') >= 0)
            {
                throw new InvalidDataException("Serialized marker probe event contains a physical newline.");
            }

            streamWriter.WriteLine(json);
            eventCount++;
            TrackEventType(logEvent.eventType);
            TrackSilence(logEvent.monotonicTimeSeconds);
            return logEvent.sequence;
        }
    }

    public MarkerProbeSessionSummary CreateFinalSummary(
        long droppedEventCount,
        MarkerProbeEndReason endReason,
        string endDetail)
    {
        lock (writeGate)
        {
            ThrowIfDisposed();

            var eventTypes = new List<string>(eventTypeCounts.Keys);
            if (!eventTypeCounts.ContainsKey("session_ended"))
            {
                eventTypes.Add("session_ended");
            }

            eventTypes.Sort(StringComparer.Ordinal);
            var counts = new MarkerProbeEventTypeCount[eventTypes.Count];
            for (int i = 0; i < eventTypes.Count; i++)
            {
                string eventType = eventTypes[i];
                counts[i] = new MarkerProbeEventTypeCount
                {
                    eventType = eventType,
                    count = (eventTypeCounts.TryGetValue(eventType, out long count) ? count : 0) +
                            (eventType == "session_ended" ? 1 : 0)
                };
            }

            return new MarkerProbeSessionSummary
            {
                finalSequence = lastSequence + 1,
                totalEventCount = eventCount + 1,
                droppedEventCount = droppedEventCount,
                maxSilenceMilliseconds = maxSilenceMilliseconds,
                endReason = endReason.ToString(),
                endDetail = endDetail,
                eventTypeCounts = counts
            };
        }
    }

    public void Flush()
    {
        lock (writeGate)
        {
            ThrowIfDisposed();
            streamWriter.Flush();
        }
    }

    public void Dispose()
    {
        lock (writeGate)
        {
            if (disposed)
            {
                return;
            }

            streamWriter.Flush();
            streamWriter.Dispose();
            disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(MarkerProbeJsonlWriter));
        }
    }

    private void TrackEventType(string eventType)
    {
        string key = string.IsNullOrEmpty(eventType) ? "unknown" : eventType;
        eventTypeCounts.TryGetValue(key, out long count);
        eventTypeCounts[key] = count + 1;
    }

    private void TrackSilence(double monotonicTimeSeconds)
    {
        if (monotonicTimeSeconds <= 0d)
        {
            return;
        }

        if (lastEventMonotonicSeconds > 0d)
        {
            double silenceMilliseconds = (monotonicTimeSeconds - lastEventMonotonicSeconds) * 1000d;
            if (silenceMilliseconds > maxSilenceMilliseconds)
            {
                maxSilenceMilliseconds = silenceMilliseconds;
            }
        }

        lastEventMonotonicSeconds = monotonicTimeSeconds;
    }
}
