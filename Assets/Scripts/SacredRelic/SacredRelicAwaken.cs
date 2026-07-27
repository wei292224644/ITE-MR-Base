using System.Collections;
using UnityEngine;
using TMPro;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MRBase.SacredRelic
{
    /// <summary>
    /// Demo driver: Sealed → Awakening → Awakened.
    /// Touch / Space / click triggers sacred-light shell collapse + inscription color restore.
    /// </summary>
    public class SacredRelicAwaken : MonoBehaviour
    {
        public enum State
        {
            Sealed,
            Awakening,
            Awakened
        }

        [Header("Layers")]
        [SerializeField] Renderer shellRenderer;
        [SerializeField] Renderer tabletRenderer;
        [SerializeField] TMP_Text inscriptionText;

        [Header("Optional Dissolver (INab)")]
        [SerializeField] MonoBehaviour dissolver; // INab.Dissolve.Dissolver when present

        [Header("Particles")]
        [SerializeField] ParticleSystem chunkParticles;
        [SerializeField] ParticleSystem ashParticles;

        [Header("Timing")]
        [SerializeField] float duration = 6.5f;
        [SerializeField] float goldSeepEnd = 0.35f;
        [SerializeField] float restoreStart = 0.55f;
        [Tooltip("Shell dissolve lags so chunks peel first, then holes open.")]
        [SerializeField] AnimationCurve dissolveCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.2f, 0.05f),
            new Keyframe(0.55f, 0.45f),
            new Keyframe(1f, 1.2f));
        [SerializeField] AnimationCurve restoreCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Sacred Gold")]
        [SerializeField] Color sacredGold = new Color(1f, 0.72f, 0.28f, 1f);
        [SerializeField] float goldEdgeIntensity = 4f;

        [Header("Inscription Colors")]
        [SerializeField] Color fadedInscription = new Color(0.45f, 0.42f, 0.38f, 1f);
        [SerializeField] Color restoredInscription = new Color(0.82f, 0.18f, 0.12f, 1f); // vermillion
        [SerializeField] Color fadedStone = new Color(0.35f, 0.33f, 0.30f, 1f);
        [SerializeField] Color restoredStone = new Color(0.62f, 0.58f, 0.50f, 1f);

        [Header("Demo Input")]
        [SerializeField] bool allowKeyboardTrigger = true;
        [SerializeField] KeyCode editorTriggerKey = KeyCode.Space;
        [SerializeField] KeyCode editorResetKey = KeyCode.R;

        static readonly int DissolveAmountId = Shader.PropertyToID("_DissolveAmount");
        static readonly int DissolveColorId = Shader.PropertyToID("_DissolveColor");
        static readonly int BurnColorId = Shader.PropertyToID("_Burn_Color");
        static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public State CurrentState { get; private set; } = State.Sealed;

        Material _shellMat;
        Material _tabletMat;
        Coroutine _sequence;
        bool _shellHasDissolve;

        void Awake()
        {
            CacheMaterials();
            ApplySealedLook();
        }

        void Update()
        {
            if (!allowKeyboardTrigger) return;

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame) TryAwaken();
                if (kb.rKey.wasPressedThisFrame) ResetToSealed();
                return;
            }
#endif
            if (Input.GetKeyDown(editorTriggerKey)) TryAwaken();
            if (Input.GetKeyDown(editorResetKey)) ResetToSealed();
        }

        void OnMouseDown()
        {
            TryAwaken();
        }

        /// <summary>XR poke / trigger hook.</summary>
        public void OnRelicTouched()
        {
            TryAwaken();
        }

        public bool TryAwaken()
        {
            if (CurrentState != State.Sealed) return false;
            if (_sequence != null) StopCoroutine(_sequence);
            CurrentState = State.Awakening; // set sync so EditMode tests / re-entry lock work immediately
            _sequence = StartCoroutine(AwakenRoutine());
            return true;
        }

        [ContextMenu("Reset To Sealed")]
        public void ResetToSealed()
        {
            if (_sequence != null)
            {
                StopCoroutine(_sequence);
                _sequence = null;
            }

            StopParticles();
            if (shellRenderer != null) shellRenderer.enabled = true;
            CurrentState = State.Sealed;
            ApplySealedLook();
        }

        void CacheMaterials()
        {
            if (shellRenderer != null)
            {
                _shellMat = shellRenderer.material; // instance
                _shellHasDissolve = _shellMat != null && _shellMat.HasProperty(DissolveAmountId);
                if (_shellHasDissolve)
                {
                    var gold = sacredGold * goldEdgeIntensity;
                    if (_shellMat.HasProperty(DissolveColorId)) _shellMat.SetColor(DissolveColorId, gold);
                    if (_shellMat.HasProperty(BurnColorId)) _shellMat.SetColor(BurnColorId, gold);
                    if (_shellMat.HasProperty(EdgeColorId)) _shellMat.SetColor(EdgeColorId, gold);
                }
            }

            if (tabletRenderer != null)
                _tabletMat = tabletRenderer.material;
        }

        void ApplySealedLook()
        {
            SetDissolve(0f);
            SetRestore(0f);
            SetInscriptionColor(fadedInscription);
            SetTabletColor(fadedStone);
        }

        IEnumerator AwakenRoutine()
        {
            PlayParticles();

            // Optional INab Dissolver
            if (dissolver != null)
            {
                var dissolveMethod = dissolver.GetType().GetMethod("Dissolve", System.Type.EmptyTypes);
                dissolveMethod?.Invoke(dissolver, null);
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float n = Mathf.Clamp01(t / duration);

                float dissolveN = dissolveCurve.Evaluate(n);
                SetDissolve(dissolveN);

                // Restore mid-late
                float restoreT = Mathf.InverseLerp(restoreStart, 1f, n);
                float restore = restoreCurve.Evaluate(Mathf.Clamp01(restoreT));
                SetRestore(restore);
                SetInscriptionColor(Color.Lerp(fadedInscription, restoredInscription, restore));
                SetTabletColor(Color.Lerp(fadedStone, restoredStone, restore));

                // Soft gold seep early
                if (n < goldSeepEnd && _shellHasDissolve && _shellMat != null && _shellMat.HasProperty(DissolveColorId))
                {
                    float seep = Mathf.InverseLerp(0f, goldSeepEnd, n);
                    _shellMat.SetColor(DissolveColorId, sacredGold * (goldEdgeIntensity * (0.5f + 0.5f * seep)));
                }

                yield return null;
            }

            SetDissolve(1f);
            SetRestore(1f);
            SetInscriptionColor(restoredInscription);
            SetTabletColor(restoredStone);

            if (shellRenderer != null) shellRenderer.enabled = false;
            // Stop emitting only — airborne chunks keep living and powderize via death sub-emitter.
            if (chunkParticles != null)
                chunkParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            CurrentState = State.Awakened;
            _sequence = null;
        }

        void SetDissolve(float amount)
        {
            if (_shellMat == null) return;
            if (_shellHasDissolve)
            {
                _shellMat.SetFloat(DissolveAmountId, amount);
                return;
            }

            // Fallback without Burn dissolve: darken + gold tint toward edge, then hide via alpha/scale cue
            if (_shellMat.HasProperty(BaseColorId))
            {
                var sealedCol = new Color(0.22f, 0.18f, 0.14f, 1f);
                var goldTint = Color.Lerp(sealedCol, sacredGold, Mathf.Clamp01(amount * 1.5f) * 0.35f);
                goldTint.a = Mathf.Lerp(1f, 0f, amount);
                _shellMat.SetColor(BaseColorId, goldTint);
            }

            if (shellRenderer != null && amount > 0.98f)
                shellRenderer.enabled = false;
        }

        void SetRestore(float amount)
        {
            // Hook for future _RestoreAmount shader; demo drives colors directly.
            if (_tabletMat != null && _tabletMat.HasProperty("_RestoreAmount"))
                _tabletMat.SetFloat("_RestoreAmount", amount);
        }

        void SetInscriptionColor(Color c)
        {
            if (inscriptionText != null)
                inscriptionText.color = c;
        }

        void SetTabletColor(Color c)
        {
            if (_tabletMat == null) return;
            if (_tabletMat.HasProperty(BaseColorId)) _tabletMat.SetColor(BaseColorId, c);
            else if (_tabletMat.HasProperty(ColorId)) _tabletMat.SetColor(ColorId, c);
        }

        void PlayParticles()
        {
            // Ash is Death sub-emitter of chunks — do not Play ash on its own.
            if (ashParticles != null)
                ashParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (chunkParticles != null)
            {
                chunkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                chunkParticles.Play();
            }
        }

        void StopParticles()
        {
            if (chunkParticles != null)
                chunkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (ashParticles != null)
                ashParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
