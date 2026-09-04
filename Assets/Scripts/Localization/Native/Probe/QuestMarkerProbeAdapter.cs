#if MRBASE_HAS_MRUK && MRBASE_QUEST
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Meta.XR.MRUtilityKit;
using UnityEngine;

/// <summary>
/// Quest-only observation adapter for MRUK QR trackables.
///
/// MRUK 205 exposes Added/Removed publicly, but consumes its native Updated callback internally
/// and mutates the existing MRUKTrackable instance in place. LateUpdate therefore observes only
/// retained instances whose state actually changed; it does not synthesize a fixed-rate stream.
/// </summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
public sealed class QuestMarkerProbeAdapter : MonoBehaviour
{
    private const float ObserverStartupTimeoutSeconds = 15f;
    private const float ObserverRetryIntervalSeconds = 0.25f;
    private const float DualEvidenceSampleIntervalSeconds = 0.1f;

    private sealed class Observation
    {
        public MRUKTrackable Trackable;
        public TrackableSignature Signature;
    }

    private readonly struct TrackableSignature : IEquatable<TrackableSignature>
    {
        public TrackableSignature(MRUKTrackable trackable)
        {
            IsTracked = trackable.IsTracked;
            Payload = trackable.MarkerPayloadString;
            AnchorUuid = trackable.Anchor.Uuid;
            Position = trackable.transform.position;
            Rotation = trackable.transform.rotation;
        }

        private bool IsTracked { get; }
        private string Payload { get; }
        private Guid AnchorUuid { get; }
        private Vector3 Position { get; }
        private Quaternion Rotation { get; }

        public bool Equals(TrackableSignature other)
        {
            return IsTracked == other.IsTracked &&
                   string.Equals(Payload, other.Payload, StringComparison.Ordinal) &&
                   AnchorUuid == other.AnchorUuid &&
                   Position == other.Position &&
                   Rotation == other.Rotation;
        }
    }

    private readonly Dictionary<int, Observation> observations = new Dictionary<int, Observation>();
    private readonly Dictionary<int, double> lastObservationTimes = new Dictionary<int, double>();
    private readonly HashSet<string> removedReappearanceKeys = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<MRUKTrackable> existingTrackables = new List<MRUKTrackable>();
    private readonly List<int> staleInstanceIds = new List<int>();

    private MarkerProbeEntry entry;
    private IMarkerIdParser markerIdParser;
    private MRUK.MRUKSettings subscribedSettings;
    private OVRAnchor.TrackerConfiguration originalRequestedConfiguration;
    private bool requestedConfigurationModified;
    private bool observationPending;
    private bool waitingEventRecorded;
    private float observationDeadline;
    private float nextObservationAttempt;
    private float nextDualEvidenceSampleTime;
    private long capturedGeneration;

    public bool IsObserving => observationPending || subscribedSettings != null;

    public bool BeginObservation(MarkerProbeEntry owner, long generation)
    {
        EndObservation();
        entry = owner;
        capturedGeneration = generation;

        if (entry == null || !entry.IsSessionGenerationCurrent(capturedGeneration))
        {
            return false;
        }

        markerIdParser = entry.CreateMarkerIdParser();
        observationPending = true;
        observationDeadline = Time.unscaledTime + ObserverStartupTimeoutSeconds;
        nextObservationAttempt = Time.unscaledTime;

        QuestMrukRuntimeInstaller.EnsureInitialized(out string bootstrapDetail);
        return TryStartObservation(bootstrapDetail);
    }

    private bool TryStartObservation(string bootstrapDetail = null)
    {
        if (entry == null || !entry.IsSessionGenerationCurrent(capturedGeneration))
        {
            EndObservation();
            return false;
        }

        try
        {
            MRUK mruk = MRUK.Instance;
            bool mrukReady = mruk != null && mruk.SceneSettings != null;
            bool qrSupported = mrukReady && mruk.QRCodeTrackingSupported;
            bool scenePermissionGranted =
                OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene);

            if (!mrukReady || !qrSupported || !scenePermissionGranted)
            {
                if (!waitingEventRecorded)
                {
                    waitingEventRecorded = true;
                    RecordWaitingState(mrukReady, qrSupported, scenePermissionGranted, bootstrapDetail);
                }

                if (Time.unscaledTime < observationDeadline)
                {
                    observationPending = true;
                    return true;
                }

                if (mrukReady)
                {
                    bool requested = mruk.SceneSettings.TrackerConfiguration.QRCodeTrackingEnabled;
                    RecordPreflight(mruk, qrSupported, scenePermissionGranted, requested, requested);
                }

                if (!mrukReady)
                {
                    RecordAdapterError(
                        "quest_mruk_unavailable",
                        "capability",
                        "MRUK_UNAVAILABLE",
                        $"MRUK runtime was not ready within {ObserverStartupTimeoutSeconds:F0} seconds; {bootstrapDetail}");
                }
                else if (!scenePermissionGranted)
                {
                    RecordAdapterError(
                        "quest_scene_permission_denied",
                        "permission",
                        "QUEST_SCENE_PERMISSION_DENIED",
                        $"Meta Scene permission was not granted within {ObserverStartupTimeoutSeconds:F0} seconds.");
                }
                else
                {
                    RecordAdapterError(
                        "quest_qr_capability_unavailable",
                        "capability",
                        "QUEST_QR_TRACKING_UNSUPPORTED",
                        $"MRUK reported QRCodeTrackingSupported=false for {ObserverStartupTimeoutSeconds:F0} seconds.");
                }

                EndObservation();
                return false;
            }

            OVRAnchor.TrackerConfiguration requestedBefore = mruk.SceneSettings.TrackerConfiguration;
            OVRAnchor.TrackerConfiguration requestedAfter = requestedBefore;
            subscribedSettings = mruk.SceneSettings;
            originalRequestedConfiguration = requestedBefore;
            requestedAfter.QRCodeTrackingEnabled = true;
            mruk.SceneSettings.TrackerConfiguration = requestedAfter;
            requestedConfigurationModified = !requestedBefore.QRCodeTrackingEnabled;
            observationPending = false;

            RecordPreflight(
                mruk,
                qrSupported,
                scenePermissionGranted,
                requestedBefore.QRCodeTrackingEnabled,
                requestedAfter.QRCodeTrackingEnabled);

            subscribedSettings.TrackableAdded.AddListener(HandleTrackableAdded);
            subscribedSettings.TrackableRemoved.AddListener(HandleTrackableRemoved);

            mruk.GetTrackables(existingTrackables);
            for (int i = 0; i < existingTrackables.Count; i++)
            {
                MRUKTrackable trackable = existingTrackables[i];
                if (IsQrTrackable(trackable))
                {
                    Observe(trackable, "quest_trackable_existing", "mruk.GetTrackables_snapshot");
                }
            }

            existingTrackables.Clear();
            return true;
        }
        catch (Exception exception)
        {
            RecordException("quest_observer_start_exception", "adapter_start", exception);
            EndObservation();
            return false;
        }
    }

    public void EndObservation()
    {
        if (subscribedSettings != null)
        {
            subscribedSettings.TrackableAdded.RemoveListener(HandleTrackableAdded);
            subscribedSettings.TrackableRemoved.RemoveListener(HandleTrackableRemoved);
            if (requestedConfigurationModified)
            {
                try
                {
                    subscribedSettings.TrackerConfiguration = originalRequestedConfiguration;
                }
                catch (Exception exception)
                {
                    RecordException("quest_observer_restore_exception", "adapter_stop", exception);
                }
            }
        }

        subscribedSettings = null;
        observations.Clear();
        lastObservationTimes.Clear();
        removedReappearanceKeys.Clear();
        existingTrackables.Clear();
        staleInstanceIds.Clear();
        entry = null;
        markerIdParser = null;
        capturedGeneration = 0;
        requestedConfigurationModified = false;
        observationPending = false;
        waitingEventRecorded = false;
        observationDeadline = 0f;
        nextObservationAttempt = 0f;
        nextDualEvidenceSampleTime = 0f;
        originalRequestedConfiguration = default;
    }

    private void LateUpdate()
    {
        if (!IsObserving)
        {
            return;
        }

        if (entry == null || !entry.IsSessionGenerationCurrent(capturedGeneration))
        {
            EndObservation();
            return;
        }

        if (observationPending && Time.unscaledTime >= nextObservationAttempt)
        {
            nextObservationAttempt = Time.unscaledTime + ObserverRetryIntervalSeconds;
            TryStartObservation();
            if (subscribedSettings == null)
            {
                return;
            }
        }

        staleInstanceIds.Clear();
        foreach (KeyValuePair<int, Observation> pair in observations)
        {
            Observation observation = pair.Value;
            MRUKTrackable trackable = observation.Trackable;
            if (!trackable)
            {
                staleInstanceIds.Add(pair.Key);
                continue;
            }

            try
            {
                var current = new TrackableSignature(trackable);
                if (current.Equals(observation.Signature))
                {
                    continue;
                }

                observation.Signature = current;
                RecordTrackableEvent(
                    trackable,
                    "quest_trackable_updated_equivalent",
                    "mruk.object_state_changed");
            }
            catch (Exception exception)
            {
                RecordException("quest_trackable_update_exception", "mruk.object_state_changed", exception);
            }
        }

        for (int i = 0; i < staleInstanceIds.Count; i++)
        {
            int staleInstanceId = staleInstanceIds[i];
            entry?.HideDiagnosticAnchor(staleInstanceId);
            observations.Remove(staleInstanceId);
            lastObservationTimes.Remove(staleInstanceId);
        }

        RecordQuestDualFrame();
    }

    private void RecordQuestDualFrame()
    {
        if (entry == null || !entry.IsDualMarkerRun)
        {
            return;
        }

        // The evidence remains same-Unity-frame evidence, but does not need to be
        // serialized once per render frame. Trackable changes are still recorded
        // immediately by RecordTrackableEvent above.
        if (Time.unscaledTime < nextDualEvidenceSampleTime)
        {
            return;
        }

        nextDualEvidenceSampleTime = Time.unscaledTime + DualEvidenceSampleIntervalSeconds;

        MRUKTrackable markerA = null;
        MRUKTrackable markerB = null;
        int markerACount = 0;
        int markerBCount = 0;
        foreach (Observation observation in observations.Values)
        {
            MRUKTrackable trackable = observation.Trackable;
            if (!IsQrTrackable(trackable))
            {
                continue;
            }

            MarkerIdParseResult parsed = markerIdParser?.Parse(trackable.MarkerPayloadString);
            if (parsed?.success != true)
            {
                continue;
            }

            if (parsed.markerId == "0")
            {
                markerACount++;
                markerA ??= trackable;
            }
            else if (parsed.markerId == "250")
            {
                markerBCount++;
                markerB ??= trackable;
            }
        }

        string rejectionReason = ResolveQuestDualRejection(
            markerA,
            markerB,
            markerACount,
            markerBCount);
        bool accepted = rejectionReason == null;
        MarkerProbePoseSnapshot markerAPose = CreateQuestWorldPose(markerA);
        MarkerProbePoseSnapshot markerBPose = CreateQuestWorldPose(markerB);
        MarkerProbePoseSnapshot relativePose = null;
        if (accepted)
        {
            var a = new Pose(markerA.transform.position, markerA.transform.rotation);
            var b = new Pose(markerB.transform.position, markerB.transform.rotation);
            Quaternion inverseA = Quaternion.Inverse(a.rotation);
            relativePose = MarkerProbePoseSerialization.FromUnityPose(
                new Pose(
                    inverseA * (b.position - a.position),
                    inverseA * b.rotation),
                "Marker A local relative transform (from same Quest Unity frame)",
                "A(ID 0) to B(ID 250)");
        }

        entry.TryRecordPlatformEvent(capturedGeneration, new MarkerProbeLogEvent
        {
            eventType = accepted
                ? "quest_dual_marker_same_frame_sample"
                : "quest_dual_marker_frame_rejected",
            nativeEventKind = "probe_same_unity_frame",
            observationSemantics = accepted
                ? "Both MRUK QR Trackables were held and valid in one Unity frame; no cross-time pairing."
                : "Frame rejected for A-to-B analysis; no Trackable pose from another frame was substituted.",
            expectedMarkerIds = new[] { "0", "250" },
            dualMarker = new MarkerProbeDualMarkerEvidence
            {
                accepted = accepted,
                rejectionReason = rejectionReason,
                markerAId = "0",
                markerBId = "250",
                sourceSnapshotSequence = 0,
                sourceUnityFrame = Time.frameCount,
                coordinateSpace = "Unity World",
                markerAPose = markerAPose,
                markerBPose = markerBPose,
                markerAToB = relativePose
            }
        });
    }

    private static string ResolveQuestDualRejection(
        MRUKTrackable markerA,
        MRUKTrackable markerB,
        int markerACount,
        int markerBCount)
    {
        if (markerACount == 0) return "missing_marker_a_id_0_in_current_frame";
        if (markerBCount == 0) return "missing_marker_b_id_250_in_current_frame";
        if (markerACount > 1) return "duplicate_marker_a_id_0_in_current_frame";
        if (markerBCount > 1) return "duplicate_marker_b_id_250_in_current_frame";
        if (!markerA.IsTracked) return "marker_a_not_tracked_in_current_frame";
        if (!markerB.IsTracked) return "marker_b_not_tracked_in_current_frame";
        if (!MarkerProbePoseSerialization.ValidateUnityPose(
                new Pose(markerA.transform.position, markerA.transform.rotation)).valid)
            return "marker_a_pose_invalid_in_current_frame";
        if (!MarkerProbePoseSerialization.ValidateUnityPose(
                new Pose(markerB.transform.position, markerB.transform.rotation)).valid)
            return "marker_b_pose_invalid_in_current_frame";
        return null;
    }

    private static MarkerProbePoseSnapshot CreateQuestWorldPose(MRUKTrackable trackable)
    {
        if (!trackable)
        {
            return null;
        }

        var pose = new Pose(trackable.transform.position, trackable.transform.rotation);
        return MarkerProbePoseSerialization.ValidateUnityPose(pose).valid
            ? MarkerProbePoseSerialization.FromUnityPose(
                pose,
                "Unity World",
                "MRUK QR Trackable transform (QR reference point)")
            : null;
    }

    private void HandleTrackableAdded(MRUKTrackable trackable)
    {
        try
        {
            if (IsQrTrackable(trackable))
            {
                string eventType = WasPreviouslyRemoved(trackable)
                    ? "quest_trackable_reappeared"
                    : "quest_trackable_added";
                Observe(trackable, eventType, "mruk.TrackableAdded");
            }
        }
        catch (Exception exception)
        {
            RecordException("quest_trackable_added_exception", "mruk.TrackableAdded", exception);
        }
    }

    private void HandleTrackableRemoved(MRUKTrackable trackable)
    {
        try
        {
            if (!IsQrTrackable(trackable))
            {
                return;
            }

            AddRemovedReappearanceKeys(trackable);
            RecordTrackableEvent(trackable, "quest_trackable_removed", "mruk.TrackableRemoved");
            int instanceId = trackable.GetInstanceID();
            entry?.HideDiagnosticAnchor(instanceId);
            observations.Remove(instanceId);
            lastObservationTimes.Remove(instanceId);
        }
        catch (Exception exception)
        {
            RecordException("quest_trackable_removed_exception", "mruk.TrackableRemoved", exception);
        }
    }

    private void Observe(MRUKTrackable trackable, string eventType, string nativeEventKind)
    {
        int instanceId = trackable.GetInstanceID();
        observations[instanceId] = new Observation
        {
            Trackable = trackable,
            Signature = new TrackableSignature(trackable)
        };
        RecordTrackableEvent(trackable, eventType, nativeEventKind);
    }

    private void RecordTrackableEvent(MRUKTrackable trackable, string eventType, string nativeEventKind)
    {
        if (entry == null || !trackable)
        {
            return;
        }


        int instanceId = trackable.GetInstanceID();
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        bool hasPreviousObservation = lastObservationTimes.TryGetValue(instanceId, out double previousTime);
        lastObservationTimes[instanceId] = now;
        MarkerIdParseResult parseResult = markerIdParser?.Parse(trackable.MarkerPayloadString);
        var worldPose = new Pose(trackable.transform.position, trackable.transform.rotation);
        MarkerProbePoseValidationResult poseValidation =
            MarkerProbePoseSerialization.ValidateUnityPose(worldPose);

        var logEvent = new MarkerProbeLogEvent
        {
            eventType = eventType,
            nativeEventKind = nativeEventKind,
            observationSemantics = ResolveObservationSemantics(eventType),
            monotonicTimeSeconds = now,
            nativeTimestampAvailable = false,
            callbackIntervalAvailable = hasPreviousObservation,
            callbackIntervalMilliseconds = hasPreviousObservation
                ? (now - previousTime) * 1000d
                : 0d,
            trackableIdentityAvailable = true,
            trackableInstanceId = instanceId,
            trackableAnchorUuid = trackable.Anchor.Uuid.ToString("D"),
            trackableObjectName = trackable.gameObject.name,
            questIsTrackedAvailable = true,
            questIsTracked = trackable.IsTracked,
            markerIdParseAttempted = parseResult != null,
            markerIdParseSuccess = parseResult?.success == true,
            markerIdParseFailure = parseResult?.failure.ToString(),
            markerIdParseFailureDetail = parseResult?.failureDetail,
            markerId = parseResult?.markerId,
            rawPayload = parseResult?.rawPayload,
            pose = new MarkerProbePoseEvidence
            {
                unityPose = poseValidation.valid
                    ? MarkerProbePoseSerialization.FromUnityPose(
                        worldPose,
                        "Unity World",
                        "MRUK QR Trackable transform (QR reference point)")
                    : null,
                validation = poseValidation
            },
            candidateOffset = new MarkerProbeOffsetEvidence
            {
                status = "not_applicable",
                description =
                    "Quest MRUK supplies the QR reference point in Unity World coordinates; " +
                    "no PICO marker-to-QR candidate offset was applied."
            },
            error = CreateObservationError(parseResult, poseValidation)
        };
        entry.ClassifyQuestObservation(logEvent, trackable.IsTracked, poseValidation.valid);
        entry.TryRecordPlatformEvent(capturedGeneration, logEvent);

        if (trackable.IsTracked &&
            poseValidation.valid &&
            !string.IsNullOrEmpty(trackable.MarkerPayloadString))
        {
            entry.ShowOrUpdateDiagnosticAnchor(
                instanceId,
                trackable.MarkerPayloadString,
                parseResult?.markerId,
                worldPose,
                trackable.PlaneRect);
        }
        else
        {
            entry.HideDiagnosticAnchor(instanceId);
        }
    }

    private void RecordPreflight(
        MRUK mruk,
        bool qrSupported,
        bool scenePermissionGranted,
        bool requestedBefore,
        bool requestedAfter)
    {
        bool active = mruk.TrackerConfiguration.QRCodeTrackingEnabled;
        entry?.TryRecordPlatformEvent(capturedGeneration, new MarkerProbeLogEvent
        {
            eventType = "quest_qr_preflight",
            nativeEventKind = "adapter_preflight",
            questPreflight = new MarkerProbeQuestPreflightSnapshot
            {
                mrukInstanceAvailable = true,
                qrCodeTrackingSupported = qrSupported,
                scenePermissionGranted = scenePermissionGranted,
                qrCodeTrackingRequestedBefore = requestedBefore,
                qrCodeTrackingRequestedAfter = requestedAfter,
                qrCodeTrackingActiveAtPreflight = active,
                detail = active
                    ? "MRUK QR tracker was active at preflight."
                    : "Requested configuration may take effect asynchronously; this is not recorded as an SDK failure."
            },
            sdkResult = new MarkerProbeSdkResult
            {
                available = true,
                operation = "MRUK QR tracker preflight",
                success = qrSupported && scenePermissionGranted,
                detail = $"supported={qrSupported}; permission={scenePermissionGranted}; " +
                         $"requestedBefore={requestedBefore}; requestedAfter={requestedAfter}; active={active}"
            }
        });
    }

    private void RecordWaitingState(
        bool mrukReady,
        bool qrSupported,
        bool scenePermissionGranted,
        string bootstrapDetail)
    {
        entry?.TryRecordPlatformEvent(capturedGeneration, new MarkerProbeLogEvent
        {
            eventType = "quest_qr_observer_waiting",
            nativeEventKind = "adapter_preflight_wait",
            observationSemantics =
                "Transient startup state; the adapter retries until the bounded startup deadline.",
            questPreflight = new MarkerProbeQuestPreflightSnapshot
            {
                mrukInstanceAvailable = mrukReady,
                qrCodeTrackingSupported = qrSupported,
                scenePermissionGranted = scenePermissionGranted,
                detail = $"timeoutSeconds={ObserverStartupTimeoutSeconds:F0}; bootstrap={bootstrapDetail}"
            },
            sdkResult = new MarkerProbeSdkResult
            {
                available = mrukReady,
                operation = "Wait for Quest MRUK QR observer prerequisites",
                success = false,
                detail = $"mrukReady={mrukReady}; supported={qrSupported}; " +
                         $"permission={scenePermissionGranted}; bootstrap={bootstrapDetail}"
            }
        });
    }

    private void RecordAdapterError(string eventType, string category, string errorCode, string message)
    {
        entry?.TryRecordPlatformEvent(capturedGeneration, new MarkerProbeLogEvent
        {
            eventType = eventType,
            nativeEventKind = "adapter_preflight",
            error = new MarkerProbeErrorContext
            {
                category = category,
                errorCode = errorCode,
                message = message
            }
        });
    }

    private void RecordException(string eventType, string nativeEventKind, Exception exception)
    {
        entry?.TryRecordPlatformEvent(capturedGeneration, new MarkerProbeLogEvent
        {
            eventType = eventType,
            nativeEventKind = nativeEventKind,
            error = new MarkerProbeErrorContext
            {
                category = "sdk_exception",
                errorCode = "QUEST_MRUK_EXCEPTION",
                exceptionType = exception.GetType().FullName,
                message = exception.Message,
                stackTrace = exception.StackTrace
            }
        });
    }

    private static MarkerProbeErrorContext CreateObservationError(
        MarkerIdParseResult parseResult,
        MarkerProbePoseValidationResult poseValidation)
    {
        if (parseResult != null && !parseResult.success)
        {
            return new MarkerProbeErrorContext
            {
                category = "payload",
                errorCode = "QUEST_PAYLOAD_" + parseResult.failure.ToString().ToUpperInvariant(),
                message = parseResult.failureDetail
            };
        }

        if (poseValidation != null && !poseValidation.valid)
        {
            return new MarkerProbeErrorContext
            {
                category = "pose",
                errorCode = "QUEST_INVALID_POSE",
                message = poseValidation.detail
            };
        }

        return null;
    }

    private bool WasPreviouslyRemoved(MRUKTrackable trackable)
    {
        foreach (string key in GetReappearanceKeys(trackable))
        {
            if (removedReappearanceKeys.Contains(key))
            {
                return true;
            }
        }

        return false;
    }

    private void AddRemovedReappearanceKeys(MRUKTrackable trackable)
    {
        foreach (string key in GetReappearanceKeys(trackable))
        {
            removedReappearanceKeys.Add(key);
        }
    }

    private IEnumerable<string> GetReappearanceKeys(MRUKTrackable trackable)
    {
        Guid uuid = trackable.Anchor.Uuid;
        if (uuid != Guid.Empty)
        {
            yield return "uuid:" + uuid.ToString("D");
        }

        MarkerIdParseResult parseResult = markerIdParser?.Parse(trackable.MarkerPayloadString);
        if (!string.IsNullOrEmpty(parseResult?.rawPayload?.sha256))
        {
            yield return "payload-sha256:" + parseResult.rawPayload.sha256;
        }
    }

    private static string ResolveObservationSemantics(string eventType)
    {
        return eventType switch
        {
            "quest_trackable_removed" =>
                "MRUK removal observation only; do not infer production MarkerLost semantics.",
            "quest_trackable_reappeared" =>
                "MRUK Added after a matching removal observation; do not infer production recovery semantics.",
            "quest_trackable_updated_equivalent" =>
                "MRUK updated the retained object in place; detected by state change, not a public Updated callback.",
            "quest_trackable_existing" =>
                "Current MRUK collection snapshot taken when the probe subscribed.",
            _ => "MRUK observation fact only; no production marker lifecycle semantics were inferred."
        };
    }

    private static bool IsQrTrackable(MRUKTrackable trackable)
    {
        return trackable && trackable.TrackableType == OVRAnchor.TrackableType.QRCode;
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
