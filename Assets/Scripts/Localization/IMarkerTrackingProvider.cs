using System;
using UnityEngine;

public interface IMarkerTrackingProvider
{
    event Action<string, Pose> MarkerResolved;
    event Action<string> MarkerLost;

    void StartTracking();
    void StopTracking();
}
