using System;
using UnityEngine;
using UnityEngine.Events;

namespace MRBase.Transitions
{
    /// <summary>
    /// Drives every MRBase/Ground Up Reveal material with one set of global shader values.
    /// The completion callbacks are the safe hand-off point for disabling passthrough.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class GroundUpRevealController : MonoBehaviour
    {
        static readonly int TransitionEnabledId = Shader.PropertyToID("_MRVT_TransitionEnabled");
        static readonly int RevealHeightId = Shader.PropertyToID("_MRVT_RevealHeight");
        static readonly int NoiseScaleId = Shader.PropertyToID("_MRVT_NoiseScale");
        static readonly int NoiseStrengthId = Shader.PropertyToID("_MRVT_NoiseStrength");
        static readonly int EdgeWidthId = Shader.PropertyToID("_MRVT_EdgeWidth");
        static readonly int EdgeColorId = Shader.PropertyToID("_MRVT_EdgeColor");
        static readonly int EdgeIntensityId = Shader.PropertyToID("_MRVT_EdgeIntensity");
        static readonly int GridScaleId = Shader.PropertyToID("_MRVT_GridScale");
        static readonly int GridWidthId = Shader.PropertyToID("_MRVT_GridWidth");
        static readonly int GridIntensityId = Shader.PropertyToID("_MRVT_GridIntensity");
        static readonly int TransitionTimeId = Shader.PropertyToID("_MRVT_Time");

        static GroundUpRevealController s_Owner;

        [Header("Sequence")]
        [SerializeField, Min(0.01f)] float duration = 4f;
        [SerializeField] AnimationCurve revealCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] bool playOnEnable;
        [SerializeField] bool useUnscaledTime = true;

        [Header("World-space range")]
        [Tooltip("Use this Transform's world Y as the virtual floor height.")]
        [SerializeField] bool useTransformAsGround = true;
        [SerializeField] float groundHeight;
        [Tooltip("Vertical distance, in metres, that must be fully reconstructed.")]
        [SerializeField, Min(0.01f)] float worldHeight = 6f;

        [Header("Reconstruction front")]
        [SerializeField, Min(0.01f)] float noiseScale = 0.65f;
        [SerializeField, Min(0f)] float noiseStrength = 0.22f;
        [SerializeField, Min(0.001f)] float edgeWidth = 0.18f;
        [SerializeField, ColorUsage(false, true)] Color edgeColor = new(0.08f, 0.75f, 1f, 1f);
        [SerializeField, Min(0f)] float edgeIntensity = 4f;

        [Header("Reconstruction grid")]
        [SerializeField, Min(0.01f)] float gridScale = 2f;
        [Tooltip("Grid line thickness in approximately screen-space pixels.")]
        [SerializeField, Range(0.25f, 4f)] float gridWidth = 1.15f;
        [SerializeField, Min(0f)] float gridIntensity = 1.75f;

        [Header("Editor preview")]
        [SerializeField, Range(0f, 1f)] float previewProgress = 1f;
        [SerializeField] bool previewInEditMode;

        [Header("Events")]
        [Tooltip("Invoked after the world is fully opaque. Disable passthrough from this callback.")]
        [SerializeField] UnityEvent onFullyRevealed = new();

        float _elapsed;

        public event Action FullyRevealed;

        public bool IsPlaying { get; private set; }
        public float NormalizedProgress { get; private set; } = 1f;
        public float GroundHeight => useTransformAsGround ? transform.position.y : groundHeight;
        public UnityEvent OnFullyRevealed => onFullyRevealed;

        void OnEnable()
        {
            if (Application.isPlaying && playOnEnable)
            {
                Play();
            }
            else if (!Application.isPlaying && previewInEditMode)
            {
                SetProgress(previewProgress);
            }
        }

        void OnDisable()
        {
            IsPlaying = false;
            if (s_Owner == this)
            {
                Shader.SetGlobalFloat(TransitionEnabledId, 0f);
                s_Owner = null;
            }
        }

        void Update()
        {
            if (!Application.isPlaying || !IsPlaying)
                return;

            _elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float linearProgress = Mathf.Clamp01(_elapsed / duration);
            ApplyProgress(linearProgress, keepMaskEnabled: true);

            if (linearProgress < 1f)
                return;

            // The last masked frame already covers the complete range. Removing the uniform
            // branch now returns all participating materials to their normal opaque cost.
            IsPlaying = false;
            Shader.SetGlobalFloat(TransitionEnabledId, 0f);
            FullyRevealed?.Invoke();
            onFullyRevealed.Invoke();
        }

        /// <summary>Hides participating geometry, then reconstructs it from the floor upward.</summary>
        public void Play()
        {
            AcquireGlobals();
            _elapsed = 0f;
            IsPlaying = true;
            ApplyProgress(0f, keepMaskEnabled: true);
        }

        /// <summary>Scrubs the effect while keeping the height mask active.</summary>
        public void SetProgress(float normalizedProgress)
        {
            AcquireGlobals();
            IsPlaying = false;
            ApplyProgress(normalizedProgress, keepMaskEnabled: true);
        }

        /// <summary>Returns to a fully hidden world, ready for a later Play call.</summary>
        public void ResetToHidden()
        {
            AcquireGlobals();
            IsPlaying = false;
            _elapsed = 0f;
            ApplyProgress(0f, keepMaskEnabled: true);
        }

        /// <summary>Shows the complete world and removes the transition shader branch.</summary>
        public void CompleteImmediately()
        {
            AcquireGlobals();
            IsPlaying = false;
            ApplyProgress(1f, keepMaskEnabled: false);
        }

        /// <summary>
        /// Height includes enough padding to keep the floor hidden at zero and the highest
        /// point visible at one, even at the extrema of the noise field and glowing edge.
        /// </summary>
        public static float CalculateRevealHeight(
            float floorHeight,
            float verticalExtent,
            float frontNoiseStrength,
            float frontEdgeWidth,
            float normalizedProgress)
        {
            float padding = Mathf.Max(0f, frontNoiseStrength) + Mathf.Max(0f, frontEdgeWidth);
            float start = floorHeight - padding;
            float end = floorHeight + Mathf.Max(0f, verticalExtent) + padding;
            return Mathf.Lerp(start, end, Mathf.Clamp01(normalizedProgress));
        }

        void ApplyProgress(float linearProgress, bool keepMaskEnabled)
        {
            NormalizedProgress = Mathf.Clamp01(linearProgress);
            float curvedProgress = revealCurve == null || revealCurve.length == 0
                ? NormalizedProgress
                : Mathf.Clamp01(revealCurve.Evaluate(NormalizedProgress));
            float revealHeight = CalculateRevealHeight(
                GroundHeight, worldHeight, noiseStrength, edgeWidth, curvedProgress);

            Shader.SetGlobalFloat(TransitionEnabledId, keepMaskEnabled ? 1f : 0f);
            Shader.SetGlobalFloat(RevealHeightId, revealHeight);
            Shader.SetGlobalFloat(NoiseScaleId, noiseScale);
            Shader.SetGlobalFloat(NoiseStrengthId, noiseStrength);
            Shader.SetGlobalFloat(EdgeWidthId, edgeWidth);
            Shader.SetGlobalColor(EdgeColorId, edgeColor);
            Shader.SetGlobalFloat(EdgeIntensityId, edgeIntensity);
            Shader.SetGlobalFloat(GridScaleId, gridScale);
            Shader.SetGlobalFloat(GridWidthId, gridWidth);
            Shader.SetGlobalFloat(GridIntensityId, gridIntensity);
            Shader.SetGlobalFloat(TransitionTimeId, Application.isPlaying ? Time.unscaledTime : 0f);
        }

        void AcquireGlobals()
        {
            if (s_Owner != null && s_Owner != this)
            {
                Debug.LogWarning(
                    "Only one GroundUpRevealController can drive the global transition at a time. The most recently used controller now owns it.",
                    this);
            }

            s_Owner = this;
        }

        void OnValidate()
        {
            duration = Mathf.Max(0.01f, duration);
            worldHeight = Mathf.Max(0.01f, worldHeight);
            noiseScale = Mathf.Max(0.01f, noiseScale);
            noiseStrength = Mathf.Max(0f, noiseStrength);
            edgeWidth = Mathf.Max(0.001f, edgeWidth);
            gridScale = Mathf.Max(0.01f, gridScale);

            if (!Application.isPlaying && previewInEditMode && isActiveAndEnabled)
                SetProgress(previewProgress);
        }
    }
}
