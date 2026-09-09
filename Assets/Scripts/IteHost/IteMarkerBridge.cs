using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MRBase.Ite.Host
{
    /// <summary>
    /// 把 <see cref="MarkerTrackingSession"/> 的原始 payload 剥壳成 tourId，再推给 ITE。
    /// 纯 C#、非 MonoBehaviour——剥壳是纯逻辑，必须能在 EditMode 穷举（design D3）。
    /// </summary>
    public sealed class IteMarkerBridge : IDisposable
    {
        private readonly MarkerTrackingSession _session;
        private readonly Regex _shell;
        private readonly Action<string, Pose> _onScan;

        /// <summary>最近一次观测的原始 payload，供 HUD 显示。尚未观测则为 null。</summary>
        public string LastObservedRawPayload { get; private set; }

        /// <summary>最近一次丢失的原始 payload，供 HUD 显示。未丢失过则为 null。</summary>
        public string LastLostRawPayload { get; private set; }

        public IteMarkerBridge(
            MarkerTrackingSession session,
            string payloadPattern,
            Action<string, Pose> onScan)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _onScan = onScan ?? throw new ArgumentNullException(nameof(onScan));
            _shell = new Regex(payloadPattern ?? throw new ArgumentNullException(nameof(payloadPattern)));

            _session.MarkerObserved += HandleObserved;
            _session.MarkerLost += HandleLost;
        }

        private void HandleObserved(MarkerObservation observation)
        {
            LastObservedRawPayload = observation.RawPayload;
            var match = _shell.Match(observation.RawPayload ?? string.Empty);
            if (!match.Success || match.Groups.Count < 2)
            {
                Ignore(observation.RawPayload);
                return;
            }

            var tourId = match.Groups[1].Value;
            if (string.IsNullOrEmpty(tourId))
            {
                Ignore(observation.RawPayload);
                return;
            }

            _onScan(tourId, observation.Pose);
        }

        private void HandleLost(MarkerPlatform platform, string rawPayload)
        {
            LastLostRawPayload = rawPayload;
        }

        private static void Ignore(string rawPayload)
        {
            Debug.Log("[ITE Host] 标记 payload 不匹配外壳格式，已忽略：" + rawPayload);
        }

        public void Dispose()
        {
            _session.MarkerObserved -= HandleObserved;
            _session.MarkerLost -= HandleLost;
        }
    }
}
