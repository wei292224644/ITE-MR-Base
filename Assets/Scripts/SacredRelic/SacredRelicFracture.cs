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

            // Everything below is in the relic's own local space, never world space. That is
            // the whole reason the shell follows the stele: move, turn or scale the relic and
            // the parent matrix carries these along for free. Cache a world pose here instead
            // and it silently detaches the moment anything moves — which is exactly what used
            // to strand the crust at the origin while the core rode away.
            [HideInInspector] public Vector3 restLocalPosition;
            [HideInInspector] public Quaternion restLocalRotation;
            /// <summary>Mesh centre at rest, in relic space. Transforms may share an origin when
            /// pieces are authored with baked vertex offsets (Sketchfab stele).</summary>
            [HideInInspector] public Vector3 restLocalCentroid;
            /// <summary>Object-space bounds, so the dissolve noise can be sized per shard
            /// instead of per metre — these meshes carry baked world offsets.</summary>
            [HideInInspector] public Vector3 boundsCentreOS;
            [HideInInspector] public Vector3 boundsSizeOS;
            [HideInInspector] public Vector3 burstDirection;
            /// <summary>Travel in relic units, so relic scale applies to it via the parent.</summary>
            [HideInInspector] public float burstDistance;
            /// <summary>Horizontal axis in the face plane the flake hinges around as it peels.</summary>
            [HideInInspector] public Vector3 hingeAxis;
            /// <summary>Lower edge of the flake — the last part still gripping the stone.</summary>
            [HideInInspector] public Vector3 hingeLocalPivot;
            [HideInInspector] public Vector3 spinAxis;
            [HideInInspector] public float spinSpeed;
            [HideInInspector] public float dustDelay;
            [HideInInspector] public float lastDissolve;
        }

        [Header("石碑本体")]
        [Tooltip("碑体中心。用来算每块碎片相对碑心的位置；留空则用本物体的 Transform。")]
        [SerializeField] Transform relicCentre;
        [Tooltip("全部外壳碎片。由 Tools/Sacred Relic/Bind Scene Stele 自动填充，不要手改。")]
        [SerializeField] List<Shard> shards = new List<Shard>();

        [Header("裂纹蔓延方向")]
        [Tooltip("Directional = 用下面的起点/终点自己指定蔓延方向；\n" +
                 "FromManifest = 用 Blender 导出的 json 里烘焙的顺序（一般是由中心向外）。")]
        [SerializeField] SpreadMode spreadMode = SpreadMode.Directional;
        [Tooltip("裂纹波的起始角，碑面 0-1 坐标：x 从左到右，y 从下到上。\n" +
                 "(0,0)=左下  (1,0)=右下  (0,1)=左上  (1,1)=右上。\n" +
                 "当前用 (0,0)→(1,1)，即从左下往右上扫，和石块飞的方向一致。")]
        [SerializeField] Vector2 spreadFrom = new Vector2(0f, 1f);
        [Tooltip("裂纹波的终止角，坐标含义同上。")]
        [SerializeField] Vector2 spreadTo = new Vector2(1f, 0f);
        [Tooltip("裂纹波前沿的宽度（占整段行程的比例）。\n" +
                 "调小 = 裂纹像一条细线快速扫过；调大 = 一大片同时开裂，界限模糊。")]
        [SerializeField, Range(0.02f, 0.45f)] float spreadFrontWidth = 0.08f;

        [Header("节拍 1 · 裂纹扫过整个外壳")]
        [Tooltip("裂纹从起始角扫到终止角要几秒。\n" +
                 "这是整段演出的总节奏基准：调大 = 整体变慢、碎片依次脱落的间隔拉长。")]
        [SerializeField, Min(0.05f)] float crackDuration = 3.2f;
        [Tooltip("单块碎片自身裂开的快慢曲线（0→1）。默认两头缓、中间快。")]
        [SerializeField] AnimationCurve crackEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [Tooltip("【金光纹路的提前量】裂纹显现前沿比碎片脱落提前多少（0-1，蔓延进度的单位）。\n" +
                 "必须大于材质上的 Crack Softness（默认 0.05），否则裂纹刚画到这片、它就已经飞走了，\n" +
                 "金光纹路根本来不及看见。0 = 显现和脱落同时发生。\n" +
                 "调大 = 提前更多，能看到成片的金色裂纹网络铺开之后才开始剥落。")]
        [SerializeField, Range(0f, 0.6f)] float crackLead = 0.2f;

        [Header("节拍 2 · 金光从裂缝渗出")]
        [Tooltip("一块碎片被裂纹切开后，缝隙金光涨到最亮需要几秒。只影响发光，不影响运动。")]
        [SerializeField, Min(0.05f)] float seepDuration = 1.4f;
        [Tooltip("金光渐亮的曲线（0→1）。")]
        [SerializeField] AnimationCurve seepEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [Tooltip("缝隙张开时碎片向外挪开的距离，单位米。很小的值，只是让缝看得见。")]
        [SerializeField] float seamOpening = 0.006f;

        [Header("节拍 3 · 外壳被风掀起吹走")]
        [Tooltip("碎片裂开后、正式脱离前，在原地亮着缝隙金光停留几秒。\n" +
                 "掀起动作（下面的 Peel Angle）也在这段时间内完成，所以太小会看不到掀的过程。\n" +
                 "每块碎片有自己的时钟，早裂的已经在飞、晚裂的还没开始。")]
        [SerializeField, Min(0f)] float goldHold = 0.28f;
        [Tooltip("碑面朝向，本物体局部空间。由 Binder 从导入的模型自动算出，不要手改。")]
        [SerializeField] Vector3 faceNormalLocal = Vector3.back;

        [Tooltip("【每片各撕各的】0 = 所有碎片朝同一个方向掀（下面 Up/Right 定的那个）；\n" +
                 "1 = 每片在碑面内完全随机选一个撕开方向，然后就朝那个方向飞。\n" +
                 "中间值 = 以 Up/Right 为中心、按比例左右摇摆（0.4 约等于 ±72°）。")]
        [SerializeField, Range(0f, 1f)] float tearRandomness = 1f;
        [Tooltip("碎片脱离碑面、朝镜头飞出的分量（相对于面内撕开方向的比例）。\n" +
                 "调小 = 贴着碑面滑走；调大 = 明显朝镜头飞出来。\n" +
                 "只影响【方向】，最后会归一化，改它不影响飞多远（那是 Burst Reach）。")]
        [SerializeField] float windForward = 0.8f;
        [Tooltip("基准撕开方向向上的分量。Tear Randomness = 1 时这只是随机的起始基准，看不出效果。")]
        [SerializeField] float windUp = 1f;
        [Tooltip("基准撕开方向向右的分量。同上，Tear Randomness 越小越能看出偏向。")]
        [SerializeField] float windRight = 1f;
        [Tooltip("在各自撕开方向基础上再叠加的随机抖动角（度），面内和离面各一次。\n" +
                 "作用是让恰好抽到相近角度的碎片不会一模一样地平移。")]
        [SerializeField, Range(0f, 45f)] float windSpread = 14f;
        [Tooltip("【防陷进石头】飞行方向离开碑面分量的下限（0-1，是与碑面法线的点积）。\n" +
                 "随机角度算完后强制抬到这个值以上，保证没有任何碎片会往石头里钻。\n" +
                 "0.2 左右足够；调大 = 所有碎片都更朝镜头飞。")]
        [SerializeField, Range(0f, 1f)] float minForward = 0.25f;
        [Tooltip("【防穿模】先离开碑面、再横向铺开的程度。\n" +
                 "0 = 沿直线飞（朝下撕的碎片会横穿过下方还没脱落的碎片）；\n" +
                 "1 = 横向位移明显滞后，碎片先脱离碑面一段距离再散开。终点不受影响。")]
        [SerializeField, Range(0f, 1f)] float wallClearance = 0.8f;
        [Tooltip("按碎片自身位置额外向外发散的强度。建议保持 0。\n" +
                 "这个向量是【由碑心指向外】，所以大于 0 会把碑下半部分的碎片往下带。")]
        [SerializeField, Range(0f, 1f)] float radialFan;
        [Tooltip("脱离前，碎片绕自己【背风那条边】掀起的角度。\n" +
                 "这是「风钻进边缘把它撬起来」的动作。0 = 不掀，直接整块平移飞走。\n" +
                 "掀起动作会故意延续到起飞之后一小段（Gold Hold + 20% 飞行时间）并缓出，\n" +
                 "这样掀和吹走之间不会有速度断层。")]
        [SerializeField, Range(0f, 90f)] float peelAngle = 26f;

        [Tooltip("【飞行距离】碎片最远能飞多少米。想让石块飞得更远就调这个。\n" +
                 "Binder 只在第一次挂上组件时按碑高播种 clamp(高度×0.38, 2.5, 10)；\n" +
                 "之后重绑不会覆盖手调值（想恢复默认就删掉组件重绑）。")]
        [SerializeField, Min(0.01f)] float burstReach = 0.5f;
        [Tooltip("最晚脱落的碎片能飞到 Burst Reach 的百分之多少。最早脱落的永远飞满 100%。\n" +
                 "1 = 所有碎片飞一样远；调小 = 先飞的冲得远、后飞的近，前沿更明显。")]
        [SerializeField, Range(0.05f, 1f)] float rimReach = 0.75f;
        [Tooltip("【飞行速度】碎片飞完全程要几秒。距离不变时，调小 = 更快，调大 = 更慢更飘。\n" +
                 "注意这段时间要和节拍 4 的消散时间配合，不然会先飞停了才开始化。")]
        [SerializeField, Min(0.05f)] float burstTravelTime = 2.8f;
        [Tooltip("整段飞行中碎片绕自身中心自转的总角度。\n" +
                 "保持小值：这是风把薄片带得微微打转，不是碎石翻跟头。150 那种量级会转晕。")]
        [SerializeField] float spinDegrees = 40f;

        [Header("节拍 4 · 碎片风化成沙")]
        [Tooltip("单块碎片从开始消散到完全消失要几秒。")]
        [SerializeField, Min(0.05f)] float dustDuration = 2.6f;
        [Tooltip("各块碎片开始消散时间的随机错开范围（秒）。0 = 全部同时化，很整齐但假。")]
        [SerializeField] float dustStagger = 0.9f;
        [Tooltip("碎片起飞后过几秒开始消散。\n" +
                 "想让它「飞的过程中就化掉」就调小这个；调大则会先飞一段再开始化。")]
        [SerializeField] float dustLead = 0.45f;
        [Tooltip("沙尘粒子的实现。用来切换烘焙点方案和 VFX Graph 方案做对比。")]
        [SerializeField] RelicDustSource dustSource;

        [Header("碑芯（露出的本体）· 圣光")]
        [Tooltip("碑芯的 Renderer。金光的真正来源是它，不是外壳。")]
        [SerializeField] Renderer coreRenderer;
        [Tooltip("圣光的颜色。会乘上强度后写进碑芯材质的 _EmissionColor。\n" +
                 "材质上必须勾选 Emission，否则关键字没开、写了也不亮。")]
        [ColorUsage(false, true)]
        [SerializeField] Color coreGlowColor = new Color(1f, 0.72f, 0.32f);
        [Tooltip("圣光强度倍率。本项目 URP 关了 HDR、也没开后处理，所以超过 1 会被直接钳掉 —— " +
                 "亮度和溢出感交给下面的加法辉光层去出，这里保持 1 左右即可。")]
        [SerializeField, Min(0f)] float coreGlowStrength = 1f;
        [Tooltip("圣光随整段演出（0-1）的变化曲线。\n" +
                 "默认：前段被外壳挡着不亮 → 外壳剥落最多时冲到峰值 → 回落留一点余光。\n" +
                 "峰值不要留在结尾：光一直亮着碑文就看不清了，回落是把注意力交还给碑文。")]
        [SerializeField] AnimationCurve coreGlowOverTime = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.25f, 0.12f),
            new Keyframe(0.68f, 1f), new Keyframe(1f, 0.22f));
        [Tooltip("留给自定义碑芯 shader 的恢复度属性名。URP/Lit 上没有这个属性，写了不生效。")]
        [SerializeField] string coreRestoreProperty = "_Restore";

        [Header("调试")]
        [Tooltip("手动拖动整段演出的进度，0-1。需要勾上下面的 Preview 才生效。")]
        [Range(0f, 1f)] public float previewTime;
        [Tooltip("勾上后可以在不进 Play 的情况下用上面的滑块预览整段动画。")]
        public bool preview;

        static readonly int ProgressId = Shader.PropertyToID("_Progress");
        static readonly int GoldId = Shader.PropertyToID("_GoldIntensity");
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        static readonly int SpreadModeId = Shader.PropertyToID("_CrackSpreadMode");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int ShardCentreId = Shader.PropertyToID("_ShardCentreOS");
        static readonly int ShardSizeId = Shader.PropertyToID("_ShardSizeOS");
        static readonly int SpreadStartId = Shader.PropertyToID("_SpreadStartWS");
        static readonly int SpreadEndId = Shader.PropertyToID("_SpreadEndWS");

        // Built on demand: Unity forbids creating one in a field initializer.
        MaterialPropertyBlock _block;
        Phase _phase = Phase.Sealed;
        float _elapsed;
        bool _playing;
        int _coreRestoreId;
        // Relic-space, like everything else cached here. Push() converts them on the way to the
        // shader, which wants world positions — doing that per frame rather than per re-cache is
        // what keeps them right when the relic is moved while the sequence is playing.
        Vector3 _spreadStartLocal;
        Vector3 _spreadEndLocal;

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

            // Put the shards back before re-reading them. Switching Preview off fires this while
            // they are still standing at the scrubbed pose, and CacheRest would otherwise bake
            // that pose in as the new rest.
            if (!preview) ApplySealed();
            CacheRest();
            if (preview) Evaluate(previewTime * TotalDuration);
        }

        void CacheRest()
        {
            Vector3 centre = LocalCentre;
            CaptureRestPoses();
            ApplySpreadTimings();
            ResolveSpreadLocalAxis(out _spreadStartLocal, out _spreadEndLocal);

            Vector3 face = FaceLocal;
            // Face basis as the camera sees the inscription, matching ResolveSpreadLocalAxis:
            // +faceRight is screen-right, +faceUp is screen-up. Up is the relic's own up now
            // that this runs in relic space — for an upright tablet that is the same axis, and
            // for a tilted one the face coordinates follow the stone, which is what they mean.
            Vector3 faceRight = Vector3.Cross(face, Vector3.up);
            if (faceRight.sqrMagnitude < 1e-6f) faceRight = Vector3.Cross(face, Vector3.right);
            faceRight.Normalize();
            Vector3 faceUp = Vector3.Cross(faceRight, face).normalized;

            // Baseline tear direction across the face. Each shard rotates away from this by its
            // own random amount, so it is a bias rather than a shared gust.
            Vector3 basePeel = faceUp * windUp + faceRight * windRight;
            if (basePeel.sqrMagnitude < 1e-6f) basePeel = faceUp;
            basePeel.Normalize();

            for (int i = 0; i < shards.Count; i++)
            {
                Shard s = shards[i];
                if (s.transform == null) continue;

                Vector3 offset = s.restLocalCentroid - centre;
                // Strip the face component so the sideways fan stays in the plane of the
                // tablet no matter how the relic is rotated in the scene.
                Vector3 sideways = offset - Vector3.Project(offset, face);
                if (sideways.sqrMagnitude < 1e-6f) sideways = Vector3.up * 0.001f;
                sideways.Normalize();

                float seed = s.detach * 977.13f + i * 31.7f;

                // Every flake tears off its own way. Spinning the baseline about the face normal
                // keeps the result in the plane of the crust no matter how the relic is posed,
                // and at tearRandomness = 1 the ±180° range is a free choice of direction.
                float tearAngle = (Frac(seed * 2.317f) * 2f - 1f) * 180f * tearRandomness;
                Vector3 peelDir = (Quaternion.AngleAxis(tearAngle, face) * basePeel).normalized;

                // Rotating about cross(peelDir, face) lifts whichever edge leads that tear out
                // along the face normal, so the hinge always matches the way the flake goes.
                Vector3 hinge = Vector3.Cross(peelDir, face).normalized;

                // It leaves along its own tear, tilted off the face. Two small wobbles keep
                // shards that drew a similar angle from moving in lockstep.
                Vector3 dir = (peelDir + face * windForward).normalized;
                dir = Quaternion.AngleAxis((Frac(seed * 1.73f) - 0.5f) * 2f * windSpread, face) * dir;
                dir = Quaternion.AngleAxis((Frac(seed * 3.11f) - 0.5f) * 2f * windSpread, hinge) * dir;
                dir = (dir + sideways * radialFan).normalized;

                // Hard floor on the off-face component: whatever the random angles came out as,
                // a shard must never set off into the stone it just peeled from.
                float intoFace = Vector3.Dot(dir, face);
                if (intoFace < minForward) dir = (dir + face * (minForward - intoFace)).normalized;

                // Early pieces (detach near 0) travel farther so the leading edge of the gust
                // reads clearly in whatever direction the crack is running.
                float lead = 1f - Mathf.Clamp01(s.detach);
                s.burstDirection = dir;
                // burstReach is authored in metres at relic scale 1, which is exactly what a
                // relic-space distance is — parent scale applies itself on the way out.
                s.burstDistance = burstReach * Mathf.Lerp(rimReach, 1f, lead);

                s.hingeAxis = hinge;
                // The flake's trailing edge — the corner furthest back along the peel, and so
                // the last bit still gripping the stone. Support point of the bounds in -peelDir.
                if (s.renderer != null)
                {
                    Bounds b = LocalBounds(s.renderer);
                    Vector3 e = b.extents;
                    float reach = Mathf.Abs(peelDir.x) * e.x
                                  + Mathf.Abs(peelDir.y) * e.y
                                  + Mathf.Abs(peelDir.z) * e.z;
                    // Pulled back to the face the crust sits against, not the slab's mid-depth.
                    // Hinging about mid-depth swings the flake's back half into the stone.
                    float depth = Mathf.Abs(face.x) * e.x
                                  + Mathf.Abs(face.y) * e.y
                                  + Mathf.Abs(face.z) * e.z;
                    s.hingeLocalPivot = b.center - peelDir * reach - face * depth;
                }
                else
                {
                    s.hingeLocalPivot = s.restLocalCentroid;
                }

                // Mostly a free random axis so no two flakes turn alike, pulled part-way back
                // toward the peel edge so the motion still reads as the wind having caught it.
                var rnd = new Vector3(Frac(seed * 3.3f) - 0.5f, Frac(seed * 4.1f) - 0.5f,
                                      Frac(seed * 5.7f) - 0.5f);
                s.spinAxis = rnd.sqrMagnitude > 1e-6f
                    ? Vector3.Slerp(hinge, rnd.normalized, 0.65f).normalized
                    : hinge;
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

                // In edit mode re-read poses so inspector tweaks stay honest — but never while
                // the preview slider is driving the shards. Mid-scrub their transforms are out
                // in mid-flight, and capturing those as the rest pose strands the whole shell
                // wherever the scrub left it, permanently.
                if (!s.restCached || (!Application.isPlaying && !preview))
                {
                    s.restLocalPosition = transform.InverseTransformPoint(s.transform.position);
                    s.restLocalRotation = Quaternion.Inverse(transform.rotation) * s.transform.rotation;
                    s.restLocalCentroid = s.renderer != null
                        ? transform.InverseTransformPoint(s.renderer.bounds.center)
                        : s.restLocalPosition;
                    s.restCached = true;
                }

                if (s.renderer != null && s.boundsSizeOS == Vector3.zero)
                {
                    Bounds local = s.renderer.localBounds;
                    s.boundsCentreOS = local.center;
                    // One number for the whole shard: the noise must stay isotropic, so scaling
                    // each axis by its own extent would smear the grain on these flat slabs.
                    float span = Mathf.Max(local.size.x, Mathf.Max(local.size.y, local.size.z));
                    s.boundsSizeOS = Vector3.one * Mathf.Max(1e-4f, span);
                }
            }
        }

        /// <summary>
        /// A renderer's bounds expressed in relic space. Standard AABB transform: the absolute
        /// matrix maps the extents, since a rotated box needs the enclosing axis-aligned one.
        /// </summary>
        Bounds LocalBounds(Renderer renderer)
        {
            Bounds local = renderer.localBounds;
            Matrix4x4 m = transform.worldToLocalMatrix * renderer.localToWorldMatrix;
            Vector3 e = local.extents;
            var extents = new Vector3(
                Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
                Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
                Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);
            return new Bounds(m.MultiplyPoint3x4(local.center), extents * 2f);
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

            if (!ResolveSpreadLocalAxis(out Vector3 start, out Vector3 end))
                return;

            Vector3 axis = end - start;
            float axisLenSq = axis.sqrMagnitude;
            if (axisLenSq < 1e-8f) return;
            float half = Mathf.Clamp(spreadFrontWidth * 0.5f, 0.01f, 0.4f);

            foreach (Shard s in shards)
            {
                if (s.transform == null) continue;
                // Projection onto from→to, 0 at start corner and 1 at end corner.
                float t = Mathf.Clamp01(Vector3.Dot(s.restLocalCentroid - start, axis) / axisLenSq);
                s.arrive = Mathf.Clamp01(t - half);
                s.detach = Mathf.Clamp01(t + half);
            }
        }

        bool ResolveSpreadLocalAxis(out Vector3 start, out Vector3 end)
        {
            start = end = Vector3.zero;
            if (shards == null || shards.Count == 0) return false;

            Vector3 face = FaceLocal;
            // Face-aligned axes as the camera sees the inscription: +right = screen-right,
            // +up = screen-up. Cross(face, worldUp) matches that when face points at the camera.
            Vector3 right = Vector3.Cross(face, Vector3.up);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(face, Vector3.right);
            right.Normalize();
            Vector3 up = Vector3.Cross(right, face).normalized;

            float minR = float.PositiveInfinity, maxR = float.NegativeInfinity;
            float minU = float.PositiveInfinity, maxU = float.NegativeInfinity;
            Vector3 origin = LocalCentre;
            bool any = false;
            foreach (Shard s in shards)
            {
                if (s.transform == null) continue;
                Vector3 p = s.restLocalCentroid;
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
            _spreadStartLocal = start;
            _spreadEndLocal = end;
            return true;
        }

        static float Frac(float v) => v - Mathf.Floor(v);

        /// <summary>Where the tablet's face points, in world space. For callers outside.</summary>
        public Vector3 FaceNormal
        {
            get
            {
                Vector3 world = transform.TransformDirection(faceNormalLocal);
                return world.sqrMagnitude > 1e-6f ? world.normalized : Vector3.back;
            }
        }

        /// <summary>The same normal in relic space — what all the internal maths runs on.</summary>
        Vector3 FaceLocal =>
            faceNormalLocal.sqrMagnitude > 1e-6f ? faceNormalLocal.normalized : Vector3.back;

        /// <summary>
        /// The point the sideways fan spreads out from, in relic space. Falls back to the relic's
        /// own origin, which in relic space is simply zero.
        /// </summary>
        Vector3 LocalCentre => relicCentre != null
            ? transform.InverseTransformPoint(relicCentre.position)
            : Vector3.zero;

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
                SetShardPose(s, s.restLocalPosition, s.restLocalRotation);
                if (s.renderer != null) s.renderer.enabled = true;
                Push(s, 0f, 0f, 0f);
            }
            SetCoreGlow(0f);
        }

        /// <summary>
        /// The single place relic space becomes world space. Reading the parent matrix here,
        /// every time, is what makes moving/turning/scaling the relic free — nothing cached
        /// has to be told about it.
        /// </summary>
        void SetShardPose(Shard shard, Vector3 localPosition, Quaternion localRotation)
        {
            shard.transform.SetPositionAndRotation(
                transform.TransformPoint(localPosition),
                transform.rotation * localRotation);
        }

        /// <summary>Places the whole relic at an absolute time, so it can be scrubbed.</summary>
        public void Evaluate(float time)
        {
            _phase = time < crackDuration ? Phase.Cracking
                : time < crackDuration + goldHold + dustLead ? Phase.Bursting
                : Phase.Dusting;

            Vector3 face = FaceLocal;

            // Where the wave has reached across the whole face. The mask's arrival channel is
            // baked in these same global units, so FromManifest has to be handed this rather
            // than a shard's own local progress — comparing a global per-pixel value against a
            // local scalar makes every pixel on a shard cross the threshold at once, which is
            // the uniform fade the directional path already suffers from.
            //
            // crackLead is not cosmetic. A cell's `detach` in the manifest IS the largest
            // arrival value on its own outline, so with no lead the front finishes drawing a
            // shard's cracks at the exact instant it lets go: the gold is never on screen long
            // enough to read. The lead eases in so nothing pops at t=0, and is allowed to carry
            // the front past 1 so the last shards' seams light up before they leave too.
            float crackWave = Mathf.Clamp01(time / Mathf.Max(0.01f, crackDuration));
            // Mathf.SmoothStep interpolates between its first two arguments — it is not HLSL's
            // smoothstep(edge0, edge1, x). Ramp 0→1 and scale, or the lead comes out 0.12x.
            float leadRamp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(crackWave / 0.12f));
            float crackGlobal = crackEase.Evaluate(crackWave) + crackLead * leadRamp;

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

                Vector3 position = s.restLocalPosition;
                Quaternion rotation = s.restLocalRotation;
                // Tracked alongside the pose because every rotation below has to turn the flake
                // about its own geometry. These pieces are authored with baked vertex offsets,
                // so all their transforms sit on the stele's origin down at the base — spinning
                // `rotation` without re-pivoting swings a shard twelve metres up the face around
                // that origin, which is what made them arc away and then sink.
                Vector3 centroid = s.restLocalCentroid;

                // The peel deliberately runs past goldHold and eases out rather than finishing
                // exactly at the hand-off. Clamping it the instant the shard let go froze a
                // hinge that was swinging the flake along at ~0.7 m/s, and the flight curve then
                // started from a standstill — that pair is what read as a hitch between the two
                // beats. Overlapping them hands the speed over.
                float peelSpan = Mathf.Max(0.05f, goldHold + burstTravelTime * 0.2f);
                float peelT = sinceFreed > 0f
                    ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sinceFreed / peelSpan))
                    : 0f;

                // The gold in a shard's seams is light leaking from behind it, not something the
                // crust emits. So it swells while the piece is still against the stone and dies
                // as the piece lifts away from what was lighting it — carrying the glow off with
                // the debris made the crust look like the treasure.
                float shardGold = Mathf.Max(crackLocal, seep) * (1f - peelT);

                if (sinceFreed > 0f)
                {
                    // The gust gets under the leading lip first: the flake hinges up off its
                    // trailing edge while that edge still grips the stone.
                    Quaternion lift = Quaternion.AngleAxis(peelAngle * peelT, s.hingeAxis);
                    position = s.hingeLocalPivot + lift * (position - s.hingeLocalPivot);
                    centroid = s.hingeLocalPivot + lift * (centroid - s.hingeLocalPivot);
                    rotation = lift * rotation;

                    Vector3 seam = face * (seamOpening * seep);
                    position += seam;
                    centroid += seam;
                }

                if (flight > 0f)
                {
                    float u = Mathf.Clamp01(flight / burstTravelTime);
                    // 0.35u + 0.65(2u² − u³). The linear term is what matters: a pure 2u² − u³
                    // leaves the shard at zero speed at u=0, so it stalls for a beat the moment
                    // the peel hands over. The linear part starts it at roughly the speed the
                    // hinge already had. Slope is still 1 at u=1, so it never stalls at the end
                    // either — an ease-out there would read as thrown rather than carried.
                    float ease = Mathf.Lerp(u, u * Mathf.Lerp(u, 1f, u), 0.65f);

                    // Flutter about the flake's own centre, so the tumble never moves it off
                    // the straight line the wind is carrying it along.
                    Quaternion tumble = Quaternion.AngleAxis(
                        spinDegrees * s.spinSpeed * ease, s.spinAxis);
                    position = centroid + tumble * (position - centroid);
                    rotation = tumble * rotation;

                    // Come off the wall before fanning out. Travelling straight along
                    // burstDirection meant a shard that tore downward set off across its
                    // neighbours while they were still attached, and cut straight through them.
                    // Running the in-plane part of the trip on a slower curve than the off-face
                    // part bends the path clear of the crust first. Same end point either way,
                    // since both curves reach 1 together.
                    float easePlane = Mathf.Lerp(ease, ease * ease, wallClearance);
                    Vector3 outward = face * Vector3.Dot(s.burstDirection, face);
                    Vector3 across = s.burstDirection - outward;
                    position += (outward * ease + across * easePlane) * s.burstDistance;
                }

                SetShardPose(s, position, rotation);

                if (s.renderer != null) s.renderer.enabled = dust < 1f;
                // FromManifest grows the network per pixel out of the mask, so it wants the
                // global wave; Directional has no per-pixel data and can only ramp the shard.
                Push(s, spreadMode == SpreadMode.FromManifest ? crackGlobal : crackLocal,
                     shardGold, dust);

                if (dustSource != null && !Mathf.Approximately(dust, s.lastDissolve))
                {
                    dustSource.Advance(i, s.lastDissolve, dust);
                }
                s.lastDissolve = dust;
            }

            SetCoreGlow(Mathf.Clamp01(time / Mathf.Max(0.01f, TotalDuration)));
        }

        MaterialPropertyBlock Block => _block ??= new MaterialPropertyBlock();

        void Push(Shard shard, float crack, float gold, float dissolve)
        {
            Renderer target = shard.renderer;
            if (target == null) return;
            target.GetPropertyBlock(Block);
            Block.SetFloat(ProgressId, crack);
            Block.SetFloat(GoldId, gold);
            Block.SetFloat(DissolveId, dissolve);
            Block.SetVector(ShardCentreId, shard.boundsCentreOS);
            Block.SetVector(ShardSizeId, shard.boundsSizeOS);
            Block.SetFloat(SpreadModeId, spreadMode == SpreadMode.Directional ? 1f : 0f);
            // The shader compares against world positions, so convert here rather than at
            // cache time — that way a relic moved mid-sequence still lines up with its mask.
            Block.SetVector(SpreadStartId, transform.TransformPoint(_spreadStartLocal));
            Block.SetVector(SpreadEndId, transform.TransformPoint(_spreadEndLocal));
            target.SetPropertyBlock(Block);
        }

        /// <summary>
        /// <summary>
        /// The relic's own light, sampled at 0-1 across the whole sequence.
        ///
        /// No mask is needed to make it appear "through the gaps": the crust is opaque and sits
        /// in front, so the core is simply revealed wherever a shard has already gone.
        /// </summary>
        void SetCoreGlow(float normalisedTime)
        {
            if (coreRenderer == null) return;
            float level = coreGlowOverTime.Evaluate(normalisedTime) * coreGlowStrength;

            coreRenderer.GetPropertyBlock(Block);
            Block.SetColor(EmissionId, coreGlowColor * Mathf.Max(0f, level));
            // Kept for a custom core shader that wants to drive albedo restoration separately;
            // URP/Lit has no such property, so on the stock material this writes into the void.
            Block.SetFloat(_coreRestoreId, normalisedTime);
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
