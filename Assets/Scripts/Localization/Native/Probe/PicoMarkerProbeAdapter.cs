#if MRBASE_HAS_PICO_SDK
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Unity.XR.PICO.TOBSupport;
using UnityEngine;
using UnityEngine.XR;
using Unity.XR.CoreUtils;

using Pose = UnityEngine.Pose;

/// <summary>
/// Isolated PICO Enterprise adapter for Marker Probe.
/// It intentionally never calls UnBindEnterpriseService: the process-wide service is not owned by
/// this probe, and late callbacks are rejected by the captured session generation instead.
/// </summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
public sealed class PicoMarkerProbeAdapter : MonoBehaviour
{
    private readonly struct PendingBindResult
    {
        public PendingBindResult(long generation, bool bound)
        {
            Generation = generation;
            Bound = bound;
            UtcTimestamp = DateTime.UtcNow.ToString("O");
            MonotonicTimeSeconds = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            ThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public long Generation { get; }
        public bool Bound { get; }
        public string UtcTimestamp { get; }
        public double MonotonicTimeSeconds { get; }
        public int ThreadId { get; }
    }

    private readonly struct PendingMarkerSample
    {
        public PendingMarkerSample(MarkerInfo marker)
        {
            WasNull = marker == null;
            MarkerId = marker?.iMarkerId ?? 0;
            ValidFlag = marker?.validFlag ?? 0;
            MarkerType = marker?.markerType ?? 0;
            NativeTimestamp = marker?.dTimestamp ?? 0d;
            PositionX = marker?.posX ?? 0d;
            PositionY = marker?.posY ?? 0d;
            PositionZ = marker?.posZ ?? 0d;
            RotationX = marker?.rotationX ?? 0d;
            RotationY = marker?.rotationY ?? 0d;
            RotationZ = marker?.rotationZ ?? 0d;
            RotationW = marker?.rotationW ?? 0d;
        }

        public bool WasNull { get; }
        public int MarkerId { get; }
        public int ValidFlag { get; }
        public int MarkerType { get; }
        public double NativeTimestamp { get; }
        public double PositionX { get; }
        public double PositionY { get; }
        public double PositionZ { get; }
        public double RotationX { get; }
        public double RotationY { get; }
        public double RotationZ { get; }
        public double RotationW { get; }
    }

    private sealed class PendingMarkerSnapshot
    {
        public long SessionGeneration;
        public long SnapshotSequence;
        public bool WasNull;
        public PendingMarkerSample[] Samples;
        public string UtcTimestamp;
        public double MonotonicTimeSeconds;
        public int ThreadId;
        public bool CallbackIntervalAvailable;
        public double CallbackIntervalMilliseconds;
    }

    private readonly ConcurrentQueue<PendingBindResult> pendingBindResults =
        new ConcurrentQueue<PendingBindResult>();
    private readonly ConcurrentQueue<PendingMarkerSnapshot> pendingMarkerSnapshots =
        new ConcurrentQueue<PendingMarkerSnapshot>();
    private readonly object markerTimingGate = new object();

    private MarkerProbeEntry entry;
    private long activeGeneration;
    private bool enterpriseBound;
    private bool markerCallbackRegistered;
    private long nextMarkerSnapshotSequence;
    private double lastMarkerCallbackMonotonicSeconds;
    private XROrigin xrOrigin;

    public bool BeginObservation(MarkerProbeEntry owner, long generation)
    {
        entry = owner;
        activeGeneration = generation;
        enterpriseBound = false;
        markerCallbackRegistered = false;
        if (entry == null || !entry.IsSessionGenerationCurrent(generation))
        {
            return false;
        }

        try
        {
            bool initialized = PXR_Enterprise.InitEnterpriseService(false);
            entry.RecordPicoEnterpriseInitResult(initialized);
            if (!initialized)
            {
                return false;
            }

            entry.RecordPicoEnterpriseBindRequested();
            PXR_Enterprise.BindEnterpriseService(
                bound => pendingBindResults.Enqueue(new PendingBindResult(generation, bound)));
            return true;
        }
        catch (Exception exception)
        {
            RecordException(generation, "pico_enterprise_start_exception", "enterprise_start", exception);
            return false;
        }
    }

    public void EndObservation()
    {
        // Deliberately no UnBindEnterpriseService call. See class contract.
        activeGeneration = 0;
        enterpriseBound = false;
        markerCallbackRegistered = false;
    }

    private void Update()
    {
        while (pendingBindResults.TryDequeue(out PendingBindResult pending))
        {
            ProcessBindResult(pending);
        }

        while (pendingMarkerSnapshots.TryDequeue(out PendingMarkerSnapshot pending))
        {
            ProcessMarkerSnapshot(pending);
        }

    }

    private void ProcessBindResult(PendingBindResult pending)
    {
        if (entry == null)
        {
            return;
        }

        bool accepted = entry.RecordPicoEnterpriseBindResult(
            pending.Generation,
            pending.Bound,
            pending.UtcTimestamp,
            pending.MonotonicTimeSeconds,
            pending.ThreadId);
        if (!accepted || !pending.Bound || !entry.IsSessionGenerationCurrent(pending.Generation))
        {
            return;
        }

        enterpriseBound = true;

        try
        {
            MarkerProbePicoRegistrationSnapshot registration =
                entry.CurrentSession?.picoMarkerRegistration;
            if (registration == null)
            {
                return;
            }

            var trackingMode = (TrackingOriginModeFlags)registration.trackingMode;
            int result = PXR_Enterprise.SetMarkerInfoCallback(
                trackingMode,
                registration.cameraYOffset,
                markers => HandleMarkerInfos(pending.Generation, markers));
            entry.RecordPicoMarkerRegistrationResult(result);
            markerCallbackRegistered = result == 0;
        }
        catch (Exception exception)
        {
            RecordException(
                pending.Generation,
                "pico_marker_registration_exception",
                "PXR_Enterprise.SetMarkerInfoCallback",
                exception);
        }
    }

    private void HandleMarkerInfos(long generation, List<MarkerInfo> markers)
    {
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        bool intervalAvailable;
        double intervalMilliseconds;
        lock (markerTimingGate)
        {
            intervalAvailable = lastMarkerCallbackMonotonicSeconds > 0d;
            intervalMilliseconds = intervalAvailable
                ? (now - lastMarkerCallbackMonotonicSeconds) * 1000d
                : 0d;
            lastMarkerCallbackMonotonicSeconds = now;
        }

        PendingMarkerSample[] samples = null;
        if (markers != null)
        {
            samples = new PendingMarkerSample[markers.Count];
            for (int i = 0; i < markers.Count; i++)
            {
                samples[i] = new PendingMarkerSample(markers[i]);
            }
        }

        pendingMarkerSnapshots.Enqueue(new PendingMarkerSnapshot
        {
            SessionGeneration = generation,
            SnapshotSequence = Interlocked.Increment(ref nextMarkerSnapshotSequence),
            WasNull = markers == null,
            Samples = samples,
            UtcTimestamp = DateTime.UtcNow.ToString("O"),
            MonotonicTimeSeconds = now,
            ThreadId = Thread.CurrentThread.ManagedThreadId,
            CallbackIntervalAvailable = intervalAvailable,
            CallbackIntervalMilliseconds = intervalMilliseconds
        });
    }

    private void ProcessMarkerSnapshot(PendingMarkerSnapshot snapshot)
    {
        if (entry == null)
        {
            return;
        }

        if (!entry.IsSessionGenerationCurrent(snapshot.SessionGeneration) ||
            snapshot.SessionGeneration != activeGeneration)
        {
            entry.TryRecordPlatformEvent(snapshot.SessionGeneration, new MarkerProbeLogEvent
            {
                eventType = "pico_marker_late_callback",
                nativeEventKind = "PXR_Enterprise.SetMarkerInfoCallback.callback",
                observationSemantics =
                    "Captured under an earlier session generation; cannot enter a current run.",
                utcTimestamp = snapshot.UtcTimestamp,
                monotonicTimeSeconds = snapshot.MonotonicTimeSeconds,
                threadId = snapshot.ThreadId,
                callbackIntervalAvailable = snapshot.CallbackIntervalAvailable,
                callbackIntervalMilliseconds = snapshot.CallbackIntervalMilliseconds,
                markerSnapshotSequence = snapshot.SnapshotSequence,
                markerSnapshotWasNull = snapshot.WasNull,
                markerSnapshotEntryCount = snapshot.Samples?.Length ?? 0
            });
            return;
        }

        int entryCount = snapshot.Samples?.Length ?? 0;
        if (entryCount == 0)
        {
            entry.TryRecordPlatformEvent(snapshot.SessionGeneration, new MarkerProbeLogEvent
            {
                eventType = "pico_marker_snapshot_empty",
                nativeEventKind = "PXR_Enterprise.SetMarkerInfoCallback.callback",
                observationSemantics =
                    "Full Marker callback snapshot contained no entries; no production Lost semantic is inferred.",
                utcTimestamp = snapshot.UtcTimestamp,
                monotonicTimeSeconds = snapshot.MonotonicTimeSeconds,
                threadId = snapshot.ThreadId,
                callbackIntervalAvailable = snapshot.CallbackIntervalAvailable,
                callbackIntervalMilliseconds = snapshot.CallbackIntervalMilliseconds,
                markerSnapshotSequence = snapshot.SnapshotSequence,
                markerSnapshotWasNull = snapshot.WasNull,
                markerSnapshotEntryCount = 0
            });
            return;
        }

        var totals = new Dictionary<int, int>();
        for (int i = 0; i < entryCount; i++)
        {
            int markerId = snapshot.Samples[i].MarkerId;
            totals.TryGetValue(markerId, out int total);
            totals[markerId] = total + 1;
        }

        var occurrences = new Dictionary<int, int>();
        var sampleEvents = new MarkerProbeLogEvent[entryCount];
        for (int i = 0; i < entryCount; i++)
        {
            PendingMarkerSample sample = snapshot.Samples[i];
            occurrences.TryGetValue(sample.MarkerId, out int occurrence);
            occurrence++;
            occurrences[sample.MarkerId] = occurrence;
            MarkerProbeLogEvent sampleEvent = CreateMarkerSampleEvent(
                snapshot,
                sample,
                i,
                entryCount,
                occurrence,
                totals[sample.MarkerId] > 1);
            sampleEvents[i] = sampleEvent;
            entry.RecordPicoMarkerSample(snapshot.SessionGeneration, sampleEvent);
        }

        if (entry.IsDualPairingComplete)
        {
            RecordPicoDualSnapshot(snapshot, sampleEvents);
        }
    }

    private void RecordPicoDualSnapshot(
        PendingMarkerSnapshot snapshot,
        MarkerProbeLogEvent[] sampleEvents)
    {
        MarkerProbeLogEvent markerA = null;
        MarkerProbeLogEvent markerB = null;
        int markerACount = 0;
        int markerBCount = 0;
        foreach (MarkerProbeLogEvent sampleEvent in sampleEvents)
        {
            if (sampleEvent.markerId == "0")
            {
                markerACount++;
                markerA ??= sampleEvent;
            }
            else if (sampleEvent.markerId == "250")
            {
                markerBCount++;
                markerB ??= sampleEvent;
            }
        }

        string rejectionReason = ResolveDualSnapshotRejection(
            markerA,
            markerB,
            markerACount,
            markerBCount);
        bool accepted = rejectionReason == null;
        MarkerProbePoseSnapshot relativePose = null;
        if (accepted)
        {
            Pose a = ToUnityPose(markerA.pose.unityTrackingOriginPose);
            Pose b = ToUnityPose(markerB.pose.unityTrackingOriginPose);
            Quaternion inverseA = Quaternion.Inverse(a.rotation);
            var aToB = new Pose(
                inverseA * (b.position - a.position),
                inverseA * b.rotation);
            relativePose = MarkerProbePoseSerialization.FromUnityPose(
                aToB,
                "Marker A local relative transform (from same PICO callback snapshot)",
                "A(ID 0) to B(ID 250)");
        }

        entry.TryRecordPlatformEvent(snapshot.SessionGeneration, new MarkerProbeLogEvent
        {
            eventType = accepted
                ? "pico_dual_marker_same_snapshot_sample"
                : "pico_dual_marker_snapshot_rejected",
            nativeEventKind = "probe_same_callback_snapshot",
            observationSemantics = accepted
                ? "A and B are valid entries from one PICO Marker callback snapshot; no cross-time pairing."
                : "Snapshot rejected for A-to-B analysis; no cached pose from another callback was substituted.",
            utcTimestamp = snapshot.UtcTimestamp,
            monotonicTimeSeconds = snapshot.MonotonicTimeSeconds,
            threadId = snapshot.ThreadId,
            callbackIntervalAvailable = snapshot.CallbackIntervalAvailable,
            callbackIntervalMilliseconds = snapshot.CallbackIntervalMilliseconds,
            markerSnapshotSequence = snapshot.SnapshotSequence,
            markerSnapshotEntryCount = sampleEvents.Length,
            xrOrigin = markerA?.xrOrigin ?? markerB?.xrOrigin,
            dualMarker = new MarkerProbeDualMarkerEvidence
            {
                accepted = accepted,
                rejectionReason = rejectionReason,
                markerAId = "0",
                markerBId = "250",
                sourceSnapshotSequence = snapshot.SnapshotSequence,
                sourceUnityFrame = Time.frameCount,
                coordinateSpace = "Unity tracking-origin local space",
                markerAPose = markerA?.pose?.unityTrackingOriginPose,
                markerBPose = markerB?.pose?.unityTrackingOriginPose,
                markerAToB = relativePose
            }
        });
    }

    private static string ResolveDualSnapshotRejection(
        MarkerProbeLogEvent markerA,
        MarkerProbeLogEvent markerB,
        int markerACount,
        int markerBCount)
    {
        if (markerACount == 0) return "missing_marker_a_id_0";
        if (markerBCount == 0) return "missing_marker_b_id_250";
        if (markerACount > 1) return "duplicate_marker_a_id_0_in_same_snapshot";
        if (markerBCount > 1) return "duplicate_marker_b_id_250_in_same_snapshot";
        if (markerA.validFlag == 0 || markerA.pose?.validation?.valid != true)
            return "marker_a_invalid_in_same_snapshot";
        if (markerB.validFlag == 0 || markerB.pose?.validation?.valid != true)
            return "marker_b_invalid_in_same_snapshot";
        if (markerA.pose.unityTrackingOriginPose == null)
            return "marker_a_tracking_origin_pose_unavailable";
        if (markerB.pose.unityTrackingOriginPose == null)
            return "marker_b_tracking_origin_pose_unavailable";
        return null;
    }

    private static Pose ToUnityPose(MarkerProbePoseSnapshot snapshot)
    {
        return new Pose(
            new Vector3(
                (float)snapshot.position.x,
                (float)snapshot.position.y,
                (float)snapshot.position.z),
            new Quaternion(
                (float)snapshot.rotation.x,
                (float)snapshot.rotation.y,
                (float)snapshot.rotation.z,
                (float)snapshot.rotation.w));
    }

    private MarkerProbeLogEvent CreateMarkerSampleEvent(
        PendingMarkerSnapshot snapshot,
        PendingMarkerSample sample,
        int entryIndex,
        int entryCount,
        int occurrence,
        bool duplicateMarkerId)
    {
        var unityPose = new Pose(
            new Vector3((float)sample.PositionX, (float)sample.PositionY, (float)sample.PositionZ),
            new Quaternion(
                (float)sample.RotationX,
                (float)sample.RotationY,
                (float)sample.RotationZ,
                (float)sample.RotationW));
        MarkerProbePoseValidationResult validation = sample.WasNull
            ? new MarkerProbePoseValidationResult
            {
                valid = false,
                detail = "MarkerInfo entry was null."
            }
            : MarkerProbePoseSerialization.ValidateUnityPose(unityPose);
        bool rawPoseFinite = IsFinite(sample.PositionX) &&
                             IsFinite(sample.PositionY) &&
                             IsFinite(sample.PositionZ) &&
                             IsFinite(sample.RotationX) &&
                             IsFinite(sample.RotationY) &&
                             IsFinite(sample.RotationZ) &&
                             IsFinite(sample.RotationW);
        if (!rawPoseFinite)
        {
            validation.valid = false;
            validation.detail = "PICO MarkerInfo pose contains NaN or Infinity; raw values are retained as strings.";
        }

        MarkerProbePoseSnapshot trackingOriginPose = validation.valid
            ? MarkerProbePoseSerialization.FromUnityPose(
                unityPose,
                "Unity tracking-origin local space",
                "ArUco marker reference point")
            : null;
        MarkerProbeXrOriginSnapshot xrOriginSnapshot = CaptureXrOrigin(
            unityPose,
            validation.valid,
            out Pose worldPose,
            out bool worldPoseAvailable);
        MarkerProbePoseSnapshot worldPoseSnapshot = worldPoseAvailable
            ? MarkerProbePoseSerialization.FromUnityPose(
                worldPose,
                "Unity World",
                "ArUco marker reference point after XR Origin transform")
            : null;

        var logEvent = new MarkerProbeLogEvent
        {
            nativeEventKind = "PXR_Enterprise.SetMarkerInfoCallback.callback",
            observationSemantics =
                "One entry from a full Marker snapshot; absence from another snapshot is not converted to Lost.",
            utcTimestamp = snapshot.UtcTimestamp,
            monotonicTimeSeconds = snapshot.MonotonicTimeSeconds,
            threadId = snapshot.ThreadId,
            callbackIntervalAvailable = snapshot.CallbackIntervalAvailable,
            callbackIntervalMilliseconds = snapshot.CallbackIntervalMilliseconds,
            nativeTimestampAvailable = !sample.WasNull && IsFinite(sample.NativeTimestamp),
            nativeTimestamp = IsFinite(sample.NativeTimestamp) ? sample.NativeTimestamp : 0d,
            nativeTimestampUnit = "PICO MarkerInfo.dTimestamp (SDK unit undocumented)",
            markerSnapshotSequence = snapshot.SnapshotSequence,
            markerSnapshotWasNull = snapshot.WasNull,
            markerSnapshotEntryIndex = entryIndex,
            markerSnapshotEntryCount = entryCount,
            markerIdOccurrenceInSnapshot = occurrence,
            duplicateMarkerIdInSnapshot = duplicateMarkerId,
            markerId = sample.MarkerId.ToString(CultureInfo.InvariantCulture),
            validFlagAvailable = !sample.WasNull,
            validFlag = sample.ValidFlag,
            markerTypeAvailable = !sample.WasNull,
            markerType = sample.MarkerType,
            picoRawPose = new MarkerProbePicoRawPoseSnapshot
            {
                positionX = ToRoundTripString(sample.PositionX),
                positionY = ToRoundTripString(sample.PositionY),
                positionZ = ToRoundTripString(sample.PositionZ),
                rotationX = ToRoundTripString(sample.RotationX),
                rotationY = ToRoundTripString(sample.RotationY),
                rotationZ = ToRoundTripString(sample.RotationZ),
                rotationW = ToRoundTripString(sample.RotationW)
            },
            pose = new MarkerProbePoseEvidence
            {
                nativePose = rawPoseFinite && !sample.WasNull
                    ? MarkerProbePoseSerialization.FromDoubleComponents(
                        sample.PositionX,
                        sample.PositionY,
                        sample.PositionZ,
                        sample.RotationX,
                        sample.RotationY,
                        sample.RotationZ,
                        sample.RotationW,
                        "PICO MarkerInfo callback coordinates after SDK handedness/origin-height processing",
                        "ArUco marker reference point")
                    : null,
                unityTrackingOriginPose = trackingOriginPose,
                unityPose = worldPoseSnapshot,
                validation = validation
            },
            xrOrigin = xrOriginSnapshot,
            candidateOffset = new MarkerProbeOffsetEvidence
            {
                status = "not_applied_axis_mapping_unconfirmed",
                description =
                    "No 210 mm ArUco-to-QR candidate offset was applied. PICO marker local axis/sign " +
                    "must be established from device evidence before defining that vector.",
                inputPose = worldPoseSnapshot
            }
        };
        if (sample.WasNull || !validation.valid)
        {
            logEvent.error = new MarkerProbeErrorContext
            {
                category = "pose",
                errorCode = sample.WasNull ? "PICO_NULL_MARKER_INFO" : "PICO_INVALID_MARKER_POSE",
                message = validation.detail
            };
        }
        else if (!worldPoseAvailable)
        {
            logEvent.error = new MarkerProbeErrorContext
            {
                category = "coordinate_space",
                errorCode = "PICO_XR_ORIGIN_UNAVAILABLE",
                message = xrOriginSnapshot.detail
            };
        }

        return logEvent;
    }

    private MarkerProbeXrOriginSnapshot CaptureXrOrigin(
        Pose trackingOriginPose,
        bool poseValid,
        out Pose worldPose,
        out bool worldPoseAvailable)
    {
        worldPose = default;
        worldPoseAvailable = false;
        if (!xrOrigin)
        {
            xrOrigin = FindFirstObjectByType<XROrigin>();
        }

        if (!xrOrigin || xrOrigin.Origin == null)
        {
            return new MarkerProbeXrOriginSnapshot
            {
                available = false,
                source = "Unity.XR.CoreUtils.XROrigin",
                detail = "No active XROrigin with an Origin GameObject was found.",
                trackingOriginMode = null
            };
        }

        Transform originTransform = xrOrigin.Origin.transform;
        Camera xrCamera = xrOrigin.Camera;
        var snapshot = new MarkerProbeXrOriginSnapshot
        {
            available = true,
            source = $"XROrigin:{xrOrigin.name}/Origin:{xrOrigin.Origin.name}",
            detail =
                "World pose = Origin.TransformPoint(markerPosition), Origin.rotation * markerRotation; " +
                "conversionVersion=pico-xr-origin-v1.",
            trackingOriginMode = xrOrigin.CurrentTrackingOriginMode.ToString(),
            originWorldPose = MarkerProbePoseSerialization.FromUnityPose(
                new Pose(originTransform.position, originTransform.rotation),
                "Unity World",
                "XR Origin"),
            originLossyScale = MarkerProbePoseSerialization.FromUnityVector(originTransform.lossyScale)
        };

        if (xrCamera != null)
        {
            Transform cameraTransform = xrCamera.transform;
            var cameraRelativePose = new Pose(
                originTransform.InverseTransformPoint(cameraTransform.position),
                Quaternion.Inverse(originTransform.rotation) * cameraTransform.rotation);
            snapshot.cameraLocalPose = MarkerProbePoseSerialization.FromUnityPose(
                cameraRelativePose,
                "XR Origin local space",
                "XR camera");
        }

        if (poseValid)
        {
            worldPose = new Pose(
                originTransform.TransformPoint(trackingOriginPose.position),
                originTransform.rotation * trackingOriginPose.rotation);
            worldPoseAvailable = MarkerProbePoseSerialization.ValidateUnityPose(worldPose).valid;
            if (!worldPoseAvailable)
            {
                snapshot.detail += " Derived World Pose failed finite/quaternion validation.";
            }
        }

        return snapshot;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static string ToRoundTripString(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private void RecordException(
        long generation,
        string eventType,
        string nativeEventKind,
        Exception exception)
    {
        entry?.TryRecordPlatformEvent(generation, new MarkerProbeLogEvent
        {
            eventType = eventType,
            nativeEventKind = nativeEventKind,
            error = new MarkerProbeErrorContext
            {
                category = "sdk_exception",
                errorCode = "PICO_ENTERPRISE_EXCEPTION",
                exceptionType = exception.GetType().FullName,
                message = exception.Message,
                stackTrace = exception.StackTrace
            }
        });
    }

    private void OnDisable()
    {
        EndObservation();
    }

    private void OnDestroy()
    {
        EndObservation();
    }
}
#endif
