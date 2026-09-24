// 桌面验收脚手架：只在 Editor 里存在，不进设备包。
//
// 为什么是 #if UNITY_EDITOR 而不是 Editor-only 的 asmdef：Unity 不允许把 Editor 程序集里的
// MonoBehaviour 挂到 GameObject 上（AddComponent 直接返回 null，场景里的引用变成 Missing）。
// 条件编译能达到同样的目的——类型在播放器构建里根本不存在——而场景与预制体的引用不受影响。
#if UNITY_EDITOR
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MRBase.Ite.Host
{
    /// <summary>
    /// 编辑器假扫码：驱动层构造会话并注入装配点。按 1–5 数字键各扫一个 thirdDemo tour。
    /// </summary>
    [AddComponentMenu("ITE/Editor Fake Scan")]
    public sealed class IteEditorFakeScan : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("装配点。留空则在本场景里找")]
        IteHostBootstrap host;
        [SerializeField] Transform cameraTransform;
        [SerializeField]
        string[] tourIds =
        {
            "wm0l5qcn_ibd",
            "4kvhqwvp_12f",
            "earyserh_i5x",
            "azdugaax_xry",
            "hkdaowxy_0hu",
        };
        [SerializeField] float markerDistance = EditorFakeScan.DefaultDistance;

        [SerializeField]
        [Tooltip("一次触发持续投喂多久。必须大于 MarkerStabilizerProfile 的 stableSeconds，否则永远判不稳")]
        float feedSeconds = 1f;

        private MockObservationSource _source;
        private MarkerTrackingSession _session;
        private float _feedRemaining;

        public MarkerTrackingSession Session
        {
            get
            {
                EnsureSession();
                return _session;
            }
        }

        public string LastTriggeredTourId { get; private set; }

        private void Awake() => EnsureSession();

        private void EnsureSession()
        {
            if (_session != null)
            {
                return;
            }

            // 与 IteDeviceMarkerRig 同一套：连线为空就自己找，找不到出声。
            // 这里的连线跨预制体（本组件在 harness 预制体里，装配点在 IteTourRig 里），
            // 预制体资产存不了场景引用——没有兜底时，把 harness 拖进新场景会得到
            // 「按键有响应、会话在跑、就是没人接」的静默失效。
            if (host == null)
            {
                host = FindFirstObjectByType<IteHostBootstrap>();
            }

            // 丢失时长由装配点统一提供（marker-rescan D2）。没有装配点时假扫码不驱动任何导览，按默认值建会话即可。
            _source = new MockObservationSource();
            _session = new MarkerTrackingSession(
                _source,
                host != null ? host.MarkerLostAfterSeconds : MarkerStabilizerProfile.DefaultLostAfterSeconds);
            _session.Open();

            if (host == null)
            {
                Debug.LogError("[ITE Editor] 场景里没有 IteHostBootstrap，假扫码不会驱动任何导览。", this);
                return;
            }

            host.AttachMarkerSession(_session);
        }

        private void Update()
        {
            TryTriggerFromKeyboard();
            Tick(Time.deltaTime);
        }

        void TryTriggerFromKeyboard()
        {
            if (tourIds == null)
            {
                return;
            }

            for (int i = 0; i < tourIds.Length && i < 9; i++)
            {
                if (WasDigitPressedThisFrame(i + 1))
                {
                    Trigger(tourIds[i]);
                }
            }
        }

        static bool WasDigitPressedThisFrame(int digit)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            switch (digit)
            {
                case 1: return keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame;
                case 2: return keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame;
                case 3: return keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame;
                case 4: return keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame;
                case 5: return keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame;
                case 6: return keyboard.digit6Key.wasPressedThisFrame || keyboard.numpad6Key.wasPressedThisFrame;
                case 7: return keyboard.digit7Key.wasPressedThisFrame || keyboard.numpad7Key.wasPressedThisFrame;
                case 8: return keyboard.digit8Key.wasPressedThisFrame || keyboard.numpad8Key.wasPressedThisFrame;
                case 9: return keyboard.digit9Key.wasPressedThisFrame || keyboard.numpad9Key.wasPressedThisFrame;
                default: return false;
            }
#else
            if (digit < 1 || digit > 9)
            {
                return false;
            }

            return Input.GetKeyDown(KeyCode.Alpha1 + (digit - 1))
                || Input.GetKeyDown(KeyCode.Keypad1 + (digit - 1));
#endif
        }

        public void Trigger(string tourId)
        {
            EnsureSession();
            var cam = cameraTransform != null ? cameraTransform : transform;
            var pose = EditorFakeScan.PoseInFront(cam.position, cam.rotation, markerDistance);
            var observation = new MarkerObservation(
                MarkerPlatform.Quest,
                EditorFakeScan.WrapPayload(tourId),
                pose);
            _source.SetNextPoll(new[] { observation });
            _feedRemaining = feedSeconds;
            LastTriggeredTourId = tourId;
        }

        /// <summary>
        /// 只管投喂窗口，**不推进会话**。会话由 <see cref="IteHostBootstrap"/> 经桥接每帧推一次；
        /// 这里再推一次就是一帧推两次，滞回与稳定窗口都会走快一倍。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_feedRemaining <= 0f)
            {
                return;
            }

            _feedRemaining -= deltaTime;
            if (_feedRemaining <= 0f)
            {
                _source.SetNextPollEmpty();
            }
        }

        private void OnDestroy()
        {
            _session?.Close();
        }
    }
}
#endif
