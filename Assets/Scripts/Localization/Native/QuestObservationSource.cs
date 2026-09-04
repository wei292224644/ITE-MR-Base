#if MRBASE_QUEST
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

/// <summary>
/// Quest 观测源:持有活动 <see cref="MRUKTrackable"/> 集合,每次 <see cref="Poll"/> 读最新
/// transform / IsTracked(design D4,行为改变)。<c>IsTracked == false</c> 一律计入缺席
/// (从 Poll 结果里跳过),MUST NOT 直接触发移除——丢失判定统一归 <see cref="MarkerTrackingSession"/>。
/// </summary>
public sealed class QuestObservationSource : IMarkerObservationSource
{
    private readonly List<MRUKTrackable> active = new List<MRUKTrackable>();
    private readonly List<MarkerObservation> pollBuffer = new List<MarkerObservation>();
    private bool paused;
    private bool subscribed;

    public void Open()
    {
        if (subscribed || MRUK.Instance == null || MRUK.Instance.SceneSettings == null)
        {
            return;
        }

        MRUK.MRUKSettings settings = MRUK.Instance.SceneSettings;
        settings.TrackableAdded.AddListener(HandleTrackableAdded);
        settings.TrackableRemoved.AddListener(HandleTrackableRemoved);
        subscribed = true;
    }

    public void Close()
    {
        if (!subscribed)
        {
            return;
        }

        if (MRUK.Instance != null && MRUK.Instance.SceneSettings != null)
        {
            MRUK.MRUKSettings settings = MRUK.Instance.SceneSettings;
            settings.TrackableAdded.RemoveListener(HandleTrackableAdded);
            settings.TrackableRemoved.RemoveListener(HandleTrackableRemoved);
        }

        subscribed = false;
        active.Clear();
    }

    public void Pause() => paused = true;

    public void Resume() => paused = false;

    public IReadOnlyList<MarkerObservation> Poll()
    {
        pollBuffer.Clear();
        if (paused)
        {
            return pollBuffer;
        }

        for (int i = 0; i < active.Count; i++)
        {
            MRUKTrackable trackable = active[i];
            if (trackable == null)
            {
                continue;
            }

            // design D4: IsTracked=false 只是让本次 Poll 不返回它(即"缺席"),不是立即移除。
            if (!trackable.IsTracked)
            {
                continue;
            }

            if (string.IsNullOrEmpty(trackable.MarkerPayloadString))
            {
                continue;
            }

            var pose = new Pose(trackable.transform.position, trackable.transform.rotation);
            pollBuffer.Add(new MarkerObservation(MarkerPlatform.Quest, trackable.MarkerPayloadString, pose));
        }

        return pollBuffer;
    }

    private void HandleTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
        {
            return;
        }

        if (!active.Contains(trackable))
        {
            active.Add(trackable);
        }
    }

    // 只从活动集合移除,不派发丢失——丢失判定统一归 Session(task 3.3)。
    private void HandleTrackableRemoved(MRUKTrackable trackable)
    {
        active.Remove(trackable);
    }
}
#endif
