// 旧"标记 → 内容"生产链下线(unified-marker-tracking-contract change,task 5.4)。
// IMarkerTrackingProvider 与 MarkerTrackingBootstrapper 已删除,本适配器暂时整体注释,
// 不补兼容层。ITE 导览若要重新接入标记扫描,应基于新的 MarkerTrackingSession /
// MarkerObservation 契约重写,而不是修补这份代码。
#if false
using System;
using UnityEngine;

namespace MRBase.Ite.Host
{
    /// <summary>
    /// 把宿主的标记识别结果转发进 ITE 包。这是 design D2 里「外 → 包」两条推入之一。
    ///
    /// **不经过 <c>MarkerAnchorService</c>**（design D13）：那条链的产物是
    /// <c>AnchorEntity</c>（按 registry 生成内容对象），与 ITE 的导览锚定是两桩不同的业务，
    /// 共用会把两边的生命周期缠在一起。两者各自消费同一个 <c>IMarkerTrackingProvider</c>。
    ///
    /// **经过 <see cref="MarkerStabilizer"/>**：`MarkerResolved` 在标记可见期间每帧都发，
    /// 而 ITE 拿这个位姿去摆整个 Tour——抖动的位姿等于抖动的导览内容。稳定器天然
    /// 「每次稳定检出只发一次」，正是包要的粒度。这里用**自己的**稳定器实例，
    /// 不复用 `MarkerAnchorService` 内部那个。
    ///
    /// 不调 <c>StartTracking</c>：追踪会话由 <c>MarkerTrackingBootstrapper</c> 起，
    /// 这里只搭一条旁路订阅。
    /// </summary>
    public class MarkerSourceAdapter : IDisposable
    {
        private readonly IMarkerTrackingProvider _provider;
        private readonly MarkerStabilizer _stabilizer;
        private readonly Action<string, Pose> _onScan;
        private readonly Func<float> _deltaTime;

        /// <param name="deltaTime">缺省取 <c>Time.deltaTime</c>；测试注入定值。</param>
        public MarkerSourceAdapter(
            IMarkerTrackingProvider provider,
            Action<string, Pose> onScan,
            MarkerStabilizer stabilizer = null,
            Func<float> deltaTime = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _onScan = onScan ?? throw new ArgumentNullException(nameof(onScan));
            _stabilizer = stabilizer ?? new MarkerStabilizer();
            _deltaTime = deltaTime ?? (() => Time.deltaTime);

            _stabilizer.Stabilized += HandleStabilized;
            _provider.MarkerResolved += HandleMarkerResolved;
            _provider.MarkerLost += HandleMarkerLost;
        }

        private void HandleMarkerResolved(string rawId, Pose rawPose)
            => _stabilizer.Feed(rawId, rawPose, _deltaTime());

        /// <summary>
        /// 丢失即复位稳定器：否则走开再回来、位姿没变，
        /// <c>HasFiredStableEvent</c> 一直是 true，重新扫码不会再触发。
        /// </summary>
        private void HandleMarkerLost(string rawId) => _stabilizer.Reset(rawId);

        private void HandleStabilized(string rawId, Pose stablePose) => _onScan(rawId, stablePose);

        public void Dispose()
        {
            _provider.MarkerResolved -= HandleMarkerResolved;
            _provider.MarkerLost -= HandleMarkerLost;
            _stabilizer.Stabilized -= HandleStabilized;
        }
    }
}
#endif
