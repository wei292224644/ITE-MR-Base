using System.Collections;
using INab.Dissolve;
using MRBase.Transitions;
using UnityEngine;

namespace MRBase.IceSpriteFx
{
    /// <summary>
    /// 冰灵的出现 / 消失 / 传送编排。
    ///
    /// 溶解本身与贴着蒙皮网格的星屑都由 INab Dissolve FX Master Kit 承担
    /// （Dissolver + DissolverVFX，在各自的 Inspector 里调）。这里只补 MasterKit
    /// 没有的三件事：弹性落位、闪光爆点、传送编排。
    ///
    /// 放在 Assembly-CSharp 而不是任何 asmdef，因为 Dissolver 就在 Assembly-CSharp 里，
    /// asmdef 引用不到它。可测的时间轴逻辑因此抽在 MRBase.Transitions.IceSpriteTeleport。
    /// </summary>
    public sealed class IceSpritePresence : MonoBehaviour
    {
        [Header("溶解（INab）")]
        [Tooltip("同一个 GameObject 上的 Dissolver。duration 也从它读，避免两处配时长。")]
        [SerializeField] Dissolver dissolver;

        [Header("弹性落位")]
        [Tooltip("物质化过程中乘在基准 scale 上的倍率。末值必须回到 1，否则冰灵会越召越大。")]
        [SerializeField] AnimationCurve scalePunch = new AnimationCurve(
            new Keyframe(0f, 0.4f),
            new Keyframe(0.65f, 1.15f),
            new Keyframe(1f, 1f));

        [Header("闪光爆点")]
        [Tooltip("留空则不打闪光。强度靠全局 Bloom 出效果。")]
        [SerializeField] Light flash;
        [SerializeField] float flashIntensity = 12f;
        [SerializeField] float flashDuration = 0.25f;

        [Header("汇聚光点")]
        [Tooltip("本体出现前在周围空间聚拢的光点。留空则不播。\n" +
                 "这一层不绑蒙皮网格 —— 它要在还没有网格可绑的时候就存在。")]
        [SerializeField] ParticleSystem convergeMotes;

        [Tooltip("光点比本体早起多久，秒。派蒙那种观感靠的就是这段提前量：先在空处聚光，人再浮现。")]
        [SerializeField] float convergeLead = 0.35f;

        [Header("传送")]
        [SerializeField] IceSpriteTeleportStyle teleportStyle = IceSpriteTeleportStyle.DissolveReform;

        [Tooltip("TrailFlight 的飞行时长，秒。")]
        [SerializeField] float flightDuration = 0.4f;

        [Tooltip("TrailFlight 的拖尾。会被移到路径上的当前位置，起飞时 Play、落地时 Stop。")]
        [SerializeField] ParticleSystem trail;

        [Tooltip("Afterimage 用的半透明材质。留空则不出残影。")]
        [SerializeField] Material afterimageMaterial;

        [Tooltip("要烘残影的蒙皮渲染器。留空则在子物体里找第一个。")]
        [SerializeField] SkinnedMeshRenderer skinnedRenderer;

        public IceSpriteTeleportStyle style
        {
            get => teleportStyle;
            set => teleportStyle = value;
        }

        Vector3 _baseScale;
        Coroutine _running;
        Coroutine _flashing;

        void Awake()
        {
            _baseScale = transform.localScale;

            if (dissolver == null) dissolver = GetComponent<Dissolver>();
            if (skinnedRenderer == null) skinnedRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            if (flash != null) flash.intensity = 0f;
        }

        public void Appear()
        {
            Restart(AppearRoutine());
        }

        public void Vanish()
        {
            // 出现动画播到一半时按消失，scale 协程还在跑，会边溶解边弹。先收干净。
            StopRunning();
            if (dissolver != null) dissolver.Dissolve();
        }

        public void TeleportTo(Vector3 target)
        {
            Restart(TeleportRoutine(target));
        }

        /// <summary>
        /// 途中再次触发会把协程推乱（scale 停在中途、闪光卡在高位），所以先停旧的、
        /// 再把受影响的状态收回原位。
        /// </summary>
        void Restart(IEnumerator routine)
        {
            StopRunning();
            _running = StartCoroutine(routine);
        }

        void StopRunning()
        {
            if (_running != null)
            {
                StopCoroutine(_running);
                _running = null;
            }

            // 闪光是独立协程。只把 intensity 归零而不停它，下一帧就被顶回去了。
            if (_flashing != null)
            {
                StopCoroutine(_flashing);
                _flashing = null;
            }

            transform.localScale = _baseScale;
            if (flash != null) flash.intensity = 0f;
            if (trail != null) trail.Stop();
            if (convergeMotes != null) convergeMotes.Stop();
        }

        void StartFlash()
        {
            if (flash == null) return;
            if (_flashing != null) StopCoroutine(_flashing);
            _flashing = StartCoroutine(FlashRoutine());
        }

        float DissolveDuration => dissolver != null ? dissolver.duration : 0.5f;

        IEnumerator AppearRoutine()
        {
            // 光点先聚。本体这段时间还是全溶解态，画面上只有空处的星光。
            if (convergeMotes != null)
            {
                convergeMotes.Play();
                if (convergeLead > 0f) yield return new WaitForSeconds(convergeLead);
            }

            if (dissolver != null) dissolver.Materialize();
            StartFlash();

            float d = DissolveDuration;
            for (float t = 0f; t < d; t += Time.deltaTime)
            {
                transform.localScale = _baseScale * scalePunch.Evaluate(t / d);
                yield return null;
            }

            transform.localScale = _baseScale;
            _running = null;
        }

        IEnumerator FlashRoutine()
        {
            for (float t = 0f; t < flashDuration; t += Time.deltaTime)
            {
                // 起手最亮，之后线性收掉。
                flash.intensity = flashIntensity * (1f - t / flashDuration);
                yield return null;
            }
            flash.intensity = 0f;
            _flashing = null;
        }

        IEnumerator TeleportRoutine(Vector3 target)
        {
            Vector3 from = transform.position;
            IceSpriteTeleportStyle s = teleportStyle;
            float d = DissolveDuration;

            if (s == IceSpriteTeleportStyle.Afterimage)
            {
                SpawnAfterimage(from);
                transform.position = target;
                _running = null;
                yield break;
            }

            // 不能走 Vanish()：它会 StopRunning()，把本协程自己停掉。
            // Restart 已经收过 scale/闪光/拖尾，这里只启动溶解。
            if (dissolver != null) dissolver.Dissolve();

            float total = IceSpriteTeleport.TotalDuration(s, d, flightDuration);
            IceSpriteTeleportPhase previous = IceSpriteTeleportPhase.Vanishing;

            for (float t = 0f; t < total; t += Time.deltaTime)
            {
                IceSpriteTeleportPhase phase =
                    IceSpriteTeleport.PhaseAt(s, t, d, flightDuration);

                if (phase != previous)
                {
                    if (phase == IceSpriteTeleportPhase.InTransit && trail != null) trail.Play();
                    if (phase == IceSpriteTeleportPhase.Appearing)
                    {
                        if (trail != null) trail.Stop();
                        // 传送里光点不带提前量：提前量会拉长时间轴，而时间轴由
                        // IceSpriteTeleport 那三个纯函数定义，改它等于改契约。
                        if (convergeMotes != null) convergeMotes.Play();
                        if (dissolver != null) dissolver.Materialize();
                        StartFlash();
                    }
                    previous = phase;
                }

                Vector3 p = IceSpriteTeleport.PositionAt(s, from, target, t, d, flightDuration);
                transform.position = p;
                if (trail != null && phase == IceSpriteTeleportPhase.InTransit)
                    trail.transform.position = p;

                if (phase == IceSpriteTeleportPhase.Appearing)
                {
                    float k = (t - (total - d)) / d;
                    transform.localScale = _baseScale * scalePunch.Evaluate(Mathf.Clamp01(k));
                }

                yield return null;
            }

            transform.position = target;
            transform.localScale = _baseScale;
            _running = null;
        }

        /// <summary>
        /// 把当前这一帧的蒙皮姿势烘成静态 mesh 留在原地淡出。每次传送烘一次，成本可忽略。
        /// </summary>
        void SpawnAfterimage(Vector3 at)
        {
            if (afterimageMaterial == null || skinnedRenderer == null) return;

            Mesh baked = new Mesh();
            skinnedRenderer.BakeMesh(baked, useScale: true);

            var ghost = new GameObject("IceSpriteAfterimage");
            ghost.transform.SetPositionAndRotation(at, transform.rotation);
            ghost.AddComponent<MeshFilter>().sharedMesh = baked;

            var renderer = ghost.AddComponent<MeshRenderer>();
            renderer.material = afterimageMaterial;   // 每个残影一份实例，淡出互不干扰

            StartCoroutine(FadeAfterimage(ghost, renderer.material, baked));
        }

        IEnumerator FadeAfterimage(GameObject ghost, Material instance, Mesh baked)
        {
            Color color = instance.color;
            float d = DissolveDuration;

            for (float t = 0f; t < d; t += Time.deltaTime)
            {
                color.a = 1f - t / d;
                instance.color = color;
                yield return null;
            }

            // 实例化出来的材质和烘出来的 mesh 都不属于任何资产，不销毁就是泄漏。
            Destroy(instance);
            Destroy(baked);
            Destroy(ghost);
        }
    }
}
