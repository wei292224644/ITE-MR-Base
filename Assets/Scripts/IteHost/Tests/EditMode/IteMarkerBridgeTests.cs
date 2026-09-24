using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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

        /// <summary>
        /// 一直盯着码看只扫一次（marker-rescan D1）。PICO 解出的位姿偶尔抖过防抖阈值，
        /// 旧规则把它当成「码移动了」，重新判稳后再提交一次（真机 2026-09-24：约 4 秒自动 Reanchor 一次）。
        /// </summary>
        [Test]
        public void ContinuouslyVisible_OccasionalJitterAboveThreshold_ForwardsOnce()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source);
            var forwarded = new List<(MarkerKind kind, string payload, Pose pose)>();

            using (var bridge = new IteMarkerBridge(session, Profile(), null,
                       (k, p, pose) => forwarded.Add((k, p, pose))))
            {
                for (int i = 0; i < 100; i++)
                {
                    // 每 2 秒抖一次 3 度（阈值 1 度），其余时间不动
                    float yaw = i > 0 && i % 20 == 0 ? 3f : 0f;
                    source.SetNextPoll(new[]
                    {
                        new MarkerObservation(MarkerPlatform.Pico, "0",
                            new Pose(Vector3.zero, Quaternion.Euler(0f, yaw, 0f)))
                    });
                    bridge.Tick(Dt);
                }
            }

            Assert.AreEqual(1, forwarded.Count, "持续可见期间的位姿抖动不算重扫");
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
        /// 丢失时长就是重扫门槛（marker-rescan D2）：移开视线不满丢失时长就看回来，算同一次出现；
        /// 满了再看回来，才算一次新的扫描。
        /// </summary>
        [Test]
        public void Rescan_RequiresLookingAwayForTheLostTime()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source, lostAfterSeconds: 3f);
            var forwarded = new List<(MarkerKind kind, string payload, Pose pose)>();
            var visible = new[] { new MarkerObservation(MarkerPlatform.Quest, QuestPayload, Pose.identity) };

            using (var bridge = new IteMarkerBridge(session, Profile(), null,
                       (k, p, pose) => forwarded.Add((k, p, pose))))
            {
                void Run(int ticks, bool seen)
                {
                    for (int i = 0; i < ticks; i++)
                    {
                        if (seen)
                        {
                            source.SetNextPoll(visible);
                        }
                        else
                        {
                            source.SetNextPollEmpty();
                        }

                        bridge.Tick(Dt);
                    }
                }

                Run(5, seen: true);
                Assert.AreEqual(1, forwarded.Count);

                Run(20, seen: false); // 移开 2 秒
                Run(5, seen: true);
                Assert.AreEqual(1, forwarded.Count, "移开不满 3 秒，算同一次出现");

                Run(31, seen: false); // 移开 3.1 秒
                Run(5, seen: true);
                Assert.AreEqual(2, forwarded.Count, "移开满 3 秒再看回来，算新的一次扫描");
            }
        }

        /// <summary>放行（marker-rescan D3）：ITE 在等第一次扫描时，视野里的码不必先移开视线。</summary>
        [Test]
        public void Rearm_WhileVisible_ForwardsAgain()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source, lostAfterSeconds: 3f);
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

                bridge.Rearm();
                for (int i = 0; i < 20; i++)
                {
                    bridge.Tick(Dt);
                }
            }

            Assert.AreEqual(2, forwarded.Count, "放行后重新判稳、再转发一次，之后仍然只算一次");
        }

        /// <summary>
        /// design D20：同一 tick 只提交一张码。落选的码重新判稳、之后单独提交（marker-rescan D7）——
        /// 判稳每次出现只发一次，丢掉它就要移开视线才能再扫；放行会让视野里的码同时判稳，落选是常态。
        /// </summary>
        [Test]
        public void TwoMarkersStabilizingInSameTick_OnePerTick_LoserFollowsLater()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source);
            var forwarded = new List<(string payload, int tick)>();
            int tick = 0;

            using (var bridge = new IteMarkerBridge(session, Profile(), null,
                       (k, p, pose) => forwarded.Add((p, tick))))
            {
                source.SetNextPoll(new[]
                {
                    new MarkerObservation(MarkerPlatform.Quest, "******a******", Pose.identity),
                    new MarkerObservation(MarkerPlatform.Quest, "******b******", Pose.identity),
                });

                for (tick = 0; tick < 10; tick++)
                {
                    bridge.Tick(Dt);
                }
            }

            Assert.AreEqual(2, forwarded.Count, "两张码都要提交");
            Assert.AreNotEqual(forwarded[0].payload, forwarded[1].payload);
            Assert.AreNotEqual(forwarded[0].tick, forwarded[1].tick, "同一 tick 内只提交一次");
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

        /// <summary>
        /// 稳定窗口是时间不是次数（design D22）：防抖要的是「这张码距上次观测过了多久」。
        /// PICO 每秒只出约 5.6 个检测结果，帧率 72——每次只记一帧的时长，0.3 秒窗口要攒约 22 次观测、
        /// 近 4 秒（真机 2026-09-24：连续识别 10～24 秒才判稳，Quest 只要 0.4 秒）。
        /// </summary>
        [Test]
        public void SlowSource_StableWindowIsWallClockTime()
        {
            const float frameDt = 1f / 72f;
            const int framesPerObservation = 13; // 72 / 13 ≈ 5.5 Hz
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source, lostAfterSeconds: 1.0f);
            float elapsed = 0f;
            float firstForwardAt = -1f;
            var observation = new[] { new MarkerObservation(MarkerPlatform.Pico, "0", Pose.identity) };

            using (var bridge = new IteMarkerBridge(session, Profile(), null,
                       (_, __, ___) => { if (firstForwardAt < 0f) firstForwardAt = elapsed; }))
            {
                for (int frame = 0; frame < 72 * 6; frame++)
                {
                    if (frame % framesPerObservation == 0)
                    {
                        source.SetNextPoll(observation);
                    }
                    else
                    {
                        source.SetNextPollEmpty();
                    }

                    elapsed += frameDt;
                    bridge.Tick(frameDt);
                }
            }

            Assert.That(firstForwardAt, Is.GreaterThan(0f), "慢速源也必须判稳");
            Assert.That(firstForwardAt, Is.LessThan(0.3f + 2f * framesPerObservation * frameDt),
                "应在稳定窗口加约一个观测间隔内判稳，而不是按帧数攒够");
        }

        /// <summary>
        /// 两次观测隔得久（还没到丢失）时，这段空档最多只算 0.25 秒稳定时长：
        /// 不能只看到两眼就判稳。
        /// </summary>
        [Test]
        public void SparseSightings_GapCreditIsCapped()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source, lostAfterSeconds: 1.0f);
            var forwarded = new List<(MarkerKind kind, string payload, Pose pose)>();
            var observation = new[] { new MarkerObservation(MarkerPlatform.Pico, "0", Pose.identity) };

            using (var bridge = new IteMarkerBridge(session, Profile(), null,
                       (k, p, pose) => forwarded.Add((k, p, pose))))
            {
                // 每 0.8 秒看到一次（低于 1 秒丢失阈值），每次只看到一帧
                for (int sighting = 0; sighting < 2; sighting++)
                {
                    source.SetNextPoll(observation);
                    bridge.Tick(0.01f);
                    source.SetNextPollEmpty();
                    bridge.Tick(0.79f);
                }

                Assert.AreEqual(0, forwarded.Count, "两眼、相隔 0.8 秒：空档只算 0.25 秒，不够 0.3 秒窗口");

                source.SetNextPoll(observation);
                bridge.Tick(0.01f);
            }

            Assert.AreEqual(1, forwarded.Count, "第三眼累计超过窗口，判稳");
        }

        /// <summary>
        /// 丢失与放行都要在真机日志里看得见：重扫门槛生不生效（例如 MRUK 离开视野后是否一直报在追踪），
        /// 只能靠这两行判断（marker-rescan D2、D3）。
        /// </summary>
        [Test]
        public void LostAndRearm_AreLogged()
        {
            var source = new MockObservationSource();
            var session = new MarkerTrackingSession(source, lostAfterSeconds: 1f);

            using (var bridge = new IteMarkerBridge(session, Profile(), null, (_, __, ___) => { }))
            {
                source.SetNextPoll(new[]
                {
                    new MarkerObservation(MarkerPlatform.Quest, QuestPayload, Pose.identity)
                });
                bridge.Tick(Dt);

                source.SetNextPollEmpty();
                LogAssert.Expect(LogType.Log, new Regex("标记丢失.*" + Regex.Escape(QuestPayload)));
                bridge.Tick(1.5f);

                LogAssert.Expect(LogType.Log, new Regex("放行"));
                bridge.Rearm();
            }
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
