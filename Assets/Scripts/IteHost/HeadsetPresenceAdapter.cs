using System;
using UnityEngine;
using UnityEngine.XR;

namespace MRBase.Ite.Host
{
    /// <summary>
    /// 把头显佩戴状态推给 ITE 包。**只在状态边沿推**——包侧收到「摘下」会停用当前 Tour，
    /// 每帧无条件推等于每帧停用一次。
    ///
    /// 不是 MonoBehaviour：它只需要有人每帧调一次 <see cref="Poll"/>，
    /// 由 <see cref="IteHostBootstrap"/> 驱动。这样场景里少一个组件，也能离机测。
    /// </summary>
    public class HeadsetPresenceAdapter
    {
        private readonly Action<bool> _onMountedChanged;
        private readonly Func<bool?> _readPresence;

        /// <summary>初值为「已佩戴」：冷启动时用户手上就戴着头显，这是正常起点。</summary>
        private bool _mounted = true;

        private bool _warnedUnsupported;

        /// <param name="readPresence">
        /// 返回 null 表示平台不上报佩戴状态。缺省读 HMD 节点的 <c>userPresence</c>。
        /// </param>
        public HeadsetPresenceAdapter(Action<bool> onMountedChanged, Func<bool?> readPresence = null)
        {
            _onMountedChanged = onMountedChanged ?? throw new ArgumentNullException(nameof(onMountedChanged));
            _readPresence = readPresence ?? ReadUserPresence;
        }

        public bool IsMounted => _mounted;

        /// <summary>每帧调一次。</summary>
        public void Poll()
        {
            var reading = _readPresence();

            // 平台不上报：保持「已佩戴」，导览照常。PICO 上这条是有实证风险的（design D7），
            // 但「读不到」绝不该退化成「摘下了」——那会把整个导览挡住。
            if (reading == null)
            {
                if (!_warnedUnsupported)
                {
                    _warnedUnsupported = true;
                    Debug.LogWarning("[ITE Host] 当前平台不上报 userPresence，佩戴状态按「已佩戴」处理");
                }

                return;
            }

            if (reading.Value == _mounted)
            {
                return;
            }

            _mounted = reading.Value;
            _onMountedChanged(_mounted);
        }

        /// <summary>读 HMD 节点的 <c>userPresence</c>。设备无效或不支持该特性时返回 null。</summary>
        public static bool? ReadUserPresence()
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid)
            {
                return null;
            }

            bool present;
            return head.TryGetFeatureValue(CommonUsages.userPresence, out present) ? present : (bool?)null;
        }
    }
}
