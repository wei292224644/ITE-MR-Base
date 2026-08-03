using System.Collections.Generic;
using UnityEngine;

namespace MRBase.SacredRelic
{
    /// <summary>
    /// Bakes a cloud of surface points per shard, each tagged with the dissolve-noise value that
    /// will erase it. Playback then emits exactly the points the erosion front just crossed, at the
    /// shard's current world transform, so the dust is welded to the stone it came from.
    ///
    /// Every shard shares one <see cref="ParticleSystem"/>, which keeps this to a single draw call.
    /// Requires the shell material to be in noise dissolve mode, since that is the field replicated
    /// here on the CPU.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class RelicDustBakedPoints : RelicDustSource
    {
        [Tooltip("Surface samples per shard. More means finer dust and a bigger bake.")]
        [SerializeField, Min(16)] int samplesPerShard = 320;

        // Authored against the relic at scale 1 (a 16 m stele). They are metres and metres per
        // second, and a ParticleSystem emitting into world space knows nothing about the relic's
        // transform — so shrinking the stele leaves 6 mm grains drifting 0.11 m/s off a 2 m
        // stone. That is eight times too big and eight times too fast, and it stops reading as
        // sand. RelicScale below puts them back in proportion.
        [Header("Dust motion (authored at relic scale 1)")]
        [SerializeField] float driftUp = 0.11f;
        [SerializeField] float pushOffSurface = 0.035f;
        [Tooltip("How much of the shard's own motion the dust keeps when it sheds. A ratio, so it is scale-free.")]
        [SerializeField, Range(0f, 1f)] float inheritVelocity = 0.3f;
        [SerializeField] float scatter = 0.03f;
        [SerializeField] Vector2 sizeRange = new Vector2(0.006f, 0.018f);
        [Tooltip("1 = grains shrink with the relic (proportional, but on a small relic they can " +
                 "fall under a pixel and vanish). 0 = keep them at the authored size, which reads " +
                 "as coarse gravel instead of sand. Motion is always fully scaled; this is size only.")]
        [SerializeField, Range(0f, 1f)] float grainSizeFollowsScale = 1f;
        [Tooltip("Seconds — deliberately NOT scaled. Scaling every length and speed together already shrinks the travel; scaling time too would make small relics crumble in slow motion.")]
        [SerializeField] Vector2 lifeRange = new Vector2(1.8f, 3.6f);

        // The ParticleSystem's own world-unit knobs. Owned here, not left in the module, so
        // ApplyRelicScaleToSystem can rewrite them from the authored value every time instead
        // of multiplying whatever it finds. Defaults are the values the look was tuned at.
        [Header("Particle system (authored at relic scale 1)")]
        [SerializeField] float gravity = -0.03f;
        [SerializeField] float noiseStrength = 0.72f;
        [SerializeField] float noiseFrequency = 0.45f;
        [SerializeField] float noiseScrollSpeed = 0.12f;

        /// <summary>
        /// Uniform scale of the relic this emitter hangs under. One number, not three: the dust
        /// has to stay isotropic or the grains smear.
        /// </summary>
        float RelicScale
        {
            get
            {
                Vector3 s = transform.lossyScale;
                return Mathf.Max(1e-4f,
                    Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))));
            }
        }

        struct SurfacePoint
        {
            public Vector3 positionOS;
            public Vector3 normalOS;
            public float threshold;
        }

        /// <summary>
        /// Everything the dissolve field needs, pulled off the shard rather than duplicated in the
        /// inspector. The shape of the field depends on the shard's own bounds, so it genuinely is
        /// per shard and cannot be one number shared by the emitter.
        /// </summary>
        public struct NoiseSettings
        {
            public Vector3 centreOS;
            public float sizeOS;
            public float scale;
            public float grainScale;
            public float grainStrength;

            /// <summary>Matches SacredRelicShell.shader's property defaults.</summary>
            public static NoiseSettings Default => new NoiseSettings
            {
                centreOS = Vector3.zero,
                sizeOS = 1f,
                scale = 9f,
                grainScale = 2.6f,
                grainStrength = 0.75f
            };
        }

        sealed class Baked
        {
            public Transform transform;
            public SurfacePoint[] points;   // sorted by threshold, ascending
            public int cursor;
            public Vector3 lastPosition;
            public bool hasLast;
        }

        ParticleSystem _system;
        readonly List<Baked> _baked = new();
        ParticleSystem.EmitParams _emit;

        public override void Prepare(IReadOnlyList<SacredRelicFracture.Shard> shards)
        {
            _system = GetComponent<ParticleSystem>();
            _baked.Clear();

            // Everything is emitted by hand, but the system still has to be running for those
            // particles to simulate.
            ParticleSystem.EmissionModule emission = _system.emission;
            emission.enabled = false;
            ApplyRelicScaleToSystem();
            if (!_system.isPlaying) _system.Play();

            foreach (SacredRelicFracture.Shard shard in shards)
            {
                var entry = new Baked { transform = shard.transform };
                Mesh mesh = ResolveMesh(shard);
                entry.points = mesh != null
                    ? Bake(mesh, ResolveNoise(shard))
                    : System.Array.Empty<SurfacePoint>();
                _baked.Add(entry);
            }
        }

        /// <summary>
        /// The ParticleSystem's own world-unit knobs, rewritten for the relic's current size.
        /// The system simulates in world space with Local scaling, so it never hears about the
        /// relic's transform: at a tenth scale a 0.72 m curl noise does not stir the grains, it
        /// throws them across the room, and the drift stops reading as sand falling off stone.
        ///
        /// The authored values live in the fields above rather than in the module, so applying
        /// this twice cannot compound — the module is always written, never multiplied.
        /// </summary>
        void ApplyRelicScaleToSystem()
        {
            if (_system == null) _system = GetComponent<ParticleSystem>();
            if (_system == null) return;
            float scale = RelicScale;

            ParticleSystem.MainModule main = _system.main;
            main.gravityModifier = gravity * scale;

            ParticleSystem.NoiseModule noise = _system.noise;
            noise.strength = noiseStrength * scale;
            noise.scrollSpeed = noiseScrollSpeed * scale;
            // Frequency is cycles per world unit, so it goes the other way — otherwise a shrunk
            // relic samples one smooth corner of the field and every grain drifts together.
            noise.frequency = noiseFrequency / scale;
        }

        /// <summary>
        /// Reads the dissolve field's shape straight off the shard and its material, so there is no
        /// second copy of these numbers to fall out of sync with the shader.
        /// </summary>
        static NoiseSettings ResolveNoise(SacredRelicFracture.Shard shard)
        {
            NoiseSettings s = NoiseSettings.Default;

            // SacredRelicFracture fills these in CacheRest, which always runs before Prepare.
            if (shard.boundsSizeOS.x > 1e-4f)
            {
                s.centreOS = shard.boundsCentreOS;
                s.sizeOS = shard.boundsSizeOS.x;
            }

            Material material = shard.renderer != null ? shard.renderer.sharedMaterial : null;
            if (material == null) return s;
            if (material.HasProperty("_NoiseScale")) s.scale = material.GetFloat("_NoiseScale");
            if (material.HasProperty("_GrainScale")) s.grainScale = material.GetFloat("_GrainScale");
            if (material.HasProperty("_GrainStrength"))
                s.grainStrength = material.GetFloat("_GrainStrength");
            return s;
        }

        static Mesh ResolveMesh(SacredRelicFracture.Shard shard)
        {
            if (shard.transform == null) return null;
            var filter = shard.transform.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return null;
            if (!filter.sharedMesh.isReadable)
            {
                Debug.LogWarning($"[RelicDust] Mesh '{filter.sharedMesh.name}' is not readable; " +
                                 "enable Read/Write on the model importer.", filter);
                return null;
            }
            return filter.sharedMesh;
        }

        SurfacePoint[] Bake(Mesh mesh, NoiseSettings noise)
        {
            Vector3[] verts = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] tris = mesh.triangles;
            int triCount = tris.Length / 3;
            if (triCount == 0) return System.Array.Empty<SurfacePoint>();

            // Area-weighted so wide faces do not get starved by slivers.
            var cumulative = new float[triCount];
            float total = 0f;
            for (int t = 0; t < triCount; t++)
            {
                Vector3 a = verts[tris[t * 3]];
                Vector3 b = verts[tris[t * 3 + 1]];
                Vector3 c = verts[tris[t * 3 + 2]];
                total += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                cumulative[t] = total;
            }
            if (total <= 0f) return System.Array.Empty<SurfacePoint>();

            var rng = new System.Random(mesh.name.GetHashCode());
            var points = new SurfacePoint[samplesPerShard];
            bool hasNormals = normals != null && normals.Length == verts.Length;

            for (int i = 0; i < samplesPerShard; i++)
            {
                int t = PickTriangle(cumulative, (float)rng.NextDouble() * total);
                int i0 = tris[t * 3], i1 = tris[t * 3 + 1], i2 = tris[t * 3 + 2];

                float s = (float)rng.NextDouble();
                float sq = Mathf.Sqrt((float)rng.NextDouble());
                float u = 1f - sq, v = (1f - s) * sq, w = s * sq;

                Vector3 p = verts[i0] * u + verts[i1] * v + verts[i2] * w;
                Vector3 n = hasNormals
                    ? (normals[i0] * u + normals[i1] * v + normals[i2] * w).normalized
                    : Vector3.up;

                points[i] = new SurfacePoint
                {
                    positionOS = p,
                    normalOS = n,
                    threshold = DissolveNoise(p, noise)
                };
            }

            System.Array.Sort(points, (x, y) => x.threshold.CompareTo(y.threshold));
            return points;
        }

        static int PickTriangle(float[] cumulative, float pick)
        {
            int lo = 0, hi = cumulative.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (cumulative[mid] < pick) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        public override void Advance(int shardIndex, float previous, float current)
        {
            if (_system == null || shardIndex < 0 || shardIndex >= _baked.Count) return;
            Baked entry = _baked[shardIndex];
            if (entry.points.Length == 0 || entry.transform == null) return;

            if (current <= previous)
            {
                if (current <= 0f) { entry.cursor = 0; entry.hasLast = false; }
                return;
            }

            Vector3 position = entry.transform.position;
            Vector3 shardVelocity = entry.hasLast && Time.deltaTime > 0f
                ? (position - entry.lastPosition) / Time.deltaTime
                : Vector3.zero;
            entry.lastPosition = position;
            entry.hasLast = true;

            // Points are sorted by the noise value that erases them, so the band the front just
            // swept is a contiguous run starting at the cursor.
            Matrix4x4 toWorld = entry.transform.localToWorldMatrix;
            float scale = RelicScale;
            while (entry.cursor < entry.points.Length &&
                   entry.points[entry.cursor].threshold <= current)
            {
                SurfacePoint point = entry.points[entry.cursor++];
                Vector3 worldPos = toWorld.MultiplyPoint3x4(point.positionOS);
                Vector3 worldNrm = toWorld.MultiplyVector(point.normalOS).normalized;

                _emit.position = worldPos + worldNrm * (pushOffSurface * scale * 0.5f);
                // shardVelocity is already in world units, so it is the one term that must not
                // be scaled — the shards themselves already travel proportionally less far.
                _emit.velocity = worldNrm * (pushOffSurface * scale)
                                 + Vector3.up * (driftUp * scale)
                                 + shardVelocity * inheritVelocity
                                 + Random.insideUnitSphere * (scatter * scale);
                _emit.startSize = Random.Range(sizeRange.x, sizeRange.y)
                                  * Mathf.Lerp(1f, scale, grainSizeFollowsScale);
                _emit.startLifetime = Random.Range(lifeRange.x, lifeRange.y);
                _emit.rotation3D = Random.insideUnitSphere * 180f;
                _system.Emit(_emit, 1);
            }
        }

        public override void ClearAll()
        {
            if (_system != null) _system.Clear();
            foreach (Baked entry in _baked)
            {
                entry.cursor = 0;
                entry.hasLast = false;
            }
        }

        // --- CPU twin of the shader's dissolve field. Every function below mirrors one in
        // SacredRelicShell.shader line for line, and a point's threshold is the _Dissolve value at
        // which clip() erases it. If the two drift apart the grains appear off the eroding edge —
        // early in mid-surface, or late after the stone there is already gone. Change one, change
        // both, and re-check the numbers.

        static float Frac(float v) => v - Mathf.Floor(v);

        static float Hash13(Vector3 p)
        {
            p = new Vector3(Frac(p.x * 0.1031f), Frac(p.y * 0.1031f), Frac(p.z * 0.1031f));
            float d = p.x * (p.y + 33.33f) + p.y * (p.z + 33.33f) + p.z * (p.x + 33.33f);
            p += new Vector3(d, d, d);
            return Frac((p.x + p.y) * p.z);
        }

        static float VNoise(Vector3 p)
        {
            var i = new Vector3(Mathf.Floor(p.x), Mathf.Floor(p.y), Mathf.Floor(p.z));
            var f = new Vector3(Frac(p.x), Frac(p.y), Frac(p.z));
            f = new Vector3(f.x * f.x * (3f - 2f * f.x),
                            f.y * f.y * (3f - 2f * f.y),
                            f.z * f.z * (3f - 2f * f.z));

            float n00 = Mathf.Lerp(Hash13(i), Hash13(i + new Vector3(1, 0, 0)), f.x);
            float n10 = Mathf.Lerp(Hash13(i + new Vector3(0, 1, 0)),
                                   Hash13(i + new Vector3(1, 1, 0)), f.x);
            float n01 = Mathf.Lerp(Hash13(i + new Vector3(0, 0, 1)),
                                   Hash13(i + new Vector3(1, 0, 1)), f.x);
            float n11 = Mathf.Lerp(Hash13(i + new Vector3(0, 1, 1)),
                                   Hash13(i + new Vector3(1, 1, 1)), f.x);

            return Mathf.Lerp(Mathf.Lerp(n00, n10, f.y), Mathf.Lerp(n01, n11, f.y), f.z);
        }

        static Vector3 Hash33(Vector3 p)
        {
            p = new Vector3(Frac(p.x * 0.1031f), Frac(p.y * 0.1030f), Frac(p.z * 0.0973f));
            // dot(p, p.yxz + 33.33)
            float d = p.x * (p.y + 33.33f) + p.y * (p.x + 33.33f) + p.z * (p.z + 33.33f);
            p += new Vector3(d, d, d);
            // frac((p.xxy + p.yxx) * p.zyx)
            return new Vector3(Frac((p.x + p.y) * p.z),
                               Frac((p.x + p.x) * p.y),
                               Frac((p.y + p.x) * p.x));
        }

        static float Worley(Vector3 p)
        {
            var i = new Vector3(Mathf.Floor(p.x), Mathf.Floor(p.y), Mathf.Floor(p.z));
            var f = new Vector3(Frac(p.x), Frac(p.y), Frac(p.z));
            float best = 1e9f;
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                var g = new Vector3(x, y, z);
                Vector3 d = g + Hash33(i + g) - f;
                best = Mathf.Min(best, Vector3.Dot(d, d));
            }
            return Mathf.Clamp01(Mathf.Sqrt(best));
        }

        public static float DissolveNoise(Vector3 positionOS, NoiseSettings noise)
        {
            // Normalised by the shard's own bounds first: these meshes carry baked vertex offsets,
            // so positionOS is really world metres, and scaling that raw made the cells sub-pixel.
            Vector3 p = (positionOS - noise.centreOS) / Mathf.Max(1e-4f, noise.sizeOS) * noise.scale;
            float fbm = VNoise(p) * 0.6f + VNoise(p * 2.3f) * 0.27f + VNoise(p * 5.1f) * 0.13f;
            float grain = Worley(p * noise.grainScale);
            return Mathf.Clamp01(Mathf.Lerp(fbm, fbm * 0.55f + grain * 0.45f, noise.grainStrength));
        }
    }
}
