using System;
using System.Collections.Generic;
using UnityEngine;

public class MarkerStabilizer
{
    private class TrackedMarker
    {
        public Pose SmoothedPose;
        public Pose TargetPose;
        public int StableFrameCount;
        public bool HasFiredStableEvent;
    }

    private readonly float positionThreshold;
    private readonly float rotationThreshold;
    private readonly float smoothTime;
    private readonly int stableFrameThreshold;

    private readonly Dictionary<string, TrackedMarker> tracked = new Dictionary<string, TrackedMarker>();

    public event Action<string, Pose> Stabilized;

    public MarkerStabilizer(float positionThreshold = 0.05f, float rotationThreshold = 1f, float smoothTime = 0.01f, int stableFrameThreshold = 30)
    {
        this.positionThreshold = positionThreshold;
        this.rotationThreshold = rotationThreshold;
        this.smoothTime = smoothTime;
        this.stableFrameThreshold = stableFrameThreshold;
    }

    public void Feed(string rawId, Pose rawPose, float deltaTime)
    {
        if (!tracked.TryGetValue(rawId, out var marker))
        {
            marker = new TrackedMarker { SmoothedPose = rawPose, TargetPose = rawPose };
            tracked[rawId] = marker;
        }

        marker.TargetPose = rawPose;

        float positionDelta = Vector3.Distance(marker.SmoothedPose.position, marker.TargetPose.position);
        float angleDelta = Quaternion.Angle(marker.SmoothedPose.rotation, marker.TargetPose.rotation);
        bool moved = positionDelta > positionThreshold || angleDelta > rotationThreshold;

        if (moved)
        {
            marker.StableFrameCount = 0;
            marker.HasFiredStableEvent = false;
        }

        // Count this frame toward stability (including first frame after a move).
        marker.StableFrameCount++;

        float t = smoothTime > 0f ? Mathf.Clamp01(deltaTime / smoothTime) : 1f;
        marker.SmoothedPose = new Pose(
            Vector3.Lerp(marker.SmoothedPose.position, marker.TargetPose.position, t),
            Quaternion.Slerp(marker.SmoothedPose.rotation, marker.TargetPose.rotation, t));

        if (!marker.HasFiredStableEvent && marker.StableFrameCount >= stableFrameThreshold)
        {
            marker.HasFiredStableEvent = true;
            Stabilized?.Invoke(rawId, marker.SmoothedPose);
        }
    }

    public void Reset(string rawId) => tracked.Remove(rawId);
}
