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
    private bool opened;
    private bool warnedUnavailable;

    public void Open()
    {
        opened = true;
        TrySubscribe();
    }

    /// <summary>
    /// MRUK 可能比本源晚一两帧才就绪(安装器建出的 GameObject 要走一次 Awake),所以订阅
    /// 不能是一次性的:Open 试一次,之后每次 Poll 再试,直到成功。失败必须出声——
    /// 此前这里是静默 return,真机上表现为"完全没反应",分不清是没扫到还是没订阅。
    /// </summary>
    private void TrySubscribe()
    {
        if (subscribed || !opened)
        {
            return;
        }

        if (MRUK.Instance == null || MRUK.Instance.SceneSettings == null)
        {
            if (!warnedUnavailable)
            {
                warnedUnavailable = true;
                Debug.LogWarning(
                    "[QuestObservationSource] MRUK 尚未就绪,订阅推迟到后续 Poll 重试。" +
                    "若此后一直没有 '已订阅' 日志,说明 QuestMrukRuntimeInstaller 没有把 MRUK 装起来。");
            }

            return;
        }

        MRUK.MRUKSettings settings = MRUK.Instance.SceneSettings;
        settings.TrackableAdded.AddListener(HandleTrackableAdded);
        settings.TrackableRemoved.AddListener(HandleTrackableRemoved);
        subscribed = true;
        Debug.Log("[QuestObservationSource] 已订阅 MRUK TrackableAdded / TrackableRemoved");
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
        opened = false;
        warnedUnavailable = false;
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

        TrySubscribe();

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
