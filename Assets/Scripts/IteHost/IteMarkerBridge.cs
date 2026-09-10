using System;
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
            _session.Tick(deltaTime);
        }

        private void HandleObserved(MarkerObservation observation)
        {
            LastObservedRawPayload = observation.RawPayload;
            StabilizerFor(observation.Platform).Feed(observation.RawPayload, observation.Pose, _deltaTime);
        }

        private void HandleLost(MarkerPlatform platform, string rawPayload)
        {
            LastLostRawPayload = rawPayload;

            // 丢失即重置：下次再出现要重新走完稳定窗口才算新的一次扫码。
            // 这条同时让「二次锚定」回到人有意重扫的动作，而不是连续观测的第 2 帧。
            StabilizerFor(platform).Reset(rawPayload);
        }

        private void Submit(MarkerPlatform platform, string rawPayload, Pose stabilizedPose)
        {
            // 同一帧内多张码同时判稳时只认第一张（design D20）。不规定就是未定义行为：
            // 跟踪表是字典、迭代顺序不保证，而后到者会把先到者刚激活的 tour 停用销毁。
            if (_submittedThisTick)
            {
                Debug.Log(
                    "[ITE Host] 同帧已提交过扫码，忽略后到的标记：" + rawPayload +
                    "（先判稳的赢）");
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
