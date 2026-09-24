using System;
using System.Collections.Generic;
using UnityEngine;
using Uality.IteTour.Core;

namespace MRBase.Ite.Host
{
    /// <summary>
    /// 会话与 ITE 之间的桥。只做三件事（design D5）：
    ///
    /// 1. **稳定** —— 观测喂 <see cref="MarkerStabilizer"/>，丢失时重置该标记；
    /// 2. **偏移** —— 对判稳后的位姿施加标记局部坐标系到内容锚点的固定偏移；
    /// 3. **透传** —— 把原始 payload、标记种类与位姿原样推给包。
    ///
    /// **不解析 payload。** 那归包所有：payload 的形状由内容方定义且会变（预期会变成
    /// 一个地址），焊在宿主意味着内容每改一次码，宿主就要出一次包。所以这里没有任何
    /// 外壳格式、正则或标记编号表。
    ///
    /// 非 MonoBehaviour：稳定与去重都是纯逻辑，必须能在 EditMode 穷举。
    /// 时间由 <see cref="Tick"/> 注入。
    /// </summary>
    public sealed class IteMarkerBridge : IDisposable
    {
        private readonly MarkerTrackingSession _session;
        private readonly Action<MarkerKind, string, Pose> _onScan;
        private readonly MarkerStabilizer _questStabilizer;
        private readonly MarkerStabilizer _picoStabilizer;
        private readonly Pose _questOffset;
        private readonly Pose _picoOffset;

        /// <summary>本 tick 是否已经提交过一次扫码（design D20：先判稳的赢）。</summary>
        private bool _submittedThisTick;

        /// <summary>本 tick 的时长，由 <see cref="Tick"/> 注入后供观测回调取用。</summary>
        private float _deltaTime;

        /// <summary>
        /// 桥接自己的时钟（<see cref="Tick"/> 累加）与每张码上次被观测的时刻。防抖要的是「这张码距上次
        /// 观测过了多久」（<see cref="MarkerStabilizer"/> 的约定，design D22），不是帧长：PICO 每秒只出约
        /// 5.6 个检测结果，按帧长记，0.5 秒窗口要攒 36～45 次观测（真机 2026-09-24 连续识别 10～24 秒才判稳）。
        /// </summary>
        private float _clock;
        private readonly Dictionary<(MarkerPlatform, string), float> _lastSeenAt =
            new Dictionary<(MarkerPlatform, string), float>();

        /// <summary>
        /// 两次观测之间的空档最多算这么长的稳定时长：没丢失（会话丢失时长默认 3 秒）但隔得久时，
        /// 不能只看到两三眼就判稳。取值高于 PICO 的观测间隔（约 0.18 秒），不影响正常连续识别。
        /// </summary>
        private const float MaxGapCreditSeconds = 0.25f;

        /// <summary>最近一次观测的原始 payload，供 HUD 显示。尚未观测则为 null。</summary>
        public string LastObservedRawPayload { get; private set; }

        /// <summary>最近一次丢失的原始 payload，供 HUD 显示。未丢失过则为 null。</summary>
        public string LastLostRawPayload { get; private set; }

        /// <summary>最近一次真正提交给包的 payload。未提交过则为 null。</summary>
        public string LastSubmittedRawPayload { get; private set; }

        public IteMarkerBridge(
            MarkerTrackingSession session,
            MarkerStabilizerProfile profile,
            PlatformOffsetConfig offsets,
            Action<MarkerKind, string, Pose> onScan)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _onScan = onScan ?? throw new ArgumentNullException(nameof(onScan));

            // 配置资产没接上时退回出厂参数：那不该让扫码整条链不工作。
            var questSettings = profile != null ? profile.quest : DefaultSettings();
            var picoSettings = profile != null ? profile.pico : DefaultSettings();
            _questStabilizer = questSettings.CreateStabilizer();
            _picoStabilizer = picoSettings.CreateStabilizer();

            _questOffset = offsets != null ? offsets.questMarkerToTargetOffset : Pose.identity;
            _picoOffset = offsets != null ? offsets.picoMarkerToTargetOffset : Pose.identity;

            _questStabilizer.Stabilized += (rawPayload, pose) => Submit(MarkerPlatform.Quest, rawPayload, pose);
            _picoStabilizer.Stabilized += (rawPayload, pose) => Submit(MarkerPlatform.Pico, rawPayload, pose);

            _session.MarkerObserved += HandleObserved;
            _session.MarkerLost += HandleLost;
        }

        private static MarkerStabilizerProfile.Settings DefaultSettings() => new MarkerStabilizerProfile.Settings
        {
            positionThreshold = 0.05f,
            rotationThreshold = 1f,
            smoothTime = 0.2f,
            stableSeconds = 0.5f,
        };

        /// <summary>
        /// 每帧一次，由装配点驱动。**它是会话唯一的推进点** —— 输入层不再自己 Tick，
        /// 否则一帧推两次，滞回与稳定窗口都会走快一倍。
        ///
        /// 顺序是刻意的：先清本帧的提交标志，再推进会话（观测在这中间派发出来）。
        /// </summary>
        public void Tick(float deltaTime)
        {
            _submittedThisTick = false;
            _deltaTime = deltaTime;
            _clock += deltaTime;
            _session.Tick(deltaTime);
        }

        /// <summary>
        /// 放行：视野里的码都当作新的一次出现，重新平滑、重新判稳（marker-rescan D3）。
        /// 宿主在 ITE 发出扫码提示时调用——那是需要第一次扫描的时候，码一直在视野里也要能扫上。
        /// </summary>
        public void Rearm()
        {
            _questStabilizer.ResetAll();
            _picoStabilizer.ResetAll();
        }

        private void HandleObserved(MarkerObservation observation)
        {
            LastObservedRawPayload = observation.RawPayload;

            // 首次观测没有「上次」，记一帧，与每帧都有观测的 Quest 一致
            var key = (observation.Platform, observation.RawPayload);
            float sinceLast = _lastSeenAt.TryGetValue(key, out float lastSeenAt) ? _clock - lastSeenAt : _deltaTime;
            _lastSeenAt[key] = _clock;

            StabilizerFor(observation.Platform).Feed(
                observation.RawPayload, observation.Pose, Mathf.Min(sinceLast, MaxGapCreditSeconds));
        }

        private void HandleLost(MarkerPlatform platform, string rawPayload)
        {
            LastLostRawPayload = rawPayload;
            _lastSeenAt.Remove((platform, rawPayload));

            // 丢失即重置：下次再出现要重新走完稳定窗口才算新的一次扫码。
            // 丢失时长就是重扫门槛（marker-rescan D2）：移开视线够久再看回来，才是人有意重扫。
            StabilizerFor(platform).Reset(rawPayload);
        }

        private void Submit(MarkerPlatform platform, string rawPayload, Pose stabilizedPose)
        {
            // 同一帧内多张码同时判稳时只提交第一张（design D20）。不规定就是未定义行为：
            // 跟踪表是字典、迭代顺序不保证。
            //
            // 落选的码重新判稳、之后单独提交（marker-rescan D7）：判稳每次出现只发一次（D1），
            // 丢掉它就要移开视线才能再扫。后到的码不会停掉先到者刚激活的 Tour——已定位后
            // ITE 只认当前 Tour 的码（ite-current-tour D6）。
            if (_submittedThisTick)
            {
                StabilizerFor(platform).Reset(rawPayload);
                Debug.Log("[ITE Host] 同帧已提交过扫码，" + rawPayload + " 重新判稳后再提交");
                return;
            }

            _submittedThisTick = true;
            LastSubmittedRawPayload = rawPayload;

            var pose = PoseMath.Compose(stabilizedPose, OffsetFor(platform));
            _onScan(KindFor(platform), rawPayload, pose);
        }

        private MarkerStabilizer StabilizerFor(MarkerPlatform platform)
            => platform == MarkerPlatform.Pico ? _picoStabilizer : _questStabilizer;

        private Pose OffsetFor(MarkerPlatform platform)
            => platform == MarkerPlatform.Pico ? _picoOffset : _questOffset;

        /// <summary>
        /// 平台 → payload 形状。这是宿主侧仅剩的一处「平台知识」，且它只回答
        /// 「这串东西是什么形状」，不回答「它是谁」——后者归包。
        /// </summary>
        private static MarkerKind KindFor(MarkerPlatform platform)
            => platform == MarkerPlatform.Pico ? MarkerKind.AprilTagId : MarkerKind.QrText;

        public void Dispose()
        {
            _session.MarkerObserved -= HandleObserved;
            _session.MarkerLost -= HandleLost;
        }
    }
}
