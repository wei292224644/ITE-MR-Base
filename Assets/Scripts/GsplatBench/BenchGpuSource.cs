namespace MRBase.GsplatBench
{
    /// <summary>
    /// GPU 时间的来源。必须记进 HUD 与 CSV —— 不同来源的绝对值没有可比性，
    /// 跨轮次比较前得先确认两轮走的是同一条路。
    /// </summary>
    public enum BenchGpuSource
    {
        /// <summary>还没拿到任何 GPU 时间。</summary>
        None = 0,

        /// <summary>Unity 的 <c>FrameTimingManager</c>。厂商中立，但 Quest 3 / Vulkan 上实测恒为 0。</summary>
        FrameTiming,

        /// <summary>
        /// Meta 的 <c>OVRPlugin.PerfMetrics.App_GpuTime_Float</c> —— OVR Metrics Tool 报的就是它。
        /// 只在 Quest 可用。
        /// </summary>
        OvrPerfMetrics,
    }
}
