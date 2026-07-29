using System;
using System.Collections.Generic;
using UnityEngine;

namespace MRBase.SacredRelic
{
    /// <summary>
    /// Drives the awakening of a pre-fractured relic: the crack races across the crust,
    /// sacred light seeps from the seams, the shell bursts into the air, then each shard
    /// erodes into dust.
    ///
    /// Narrative (locked): what fractures is the decay shell — mud caking, grime, lichen and
    /// weathered mineral crust — not the stone body. The core keeps the inscription geometry
    /// (and any historical scars) and only restores colour/clarity as the shell peels away.
    ///
    /// Crack / burst order is controllable: set <see cref="spreadMode"/> to Directional and
    /// pick <see cref="spreadFrom"/> → <see cref="spreadTo"/> on the face (default top-left
    /// toward bottom-right). Manifest timings remain available as a fallback.
    /// </summary>
    public class SacredRelicFracture : MonoBehaviour
    {
        public enum Phase
        {
            Sealed,
            Cracking,
            Seeping,
            Bursting,
            Dusting,
            Awakened
        }

        public enum SpreadMode
        {
            /// <summary>Use arrive/detach baked into the Blender manifest (usually centre-out).</summary>
            FromManifest = 0,
            /// <summary>Recompute order from a face direction you pick in the inspector.</summary>
            Directional = 1
        }

        [Serializable]
        public class Shard
        {
            public Transform transform;
            public Renderer renderer;

            [Tooltip("0-1, when the crack network first touches this shard.")]
            public float arrive;

            [Tooltip("0-1, when the crack has fully encircled this shard and freed it.")]
            public float detach;

            [HideInInspector] public float manifestArrive;
            [HideInInspector] public float manifestDetach;
            [HideInInspector] public bool manifestCached;
            [HideInInspector] public bool restCached;
            [HideInInspector] public Vector3 restPosition;
            [HideInInspector] public Quaternion restRotation;
            /// <summary>World-space mesh centre at rest. Transforms may share an origin when
            /// pieces are authored with baked vertex offsets (Sketchfab stele).</summary>
            [HideInInspector] public Vector3 restCentroid;
            [HideInInspector] public Vector3 burstDirection;
            [HideInInspector] public float burstDistance;
            [HideInInspector] public Vector3 spinAxis;
            [HideInInspector] public float spinSpeed;
            [HideInInspector] public float dustDelay;
            [HideInInspector] public float lastDissolve;
        }

        [Header("Relic")]
        [SerializeField] Transform relicCentre;
        [SerializeField] List<Shard> shards = new List<Shard>();

        [Header("Crack / burst direction")]
        [Tooltip("Directional lets you pick where the awakening starts and where it finishes.")]
        [SerializeField] SpreadMode spreadMode = SpreadMode.Directional;
        [Tooltip("Start corner on the tablet face, in 0-1 face UV: (0,1)=left-top, (1,0)=right-bottom.")]
        [SerializeField] Vector2 spreadFrom = new Vector2(0f, 1f);
        [Tooltip("End corner on the tablet face, in 0-1 face UV.")]
        [SerializeField] Vector2 spreadTo = new Vector2(1f, 0f);
        [Tooltip("How wide the crack front is along that axis (in normalised 0-1 span).")]
        [SerializeField, Range(0.02f, 0.45f)] float spreadFrontWidth = 0.08f;

        [Header("Beat 1 - crack races across the crust")]
        [SerializeField, Min(0.05f)] float crackDuration = 3.2f;
        [SerializeField] AnimationCurve crackEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Beat 2 - sacred light seeps from the seams")]
        [Tooltip("How long a shard's own seams take to reach full glow once the crack frees it.")]
        [SerializeField, Min(0.05f)] float seepDuration = 1.4f;
        [SerializeField] AnimationCurve seepEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [Tooltip("How far each shard drifts outward while the seams widen, in metres.")]
        [SerializeField] float seamOpening = 0.006f;

        [Header("Beat 3 - crust bursts into the air")]
        [Tooltip("Seconds a shard glows at its freshly opened seams before letting go. Every " +
                 "shard runs on its own clock keyed to when the crack reached it, so early " +
                 "pieces are already flying while later ones are still cracking.")]
        [SerializeField, Min(0f)] float goldHold = 0.28f;
        [Tooltip("Direction the tablet's face points, in this object's local space. " +
                 "The builder fills this in from the imported geometry.")]
        [SerializeField] Vector3 faceNormalLocal = Vector3.back;
        [Tooltip("How much shards throw themselves off the face versus sliding sideways.")]
        [SerializeField] float faceBias = 1f;
        [SerializeField] float sidewaysSpread = 0.55f;
        [Tooltip("How much shards also carry upward as they go.")]
        [SerializeField] float riseBias = 0.35f;

        [Tooltip("Hard cap on how far any shard gets from where it started, in metres. " +
                 "No piece ever travels further than this.")]
        [SerializeField, Min(0.01f)] float burstReach = 0.5f;
        [Tooltip("Fraction of the reach the last shards cover. Pieces freed first go further.")]
        [SerializeField, Range(0.05f, 1f)] float rimReach = 0.55f;
        [Tooltip("Seconds a shard takes to coast out to its reach, easing off like air drag.")]
        [SerializeField, Min(0.05f)] float burstTravelTime = 2.8f;
        [SerializeField] float spinDegrees = 150f;

        [Header("Beat 4 - shards erode into dust")]
        [SerializeField, Min(0.05f)] float dustDuration = 2.6f;
        [SerializeField] float dustStagger = 0.9f;
        [Tooltip("Seconds after a shard launches before it starts crumbling. Each shard runs on " +
                 "its own clock, so pieces are still in flight while they turn to powder.")]
        [SerializeField] float dustLead = 0.45f;
        [Tooltip("Swap implementations to compare the baked-point and VFX Graph dust.")]
        [SerializeField] RelicDustSource dustSource;

        [Header("Core")]
        [Tooltip("Ramps 0-1 across beats 3 and 4 so the inscription regains its colour.")]
        [SerializeField] Renderer coreRenderer;
        [SerializeField] string coreRestoreProperty = "_Restore";

        [Header("Debug")]
        [Tooltip("Scrub the whole sequence by hand, 0-1. Only used when Preview is on.")]
        [Range(0f, 1f)] public float previewTime;
        public bool preview;

        static readonly int ProgressId = Shader.PropertyToID("_Progress");
        static readonly int GoldId = Shader.PropertyToID("_GoldIntensity");
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        static readonly int SpreadModeId = Shader.PropertyToID("_CrackSpreadMode");
        static readonly int SpreadStartId = Shader.PropertyToID("_SpreadStartWS");
        static readonly int SpreadEndId = Shader.PropertyToID("_SpreadEndWS");

        // Built on demand: Unity forbids creating one in a field initializer.
        MaterialPropertyBlock _block;
        Phase _phase = Phase.Sealed;
        float _elapsed;
        bool _playing;
        int _coreRestoreId;
        Vector3 _spreadStartWS;
        Vector3 _spreadEndWS;

        public Phase CurrentPhase => _phase;
        public SpreadMode CurrentSpreadMode => spreadMode;
        public Vector2 SpreadFrom => spreadFrom;
        public Vector2 SpreadTo => spreadTo;

        /// <summary>
        /// Ends when the last shard the crack reaches has finished crumbling. Beats overlap, so
        /// this is shorter than the sum of their durations.
        /// </summary>
        public float TotalDuration =>
            crackDuration + goldHold + dustLead + dustStagger + dustDuration;

        void Awake()
        {
            _coreRestoreId = Shader.PropertyToID(coreRestoreProperty);
            CacheRest();
            if (dustSource != null) dustSource.Prepare(shards);
            ApplySealed();
        }

        void OnValidate()
        {
            if (_coreRestoreId == 0) _coreRestoreId = Shader.PropertyToID(coreRestoreProperty);
            if (Application.isPlaying) return;
            CacheRest();
            if (preview) Evaluate(previewTime * TotalDuration);
            else ApplySealed();
        }

        void CacheRest()
        {
            Vector3 centre = relicCentre != null ? relicCentre.position : transform.position;
            CaptureRestPoses();
            ApplySpreadTimings();
            ResolveSpreadWorldAxis(out _spreadStartWS, out _spreadEndWS);

            for (int i = 0; i < shards.Count; i++)
            {
                Shard s = shards[i];
                if (s.transform == null) continue;

                Vector3 face = FaceNormal;
                Vector3 offset = s.restCentroid - centre;
                // Strip the face component so the sideways fan stays in the plane of the
                // tablet no matter how the relic is rotated in the scene.
                Vector3 sideways = offset - Vector3.Project(offset, face);
                if (sideways.sqrMagnitude < 1e-6f) sideways = Vector3.up * 0.001f;
                sideways.Normalize();

                float seed = s.detach * 977.13f + i * 31.7f;
                Vector3 jitter = new Vector3(Frac(seed) - 0.5f, Frac(seed * 1.7f) - 0.5f,
                                             Frac(seed * 2.9f) - 0.5f) * 0.3f;

                // Early pieces (detach near 0) throw farther so the leading edge of the blast
                // reads clearly in whatever direction the crack is travelling.
                float lead = 1f - Mathf.Clamp01(s.detach);
                s.burstDirection = (sideways * sidewaysSpread
                                    + face * (faceBias * Mathf.Lerp(1f, 1.6f, lead))
                                    + Vector3.up * riseBias
                                    + jitter).normalized;
                s.burstDistance = burstReach * Mathf.Lerp(rimReach, 1f, lead);
                s.spinAxis = new Vector3(Frac(seed * 3.3f) - 0.5f, Frac(seed * 4.1f) - 0.5f,
                                         Frac(seed * 5.7f) - 0.5f).normalized;
                s.spinSpeed = Mathf.Lerp(0.55f, 1.45f, Frac(seed * 6.3f));
                s.dustDelay = Frac(seed * 7.9f);
            }
        }

        void CaptureRestPoses()
        {
            for (int i = 0; i < shards.Count; i++)
            {
                Shard s = shards[i];
                if (s.transform == null) continue;

                // Builder timings must be frozen before Directional remaps arrive/detach.
                if (!s.manifestCached)
                {
                    s.manifestArrive = s.arrive;
                    s.manifestDetach = s.detach;
                    s.manifestCached = true;
                }

                // In edit mode always re-read poses so inspector tweaks stay honest.
                if (!s.restCached || !Application.isPlaying)
                {
                    s.restPosition = s.transform.position;
                    s.restRotation = s.transform.rotation;
                    s.restCentroid = s.renderer != null
                        ? s.renderer.bounds.center
                        : s.restPosition;
                    s.restCached = true;
                }
            }
        }

        /// <summary>
        /// Pick where the awakening starts and ends on the face. Coordinates are 0-1 on the
        /// tablet face: x = left→right, y = bottom→top. Example: (0,1)→(1,0) is top-left to
        /// bottom-right.
        /// </summary>
        public void SetSpreadDirection(Vector2 from, Vector2 to)
        {
            spreadMode = SpreadMode.Directional;
            spreadFrom = from;
            spreadTo = to;
            CacheRest();
        }

        void ApplySpreadTimings()
        {
            if (spreadMode == SpreadMode.FromManifest)
            {
                foreach (Shard s in shards)
                {
                    if (s.manifestDetach > 0f || s.manifestArrive > 0f)
                    {
                        s.arrive = s.manifestArrive;
                        s.detach = s.manifestDetach;
                    }
                }
                return;
            }

            if (!ResolveSpreadWorldAxis(out Vector3 start, out Vector3 end))
                return;

            Vector3 axis = end - start;
            float axisLenSq = axis.sqrMagnitude;
            if (axisLenSq < 1e-8f) return;
            float half = Mathf.Clamp(spreadFrontWidth * 0.5f, 0.01f, 0.4f);

            foreach (Shard s in shards)
            {
                if (s.transform == null) continue;
                // Projection onto from→to, 0 at start corner and 1 at end corner.
                float t = Mathf.Clamp01(Vector3.Dot(s.restCentroid - start, axis) / axisLenSq);
                s.arrive = Mathf.Clamp01(t - half);
                s.detach = Mathf.Clamp01(t + half);
            }
        }

        bool ResolveSpreadWorldAxis(out Vector3 start, out Vector3 end)
        {
            start = end = Vector3.zero;
            if (shards == null || shards.Count == 0) return false;

            Vector3 face = FaceNormal;
            // Face-aligned axes as the camera sees the inscription: +right = screen-right,
            // +up = screen-up. Cross(face, worldUp) matches that when face points at the camera.
            Vector3 right = Vector3.Cross(face, Vector3.up);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(face, Vector3.right);
            right.Normalize();
            Vector3 up = Vector3.Cross(right, face).normalized;

            float minR = float.PositiveInfinity, maxR = float.NegativeInfinity;
            float minU = float.PositiveInfinity, maxU = float.NegativeInfinity;
            Vector3 origin = relicCentre != null ? relicCentre.position : transform.position;
            bool any = false;
            foreach (Shard s in shards)
            {
                if (s.transform == null) continue;
                Vector3 p = s.restCentroid;
                float r = Vector3.Dot(p - origin, right);
                float u = Vector3.Dot(p - origin, up);
                minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
                minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
                any = true;
            }
            if (!any) return false;

            Vector2 from = new Vector2(Mathf.Clamp01(spreadFrom.x), Mathf.Clamp01(spreadFrom.y));
            Vector2 to = new Vector2(Mathf.Clamp01(spreadTo.x), Mathf.Clamp01(spreadTo.y));
            start = origin
                    + right * Mathf.Lerp(minR, maxR, from.x)
                    + up * Mathf.Lerp(minU, maxU, from.y);
            end = origin
                  + right * Mathf.Lerp(minR, maxR, to.x)
                  + up * Mathf.Lerp(minU, maxU, to.y);
            _spreadStartWS = start;
            _spreadEndWS = end;
            return true;
        }

        static float Frac(float v) => v - Mathf.Floor(v);

        /// <summary>Where the tablet's face points, in world space.</summary>
        public Vector3 FaceNormal
        {
            get
            {
                Vector3 world = transform.TransformDirection(faceNormalLocal);
                return world.sqrMagnitude > 1e-6f ? world.normalized : Vector3.back;
            }
        }

        public void Trigger()
        {
            if (_playing || _phase == Phase.Awakened) return;
            _playing = true;
            _elapsed = 0f;
            if (dustSource != null) dustSource.ClearAll();
        }

        public void ResetToSealed()
        {
            _playing = false;
            _elapsed = 0f;
            _phase = Phase.Sealed;
            if (dustSource != null) dustSource.ClearAll();
            foreach (Shard s in shards) s.lastDissolve = 0f;
            ApplySealed();
        }

        void Update()
        {
            if (!_playing) return;
            _elapsed += Time.deltaTime;
            Evaluate(_elapsed);
            if (_elapsed >= TotalDuration)
            {
                _playing = false;
                _phase = Phase.Awakened;
            }
        }

        void ApplySealed()
        {
            foreach (Shard s in shards)
            {
                if (s.transform == null) continue;
                s.transform.SetPositionAndRotation(s.restPosition, s.restRotation);
                if (s.renderer != null) s.renderer.enabled = true;
                Push(s.renderer, 0f, 0f, 0f);
            }
            SetCoreRestore(0f);
        }

        /// <summary>Places the whole relic at an absolute time, so it can be scrubbed.</summary>
        public void Evaluate(float time)
        {
            _phase = time < crackDuration ? Phase.Cracking
                : time < crackDuration + goldHold + dustLead ? Phase.Bursting
                : Phase.Dusting;

            Vector3 centre = relicCentre != null ? relicCentre.position : transform.position;
            Vector3 face = FaceNormal;

            for (int i = 0; i < shards.Count; i++)
            {
                Shard s = shards[i];
                if (s.transform == null) continue;

                // Each shard runs its own crack → seep → burst → dust clock, keyed to where it
                // sits on the from→to wave. Early stones are already powder while late ones crack.
                float arriveAt = Mathf.Clamp01(s.arrive) * crackDuration;
                float detachAt = Mathf.Clamp01(s.detach) * crackDuration;
                float crackSpan = Mathf.Max(0.08f, detachAt - arriveAt);
                float crackLocal = crackEase.Evaluate(
                    Mathf.Clamp01((time - arriveAt) / crackSpan));

                float freedAt = detachAt;
                float sinceFreed = time - freedAt;
                float seep = seepEase.Evaluate(Mathf.Clamp01(sinceFreed / seepDuration));
                float flight = Mathf.Max(0f, sinceFreed - goldHold);
                float dust = Mathf.Clamp01(
                    (flight - (dustLead + s.dustDelay * dustStagger)) / dustDuration);

                Vector3 position = s.restPosition;
                Quaternion rotation = s.restRotation;

                if (sinceFreed > 0f)
                {
                    // Seams widen along the piece's own centre so peel follows the crack cell,
                    // not a shared transform origin.
                    Vector3 offset = s.restCentroid - centre;
                    Vector3 sideways = offset - Vector3.Project(offset, face);
                    if (sideways.sqrMagnitude > 1e-6f) sideways.Normalize();
                    position += sideways * (seamOpening * seep)
                                + face * (seamOpening * 0.4f * seep);
                }

                if (flight > 0f)
                {
                    float u = Mathf.Clamp01(flight / burstTravelTime);
                    float ease = 1f - (1f - u) * (1f - u) * (1f - u);
                    position += s.burstDirection * (s.burstDistance * ease);
                    rotation = Quaternion.AngleAxis(spinDegrees * s.spinSpeed * ease, s.spinAxis)
                               * s.restRotation;
                }

                s.transform.SetPositionAndRotation(position, rotation);

                if (s.renderer != null) s.renderer.enabled = dust < 1f;
                Push(s.renderer, crackLocal, seep, dust);

                if (dustSource != null && !Mathf.Approximately(dust, s.lastDissolve))
                {
                    dustSource.Advance(i, s.lastDissolve, dust);
                }
                s.lastDissolve = dust;
            }

            float restoreStart = goldHold;
            float restoreEnd = crackDuration + goldHold + dustLead + dustDuration * 0.6f;
            float restore = Mathf.Clamp01((time - restoreStart) /
                                          Mathf.Max(0.01f, restoreEnd - restoreStart));
            SetCoreRestore(restore);
        }

        MaterialPropertyBlock Block => _block ??= new MaterialPropertyBlock();

        void Push(Renderer target, float crack, float gold, float dissolve)
        {
            if (target == null) return;
            target.GetPropertyBlock(Block);
            Block.SetFloat(ProgressId, crack);
            Block.SetFloat(GoldId, gold);
            Block.SetFloat(DissolveId, dissolve);
            Block.SetFloat(SpreadModeId, spreadMode == SpreadMode.Directional ? 1f : 0f);
            Block.SetVector(SpreadStartId, _spreadStartWS);
            Block.SetVector(SpreadEndId, _spreadEndWS);
            target.SetPropertyBlock(Block);
        }

        void SetCoreRestore(float value)
        {
            if (coreRenderer == null) return;
            coreRenderer.GetPropertyBlock(Block);
            Block.SetFloat(_coreRestoreId, value);
            coreRenderer.SetPropertyBlock(Block);
        }

        public void Bind(Transform centre, Renderer core, List<Shard> pieces, RelicDustSource dust,
                         Vector3 faceLocal)
        {
            relicCentre = centre;
            coreRenderer = core;
            shards = pieces;
            dustSource = dust;
            faceNormalLocal = faceLocal;

            // Awake never runs while the builder is assembling the scene, so bake the rest poses
            // and burst vectors here or they stay zeroed and the shards only fall straight down.
            foreach (Shard s in shards)
            {
                s.restCached = false;
                s.manifestCached = false;
            }
            CacheRest();
            if (dustSource != null) dustSource.Prepare(shards);
        }
    }
}
