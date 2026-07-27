#if MRBASE_PICO
using System;
using UnityEngine;
using Unity.XR.PXR;

public class PicoMarkerProvider : IMarkerTrackingProvider
{
    public event Action<string, Pose> MarkerResolved;
    public event Action<string> MarkerLost;

    public void StartTracking()
    {
        PXR_Enterprise.SetMarkerInfoCallback(HandleMarkerInfo);
    }

    public void StopTracking()
    {
        PXR_Enterprise.SetMarkerInfoCallback(null);
    }

    private void HandleMarkerInfo(int markerId, Vector3 position, Quaternion rotation, bool isTracked)
    {
        string rawId = markerId.ToString();

        if (!isTracked)
        {
            MarkerLost?.Invoke(rawId);
            return;
        }

        MarkerResolved?.Invoke(rawId, new Pose(position, rotation));
    }
}
#endif
