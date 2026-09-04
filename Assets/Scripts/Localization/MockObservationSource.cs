using System.Collections.Generic;

/// <summary>
/// 测试用观测源:可注入任意观测序列与"本周期无观测"。不含 event,不持有丢失判定所需的
/// 历史状态——丢失完全归 <see cref="MarkerTrackingSession"/>(design D1)。
/// </summary>
public sealed class MockObservationSource : IMarkerObservationSource
{
    private static readonly IReadOnlyList<MarkerObservation> Empty = new List<MarkerObservation>();

    private IReadOnlyList<MarkerObservation> nextPoll = Empty;

    public bool IsOpen { get; private set; }
    public bool IsPaused { get; private set; }
    public int OpenCount { get; private set; }
    public int CloseCount { get; private set; }
    public int PauseCount { get; private set; }
    public int ResumeCount { get; private set; }
    public int PollCount { get; private set; }

    public void SetNextPoll(IReadOnlyList<MarkerObservation> observations)
    {
        nextPoll = observations ?? Empty;
    }

    public void SetNextPollEmpty()
    {
        nextPoll = Empty;
    }

    public void Open()
    {
        IsOpen = true;
        OpenCount++;
    }

    public void Close()
    {
        IsOpen = false;
        CloseCount++;
    }

    public void Pause()
    {
        IsPaused = true;
        PauseCount++;
    }

    public void Resume()
    {
        IsPaused = false;
        ResumeCount++;
    }

    public IReadOnlyList<MarkerObservation> Poll()
    {
        PollCount++;
        return nextPoll;
    }
}
