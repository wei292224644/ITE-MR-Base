using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MRBase.Ite.Host;

namespace MRBase.Ite.Host.Tests
{
    /// <summary>
    /// 佩戴状态只在**边沿**推给包：源实现每帧无条件调，包侧一次摘下会反复停用当前 Tour。
    /// 「平台不上报」是 PICO 上的真实风险（design D7），必须退化成「已佩戴」而不是挡住导览。
    /// </summary>
    public class HeadsetPresenceAdapterTests
    {
        [Test]
        public void Poll_OnlyEmitsOnStateChange()
        {
            bool? reading = true;
            var emitted = new List<bool>();
            var adapter = new HeadsetPresenceAdapter(emitted.Add, () => reading);

            adapter.Poll();            // 初始就是已佩戴，无变化
            adapter.Poll();

            reading = false;
            adapter.Poll();
            adapter.Poll();            // 仍是摘下，无变化

            reading = true;
            adapter.Poll();

            CollectionAssert.AreEqual(new[] { false, true }, emitted);
        }

        /// <summary>
        /// 「读不到佩戴状态」绝不能退化成「摘下了」——那会把整个导览挡住。
        /// 警告只发一次，否则每帧一条刷爆日志。
        /// </summary>
        [Test]
        public void Poll_WhenPlatformDoesNotReport_KeepsMountedAndWarnsOnce()
        {
            var emitted = new List<bool>();
            var adapter = new HeadsetPresenceAdapter(emitted.Add, () => null);

            LogAssert.Expect(LogType.Warning, new Regex("userPresence"));

            adapter.Poll();
            adapter.Poll();
            adapter.Poll();

            CollectionAssert.IsEmpty(emitted);
            Assert.IsTrue(adapter.IsMounted);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
