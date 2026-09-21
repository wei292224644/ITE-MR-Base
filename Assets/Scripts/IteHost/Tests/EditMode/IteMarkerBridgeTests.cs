using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Uality.IteTour.Core;

namespace MRBase.Ite.Host.Tests
{
    /// <summary>
    /// 桥接只做三件事（design D5）：稳定、施加偏移、原样透传。
    /// **它不再解析 payload** —— 解析归包，那部分用例在 `MarkerIdentityTests`。
    /// </summary>
    public class IteMarkerBridgeTests
    {
        private const string QuestPayload = "******wm0l5qcn_ibd******";
        private const float Dt = 0.1f;

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _created.Clear();
        }

        /// <summary>稳定窗口 0.3 s、无平滑滞后干扰阈值判定的一组测试参数。</summary>
        private MarkerStabilizerProfile Profile()
        {
            var profile = ScriptableObject.CreateInstance<MarkerStabilizerProfile>();
            _created.Add(profile);
            var settings = new MarkerStabilizerProfile.Settings
            {
                positionThreshold = 0.05f,
                rotationThreshold = 1f,
                smoothTime = 0.2f,
                stableSeconds = 0.3f,
            };
            profile.quest = settings;
            profile.pico = settings;
            return profile;
        }

        private PlatformOffsetConfig Offsets(Pose quest, Pose pico)
        {
            var config = ScriptableObject.CreateInstance<PlatformOffsetConfig>();
            _created.Add(config);
            config.questMarkerToTargetOffset = quest;
            config.picoMarkerToTargetOffset = pico;
            return config;
        }

        [Test]
        public void ContinuouslyVisible_ForwardsExactlyOnce()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source);
            var forwarded = new List<(MarkerKind kind, string payload, Pose pose)>();

            using (var bridge = new IteMarkerBridge(session, Profile(), null,
                       (k, p, pose) => forwarded.Add((k, p, pose))))
            {
                source.SetNextPoll(new[]
                {
                    new MarkerObservation(MarkerPlatform.Quest, QuestPayload, Pose.identity)
                });

                for (int i = 0; i < 20; i++)
                {
                    bridge.Tick(Dt);
                }
            }

            Assert.AreEqual(1, forwarded.Count, "持续可见期间只算一次扫码");
            Assert.AreEqual(QuestPayload, forwarded[0].payload, "payload 原样透传，不剥壳");
        }

        [Test]
        public void LostThenSeenAgain_ForwardsASecondTime()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source, lostAfterSeconds: 1.0f);
            var forwarded = new List<(MarkerKind kind, string payload, Pose pose)>();

            using (var bridge = new IteMarkerBridge(session, Profile(), null,
                       (k, p, pose) => forwarded.Add((k, p, pose))))
            {
                source.SetNextPoll(new[]
                {
                    new MarkerObservation(MarkerPlatform.Quest, QuestPayload, Pose.identity)
                });
                for (int i = 0; i < 5; i++)
                {
                    bridge.Tick(Dt);
                }

                Assert.AreEqual(1, forwarded.Count);

                source.SetNextPollEmpty();
                bridge.Tick(1.5f); // 超过滞回 → Lost → 稳定状态被重置

                source.SetNextPoll(new[]
                {
                    new MarkerObservation(MarkerPlatform.Quest, QuestPayload, Pose.identity)
                });
                for (int i = 0; i < 5; i++)
                {
                    bridge.Tick(Dt);
                }
            }

            Assert.AreEqual(2, forwarded.Count, "丢失后重新扫到才算新的一次");
        }

        /// <summary>
        /// design D20：同帧多张码只认最先判稳的那张。不规定就是未定义行为——
        /// 跟踪表是字典、迭代顺序不保证，而后到者会把先到者刚激活的 tour 停用销毁。
        /// </summary>
        [Test]
        public void TwoMarkersStabilizingInSameTick_OnlyFirstIsForwarded()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source);
            var forwarded = new List<(MarkerKind kind, string payload, Pose pose)>();

            using (var bridge = new IteMarkerBridge(session, Profile(), null,
                       (k, p, pose) => forwarded.Add((k, p, pose))))
            {
                source.SetNextPoll(new[]
                {
                    new MarkerObservation(MarkerPlatform.Quest, "******a******", Pose.identity),
                    new MarkerObservation(MarkerPlatform.Quest, "******b******", Pose.identity),
                });

                for (int i = 0; i < 10; i++)
                {
                    bridge.Tick(Dt);
                }
            }

            Assert.AreEqual(1, forwarded.Count, "同一 tick 内只提交一次");
        }

        [Test]
        public void PlatformMapsToPayloadShape()
        {
            var forwarded = new List<(MarkerKind kind, string payload, Pose pose)>();

            RunSingle(MarkerPlatform.Quest, QuestPayload, Pose.identity, null, forwarded);
            RunSingle(MarkerPlatform.Pico, "250", Pose.identity, null, forwarded);

            Assert.AreEqual(MarkerKind.QrText, forwarded[0].kind);
            Assert.AreEqual(MarkerKind.AprilTagId, forwarded[1].kind);
        }

        [Test]
        public void Offset_IsAppliedInMarkerLocalSpace()
        {
            var forwarded = new List<(MarkerKind kind, string payload, Pose pose)>();
            var offset = new Pose(new Vector3(0f, 0.25f, 0f), Quaternion.identity);
            var markerPose = new Pose(new Vector3(1f, 0f, 0f), Quaternion.identity);

            RunSingle(MarkerPlatform.Quest, QuestPayload, markerPose,
                Offsets(offset, Pose.identity), forwarded);

            Assert.AreEqual(1, forwarded.Count);
            Assert.That(forwarded[0].pose.position.y, Is.EqualTo(0.25f).Within(1e-3f));
            Assert.That(forwarded[0].pose.position.x, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void IdentityOffset_LeavesPoseUnchanged()
        {
            var forwarded = new List<(MarkerKind kind, string payload, Pose pose)>();
            var markerPose = new Pose(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 30f, 0f));

            RunSingle(MarkerPlatform.Quest, QuestPayload, markerPose,
                Offsets(Pose.identity, Pose.identity), forwarded);

            Assert.That(Vector3.Distance(forwarded[0].pose.position, markerPose.position),
                Is.LessThan(0.01f), "平滑收敛后应当落在标记位姿上");
        }

        /// <summary>桥接是会话唯一的推进点——输入层再 Tick 一次就是一帧推两次。</summary>
        [Test]
        public void Tick_AdvancesTheSessionExactlyOncePerCall()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source);

            using (var bridge = new IteMarkerBridge(session, Profile(), null, (_, __, ___) => { }))
            {
                bridge.Tick(Dt);
                bridge.Tick(Dt);
                bridge.Tick(Dt);
            }

            Assert.AreEqual(3, source.PollCount);
        }

        private void RunSingle(
            MarkerPlatform platform,
            string payload,
            Pose pose,
            PlatformOffsetConfig offsets,
            List<(MarkerKind kind, string payload, Pose pose)> sink)
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source);

            using (var bridge = new IteMarkerBridge(session, Profile(), offsets,
                       (k, p, resultPose) => sink.Add((k, p, resultPose))))
            {
                source.SetNextPoll(new[] { new MarkerObservation(platform, payload, pose) });
                for (int i = 0; i < 40; i++)
                {
                    bridge.Tick(Dt);
                }
            }
        }
    }
}
