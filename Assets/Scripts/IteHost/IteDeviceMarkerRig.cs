using System.Collections;
using UnityEngine;
using UnityEngine.XR;
#if MRBASE_PICO && MRBASE_HAS_PICO_SDK
using Unity.XR.PXR;
#endif

namespace MRBase.Ite.Host
{
    /// <summary>
    /// 真机上的输入层：取观测源、建会话、注入装配点，并承担三件只有头显上才存在的事——
    /// 触发体积的物理前提、应用生命周期、系统重定位。
    ///
    /// 与编辑器驱动层（<see cref="IteEditorFakeScan"/>）是同一位置的两种实现：
    /// 引用方向都是输入层 → 装配点，装配点对两者都无知。
    ///
    /// **不 Tick 会话** —— 那归桥接（<see cref="IteMarkerBridge.Tick"/>），
    /// 由装配点每帧推一次。这里再推一次就是一帧推两次。
    /// </summary>
    [AddComponentMenu("ITE/Device Marker Rig")]
    [DisallowMultipleComponent]
    public sealed class IteDeviceMarkerRig : MonoBehaviour
    {
        private const string LogPrefix = "[ITE Device]";

        [SerializeField]
        [Tooltip("装配点。留空则在本场景里找")]
        private IteHostBootstrap host;

        [SerializeField]
        [Tooltip("标记连续缺席多久算丢失")]
        private float lostAfterSeconds = 1.0f;

        [Header("触发体积的物理前提")]
        [SerializeField]
        [Tooltip("给相机挂的触发碰撞体半径（米）。太大容易提前触发，太小容易穿过去")]
        private float cameraColliderRadius = 0.15f;

        [Header("就绪等待")]
        [SerializeField]
        [Tooltip("等 XR 相机出现的秒数。超时视为装配错误，不静默等下去")]
        private float xrReadyTimeoutSeconds = 20f;

        private IMarkerObservationSource _source;
        private MarkerTrackingSession _session;

        /// <summary>只撤除自己挂上去的那两个组件，已存在的不动。</summary>
        private SphereCollider _addedCollider;
        private Rigidbody _addedRigidbody;
        private Transform _camera;

        private bool _recenterHooked;

        public MarkerTrackingSession Session => _session;

        private IEnumerator Start()
        {
            if (host == null)
            {
                host = FindFirstObjectByType<IteHostBootstrap>();
            }

            if (host == null)
            {
                Debug.LogError($"{LogPrefix} 场景里没有 IteHostBootstrap，导览无法装配。", this);
                enabled = false;
                yield break;
            }

            // XR 相机是加载链的前置：拿不到就不该进 IteRuntime.Create（design D15）。
            // 等，但**有期限** —— 无限重试会把「还在等」和「配错了」混成同一个现象。
            float deadline = Time.unscaledTime + xrReadyTimeoutSeconds;
            while (ResolveXrCamera() == null && Time.unscaledTime < deadline)
            {
                yield return null;
            }

            _camera = ResolveXrCamera();
            if (_camera == null)
            {
                Debug.LogError(
                    $"{LogPrefix} {xrReadyTimeoutSeconds} 秒内没等到 XR 相机（MRContext 未就绪或场景里没有 XR Origin）。", this);
                enabled = false;
                yield break;
            }

            AttachCameraTrigger(_camera);
            WarnIfLayersCannotCollide(_camera.gameObject.layer);

            if (!TryOpenSession())
            {
                // 观测源起不来不影响导览本身：内容照常加载、区域触发照常工作，
                // 只是扫码激活不可用。所以这里不 return——仍然启动加载链。
                Debug.LogWarning($"{LogPrefix} 标记输入不可用，导览将以「无扫码」状态运行。", this);
            }

            HookRecenter();

            // 加载链是 Task 不是协程，不能 yield 它。装配点内部自己处理失败与重复调用。
            _ = host.StartRuntimeAsync();
        }

        private static Transform ResolveXrCamera()
        {
            var context = MRContext.Instance;
            return context != null && context.Camera != null ? context.Camera.transform : null;
        }

        private bool TryOpenSession()
        {
            _source = MarkerSourceFactory.Create(gameObject, out var failure, out string detail);

            if (_source == null)
            {
                // 两类失败分开报：一类是构建配置错，一类是包没装（design D4）。
                Debug.LogError(
                    failure == MarkerSourceFactory.Failure.PlatformSdkMissing
                        ? $"{LogPrefix} 平台 SDK 未安装：{detail}"
                        : $"{LogPrefix} 平台未配置：{detail}", this);
                return false;
            }

            if (!string.IsNullOrEmpty(detail))
            {
                Debug.LogError($"{LogPrefix} {detail}", this);
            }

            _session = new MarkerTrackingSession(_source, lostAfterSeconds);

            try
            {
                _session.Open();
            }
            catch (System.Exception e)
            {
                // 第三条失败路径：源建出来了但打不开（相机被占、企业服务未授权……）。
                // 与上面两条分开报，否则真机上分不清该查哪里。
                Debug.LogError($"{LogPrefix} 观测源打开失败：{e.GetType().Name} {e.Message}", this);
                _session = null;
                return false;
            }

            host.AttachMarkerSession(_session);
            return true;
        }

        /// <summary>
        /// Unity 的触发回调要求参与的两个碰撞体中至少一个带 Rigidbody，而真机的 XR 相机上没有。
        /// 挂在这里而不是常驻 MRCore：MRCore 只承担 MR 基座行为，不该为 ITE 买单（design D3）。
        /// </summary>
        private void AttachCameraTrigger(Transform camera)
        {
            if (camera.GetComponent<Collider>() == null)
            {
                _addedCollider = camera.gameObject.AddComponent<SphereCollider>();
                _addedCollider.isTrigger = true;
                _addedCollider.radius = cameraColliderRadius;
            }

            if (camera.GetComponent<Rigidbody>() == null)
            {
                _addedRigidbody = camera.gameObject.AddComponent<Rigidbody>();
                _addedRigidbody.isKinematic = true;
                _addedRigidbody.useGravity = false;
            }
        }

        /// <summary>
        /// 相机所在 layer 与触发体积 layer 在碰撞矩阵里不互相碰撞时，区域触发永不产生事件
        /// 且不报任何错。必须出声。
        /// </summary>
        private void WarnIfLayersCannotCollide(int cameraLayer)
        {
            bool anyCollidable = false;
            for (int layer = 0; layer < 32; layer++)
            {
                if (!Physics.GetIgnoreLayerCollision(cameraLayer, layer))
                {
                    anyCollidable = true;
                    break;
                }
            }

            if (!anyCollidable)
            {
                Debug.LogError(
                    $"{LogPrefix} 相机所在 layer（{LayerMask.LayerToName(cameraLayer)}）与所有 layer 的碰撞都被关掉了，" +
                    "区域触发不会产生任何事件。检查 Project Settings > Physics 的碰撞矩阵。", this);
            }
        }

        /// <summary>
        /// 系统重定位改的是追踪空间原点：内容在世界坐标里没动，物理世界却整个转了过去，
        /// 上一次锚定不再可信（design D18）。语义与「戴上头显」一致：要求重扫。
        /// </summary>
        private void HookRecenter()
        {
#if MRBASE_PICO && MRBASE_HAS_PICO_SDK
            PXR_Plugin.System.RecenterSuccess += HandleRecentered;
            _recenterHooked = true;
#else
            var subsystem = GetInputSubsystem();
            if (subsystem != null)
            {
                subsystem.trackingOriginUpdated += HandleTrackingOriginUpdated;
                _recenterHooked = true;
            }

            // 加固：关掉「世界原点跟随系统重定位」。关掉之后这边基本不会再触发，语义仍成立。
            UnityEngine.XR.OpenXR.OpenXRSettings.SetAllowRecentering(false);
#endif
        }

        private void UnhookRecenter()
        {
            if (!_recenterHooked)
            {
                return;
            }

#if MRBASE_PICO && MRBASE_HAS_PICO_SDK
            PXR_Plugin.System.RecenterSuccess -= HandleRecentered;
#else
            var subsystem = GetInputSubsystem();
            if (subsystem != null)
            {
                subsystem.trackingOriginUpdated -= HandleTrackingOriginUpdated;
            }
#endif
            _recenterHooked = false;
        }

        private void HandleTrackingOriginUpdated(XRInputSubsystem _) => HandleRecentered();

        private void HandleRecentered()
        {
            Debug.Log($"{LogPrefix} 追踪原点变化，上一次锚定作废，要求重新扫码。", this);
            host?.Runtime?.RequireScan();
        }

        private static XRInputSubsystem GetInputSubsystem()
        {
            var subsystems = new System.Collections.Generic.List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            return subsystems.Count > 0 ? subsystems[0] : null;
        }

        /// <summary>
        /// 灭屏 / 摘下 / 切系统菜单都会走这里。暂停期间会话不派发也**不累计缺席时长**——
        /// 不接的话，恢复后会立刻收到一串虚假的 MarkerLost。
        /// </summary>
        private void OnApplicationPause(bool paused)
        {
            if (_session == null)
            {
                return;
            }

            if (paused)
            {
                _session.Pause();
                return;
            }

            _session.Resume();
        }

        private void OnDestroy()
        {
            UnhookRecenter();

            _session?.Close();
            _session = null;

            // 只撤自己挂的，已存在的组件不动。
            if (_addedCollider != null)
            {
                Destroy(_addedCollider);
            }

            if (_addedRigidbody != null)
            {
                Destroy(_addedRigidbody);
            }
        }
    }
}
