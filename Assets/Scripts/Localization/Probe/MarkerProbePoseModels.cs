using System;
using UnityEngine;

[Serializable]
public sealed class MarkerProbeVector3Snapshot
{
    public double x;
    public double y;
    public double z;
}

[Serializable]
public sealed class MarkerProbeQuaternionSnapshot
{
    public double x;
    public double y;
    public double z;
    public double w;
}

[Serializable]
public sealed class MarkerProbePicoRawPoseSnapshot
{
    public string positionX;
    public string positionY;
    public string positionZ;
    public string rotationX;
    public string rotationY;
    public string rotationZ;
    public string rotationW;
}

[Serializable]
public sealed class MarkerProbePoseSnapshot
{
    public string coordinateSpace;
    public string referencePoint;
    public MarkerProbeVector3Snapshot position;
    public MarkerProbeQuaternionSnapshot rotation;
}

[Serializable]
public sealed class MarkerProbePoseEvidence
{
    public MarkerProbePoseSnapshot nativePose;
    public MarkerProbePoseSnapshot unityTrackingOriginPose;
    public MarkerProbePoseSnapshot unityPose;
    public MarkerProbePoseValidationResult validation;
}

[Serializable]
public sealed class MarkerProbePoseValidationResult
{
    public bool valid;
    public bool positionFinite;
    public bool rotationFinite;
    public bool rotationNormalizable;
    public bool rotationMagnitudeAvailable;
    public double rotationMagnitude;
    public string detail;
}

[Serializable]
public sealed class MarkerProbeXrOriginSnapshot
{
    public bool available;
    public string source;
    public string detail;
    public string trackingOriginMode;
    public MarkerProbePoseSnapshot originWorldPose;
    public MarkerProbeVector3Snapshot originLossyScale;
    public MarkerProbePoseSnapshot cameraLocalPose;
}

[Serializable]
public sealed class MarkerProbeOffsetEvidence
{
    public string status;
    public string description;
    public MarkerProbePoseSnapshot inputPose;
    public MarkerProbePoseSnapshot candidateOffset;
    public MarkerProbePoseSnapshot outputPose;
}

[Serializable]
public sealed class MarkerProbeDualMarkerEvidence
{
    public bool accepted;
    public string rejectionReason;
    public string markerAId;
    public string markerBId;
    public long sourceSnapshotSequence;
    public int sourceUnityFrame;
    public string coordinateSpace;
    public MarkerProbePoseSnapshot markerAPose;
    public MarkerProbePoseSnapshot markerBPose;
    public MarkerProbePoseSnapshot markerAToB;
}

[Serializable]
public sealed class MarkerProbeSdkResult
{
    public bool available;
    public string operation;
    public int resultCode;
    public bool success;
    public string detail;
}

public static class MarkerProbePoseSerialization
{
    private const float MinimumQuaternionMagnitudeSquared = 1e-12f;

    public static MarkerProbePoseValidationResult ValidateUnityPose(Pose pose)
    {
        bool positionFinite = IsFinite(pose.position.x) &&
                              IsFinite(pose.position.y) &&
                              IsFinite(pose.position.z);
        bool rotationFinite = IsFinite(pose.rotation.x) &&
                              IsFinite(pose.rotation.y) &&
                              IsFinite(pose.rotation.z) &&
                              IsFinite(pose.rotation.w);
        float magnitudeSquared = rotationFinite
            ? pose.rotation.x * pose.rotation.x +
              pose.rotation.y * pose.rotation.y +
              pose.rotation.z * pose.rotation.z +
              pose.rotation.w * pose.rotation.w
            : 0f;
        bool rotationNormalizable = rotationFinite && magnitudeSquared > MinimumQuaternionMagnitudeSquared;
        bool valid = positionFinite && rotationNormalizable;

        return new MarkerProbePoseValidationResult
        {
            valid = valid,
            positionFinite = positionFinite,
            rotationFinite = rotationFinite,
            rotationNormalizable = rotationNormalizable,
            rotationMagnitudeAvailable = rotationFinite,
            rotationMagnitude = rotationFinite ? Math.Sqrt(magnitudeSquared) : 0d,
            detail = valid
                ? "Finite Unity position and normalizable quaternion."
                : BuildValidationFailureDetail(positionFinite, rotationFinite, rotationNormalizable)
        };
    }

    public static MarkerProbePoseSnapshot FromUnityPose(
        Pose pose,
        string coordinateSpace,
        string referencePoint)
    {
        return new MarkerProbePoseSnapshot
        {
            coordinateSpace = coordinateSpace,
            referencePoint = referencePoint,
            position = FromUnityVector(pose.position),
            rotation = FromUnityQuaternion(pose.rotation)
        };
    }

    public static MarkerProbePoseSnapshot FromDoubleComponents(
        double positionX,
        double positionY,
        double positionZ,
        double rotationX,
        double rotationY,
        double rotationZ,
        double rotationW,
        string coordinateSpace,
        string referencePoint)
    {
        return new MarkerProbePoseSnapshot
        {
            coordinateSpace = coordinateSpace,
            referencePoint = referencePoint,
            position = new MarkerProbeVector3Snapshot
            {
                x = positionX,
                y = positionY,
                z = positionZ
            },
            rotation = new MarkerProbeQuaternionSnapshot
            {
                x = rotationX,
                y = rotationY,
                z = rotationZ,
                w = rotationW
            }
        };
    }

    public static MarkerProbeVector3Snapshot FromUnityVector(Vector3 value)
    {
        return new MarkerProbeVector3Snapshot
        {
            x = value.x,
            y = value.y,
            z = value.z
        };
    }

    public static MarkerProbeQuaternionSnapshot FromUnityQuaternion(Quaternion value)
    {
        return new MarkerProbeQuaternionSnapshot
        {
            x = value.x,
            y = value.y,
            z = value.z,
            w = value.w
        };
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string BuildValidationFailureDetail(
        bool positionFinite,
        bool rotationFinite,
        bool rotationNormalizable)
    {
        if (!positionFinite)
        {
            return "Unity position contains NaN or Infinity.";
        }

        if (!rotationFinite)
        {
            return "Unity rotation contains NaN or Infinity.";
        }

        if (!rotationNormalizable)
        {
            return "Unity rotation quaternion magnitude is too close to zero to normalize.";
        }

        return "Pose validation failed for an unspecified reason.";
    }
}
