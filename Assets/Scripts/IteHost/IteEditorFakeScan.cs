using UnityEngine;

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
            for (int i = 0; i < tourIds.Length && i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    Trigger(tourIds[i]);
                }
            }

            Tick(Time.deltaTime);
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
