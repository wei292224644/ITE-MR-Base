namespace Uality.IteTour.Core
{
    /// <summary>
    /// 导览所处的状态（ite-guide-state-machine D1）。取代原来的 <c>_paused</c> 与
    /// <c>_forcedScanPending</c> 两个 bool：两者本是一个状态的两面，拆开之后各策略
    /// 各看一半——摘下头显期间区域仍能唤醒 Tour 就是这么漏出来的。
    ///
    /// 顺序刻意让 <c>default</c> 落在 <see cref="Suspended"/>：未初始化的状态什么都不做。
    /// </summary>
    public enum GuideState
    {
        /// <summary>头显摘下：不认扫码，区域不唤醒 Tour，无 Tour 在播。</summary>
        Suspended,

        /// <summary>
        /// 等待扫码定位，与冷启动相同：扫任一已装配 Tour 的码即激活并锚定（不看区域），
        /// 区域不唤醒 Tour，无 Tour 在播。
        /// </summary>
        AwaitingScan,

        /// <summary>已定位：区域门禁生效，区域可唤醒 regionalTrigger。</summary>
        Anchored,
    }

    /// <summary>进入当前状态的原因。宿主据此决定提示文案，例如重定位黄条（ite-guide-state-machine D8）。</summary>
    public enum GuideStateReason
    {
        ColdStart,
        HeadsetRemoved,
        HeadsetMounted,
        Recentered,
        HostRequested,
        Scanned,
    }
}
