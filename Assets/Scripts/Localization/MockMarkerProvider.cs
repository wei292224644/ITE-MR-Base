using System;
using UnityEngine;

public class MockMarkerProvider : IMarkerTrackingProvider
{
    public event Action<string, Pose> MarkerResolved;
    public event Action<string> MarkerLost;

    public bool IsTracking { get; private set; }

    public void StartTracking() => IsTracking = true;
    public void StopTracking() => IsTracking = false;

    public void SimulateMarkerResolved(string rawId, Pose pose)
    {
        MarkerResolved?.Invoke(rawId, pose);
    }

    public void SimulateMarkerLost(string rawId)
    {
        MarkerLost?.Invoke(rawId);
    }
}
