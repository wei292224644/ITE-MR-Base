using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MRBase.Ite.Host;

namespace MRBase.Ite.Host.Tests
{
    /// <summary>
    /// 剥壳是宿主侧唯一新增的纯逻辑：会话只给原始 payload，TourScanPolicy 做字面比对。
    /// </summary>
    public class IteMarkerBridgeTests
    {
        private const string DefaultPattern = @"^\*{6}(.*?)\*{6}$";

        [Test]
        public void Observed_CompliantPayload_ForwardsStrippedTourIdAndPose()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source);
            var forwarded = new List<(string id, Pose pose)>();
            var pose = new Pose(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 20f, 30f));

            using (new IteMarkerBridge(session, DefaultPattern, (id, p) => forwarded.Add((id, p))))
            {
                source.SetNextPoll(new[]
                {
                    new MarkerObservation(MarkerPlatform.Quest, "******wm0l5qcn_ibd******", pose)
                });
                session.Tick(0.1f);
            }

            Assert.AreEqual(1, forwarded.Count);
            Assert.AreEqual("wm0l5qcn_ibd", forwarded[0].id);
            Assert.AreEqual(pose.position, forwarded[0].pose.position);
            Assert.AreEqual(pose.rotation, forwarded[0].pose.rotation);
        }

        [Test]
        public void Observed_NonCompliantPayload_DoesNotForwardAndLogsRaw()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source);
            var forwarded = new List<(string id, Pose pose)>();

            LogAssert.Expect(LogType.Log, new Regex("250"));

            using (new IteMarkerBridge(session, DefaultPattern, (id, p) => forwarded.Add((id, p))))
            {
                source.SetNextPoll(new[]
                {
                    new MarkerObservation(MarkerPlatform.Quest, "250", Pose.identity)
                });
                session.Tick(0.1f);
            }

            CollectionAssert.IsEmpty(forwarded);
        }

        [Test]
        public void Observed_EmptyPayload_DoesNotForwardAndLogsRaw()
        {
            AssertIgnored("", "已忽略");
        }

        [Test]
        public void Observed_WrongStarCount_DoesNotForwardAndLogsRaw()
        {
            AssertIgnored("*****wm0l5qcn_ibd*****", @"\*{5}wm0l5qcn_ibd\*{5}");
        }

        [Test]
        public void Observed_EmptyInner_DoesNotForwardAndLogsRaw()
        {
            AssertIgnored("************", @"\*{12}");
        }

        [Test]
        public void Lost_RecordsPayloadAndDoesNotForward()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source, lostAfterSeconds: 1.0f);
            var forwarded = new List<(string id, Pose pose)>();
            const string payload = "******wm0l5qcn_ibd******";

            using (var bridge = new IteMarkerBridge(session, DefaultPattern, (id, p) => forwarded.Add((id, p))))
            {
                source.SetNextPoll(new[]
                {
                    new MarkerObservation(MarkerPlatform.Quest, payload, Pose.identity)
                });
                session.Tick(0.1f);

                source.SetNextPollEmpty();
                session.Tick(1.1f);

                Assert.AreEqual(1, forwarded.Count, "Lost 不得再向 ITE 转发");
                Assert.AreEqual(payload, bridge.LastLostRawPayload);
            }
        }

        static void AssertIgnored(string payload, string loggedFragment)
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source);
            var forwarded = new List<(string id, Pose pose)>();

            LogAssert.Expect(LogType.Log, new Regex(loggedFragment));

            using (new IteMarkerBridge(session, DefaultPattern, (id, p) => forwarded.Add((id, p))))
            {
                source.SetNextPoll(new[]
                {
                    new MarkerObservation(MarkerPlatform.Quest, payload, Pose.identity)
                });
                session.Tick(0.1f);
            }

            CollectionAssert.IsEmpty(forwarded);
        }
    }
}
