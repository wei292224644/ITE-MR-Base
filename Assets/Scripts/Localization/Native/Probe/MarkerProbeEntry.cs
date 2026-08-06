using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.XR;

using Debug = UnityEngine.Debug;

/// <summary>
/// Explicit, development-build-only entry point for the isolated marker probe.
///
/// This component deliberately does not create an <see cref="IMarkerTrackingProvider"/>,
/// call <see cref="MarkerAnchorService"/>, or alter the production marker lifecycle.
/// Platform observation is attached behind this boundary by the diagnostic probe only.
/// </summary>
[AddComponentMenu("MR Base/Diagnostics/Marker Probe Entry")]
[DisallowMultipleComponent]
public sealed class MarkerProbeEntry : MonoBehaviour
{
    private const string LogPrefix = "[MarkerProbe]";

    [Tooltip("Keep disabled by default. When enabled, the probe still starts only in the Editor or a Development Build.")]
    [SerializeField] private bool startProbeOnStart;

    [Tooltip("Development-only IMGUI controls. The same public methods can be wired to a later XR diagnostic canvas.")]
    [SerializeField] private bool showDiagnosticOverlay = true;

    [Min(0.1f)]
    [SerializeField] private float logFlushIntervalSeconds = 2f;

    [Min(0.1f)]
    [SerializeField] private float poseConsoleIntervalSeconds = 1f;

    [Tooltip("Privacy warning: stores full QR text in device logs. Development Builds only; disabled by default.")]
    [SerializeField] private bool recordRawPayloadPlaintext;

    [SerializeField] private MarkerProbeFixtureKind selectedFixture = MarkerProbeFixtureKind.StaticId0;

    [Header("Measured printed fixture (required for Pose acceptance)")]
    [SerializeField] private float measuredArucoOuterSizeMm;
    [SerializeField] private float measuredQrToArucoCenterDistanceMm;
    [SerializeField] private string fixtureFlatnessNotes;
    [SerializeField] private string fixtureInstallationNotes;

    private MarkerProbeSessionModel currentSession;
    private MarkerProbeRegistry markerRegistry;
    private MarkerProbeRunModel currentRun;
    private MarkerProbeJsonlWriter logWriter;
    private MarkerProbeConsoleMirror consoleMirror;
    private MarkerProbeVisualAnchorManager visualAnchorManager;
#if MRBASE_HAS_MRUK && MRBASE_QUEST
    private QuestMarkerProbeAdapter questAdapter;
#endif
#if MRBASE_HAS_PICO_SDK
    private PicoMarkerProbeAdapter picoAdapter;
#endif
    private int nextRunIndex = 1;
    private long sessionGeneration;
    private long droppedEventCount;
    private float nextLogFlushTime;

    public bool IsProbeRunning { get; private set; }

    public string LastStartFailure { get; private set; }

    public static bool IsProbeBuildEnabled => Debug.isDebugBuild;

    public MarkerProbeFixtureKind SelectedFixture => selectedFixture;

    public MarkerProbeSessionModel CurrentSession => currentSession;

    public MarkerProbeRunModel CurrentRun => currentRun;

    public bool IsDualMarkerRun =>
        currentRun != null &&
        currentRun.state != MarkerProbeState.RunEnded &&
        currentRun.dualMarkerMode;

    public bool IsDualPairingComplete => IsDualMarkerRun && currentRun.dualPairingComplete;

    public bool IsSessionActive =>
        currentSession != null &&
        currentSession.state != MarkerProbeState.SessionEnded &&
        currentSession.state != MarkerProbeState.Faulted;

    public string CurrentLogPath => currentSession?.logFilePath;

    public bool IsRawPayloadPlaintextEnabled => recordRawPayloadPlaintext && Debug.isDebugBuild;

    public int DiagnosticVisualAnchorCount => visualAnchorManager?.ActiveAnchorCount ?? 0;

    /// <summary>
    /// Capture this token when subscribing or dispatching asynchronous platform work.
    /// A callback may write only while <see cref="IsSessionGenerationCurrent"/> returns true.
    /// </summary>
    public long CurrentSessionGeneration => Interlocked.Read(ref sessionGeneration);

    public MarkerProbeState CurrentState => currentRun != null && currentRun.state != MarkerProbeState.RunEnded
        ? currentRun.state
        : currentSession?.state ?? (IsProbeRunning ? MarkerProbeState.Idle : MarkerProbeState.Disabled);

    private void Start()
    {
        if (startProbeOnStart)
        {
            TryStartProbe();
        }
    }

    /// <summary>
    /// Starts the diagnostic boundary. Platform callbacks are wired by later probe tasks.
    /// </summary>
    public bool TryStartProbe()
    {
        if (IsProbeRunning)
        {
            return true;
        }

        if (!IsProbeBuildEnabled)
        {
            LastStartFailure = "Marker Probe is available only in the Unity Editor or a Development Build.";
            Debug.LogError($"{LogPrefix} {LastStartFailure}", this);
            return false;
        }

        if (TryGetProductionConflict(out MarkerTrackingBootstrapper productionBootstrapper))
        {
            LastStartFailure =
                $"Refusing to start because production {nameof(MarkerTrackingBootstrapper)} " +
                $"'{productionBootstrapper.name}' is loaded in scene '{productionBootstrapper.gameObject.scene.name}'. " +
                "PICO exposes a single set-only marker callback slot, so the probe must run in an isolated scene.";
            Debug.LogError($"{LogPrefix} {LastStartFailure}", this);
            return false;
        }

        LastStartFailure = null;
        IsProbeRunning = true;
        Debug.Log($"{LogPrefix} Diagnostic entry started. Production marker services were not modified.", this);
        return true;
    }

    public void StopProbe()
    {
        StopProbe(MarkerProbeEndReason.OperatorStopped, "Probe stopped by the operator.");
    }

    private void StopProbe(MarkerProbeEndReason reason, string detail)
    {
        if (!IsProbeRunning)
        {
            return;
        }

        EndSession(reason, detail);
        Interlocked.Increment(ref sessionGeneration);
        IsProbeRunning = false;
        Debug.Log($"{LogPrefix} Diagnostic entry stopped; reason={reason}.", this);
    }

    public bool StartSession()
    {
        if (!TryStartProbe())
        {
            return false;
        }

        if (IsSessionActive)
        {
            Debug.LogWarning($"{LogPrefix} A session is already active: {currentSession.sessionId}", this);
            return false;
        }

        string sessionId = CreateSessionId();
        MarkerProbePicoRegistrationSnapshot picoRegistration =
            CreatePicoRegistrationSnapshot(out MarkerProbeXrInputSubsystemSnapshot[] xrInputSubsystems);
        Interlocked.Increment(ref sessionGeneration);
        Interlocked.Exchange(ref droppedEventCount, 0);
        currentRun = null;
        nextRunIndex = 1;
        currentSession = new MarkerProbeSessionModel
        {
            schemaVersion = "marker-probe-v1",
            sessionId = sessionId,
            platform = ResolvePlatform(),
            state = MarkerProbeState.SessionActive,
            startedUtc = DateTime.UtcNow.ToString("O"),
            endedUtc = null,
            endReason = MarkerProbeEndReason.None,
            logFilePath = null,
            fixture = CreateFixtureMetadata(selectedFixture),
            environment = CreateEnvironmentSnapshot(xrInputSubsystems),
            picoMarkerRegistration = picoRegistration,
            markerRegistry = null,
            privacy = new MarkerProbePrivacySettings
            {
                rawPayloadPlaintextRequested = recordRawPayloadPlaintext,
                rawPayloadPlaintextEnabled = IsRawPayloadPlaintextEnabled,
                warning = IsRawPayloadPlaintextEnabled
                    ? "SENSITIVE: full RawPayload plaintext is stored in this device log. Redact before sharing."
                    : "RawPayload plaintext disabled; only UTF-8 byte length and SHA-256 are stored."
            }
        };
        markerRegistry = MarkerProbeRegistry.LoadDefault();
        currentSession.markerRegistry = new MarkerProbeRegistrySnapshot
        {
            version = markerRegistry.Version,
            sourceSha256 = markerRegistry.SourceSha256
        };

        try
        {
            logWriter = new MarkerProbeJsonlWriter(sessionId);
            currentSession.logFilePath = logWriter.FilePath;
            logWriter.Write(CreateLogEvent("session_started", includeSessionSnapshot: true));
            nextLogFlushTime = Time.unscaledTime + logFlushIntervalSeconds;
            consoleMirror = new MarkerProbeConsoleMirror(
                currentSession.sessionId,
                currentSession.logFilePath,
                poseConsoleIntervalSeconds);
#if MRBASE_HAS_MRUK && MRBASE_QUEST
            questAdapter = GetComponent<QuestMarkerProbeAdapter>();
            if (questAdapter == null)
            {
                questAdapter = gameObject.AddComponent<QuestMarkerProbeAdapter>();
            }

            questAdapter.BeginObservation(this, CurrentSessionGeneration);
#endif
#if MRBASE_HAS_PICO_SDK && MRBASE_PICO
            picoAdapter = GetComponent<PicoMarkerProbeAdapter>();
            if (picoAdapter == null)
            {
                picoAdapter = gameObject.AddComponent<PicoMarkerProbeAdapter>();
            }

            picoAdapter.BeginObservation(this, CurrentSessionGeneration);
#endif
        }
        catch (Exception exception)
        {
            logWriter?.Dispose();
            logWriter = null;
            currentSession.state = MarkerProbeState.Faulted;
            currentSession.endReason = MarkerProbeEndReason.UnrecoverableError;
            LastStartFailure = $"Could not create Marker Probe JSONL log: {exception.Message}";
            Debug.LogError($"{LogPrefix} {LastStartFailure}", this);
            return false;
        }

        consoleMirror.LogSessionStarted(CurrentState.ToString());
        return true;
    }

    /// <summary>
    /// Called by the PICO adapter immediately after SetMarkerInfoCallback returns.
    /// The attempted flag disambiguates a real zero success code from the preflight default.
    /// </summary>
    public void RecordPicoMarkerRegistrationResult(int result)
    {
        if (!IsSessionActive || currentSession.picoMarkerRegistration == null)
        {
            Debug.LogWarning($"{LogPrefix} Ignored PICO registration result outside an active session.", this);
            return;
        }

        currentSession.picoMarkerRegistration.registrationAttempted = true;
        currentSession.picoMarkerRegistration.setMarkerInfoCallbackResult = result;
        currentSession.picoMarkerRegistration.markerTrackingDeviceSupport = result == 0
            ? MarkerProbeAvailability.Available
            : MarkerProbeAvailability.Error;

        if (logWriter != null)
        {
            logWriter.Write(CreateLogEvent(
                "pico_marker_callback_registration",
                includeSessionSnapshot: true));
        }

        Debug.Log(
            $"{LogPrefix} SetMarkerInfoCallback result={result}; " +
            $"trackingMode={currentSession.picoMarkerRegistration.trackingModeName} " +
            $"({currentSession.picoMarkerRegistration.trackingModeSource}); " +
            $"cameraYOffset={currentSession.picoMarkerRegistration.cameraYOffset:F3}",
            this);
        consoleMirror?.LogState(
            "pico_marker_callback_registration",
            CurrentState.ToString(),
            $"result={result}");
    }

    public void RecordPicoEnterpriseInitResult(bool succeeded)
    {
        if (!IsSessionActive || currentSession.picoMarkerRegistration == null)
        {
            return;
        }

        MarkerProbePicoRegistrationSnapshot snapshot = currentSession.picoMarkerRegistration;
        snapshot.initAttempted = true;
        snapshot.initSucceeded = succeeded;
        snapshot.enterpriseServiceSupport = succeeded
            ? MarkerProbeAvailability.Available
            : MarkerProbeAvailability.Error;
        snapshot.enterpriseServiceDetail = succeeded
            ? "InitEnterpriseService(false) succeeded; feature-specific device support remains unproven until runtime callbacks."
            : "InitEnterpriseService(false) failed before BindEnterpriseService.";

        MarkerProbeLogEvent logEvent = CreateLogEvent("pico_enterprise_init", includeSessionSnapshot: true);
        logEvent.nativeEventKind = "PXR_Enterprise.InitEnterpriseService";
        logEvent.sdkResult = new MarkerProbeSdkResult
        {
            available = true,
            operation = "InitEnterpriseService(false)",
            success = succeeded,
            resultCode = succeeded ? 0 : 1,
            detail = snapshot.enterpriseServiceDetail
        };
        if (!succeeded)
        {
            logEvent.error = new MarkerProbeErrorContext
            {
                category = "enterprise_service",
                errorCode = "PICO_ENTERPRISE_INIT_FAILED",
                message = snapshot.enterpriseServiceDetail
            };
        }

        logWriter?.Write(logEvent);
        if (!succeeded)
        {
            logWriter?.Flush();
            consoleMirror?.LogError(logEvent.eventType, logEvent.error);
        }
        else
        {
            consoleMirror?.LogState(logEvent.eventType, CurrentState.ToString());
        }
    }

    public void RecordPicoEnterpriseBindRequested()
    {
        if (!IsSessionActive || currentSession.picoMarkerRegistration == null)
        {
            return;
        }

        currentSession.picoMarkerRegistration.bindRequested = true;
        MarkerProbeLogEvent logEvent = CreateLogEvent("pico_enterprise_bind_requested");
        logEvent.nativeEventKind = "PXR_Enterprise.BindEnterpriseService";
        logWriter?.Write(logEvent);
        consoleMirror?.LogState(logEvent.eventType, CurrentState.ToString());
    }

    public bool RecordPicoEnterpriseBindResult(
        long capturedGeneration,
        bool bound,
        string callbackUtc,
        double callbackMonotonicSeconds,
        int callbackThreadId)
    {
        if (!IsSessionGenerationCurrent(capturedGeneration) ||
            currentSession?.picoMarkerRegistration == null)
        {
            return TryRecordPlatformEvent(capturedGeneration, new MarkerProbeLogEvent
            {
                eventType = "pico_enterprise_bind_late_callback",
                nativeEventKind = "PXR_Enterprise.BindEnterpriseService.callback",
                utcTimestamp = callbackUtc,
                monotonicTimeSeconds = callbackMonotonicSeconds,
                threadId = callbackThreadId
            });
        }

        MarkerProbePicoRegistrationSnapshot snapshot = currentSession.picoMarkerRegistration;
        snapshot.bindCallbackReceived = true;
        snapshot.bindSucceeded = bound;
        snapshot.enterpriseServiceSupport = bound
            ? MarkerProbeAvailability.Available
            : MarkerProbeAvailability.Error;
        snapshot.tobAuthorization = bound
            ? MarkerProbeAvailability.Available
            : MarkerProbeAvailability.Error;
        snapshot.enterpriseServiceDetail = bound
            ? "Bind callback=true. Used as an operational TOB access fact; this SDK exposes no QR/Marker-specific authorization query."
            : "Bind callback=false. The SDK does not distinguish missing enterprise support, service failure, or TOB authorization denial.";

        var logEvent = new MarkerProbeLogEvent
        {
            eventType = "pico_enterprise_bind_result",
            nativeEventKind = "PXR_Enterprise.BindEnterpriseService.callback",
            utcTimestamp = callbackUtc,
            monotonicTimeSeconds = callbackMonotonicSeconds,
            threadId = callbackThreadId,
            session = currentSession,
            sdkResult = new MarkerProbeSdkResult
            {
                available = true,
                operation = "BindEnterpriseService callback",
                resultCode = bound ? 0 : 1,
                success = bound,
                detail = snapshot.enterpriseServiceDetail
            },
            error = bound
                ? null
                : new MarkerProbeErrorContext
                {
                    category = "enterprise_service",
                    errorCode = "PICO_ENTERPRISE_BIND_FAILED",
                    message = snapshot.enterpriseServiceDetail
                }
        };
        return TryRecordPlatformEvent(capturedGeneration, logEvent);
    }

    public bool IsSessionGenerationCurrent(long generation)
    {
        return IsProbeRunning &&
               IsSessionActive &&
               generation == Interlocked.Read(ref sessionGeneration);
    }

    public IMarkerIdParser CreateMarkerIdParser()
    {
        return new StandardFixtureMarkerIdParser(IsRawPayloadPlaintextEnabled);
    }

    public void ShowOrUpdateDiagnosticAnchor(
        int sourceInstanceId,
        string qrContent,
        string markerId,
        Pose markerPose,
        Rect? qrPlaneRect = null)
    {
        if (!IsSessionActive)
        {
            return;
        }

        if (visualAnchorManager == null)
        {
            visualAnchorManager = GetComponent<MarkerProbeVisualAnchorManager>();
            if (visualAnchorManager == null)
            {
                visualAnchorManager = gameObject.AddComponent<MarkerProbeVisualAnchorManager>();
            }
        }

        visualAnchorManager.ShowOrUpdate(sourceInstanceId, qrContent, markerId, markerPose, qrPlaneRect);
    }

    public void HideDiagnosticAnchor(int sourceInstanceId)
    {
        visualAnchorManager?.Hide(sourceInstanceId);
    }

    /// <summary>
    /// Adds run-relative facts to a Quest observation without advancing the probe state machine.
    /// Quest continuously reports QR Trackables, so matching is evidence rather than a scan trigger.
    /// </summary>
    public void ClassifyQuestObservation(
        MarkerProbeLogEvent logEvent,
        bool isTracked,
        bool poseValid)
    {
        if (logEvent == null)
        {
            return;
        }

        bool hasActiveRun = currentRun != null && currentRun.state != MarkerProbeState.RunEnded;
        logEvent.runId = hasActiveRun ? currentRun.runId : null;
        logEvent.expectedMarkerId = hasActiveRun ? currentRun.expectedMarkerId : null;
        logEvent.expectedMarkerIds = hasActiveRun ? currentRun.expectedMarkerIds : null;

        bool parseSucceeded = logEvent.markerIdParseAttempted &&
                              logEvent.markerIdParseSuccess &&
                              !string.IsNullOrEmpty(logEvent.markerId);
        bool idExpected = hasActiveRun &&
                          parseSucceeded &&
                          currentRun.expectedMarkerIds != null &&
                          Array.IndexOf(currentRun.expectedMarkerIds, logEvent.markerId) >= 0;
        logEvent.markerMatchesCurrentRun = hasActiveRun &&
                                           isTracked &&
                                           poseValid &&
                                           idExpected;

        if (!logEvent.markerIdParseAttempted)
        {
            logEvent.markerSampleClassification = "parse_not_attempted";
        }
        else if (!parseSucceeded)
        {
            logEvent.markerSampleClassification = "payload_parse_failed";
        }
        else if (!hasActiveRun)
        {
            logEvent.markerSampleClassification = "no_active_run";
        }
        else if (!isTracked)
        {
            logEvent.markerSampleClassification = "not_tracked";
        }
        else if (!poseValid)
        {
            logEvent.markerSampleClassification = "invalid_pose";
        }
        else if (!idExpected)
        {
            logEvent.markerSampleClassification = "valid_id_mismatch";
        }
        else
        {
            logEvent.markerSampleClassification = "valid_exact_match";
        }
    }

    public void EndSession()
    {
        EndSession(MarkerProbeEndReason.OperatorStopped, "Session ended by the operator.");
    }

    public bool StartNextRun()
    {
        if (!IsSessionActive)
        {
            Debug.LogWarning($"{LogPrefix} Start a session before starting a run.", this);
            return false;
        }

        if (currentRun != null && currentRun.state != MarkerProbeState.RunEnded)
        {
            Debug.LogWarning($"{LogPrefix} End the current run before starting the next one.", this);
            return false;
        }

        currentSession.fixture = CreateFixtureMetadata(selectedFixture);
        string[] expectedMarkerIds = currentSession.fixture.expectedMarkerIds;
        currentRun = new MarkerProbeRunModel
        {
            sessionId = currentSession.sessionId,
            runId = Guid.NewGuid().ToString("N"),
            runIndex = nextRunIndex++,
            fixtureId = currentSession.fixture.fixtureId,
            expectedMarkerId = expectedMarkerIds[0],
            expectedMarkerIds = expectedMarkerIds,
            pairedMarkerIds = Array.Empty<string>(),
            dualMarkerMode = selectedFixture == MarkerProbeFixtureKind.DualMarker0And250,
            dualPairingComplete = false,
            state = MarkerProbeState.TrackingRequested,
            startedUtc = DateTime.UtcNow.ToString("O"),
            endedUtc = null,
            endReason = MarkerProbeEndReason.None,
            endDetail = null
        };
        currentSession.state = MarkerProbeState.TrackingRequested;
        logWriter?.Write(CreateLogEvent("run_started"));
        consoleMirror?.LogState(
            "run_started",
            CurrentState.ToString(),
            $"run={currentRun.runId} fixture={currentRun.fixtureId} MarkerID={currentRun.expectedMarkerId}");

        Debug.Log(
            $"{LogPrefix} Run {currentRun.runIndex} started: {currentRun.runId}; " +
            $"fixture={currentRun.fixtureId}; registry={markerRegistry?.Version}",
            this);
        return true;
    }

    public bool RecordPicoQrScanResult(
        long capturedGeneration,
        long scanGeneration,
        string runId,
        MarkerIdParseResult parseResult,
        bool sdkReportedUnsupported,
        string callbackUtc,
        double callbackMonotonicSeconds,
        int callbackThreadId,
        double requestLatencyMilliseconds)
    {
        if (!IsCurrentRun(capturedGeneration, runId))
        {
            return false;
        }

        currentRun.state = MarkerProbeState.QrResultReceived;
        currentSession.state = MarkerProbeState.QrResultReceived;
        MarkerProbePicoRegistrationSnapshot registration = currentSession.picoMarkerRegistration;
        if (registration != null)
        {
            registration.qrScanDeviceSupport = sdkReportedUnsupported
                ? MarkerProbeAvailability.Unavailable
                : MarkerProbeAvailability.Available;
        }

        var logEvent = new MarkerProbeLogEvent
        {
            eventType = "pico_qr_scan_result",
            nativeEventKind = "PXR_Enterprise.ScanQRCode.callback",
            observationSemantics =
                "Raw SDK scan result; null/empty/cancel behavior is recorded without production retry semantics.",
            runId = runId,
            scanGeneration = scanGeneration,
            utcTimestamp = callbackUtc,
            monotonicTimeSeconds = callbackMonotonicSeconds,
            threadId = callbackThreadId,
            requestLatencyAvailable = true,
            requestLatencyMilliseconds = requestLatencyMilliseconds,
            markerIdParseAttempted = parseResult != null,
            markerIdParseSuccess = parseResult?.success == true,
            markerIdParseFailure = parseResult?.failure.ToString(),
            markerIdParseFailureDetail = parseResult?.failureDetail,
            markerId = parseResult?.markerId,
            rawPayload = parseResult?.rawPayload,
            sdkResult = new MarkerProbeSdkResult
            {
                available = !sdkReportedUnsupported,
                operation = "ScanQRCode callback",
                resultCode = sdkReportedUnsupported ? -2 : parseResult?.success == true ? 0 : 1,
                success = parseResult?.success == true,
                detail = sdkReportedUnsupported
                    ? "PICO SDK documented sentinel -2: QR scan is unsupported by this device."
                    : parseResult?.failureDetail
            }
        };
        if (sdkReportedUnsupported || parseResult?.success != true)
        {
            logEvent.error = new MarkerProbeErrorContext
            {
                category = sdkReportedUnsupported ? "capability" : "payload",
                errorCode = sdkReportedUnsupported
                    ? "PICO_QR_SCAN_UNSUPPORTED"
                    : "PICO_QR_" + (parseResult?.failure.ToString().ToUpperInvariant() ?? "NO_RESULT"),
                message = sdkReportedUnsupported
                    ? "ScanQRCode returned the documented -2 unsupported sentinel."
                    : parseResult?.failureDetail ?? "ScanQRCode returned no parseable result."
            };
        }

        TryRecordPlatformEvent(capturedGeneration, logEvent);

        if (sdkReportedUnsupported)
        {
            EndCurrentRun(MarkerProbeEndReason.CapabilityUnavailable, logEvent.error.message);
            return true;
        }

        if (parseResult?.success != true)
        {
            EndCurrentRun(MarkerProbeEndReason.InvalidPayload, logEvent.error.message);
            return true;
        }

        if (!string.Equals(parseResult.markerId, currentRun.expectedMarkerId, StringComparison.Ordinal))
        {
            string detail =
                $"Scanned MarkerID {parseResult.markerId} does not match selected fixture MarkerID {currentRun.expectedMarkerId}.";
            TryRecordPlatformEvent(capturedGeneration, new MarkerProbeLogEvent
            {
                eventType = "pico_qr_fixture_mismatch",
                nativeEventKind = "probe_fixture_validation",
                runId = runId,
                scanGeneration = scanGeneration,
                markerId = parseResult.markerId,
                error = new MarkerProbeErrorContext
                {
                    category = "fixture",
                    errorCode = "PICO_QR_FIXTURE_ID_MISMATCH",
                    message = detail
                }
            });
            EndCurrentRun(MarkerProbeEndReason.InvalidPayload, detail);
            return true;
        }

        currentRun.state = MarkerProbeState.AwaitingMatchingMarker;
        currentSession.state = MarkerProbeState.AwaitingMatchingMarker;
        TryRecordPlatformEvent(capturedGeneration, new MarkerProbeLogEvent
        {
            eventType = "pico_awaiting_matching_marker",
            nativeEventKind = "probe_state_transition",
            runId = runId,
            scanGeneration = scanGeneration,
            markerId = parseResult.markerId
        });
        return true;
    }

    public bool RecordPicoQrScanTerminalEvent(
        long capturedGeneration,
        long scanGeneration,
        string runId,
        string eventType,
        MarkerProbeEndReason reason,
        string detail,
        MarkerProbeErrorContext error = null)
    {
        if (!IsCurrentRun(capturedGeneration, runId))
        {
            return false;
        }

        TryRecordPlatformEvent(capturedGeneration, new MarkerProbeLogEvent
        {
            eventType = eventType,
            nativeEventKind = "probe_qr_watchdog",
            observationSemantics =
                "Diagnostic run termination only; no production timeout, cancellation, or retry policy is implied.",
            runId = runId,
            scanGeneration = scanGeneration,
            error = error
        });
        EndCurrentRun(reason, detail);
        return true;
    }

    public bool RecordPicoMarkerSample(long capturedGeneration, MarkerProbeLogEvent logEvent)
    {
        if (!IsSessionGenerationCurrent(capturedGeneration) || logEvent == null)
        {
            return TryRecordPlatformEvent(capturedGeneration, logEvent);
        }

        bool valid = logEvent.validFlagAvailable && logEvent.validFlag != 0;
        bool hasActiveRun = currentRun != null && currentRun.state != MarkerProbeState.RunEnded;
        bool trackingActive = hasActiveRun &&
                              (currentRun.state == MarkerProbeState.TrackingRequested ||
                               currentRun.state == MarkerProbeState.MarkerObserved ||
                               currentRun.state == MarkerProbeState.RegistryResolved ||
                               currentRun.state == MarkerProbeState.RegistryMiss ||
                               currentRun.state == MarkerProbeState.AwaitingMatchingMarker ||
                               currentRun.state == MarkerProbeState.MatchingMarkerObserved);
        logEvent.runId = hasActiveRun ? currentRun.runId : null;
        logEvent.expectedMarkerId = hasActiveRun ? currentRun.expectedMarkerId : null;
        logEvent.expectedMarkerIds = hasActiveRun ? currentRun.expectedMarkerIds : null;
        bool isAlreadyPairedDualMarker = hasActiveRun &&
                                         currentRun.dualPairingComplete &&
                                         valid &&
                                         Array.IndexOf(currentRun.pairedMarkerIds, logEvent.markerId) >= 0;
        int picoId = 0;
        int.TryParse(logEvent.markerId, out picoId);
        MarkerProbeRegistryEntry registryEntry = null;
        bool registryResolved = valid && markerRegistry != null &&
                                markerRegistry.TryResolve(picoId, out registryEntry);
        logEvent.picoArUcoId = picoId;
        logEvent.registryResolved = registryResolved;
        logEvent.qrId = registryEntry?.qrId;
        logEvent.logicalMarkerId = registryEntry?.logicalMarkerId;
        logEvent.businessObjectId = registryEntry?.businessObjectId;
        logEvent.registryVersion = markerRegistry?.Version;
        logEvent.registrySourceSha256 = markerRegistry?.SourceSha256;
        logEvent.markerMatchesCurrentRun = trackingActive && valid && registryResolved;
        if (registryResolved && currentRun != null && currentRun.dualMarkerMode)
        {
            AddCurrentDualPairing(logEvent.markerId);
        }

        if (!valid)
        {
            logEvent.eventType = "pico_marker_invalid_sample";
            logEvent.markerSampleClassification = "invalid_flag";
        }
        else if (!hasActiveRun)
        {
            logEvent.eventType = "pico_marker_sample_no_active_run";
            logEvent.markerSampleClassification = "no_active_run";
        }
        else if (!trackingActive)
        {
            logEvent.eventType = "pico_marker_sample_before_tracking";
            logEvent.markerSampleClassification = "run_not_tracking";
        }
        else if (!registryResolved)
        {
            logEvent.eventType = "pico_marker_registry_miss";
            logEvent.markerSampleClassification = "valid_registry_miss";
            currentRun.state = MarkerProbeState.RegistryMiss;
            currentSession.state = MarkerProbeState.RegistryMiss;
        }
        else if (currentRun.state == MarkerProbeState.TrackingRequested)
        {
            logEvent.eventType = "pico_marker_registry_resolved";
            logEvent.markerSampleClassification = "first_valid_registry_match";
            currentRun.state = MarkerProbeState.RegistryResolved;
            currentSession.state = MarkerProbeState.RegistryResolved;
        }
        else if (isAlreadyPairedDualMarker)
        {
            logEvent.eventType = "pico_paired_dual_marker_sample";
            logEvent.markerSampleClassification = "valid_previously_paired_dual_id";
        }
        else if (currentRun.state != MarkerProbeState.MatchingMarkerObserved)
        {
            currentRun.state = MarkerProbeState.MatchingMarkerObserved;
            currentSession.state = MarkerProbeState.MatchingMarkerObserved;
            logEvent.eventType = "pico_matching_marker_observed";
            logEvent.markerSampleClassification = currentRun.dualMarkerMode
                ? currentRun.dualPairingComplete
                    ? "dual_pairing_complete"
                    : "dual_first_pair_complete_waiting_for_operator"
                : "first_valid_exact_match";
        }
        else
        {
            logEvent.eventType = "pico_matching_marker_sample";
            logEvent.markerSampleClassification = "continuing_valid_exact_match";
        }

        return TryRecordPlatformEvent(capturedGeneration, logEvent);
    }

    private void AddCurrentDualPairing(string markerId)
    {
        string[] paired = currentRun.pairedMarkerIds ?? Array.Empty<string>();
        if (Array.IndexOf(paired, markerId) >= 0)
        {
            return;
        }

        var updated = new string[paired.Length + 1];
        Array.Copy(paired, updated, paired.Length);
        updated[paired.Length] = markerId;
        currentRun.pairedMarkerIds = updated;
        currentRun.dualPairingComplete =
            currentRun.expectedMarkerIds != null &&
            updated.Length == currentRun.expectedMarkerIds.Length;
    }

    private bool IsCurrentRun(long capturedGeneration, string runId)
    {
        return IsSessionGenerationCurrent(capturedGeneration) &&
               currentRun != null &&
               currentRun.state != MarkerProbeState.RunEnded &&
               string.Equals(currentRun.runId, runId, StringComparison.Ordinal);
    }

    public void EndCurrentRun()
    {
        EndCurrentRun(MarkerProbeEndReason.OperatorStopped, "Run ended by the operator.");
    }

    public void SelectStaticFixture()
    {
        SelectFixture(MarkerProbeFixtureKind.StaticId0);
    }

    public void SelectDynamicFixture()
    {
        SelectFixture(MarkerProbeFixtureKind.DynamicId250);
    }

    public void SelectDualFixture()
    {
        SelectFixture(MarkerProbeFixtureKind.DualMarker0And250);
    }

    public bool ContinueDualPairing()
    {
        if (!IsDualMarkerRun ||
            currentRun.dualPairingComplete ||
            currentRun.pairedMarkerIds == null ||
            currentRun.pairedMarkerIds.Length != 1 ||
            currentRun.state != MarkerProbeState.MatchingMarkerObserved)
        {
            Debug.LogWarning($"{LogPrefix} Dual marker sampling is not ready.", this);
            return false;
        }

        currentRun.expectedMarkerId = currentRun.expectedMarkerIds[1];
        currentRun.state = MarkerProbeState.TrackingRequested;
        currentSession.state = MarkerProbeState.TrackingRequested;
        MarkerProbeLogEvent transition = CreateLogEvent("pico_dual_second_marker_requested");
        transition.expectedMarkerId = currentRun.expectedMarkerId;
        transition.expectedMarkerIds = currentRun.expectedMarkerIds;
        logWriter?.Write(transition);
        consoleMirror?.LogState(
            transition.eventType,
            CurrentState.ToString(),
            $"run={currentRun.runId} MarkerID={currentRun.expectedMarkerId}");

        return true;
    }

    private void EndSession(MarkerProbeEndReason reason, string detail)
    {
        if (!IsSessionActive)
        {
            return;
        }

#if MRBASE_HAS_MRUK && MRBASE_QUEST
        questAdapter?.EndObservation();
#endif
        visualAnchorManager?.Clear();

        EndCurrentRun(reason, detail);
#if MRBASE_HAS_PICO_SDK && MRBASE_PICO
        picoAdapter?.EndObservation();
#endif
        currentSession.state = MarkerProbeState.SessionEnded;
        currentSession.endedUtc = DateTime.UtcNow.ToString("O");
        currentSession.endReason = reason;
        Interlocked.Increment(ref sessionGeneration);

        if (logWriter != null)
        {
            try
            {
                MarkerProbeSessionSummary summary = logWriter.CreateFinalSummary(
                    Interlocked.Read(ref droppedEventCount),
                    reason,
                    detail);
                MarkerProbeLogEvent sessionEndedEvent = CreateLogEvent(
                    "session_ended",
                    includeSessionSnapshot: true);
                sessionEndedEvent.sessionSummary = summary;
                logWriter.Write(sessionEndedEvent);
                logWriter.Flush();
            }
            catch (Exception exception)
            {
                Debug.LogError($"{LogPrefix} Could not write session end event: {exception}", this);
            }
            finally
            {
                try
                {
                    logWriter.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogError($"{LogPrefix} Could not close JSONL log cleanly: {exception}", this);
                }

                logWriter = null;
            }
        }

        consoleMirror?.LogState("session_ended", CurrentState.ToString(), $"reason={reason}");
        consoleMirror = null;
        Debug.Log($"{LogPrefix} Session ended: {currentSession.sessionId}; reason={reason}", this);
    }

    private void EndCurrentRun(MarkerProbeEndReason reason, string detail)
    {
        if (currentRun == null || currentRun.state == MarkerProbeState.RunEnded)
        {
            return;
        }

        currentRun.state = MarkerProbeState.RunEnded;
        currentRun.endedUtc = DateTime.UtcNow.ToString("O");
        currentRun.endReason = reason;
        currentRun.endDetail = detail;

        if (IsSessionActive)
        {
            currentSession.state = MarkerProbeState.SessionActive;
        }

        if (logWriter != null)
        {
            try
            {
                MarkerProbeLogEvent runEndedEvent = CreateLogEvent("run_ended");
                runEndedEvent.probeState = MarkerProbeState.RunEnded.ToString();
                logWriter.Write(runEndedEvent);
                logWriter.Flush();
            }
            catch (Exception exception)
            {
                Debug.LogError($"{LogPrefix} Could not flush run end event: {exception}", this);
            }
        }

        consoleMirror?.LogState(
            "run_ended",
            MarkerProbeState.RunEnded.ToString(),
            $"run={currentRun.runId} reason={reason}");
        Debug.Log($"{LogPrefix} Run ended: {currentRun.runId}; reason={reason}", this);
    }

    private void SelectFixture(MarkerProbeFixtureKind fixture)
    {
        if (currentRun != null && currentRun.state != MarkerProbeState.RunEnded)
        {
            Debug.LogWarning($"{LogPrefix} End the current run before changing fixtures.", this);
            return;
        }

        selectedFixture = fixture;
        Debug.Log($"{LogPrefix} Selected fixture: {selectedFixture}", this);
    }

    private static bool TryGetProductionConflict(out MarkerTrackingBootstrapper productionBootstrapper)
    {
        // The current production provider has only one construction path: MarkerTrackingBootstrapper.Start.
        // Include inactive objects because disabling the bootstrapper does not stop the provider it created.
        MarkerTrackingBootstrapper[] bootstrappers =
            UnityEngine.Object.FindObjectsByType<MarkerTrackingBootstrapper>(FindObjectsInactive.Include);

        productionBootstrapper = bootstrappers.Length > 0 ? bootstrappers[0] : null;
        return productionBootstrapper != null;
    }

    private static string CreateSessionId()
    {
        return DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss.fff'Z'") + "-" + Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Records an adapter event only if its captured session generation is still current.
    /// Platform code may set native timestamp, callback interval, and error fields before calling.
    /// </summary>
    public bool TryRecordPlatformEvent(long capturedGeneration, MarkerProbeLogEvent logEvent)
    {
        if (!IsSessionGenerationCurrent(capturedGeneration) || logWriter == null || logEvent == null)
        {
            Interlocked.Increment(ref droppedEventCount);
            return false;
        }

        try
        {
            PopulateCommonEventFields(logEvent);
            logWriter.Write(logEvent);
            if (logEvent.error != null)
            {
                logWriter.Flush();
                consoleMirror?.LogError(logEvent.eventType, logEvent.error);
            }

            if (logEvent.pose?.unityPose != null)
            {
                consoleMirror?.TryLogPose(logEvent);
            }
            else if (logEvent.error == null)
            {
                consoleMirror?.LogState(logEvent.eventType, logEvent.probeState);
            }

            return true;
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref droppedEventCount);
            Debug.LogError($"{LogPrefix} Could not persist platform event: {exception}", this);
            return false;
        }
    }

    private MarkerProbeLogEvent CreateLogEvent(string eventType, bool includeSessionSnapshot = false)
    {
        var logEvent = new MarkerProbeLogEvent
        {
            eventType = eventType,
            session = includeSessionSnapshot ? currentSession : null
        };
        PopulateCommonEventFields(logEvent);
        return logEvent;
    }

    private void PopulateCommonEventFields(MarkerProbeLogEvent logEvent)
    {
        logEvent.schemaVersion = currentSession?.schemaVersion ?? "marker-probe-v1";
        logEvent.platform ??= currentSession?.platform.ToString() ?? ResolvePlatform().ToString();
        logEvent.runId ??= currentRun?.runId;
        logEvent.fixtureId ??= currentRun?.fixtureId ?? currentSession?.fixture?.fixtureId;
        logEvent.expectedMarkerId ??= currentRun?.expectedMarkerId;
        logEvent.expectedMarkerIds ??= currentRun?.expectedMarkerIds;
        logEvent.utcTimestamp ??= DateTime.UtcNow.ToString("O");
        if (logEvent.monotonicTimeSeconds <= 0d)
        {
            logEvent.monotonicTimeSeconds = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        }

        if (logEvent.unityFrame <= 0)
        {
            logEvent.unityFrame = Time.frameCount;
        }

        if (logEvent.threadId == 0)
        {
            logEvent.threadId = Thread.CurrentThread.ManagedThreadId;
        }

        logEvent.probeState ??= CurrentState.ToString();
    }

    private void Update()
    {
        if (logWriter == null || Time.unscaledTime < nextLogFlushTime)
        {
            return;
        }

        try
        {
            logWriter.Flush();
            nextLogFlushTime = Time.unscaledTime + logFlushIntervalSeconds;
        }
        catch (Exception exception)
        {
            Debug.LogError($"{LogPrefix} Periodic JSONL flush failed: {exception}", this);
            EndSession(MarkerProbeEndReason.UnrecoverableError, "Periodic JSONL flush failed.");
        }
    }

    private static MarkerProbePlatform ResolvePlatform()
    {
#if MRBASE_QUEST
        return MarkerProbePlatform.Quest;
#elif MRBASE_PICO
        return MarkerProbePlatform.Pico;
#else
        return MarkerProbePlatform.Unknown;
#endif
    }

    private MarkerProbeFixtureMetadata CreateFixtureMetadata(MarkerProbeFixtureKind fixture)
    {
        const string staticHash = "4aa969097dbebf232e9e4725f6b2ec7b8ed5975848b3ac9bea0a935ec5d356ca";
        const string dynamicHash = "76e340775a25a7cd8f6e54874a21ad8bbf3af9c3ce5e502593a7b4a007c114b7";
        bool isDynamic = fixture == MarkerProbeFixtureKind.DynamicId250;
        bool isDual = fixture == MarkerProbeFixtureKind.DualMarker0And250;
        string markerId = isDynamic ? "250" : "0";

        return new MarkerProbeFixtureMetadata
        {
            fixtureId = isDual ? "dual-static-0-and-dynamic-250-a3" :
                isDynamic ? "dynamic-250-a3" : "static-0-a3",
            fixtureKind = fixture,
            formatVersion = "a3-marker-fixture-v1",
            arucoDictionary = "DICT_4X4_1000",
            expectedMarkerIds = isDual ? new[] { "0", "250" } : new[] { markerId },
            sourceFileSha256 = isDual ? null : isDynamic ? dynamicHash : staticHash,
            sourceFileSha256s = isDual ? new[] { staticHash, dynamicHash } : null,
            pageSize = "A3 landscape",
            printScalePercent = 100f,
            nominalQrOuterSizeMm = 160f,
            nominalArucoOuterSizeMm = 160f,
            measuredArucoOuterSizeMm = measuredArucoOuterSizeMm,
            measuredQrToArucoCenterDistanceMm = measuredQrToArucoCenterDistanceMm,
            flatnessNotes = fixtureFlatnessNotes,
            installationNotes = fixtureInstallationNotes
        };
    }

    private static MarkerProbeEnvironmentSnapshot CreateEnvironmentSnapshot(
        MarkerProbeXrInputSubsystemSnapshot[] xrInputSubsystems)
    {
        MarkerProbePlatform platform = ResolvePlatform();
        return new MarkerProbeEnvironmentSnapshot
        {
            developmentBuild = Debug.isDebugBuild,
            productName = Application.productName,
            applicationIdentifier = Application.identifier,
            applicationVersion = Application.version,
            applicationBuildGuid = Application.buildGUID,
            unityVersion = Application.unityVersion,
            deviceModel = SystemInfo.deviceModel,
            deviceType = SystemInfo.deviceType.ToString(),
            operatingSystem = SystemInfo.operatingSystem,
            graphicsDeviceName = SystemInfo.graphicsDeviceName,
            questSdkVersion = ResolveQuestSdkVersion(),
            picoSdkVersion = ResolvePicoSdkVersion(),
            capabilities = CreateInitialCapabilitySnapshot(platform),
            xrInputSubsystems = xrInputSubsystems
        };
    }

    private static MarkerProbePicoRegistrationSnapshot CreatePicoRegistrationSnapshot(
        out MarkerProbeXrInputSubsystemSnapshot[] xrInputSubsystems)
    {
        var subsystems = new List<XRInputSubsystem>();
        SubsystemManager.GetSubsystems(subsystems);

        var snapshots = new MarkerProbeXrInputSubsystemSnapshot[subsystems.Count];
        TrackingOriginModeFlags selectedMode = TrackingOriginModeFlags.Unknown;
        string source = null;

        for (int i = 0; i < subsystems.Count; i++)
        {
            XRInputSubsystem subsystem = subsystems[i];
            TrackingOriginModeFlags mode = subsystem.GetTrackingOriginMode();
            snapshots[i] = new MarkerProbeXrInputSubsystemSnapshot
            {
                subsystemId = subsystem.subsystemDescriptor?.id,
                running = subsystem.running,
                trackingOriginMode = mode.ToString()
            };

            if (selectedMode == TrackingOriginModeFlags.Unknown && mode != TrackingOriginModeFlags.Unknown)
            {
                selectedMode = mode;
                source = $"xr_input_subsystem:{snapshots[i].subsystemId ?? "unknown"}";
            }
        }

        bool detected = selectedMode != TrackingOriginModeFlags.Unknown;
        if (!detected)
        {
            selectedMode = TrackingOriginModeFlags.Floor;
            source = "fallback:Floor (no initialized XRInputSubsystem reported a known mode)";
        }

        xrInputSubsystems = snapshots;
        return new MarkerProbePicoRegistrationSnapshot
        {
            applicable = ResolvePlatform() == MarkerProbePlatform.Pico,
            initAttempted = false,
            initSucceeded = false,
            bindRequested = false,
            bindCallbackReceived = false,
            bindSucceeded = false,
            enterpriseServiceSupport = MarkerProbeAvailability.NotChecked,
            tobAuthorization = MarkerProbeAvailability.NotChecked,
            qrScanDeviceSupport = MarkerProbeAvailability.NotChecked,
            markerTrackingDeviceSupport = MarkerProbeAvailability.NotChecked,
            enterpriseServiceDetail =
                "PICO SDK exposes no QR/Marker-specific TOB authorization query; runtime Init/Bind/API results are recorded separately.",
            trackingMode = (int)selectedMode,
            trackingModeName = selectedMode.ToString(),
            trackingModeDetected = detected,
            trackingModeSource = source,
            cameraYOffset = 0f,
            registrationAttempted = false,
            setMarkerInfoCallbackResult = 0
        };
    }

    private static MarkerProbeCapabilitySnapshot[] CreateInitialCapabilitySnapshot(MarkerProbePlatform platform)
    {
        if (platform == MarkerProbePlatform.Quest)
        {
            return new[]
            {
                CreateCapability(
                    "quest_qr_trackable",
                    IsQuestSdkCompiled() ? MarkerProbeAvailability.Available : MarkerProbeAvailability.Unavailable,
                    "Permission and runtime capability are recorded by the Quest adapter preflight.")
            };
        }

        if (platform == MarkerProbePlatform.Pico)
        {
            MarkerProbeAvailability support = IsPicoSdkCompiled()
                ? MarkerProbeAvailability.Available
                : MarkerProbeAvailability.Unavailable;
            return new[]
            {
                CreateCapability("pico_enterprise_service", support, "TOB authorization is recorded after BindEnterpriseService."),
                CreateCapability("pico_qr_scan", support, "Runtime result is recorded after ScanQRCode."),
                CreateCapability("pico_aruco_marker_callback", support, "Runtime result is recorded after SetMarkerInfoCallback.")
            };
        }

        return new[]
        {
            CreateCapability(
                "marker_probe_platform_define",
                MarkerProbeAvailability.Unavailable,
                "Neither MRBASE_QUEST nor MRBASE_PICO is active in this build.")
        };
    }

    private static MarkerProbeCapabilitySnapshot CreateCapability(
        string capability,
        MarkerProbeAvailability support,
        string detail)
    {
        return new MarkerProbeCapabilitySnapshot
        {
            capability = capability,
            support = support,
            permission = MarkerProbeAvailability.NotChecked,
            authorization = MarkerProbeAvailability.NotChecked,
            detail = detail
        };
    }

    private static bool IsQuestSdkCompiled()
    {
#if MRBASE_HAS_MRUK
        return true;
#else
        return false;
#endif
    }

    private static bool IsPicoSdkCompiled()
    {
#if MRBASE_HAS_PICO_SDK
        return true;
#else
        return false;
#endif
    }

    private static string ResolveQuestSdkVersion()
    {
#if MRBASE_HAS_MRUK
        return GetSdkAssemblyVersion(typeof(Meta.XR.MRUtilityKit.MRUK));
#else
        return null;
#endif
    }

    private static string ResolvePicoSdkVersion()
    {
#if MRBASE_HAS_PICO_SDK
        return GetSdkAssemblyVersion(typeof(Unity.XR.PICO.TOBSupport.PXR_Enterprise));
#else
        return null;
#endif
    }

    private static string GetSdkAssemblyVersion(Type sdkType)
    {
        var assemblyName = sdkType.Assembly.GetName();
        return $"{assemblyName.Name}/{assemblyName.Version}";
    }

    private void OnGUI()
    {
        if (!IsProbeBuildEnabled || !showDiagnosticOverlay)
        {
            return;
        }

        float width = Mathf.Min(720f, Screen.width - 32f);
        GUILayout.BeginArea(new Rect(16f, 16f, width, 430f), "Marker Probe (Development Only)", GUI.skin.window);
        GUILayout.Label($"Probe: {(IsProbeRunning ? "Running" : "Stopped")}");
        GUILayout.Label($"State: {CurrentState}");
        GUILayout.Label($"Fixture: {selectedFixture}");
        GUILayout.Label($"Session: {currentSession?.sessionId ?? "-"}");
        GUILayout.Label($"Run: {currentRun?.runId ?? "-"}");
        GUILayout.Label($"Log: {CurrentLogPath ?? "created when a session starts"}");
        GUILayout.Label(
            $"Measured ArUco: {measuredArucoOuterSizeMm:F1} mm; " +
            $"QR→ArUco centers: {measuredQrToArucoCenterDistanceMm:F1} mm");
        GUILayout.Label(
            IsRawPayloadPlaintextEnabled
                ? "WARNING: RawPayload plaintext logging ENABLED — redact logs before sharing."
                : "RawPayload privacy: plaintext OFF; length + SHA-256 only.");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Start Probe")) TryStartProbe();
        if (GUILayout.Button("Stop Probe")) StopProbe();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Static ID 0")) SelectStaticFixture();
        if (GUILayout.Button("Dynamic ID 250")) SelectDynamicFixture();
        if (GUILayout.Button("Dual 0 + 250")) SelectDualFixture();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Start Session")) StartSession();
        if (GUILayout.Button("End Session")) EndSession();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Start Next Run")) StartNextRun();
        if (GUILayout.Button("Scan 2nd Dual QR")) ContinueDualPairing();
        if (GUILayout.Button("End Run")) EndCurrentRun();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void OnDestroy()
    {
        StopProbe(MarkerProbeEndReason.SceneUnloaded, "Probe object was destroyed or its scene was unloaded.");
    }

    private void OnDisable()
    {
        StopProbe(MarkerProbeEndReason.ProbeDisabled, "Probe component or GameObject was disabled.");
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            StopProbe(MarkerProbeEndReason.ApplicationPaused, "Application was paused.");
        }
    }

    private void OnApplicationQuit()
    {
        StopProbe(MarkerProbeEndReason.ApplicationQuit, "Application is quitting.");
    }
}
