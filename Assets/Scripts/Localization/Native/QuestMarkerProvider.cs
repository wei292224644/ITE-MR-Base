#if MRBASE_QUEST
using System;
using Meta.XR.MRUtilityKit;
using UnityEngine;

public class QuestMarkerProvider : IMarkerTrackingProvider
{
    public event Action<string, Pose> MarkerResolved;
    public event Action<string> MarkerLost;

    public void StartTracking()
    {
        var settings = MRUK.Instance.SceneSettings;
        settings.TrackableAdded.AddListener(HandleTrackableAdded);
        settings.TrackableRemoved.AddListener(HandleTrackableRemoved);
    }

    public void StopTracking()
    {
        if (MRUK.Instance == null || MRUK.Instance.SceneSettings == null)
        {
            return;
        }

        var settings = MRUK.Instance.SceneSettings;
        settings.TrackableAdded.RemoveListener(HandleTrackableAdded);
        settings.TrackableRemoved.RemoveListener(HandleTrackableRemoved);
    }

    private void HandleTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;
        if (string.IsNullOrEmpty(trackable.MarkerPayloadString)) return;

        var pose = new Pose(trackable.transform.position, trackable.transform.rotation);
        MarkerResolved?.Invoke(trackable.MarkerPayloadString, pose);
    }

    private void HandleTrackableRemoved(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;
        if (string.IsNullOrEmpty(trackable.MarkerPayloadString)) return;

        MarkerLost?.Invoke(trackable.MarkerPayloadString);
    }
}
#endif
