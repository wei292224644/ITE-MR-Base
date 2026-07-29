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

        [Tooltip("Must match the shell material's Noise Scale or the dust will lag the erosion.")]
        [SerializeField] float noiseScale = 42f;

        [Header("Dust motion")]
        [SerializeField] float driftUp = 0.11f;
        [SerializeField] float pushOffSurface = 0.035f;
        [Tooltip("How much of the shard's own motion the dust keeps when it sheds.")]
        [SerializeField, Range(0f, 1f)] float inheritVelocity = 0.3f;
        [SerializeField] float scatter = 0.03f;
        [SerializeField] Vector2 sizeRange = new Vector2(0.006f, 0.018f);
        [SerializeField] Vector2 lifeRange = new Vector2(1.8f, 3.6f);

        struct SurfacePoint
        {
            public Vector3 positionOS;
            public Vector3 normalOS;
            public float threshold;
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
            if (!_system.isPlaying) _system.Play();

            foreach (SacredRelicFracture.Shard shard in shards)
            {
                var entry = new Baked { transform = shard.transform };
                Mesh mesh = ResolveMesh(shard);
                entry.points = mesh != null ? Bake(mesh) : System.Array.Empty<SurfacePoint>();
                _baked.Add(entry);
            }
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

        SurfacePoint[] Bake(Mesh mesh)
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
                    threshold = DissolveNoise(p, noiseScale)
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
            while (entry.cursor < entry.points.Length &&
                   entry.points[entry.cursor].threshold <= current)
            {
                SurfacePoint point = entry.points[entry.cursor++];
                Vector3 worldPos = toWorld.MultiplyPoint3x4(point.positionOS);
                Vector3 worldNrm = toWorld.MultiplyVector(point.normalOS).normalized;

                _emit.position = worldPos + worldNrm * pushOffSurface * 0.5f;
                _emit.velocity = worldNrm * pushOffSurface
                                 + Vector3.up * driftUp
                                 + shardVelocity * inheritVelocity
                                 + Random.insideUnitSphere * scatter;
                _emit.startSize = Random.Range(sizeRange.x, sizeRange.y);
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

        // --- CPU twin of the shader's dissolve field. Keep in lockstep with
        // SacredRelicShell.shader's DissolveNoise or the dust will drift off the erosion front.

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

        public static float DissolveNoise(Vector3 positionOS, float scale)
        {
            Vector3 p = positionOS * scale;
            return Mathf.Clamp01(VNoise(p) * 0.68f + VNoise(p * 2.7f) * 0.32f);
        }
    }
}
