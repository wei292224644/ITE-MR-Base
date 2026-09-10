using UnityEngine;

/// <summary>
/// 按平台建观测源的**唯一**入口（design D4）。
///
/// 收敛的理由不是"少写几行"，是这里有三条各自独立的失败路径——MRUK 未装配、
/// 平台 SDK 未安装、构建意图未配置——它们的差异只在真机上显形。散成两处就要维护两份，
/// 而两份迟早不一样。
///
/// Quest 侧的 MRUK 必须在这里显式装配：内容场景与探针场景都是加性加载的，
/// 没有任何自动装配钩子赶得上，漏了就是 <c>MRUK.Instance</c> 为 null、订阅静默失败
/// （真机已复现）。
/// </summary>
public static class MarkerSourceFactory
{
    public enum Failure
    {
        None,

        /// <summary>构建意图未配置：既不是 Quest 也不是 PICO。</summary>
        PlatformNotConfigured,

        /// <summary>构建意图是 PICO，但 <c>com.unity.xr.picoxr</c> 不在工程里。</summary>
        PlatformSdkMissing,
    }

    /// <summary>
    /// 建一个观测源。失败时返回 null 并给出可区分的原因。
    /// </summary>
    /// <param name="host">
    /// PICO 的观测源是 MonoBehaviour（要相机会话与检测循环），挂在这个对象上。
    /// Quest 的观测源是纯 C#，不用它。
    /// </param>
    /// <param name="detail">
    /// 人可读的补充说明。**成功时也可能非空** —— 例如 MRUK 没装起来：
    /// 观测源仍然建得出来，只是订阅不会成功，调用方应当把它打出来。
    /// </param>
    public static IMarkerObservationSource Create(GameObject host, out Failure failure, out string detail)
    {
        failure = Failure.None;
        detail = null;

#if MRBASE_QUEST
        if (!QuestMrukRuntimeInstaller.EnsureInitialized(out string mrukDetail))
        {
            // 不算失败:源建得出来,只是订阅会一直重试并出声。把原因带出去由调用方报。
            detail = "Quest MRUK 运行时未就绪：" + mrukDetail;
        }

        return new QuestObservationSource();
#elif MRBASE_PICO && MRBASE_HAS_PICO_SDK
        if (host == null)
        {
            failure = Failure.PlatformNotConfigured;
            detail = "PICO 观测源是 MonoBehaviour，必须给出承载对象";
            return null;
        }

        return host.AddComponent<PicoFiducialObservationSource>();
#elif MRBASE_PICO
        // 两条 define 轴是独立的:分开报才能一眼看出是「包没装」而不是「平台没配」。
        failure = Failure.PlatformSdkMissing;
        detail = "构建意图为 PICO，但未安装 com.unity.xr.picoxr。";
        return null;
#else
        failure = Failure.PlatformNotConfigured;
        detail = "未识别到 MRBASE_QUEST 或 MRBASE_PICO 平台定义，部署配置错误。";
        return null;
#endif
    }
}
