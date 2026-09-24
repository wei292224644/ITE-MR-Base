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

    // 应用暂停过 ⇒ OpenXR 会话可能已停掉又重开（真机 2026-09-24：每次会话停止都紧跟 OnApplicationPause(true)）。
    // 没暂停过就不重建：重建会丢掉已认出的码，重新识别要 1.6～41 秒（同日真机）。
    private bool pausedSinceRebuild;

    public void Open()
    {
        opened = true;
        OVRManager.InputFocusAcquired += HandleInputFocusAcquired;
        OVRManager.HMDMounted += HandleHmdMounted;
        TrySubscribe();
    }

    private void HandleInputFocusAcquired() => RebuildQrTracking("输入焦点恢复");

    // InputFocusAcquired 不可靠：会话重开后已是 FOCUSED，OVRPlugin.hasInputFocus 却可能一直是 false，
    // 事件就不来（真机 2026-09-24，30 秒内都没来，扫码全程无观测）。会话重开总伴随摘下再戴上，所以戴上也重建。
    private void HandleHmdMounted() => RebuildQrTracking("戴上头显");

    /// <summary>
    /// OpenXR 会话被整个停掉又重开时（摘下头显较久时 Quest 会这么做），原生层的 QR 追踪上下文跟着旧会话
    /// 没了，可 MRUK 只在「期望配置 ≠ 当前配置」时才重配追踪器（MRUK.UpdateTrackables），它记着的仍是
    /// 「QR 已开」，于是永远不再重配，扫码从此没有任何观测（真机 2026-09-23）。
    ///
    /// 关一下 MRUK 组件：它的 OnDisable 会 ConfigureTrackers(0) 并清掉记住的配置，下一帧
    /// Update 就按期望配置重新建 QR 追踪。
    /// </summary>
    private void RebuildQrTracking(string reason)
    {
        if (!opened || MRUK.Instance == null)
        {
            return;
        }

        if (!pausedSinceRebuild)
        {
            Debug.Log($"[QuestObservationSource] {reason}，应用未暂停过，保留 MRUK QR 追踪器");
            return;
        }

        // 同一次戴上两个信号都可能到（相隔约 0.25 秒），只重建先到的那次：真机 2026-09-24 两次会话重开，
        // 先到的 HMDMounted 虽早于会话进入 FOCUSED，重建都有效。
        // ponytail: 触发靠 OVRManager 事件 + 暂停标记推断会话重开；再漏就改为直接监听 OpenXR 会话重开（自定义 OpenXRFeature）。
        pausedSinceRebuild = false;
        MRUK.Instance.enabled = false;
        MRUK.Instance.enabled = true;
        Debug.Log($"[QuestObservationSource] {reason}，重建 MRUK QR 追踪器");
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
        OVRManager.InputFocusAcquired -= HandleInputFocusAcquired;
        OVRManager.HMDMounted -= HandleHmdMounted;

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

    public void Pause()
    {
        paused = true;
        pausedSinceRebuild = true;
    }

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
