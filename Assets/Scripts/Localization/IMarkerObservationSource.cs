using System.Collections.Generic;

/// <summary>
/// 平台观测源:只提供纯查询与生命周期,不发任何事件(design D1)。
/// 平台差异只允许存在于 <see cref="Poll"/> 的实现里;派发节奏、丢失判定等语义只属于
/// <see cref="MarkerTrackingSession"/>。实现类型 MUST NOT 声明 event,MUST NOT 持有
/// 丢失判定所需的历史状态(如上一次可见 ID 集合)。
/// </summary>
public interface IMarkerObservationSource
{
    /// <summary>打开硬件会话的生命周期入口。</summary>
    void Open();

    /// <summary>释放硬件会话。与 <see cref="Pause"/> 不同,Close 之后重开需要完整的初始化流程。</summary>
    void Close();

    /// <summary>停止派发与检测开销,但不释放底层硬件会话(design D6)。</summary>
    void Pause();

    /// <summary>从 Pause 恢复,不触发任何权限申请或硬件会话重建。</summary>
    void Resume();

    /// <summary>返回本次调用时刻当前可见的全部观测。不缓存跨调用的历史状态。</summary>
    IReadOnlyList<MarkerObservation> Poll();
}
