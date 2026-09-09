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
        [SerializeField] IteHostBootstrap host;
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
        [SerializeField] float feedSeconds = 0.35f;
        [SerializeField] float lostAfterSeconds = 1f;

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

            _source = new MockObservationSource();
            _session = new MarkerTrackingSession(_source, lostAfterSeconds);
            _session.Open();
            if (host != null)
            {
                host.AttachMarkerSession(_session);
            }
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

        public void Tick(float deltaTime)
        {
            if (_session == null)
            {
                return;
            }

            if (_feedRemaining > 0f)
            {
                _feedRemaining -= deltaTime;
                if (_feedRemaining <= 0f)
                {
                    _source.SetNextPollEmpty();
                }
            }

            _session.Tick(deltaTime);
        }

        private void OnDestroy()
        {
            _session?.Close();
        }
    }
}
