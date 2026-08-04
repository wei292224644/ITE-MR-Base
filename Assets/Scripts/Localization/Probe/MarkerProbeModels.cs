using System;

public enum MarkerProbePlatform
{
    Unknown,
    Quest,
    Pico
}

public enum MarkerProbeState
{
    Disabled,
    Idle,
    SessionActive,
    QrScanRequested,
    QrResultReceived,
    AwaitingMatchingMarker,
    MatchingMarkerObserved,
    RunEnded,
    SessionEnded,
    Faulted
}

public enum MarkerProbeEndReason
{
    None,
    Completed,
    OperatorStopped,
    ProbeDisabled,
    WatchdogTimeout,
    ApplicationPaused,
    ApplicationQuit,
    SceneUnloaded,
    CapabilityUnavailable,
    PermissionDenied,
    AuthorizationDenied,
    InvalidPayload,
    InvalidPose,
    SdkError,
    ProductionProviderConflict,
    ReplacedByNextRun,
    UnrecoverableError
}

public enum MarkerProbeFixtureKind
{
    Unknown,
    StaticId0,
    DynamicId250,
    DualMarker0And250
}

public enum MarkerProbeAvailability
{
    NotChecked,
    Unavailable,
    Denied,
    Available,
    Error
}

/// <summary>
/// Platform-neutral session state. UTC values use the round-trip ISO 8601 format.
/// Raw QR payload text is intentionally absent; only its later privacy-safe summary is logged.
/// </summary>
[Serializable]
public sealed class MarkerProbeSessionModel
{
    public string schemaVersion;
    public string sessionId;
    public MarkerProbePlatform platform;
    public MarkerProbeState state;
    public string startedUtc;
    public string endedUtc;
    public MarkerProbeEndReason endReason;
    public string logFilePath;
    public MarkerProbeFixtureMetadata fixture;
    public MarkerProbeEnvironmentSnapshot environment;
    public MarkerProbePicoRegistrationSnapshot picoMarkerRegistration;
    public MarkerProbePrivacySettings privacy;
}

/// <summary>A single operator-triggered acquisition attempt within a session.</summary>
[Serializable]
public sealed class MarkerProbeRunModel
{
    public string sessionId;
    public string runId;
    public int runIndex;
    public string fixtureId;
    public string expectedMarkerId;
    public string[] expectedMarkerIds;
    public string[] pairedMarkerIds;
    public bool dualMarkerMode;
    public bool dualPairingComplete;
    public MarkerProbeState state;
    public string startedUtc;
    public string endedUtc;
    public MarkerProbeEndReason endReason;
    public string endDetail;
}

[Serializable]
public sealed class MarkerProbeFixtureMetadata
{
    public string fixtureId;
    public MarkerProbeFixtureKind fixtureKind;
    public string formatVersion;
    public string arucoDictionary;
    public string[] expectedMarkerIds;
    public string sourceFileSha256;
    public string[] sourceFileSha256s;
    public string pageSize;
    public float printScalePercent;
    public float nominalQrOuterSizeMm;
    public float nominalArucoOuterSizeMm;
    public float measuredArucoOuterSizeMm;
    public float measuredQrToArucoCenterDistanceMm;
    public string flatnessNotes;
    public string installationNotes;
}

[Serializable]
public sealed class MarkerProbeEnvironmentSnapshot
{
    public bool developmentBuild;
    public string productName;
    public string applicationIdentifier;
    public string applicationVersion;
    public string applicationBuildGuid;
    public string unityVersion;
    public string deviceModel;
    public string deviceType;
    public string operatingSystem;
    public string graphicsDeviceName;
    public string questSdkVersion;
    public string picoSdkVersion;
    public MarkerProbeCapabilitySnapshot[] capabilities;
    public MarkerProbeXrInputSubsystemSnapshot[] xrInputSubsystems;
}

[Serializable]
public sealed class MarkerProbeCapabilitySnapshot
{
    public string capability;
    public MarkerProbeAvailability support;
    public MarkerProbeAvailability permission;
    public MarkerProbeAvailability authorization;
    public string detail;
}

/// <summary>
/// Stores XR subsystem facts as strings so the core probe assembly has no dependency on
/// platform SDKs or UnityEngine.XR types.
/// </summary>
[Serializable]
public sealed class MarkerProbeXrInputSubsystemSnapshot
{
    public string subsystemId;
    public bool running;
    public string trackingOriginMode;
}

[Serializable]
public sealed class MarkerProbePicoRegistrationSnapshot
{
    public bool applicable;
    public bool initAttempted;
    public bool initSucceeded;
    public bool bindRequested;
    public bool bindCallbackReceived;
    public bool bindSucceeded;
    public MarkerProbeAvailability enterpriseServiceSupport;
    public MarkerProbeAvailability tobAuthorization;
    public MarkerProbeAvailability qrScanDeviceSupport;
    public MarkerProbeAvailability markerTrackingDeviceSupport;
    public string enterpriseServiceDetail;
    public int trackingMode;
    public string trackingModeName;
    public bool trackingModeDetected;
    public string trackingModeSource;
    public float cameraYOffset;
    public bool registrationAttempted;
    public int setMarkerInfoCallbackResult;
}

[Serializable]
public sealed class MarkerProbePrivacySettings
{
    public bool rawPayloadPlaintextRequested;
    public bool rawPayloadPlaintextEnabled;
    public string warning;
}

[Serializable]
public sealed class MarkerProbeQuestPreflightSnapshot
{
    public bool mrukInstanceAvailable;
    public bool qrCodeTrackingSupported;
    public bool scenePermissionGranted;
    public bool qrCodeTrackingRequestedBefore;
    public bool qrCodeTrackingRequestedAfter;
    public bool qrCodeTrackingActiveAtPreflight;
    public string detail;
}
