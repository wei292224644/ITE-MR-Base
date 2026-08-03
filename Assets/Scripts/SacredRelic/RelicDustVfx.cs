using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.VFX;

namespace MRBase.SacredRelic
{
    /// <summary>
    /// Gives every shard its own <see cref="VisualEffect"/> whose particles are sampled from that
    /// shard's mesh, which is what binds the dust to the stone. The graph recomputes the same axis
    /// dissolve the shell material uses, so both must be fed identical numbers - see
    /// <see cref="PushDissolveSettings"/>.
    ///
    /// Costs one VFX instance per shard. Denser dust than <see cref="RelicDustBakedPoints"/> but far
    /// more draw calls, so measure before shipping this to a headset.
    /// </summary>
    public sealed class RelicDustVfx : RelicDustSource
    {
        /// <summary>
        /// Byte-identical to the <c>TriangleSampling</c> struct the graph's buffer expects: a
        /// barycentric coordinate plus a triangle index. Declared here so this assembly stays free
        /// of any dependency on the asset that ships the graph.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        struct TriangleSample
        {
            public Vector2 coord;
            public uint index;
        }

        [Tooltip("The dissolve graph to clone onto each shard.")]
        [SerializeField] VisualEffectAsset dissolveGraph;

        [Tooltip("Fed to the graph's Guide Texture; must match the shell material's guide.")]
        [SerializeField] Texture2D guideTexture;

        [SerializeField, Min(32)] int samplesPerShard = 512;

        [Header("Must match the shell material")]
        [SerializeField] float axisMin = -0.1f;
        [SerializeField] float axisMax = 0.1f;
        [SerializeField] float guideTilling = 8f;
        [SerializeField] float guideStrength = 0.035f;

        [Header("Graph look")]
        [SerializeField] float width = 0.02f;
        [Tooltip("Particles spawned per step by the graph.")]
        [SerializeField] float particleDensity = 64f;

        static readonly int MeshId = Shader.PropertyToID("Mesh");
        static readonly int DissolveAmountId = Shader.PropertyToID("Dissolve Amount");

        const string BufferName = "UniformMeshBuffer";

        sealed class Instance
        {
            public VisualEffect effect;
            public GraphicsBuffer buffer;
        }

        readonly List<Instance> _instances = new();

        const string HostPrefix = "Dust_";

        public override void Prepare(IReadOnlyList<SacredRelicFracture.Shard> shards)
        {
            Release();
            // Emitters live under the shards rather than under this component, so a domain reload
            // leaves them orphaned with an empty tracking list. Sweep by name or they pile up.
            foreach (SacredRelicFracture.Shard shard in shards) DestroyHosts(shard.transform);

            if (dissolveGraph == null)
            {
                Debug.LogWarning("[RelicDustVfx] No dissolve graph assigned; dust is disabled.", this);
                return;
            }

            foreach (SacredRelicFracture.Shard shard in shards)
            {
                _instances.Add(BuildFor(shard));
            }
        }

        Instance BuildFor(SacredRelicFracture.Shard shard)
        {
            var instance = new Instance();
            if (shard.transform == null) return instance;

            var filter = shard.transform.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable)
            {
                Debug.LogWarning($"[RelicDustVfx] '{shard.transform.name}' has no readable mesh; " +
                                 "enable Read/Write on the model importer.", shard.transform);
                return instance;
            }

            // Parented to the shard so the emitter rides along as the piece tumbles away.
            var host = new GameObject(HostPrefix + shard.transform.name);
            host.transform.SetParent(shard.transform, false);

            var effect = host.AddComponent<VisualEffect>();
            effect.visualEffectAsset = dissolveGraph;
            effect.SetMesh(MeshId, mesh);

            TriangleSample[] samples = Sample(mesh, samplesPerShard);
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, samples.Length,
                                            Marshal.SizeOf<TriangleSample>());
            buffer.SetData(samples);
            if (effect.HasGraphicsBuffer(BufferName)) effect.SetGraphicsBuffer(BufferName, buffer);
            else Debug.LogWarning($"[RelicDustVfx] Graph has no '{BufferName}' buffer.", this);

            instance.effect = effect;
            instance.buffer = buffer;
            PushDissolveSettings(effect);
            effect.SetFloat(DissolveAmountId, 0f);
            return instance;
        }

        /// <summary>
        /// Mirrors the shell material's axis dissolve. If these drift apart the particles stop
        /// appearing on the eroding edge and the binding falls apart visually.
        /// </summary>
        void PushDissolveSettings(VisualEffect effect)
        {
            if (effect.HasFloat("Min Value")) effect.SetFloat("Min Value", axisMin);
            if (effect.HasFloat("Max Value")) effect.SetFloat("Max Value", axisMax);
            if (effect.HasFloat("Guide Tilling")) effect.SetFloat("Guide Tilling", guideTilling);
            if (effect.HasFloat("Guide Strength")) effect.SetFloat("Guide Strength", guideStrength);
            if (effect.HasFloat("Width")) effect.SetFloat("Width", width);
            if (effect.HasBool("Invert Direction")) effect.SetBool("Invert Direction", false);

            // The graph exposes density as an integer count, not a scale.
            if (effect.HasFloat("Particle Density"))
                effect.SetFloat("Particle Density", particleDensity);
            else if (effect.HasUInt("Particle Density"))
                effect.SetUInt("Particle Density", (uint)Mathf.Max(1f, particleDensity));
            else if (effect.HasInt("Particle Density"))
                effect.SetInt("Particle Density", Mathf.Max(1, (int)particleDensity));
            if (guideTexture != null && effect.HasTexture("Guide Texture"))
                effect.SetTexture("Guide Texture", guideTexture);
        }

        static TriangleSample[] Sample(Mesh mesh, int count)
        {
            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;
            int triCount = tris.Length / 3;
            if (triCount == 0) return System.Array.Empty<TriangleSample>();

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
            if (total <= 0f) return System.Array.Empty<TriangleSample>();

            var rng = new System.Random(123);
            var samples = new TriangleSample[count];
            for (int i = 0; i < count; i++)
            {
                float pick = (float)rng.NextDouble() * total;
                int lo = 0, hi = triCount - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (cumulative[mid] < pick) lo = mid + 1;
                    else hi = mid;
                }

                // Uniform barycentric coordinate; the graph recovers the third as 1 - u - v.
                float s = (float)rng.NextDouble();
                float t = Mathf.Sqrt((float)rng.NextDouble());
                samples[i] = new TriangleSample
                {
                    coord = new Vector2(1f - t, (1f - s) * t),
                    index = (uint)lo
                };
            }
            return samples;
        }

        public override void Advance(int shardIndex, float previous, float current)
        {
            if (shardIndex < 0 || shardIndex >= _instances.Count) return;
            VisualEffect effect = _instances[shardIndex].effect;
            if (effect == null) return;

            effect.SetFloat(DissolveAmountId, current);
            if (previous <= 0f && current > 0f) effect.SendEvent("OnPlay");
        }

        public override void ClearAll()
        {
            foreach (Instance instance in _instances)
            {
                if (instance.effect == null) continue;
                instance.effect.SendEvent("OnStop");
                instance.effect.Reinit();
                instance.effect.SetFloat(DissolveAmountId, 0f);
            }
        }

        void Release()
        {
            foreach (Instance instance in _instances)
            {
                instance.buffer?.Release();
                if (instance.effect != null) DestroyHost(instance.effect.gameObject);
            }
            _instances.Clear();
        }

        static void DestroyHosts(Transform shard)
        {
            if (shard == null) return;
            for (int i = shard.childCount - 1; i >= 0; i--)
            {
                Transform child = shard.GetChild(i);
                if (child.name.StartsWith(HostPrefix)) DestroyHost(child.gameObject);
            }
        }

        static void DestroyHost(GameObject host)
        {
            if (Application.isPlaying) Destroy(host);
            else DestroyImmediate(host);
        }

        void OnDisable() => Release();
    }
}
