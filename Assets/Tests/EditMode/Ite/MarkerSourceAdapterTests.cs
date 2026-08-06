using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MRBase.Ite.Host;

namespace MRBase.Ite.Host.Tests
{
    /// <summary>
    /// 标记源到 ITE 包的转发。
    ///
    /// 走 <see cref="MarkerStabilizer"/> 而不是把原始事件直接怼进去：`MarkerResolved`
    /// 在标记可见期间**每帧都发**，而 ITE 拿这个位姿去摆整个 Tour——抖动的位姿等于抖动的
    /// 导览内容。稳定器天然「每次稳定检出只发一次」，正是包要的粒度。
    /// </summary>
    public class MarkerSourceAdapterTests
    {
        private const float Frame = 1f / 60f;

        [Test]
        public void StableDetection_ForwardsExactlyOnce()
        {
            var provider = new MockMarkerProvider();
            var scans = new List<string>();
            var stabilizer = new MarkerStabilizer(stableFrameThreshold: 3);

            using (new MarkerSourceAdapter(provider, (id, pose) => scans.Add(id), stabilizer, () => Frame))
            {
                for (int i = 0; i < 10; i++)
                {
                    provider.SimulateMarkerResolved("marker-a", Pose.identity);
                }
            }

            CollectionAssert.AreEqual(new[] { "marker-a" }, scans);
        }

        /// <summary>
        /// 走开再回来必须能重新扫上。稳定器的 <c>HasFiredStableEvent</c> 只在**位姿变动**时
        /// 才复位，站回原处不动就永远不再触发——所以丢失时要显式 <c>Reset</c>。
        /// </summary>
        [Test]
        public void MarkerLostThenFoundAgain_ForwardsAgain()
        {
            var provider = new MockMarkerProvider();
            var scans = new List<string>();
            var stabilizer = new MarkerStabilizer(stableFrameThreshold: 3);

            using (new MarkerSourceAdapter(provider, (id, pose) => scans.Add(id), stabilizer, () => Frame))
            {
                for (int i = 0; i < 5; i++) provider.SimulateMarkerResolved("marker-a", Pose.identity);

                provider.SimulateMarkerLost("marker-a");

                // 位姿完全没变，靠 Reset 才能再次成立
                for (int i = 0; i < 5; i++) provider.SimulateMarkerResolved("marker-a", Pose.identity);
            }

            CollectionAssert.AreEqual(new[] { "marker-a", "marker-a" }, scans);
        }
    }
}
