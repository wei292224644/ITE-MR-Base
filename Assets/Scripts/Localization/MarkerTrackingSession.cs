using System;
using System.Collections.Generic;

/// <summary>
/// 事件唯一的发出点(design D1)。纯 C#、不继承 MonoBehaviour、不读 Time.deltaTime——
/// 时间由外部通过 <see cref="Tick"/> 注入,以保证滞回逻辑可在 EditMode 下测试(design D2)。
///
/// 派发节奏不做跨平台限流(design D7):只要标记出现在 <see cref="IMarkerObservationSource.Poll"/>
/// 的结果里,本次 Tick 就会派发一次 MarkerObserved。"平台移除信号"没有专门的 API——
/// Quest 的 TrackableRemoved 只是让平台实现的 Poll() 不再返回该标记,其效果与任何其他缺席
/// (遮挡、IsTracked=false、PICO 单帧漏检)完全一样,统一走下面的丢失滞回判定(design D5)。
/// </summary>
public sealed class MarkerTrackingSession
{
    private class TrackedState
    {
        public float AbsentSeconds;
        public bool HasFiredLost;
    }

    private readonly IMarkerObservationSource source;
    private readonly float lostAfterSeconds;
    private readonly Dictionary<(MarkerPlatform Platform, string RawPayload), TrackedState> tracked =
        new Dictionary<(MarkerPlatform, string), TrackedState>();
    private readonly HashSet<(MarkerPlatform Platform, string RawPayload)> seenThisTick =
        new HashSet<(MarkerPlatform, string)>();

    private bool paused;

    public event Action<MarkerObservation> MarkerObserved;
    public event Action<MarkerPlatform, string> MarkerLost;

    public MarkerTrackingSession(IMarkerObservationSource source, float lostAfterSeconds = 1.0f)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.lostAfterSeconds = lostAfterSeconds;
    }

    public void Open() => source.Open();

    public void Close() => source.Close();

    /// <summary>暂停期间不派发任何事件,且不累计缺席时长(design D6)。</summary>
    public void Pause()
    {
        paused = true;
        source.Pause();
    }

    public void Resume()
    {
        paused = false;
        source.Resume();
    }

    public void Tick(float deltaTime)
    {
        if (paused)
        {
            return;
        }

        seenThisTick.Clear();

        IReadOnlyList<MarkerObservation> observations = source.Poll();
        for (int i = 0; i < observations.Count; i++)
        {
            MarkerObservation observation = observations[i];
            var key = (observation.Platform, observation.RawPayload);
            seenThisTick.Add(key);

            if (!tracked.TryGetValue(key, out TrackedState state))
            {
                state = new TrackedState();
                tracked[key] = state;
            }

            // 丢失后重现按新一轮处理:缺席计时重置,HasFiredLost 复位。
            state.AbsentSeconds = 0f;
            state.HasFiredLost = false;

            MarkerObserved?.Invoke(observation);
        }

        foreach (KeyValuePair<(MarkerPlatform Platform, string RawPayload), TrackedState> entry in tracked)
        {
            if (seenThisTick.Contains(entry.Key))
            {
                continue;
            }

            TrackedState state = entry.Value;
            state.AbsentSeconds += deltaTime;

            if (!state.HasFiredLost && state.AbsentSeconds >= lostAfterSeconds)
            {
                state.HasFiredLost = true;
                MarkerLost?.Invoke(entry.Key.Platform, entry.Key.RawPayload);
            }
        }
    }
}
