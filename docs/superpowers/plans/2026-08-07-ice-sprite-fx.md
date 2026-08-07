# 冰灵出现 / 消失 / 传送特效测试台 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给测试模型 `pet_501001.fbx` 做一套可在编辑器里反复触发的「凭空物质化」演出 —— 出现、消失、三种风格的传送。

**Architecture:** 三层。纯传送阶段逻辑放 `MRBase.Transitions` asmdef（可离机单测）；`IceSpritePresence` MonoBehaviour 放 Assembly-CSharp（因为要引用第三方 `Dissolver`，asmdef 引用不到 Assembly-CSharp，方向是反的）；溶解与蒙皮表面星屑全部由 INab Dissolve FX Master Kit 承担，自写代码只做编排、弹性落位、闪光爆点。

**Tech Stack:** Unity URP，VFX Graph，Shader Graph，Input System（双分支兼容旧 Input Manager），NUnit EditMode 测试，第三方 INab Dissolve FX Master Kit。

设计文档：`docs/superpowers/specs/2026-08-07-ice-sprite-fx-design.md`

## Global Constraints

- 本次是**纯特效测试台**。不接手势，不接业务，不做锚点/位置策略，不做跟随与常驻 idle 行为。
- 新 MonoBehaviour 放 `Assets/Scripts/IceSpriteFx/`，**不建 asmdef**。`Assets/INab Studio/` 无 asmdef，`Dissolver` 与 `DissolverVFX` 在 `Assembly-CSharp` 里，任何 asmdef 都引用不到它们。
- 纯逻辑放 `Assets/Scripts/Transitions/`（`MRBase.Transitions`，`rootNamespace: MRBase.Transitions`，references 为空）。该 asmdef `autoReferenced: true`，所以 Assembly-CSharp 能引用它。
- 测试放 `Assets/Tests/EditMode/Transitions/`（`MRBase.Transitions.Tests`），沿用既有 `GroundUpRevealControllerTests.cs` 的风格：`[Test]` / `[TestCase]` + `Assert.That(..., Is.EqualTo(x).Within(0.0001f))`。
- 键盘输入沿用 `Assets/Scripts/SacredRelic/SacredRelicTrigger.cs` 的既有形状：`#if ENABLE_INPUT_SYSTEM` / `#else` 双分支。
- 默认用 `Skinned Standard Materialize / Dissolve Template.vfx`；Axis 版本同时准备好，靠 `DissolverVFX` 的 Inspector 字段切换，不写代码分支。
- **不要**碰 `Assets/INab Studio/` 下的任何文件。要改就复制一份出来改。

---

### Task 1: 传送阶段纯逻辑 + 单元测试

三种传送风格的时间轴推进与位置计算，全是纯函数，不碰 `MonoBehaviour`、不碰第三方类型，所以能在 EditMode 里直接测。

**Files:**
- Create: `Assets/Scripts/Transitions/IceSpriteTeleport.cs`
- Test: `Assets/Tests/EditMode/Transitions/IceSpriteTeleportTests.cs`

**Interfaces:**
- Consumes: 无（只依赖 `UnityEngine.Vector3` 与 `Mathf`）
- Produces:
  - `enum MRBase.Transitions.IceSpriteTeleportStyle { DissolveReform, TrailFlight, Afterimage }`
  - `enum MRBase.Transitions.IceSpriteTeleportPhase { Vanishing, InTransit, Appearing, Done }`
  - `static IceSpriteTeleportPhase IceSpriteTeleport.PhaseAt(IceSpriteTeleportStyle style, float elapsed, float dissolveDuration, float flightDuration)`
  - `static Vector3 IceSpriteTeleport.PositionAt(IceSpriteTeleportStyle style, Vector3 from, Vector3 to, float elapsed, float dissolveDuration, float flightDuration)`
  - `static float IceSpriteTeleport.TotalDuration(IceSpriteTeleportStyle style, float dissolveDuration, float flightDuration)`

三种风格的时间轴语义（`d` = dissolveDuration，`f` = flightDuration）：

| Style | 时间轴 | 总时长 |
|---|---|---|
| `DissolveReform` | `[0,d)` Vanishing@from → `[d,2d)` Appearing@to → Done | `2d` |
| `TrailFlight` | `[0,d)` Vanishing@from → `[d,d+f)` InTransit@lerp → `[d+f,2d+f)` Appearing@to → Done | `2d+f` |
| `Afterimage` | 本体一开始就在 to，全程 Done；`d` 只是 A 点残影的淡出时长 | `d` |

- [ ] **Step 1: 写失败的测试**

创建 `Assets/Tests/EditMode/Transitions/IceSpriteTeleportTests.cs`：

```csharp
using NUnit.Framework;
using UnityEngine;

namespace MRBase.Transitions.Tests
{
    public class IceSpriteTeleportTests
    {
        static readonly Vector3 From = new Vector3(0f, 1f, 0f);
        static readonly Vector3 To = new Vector3(4f, 1f, 0f);

        const float D = 0.5f;   // dissolveDuration
        const float F = 0.4f;   // flightDuration

        // ---- DissolveReform：消散完才重组，中间没有飞行段 ----

        [TestCase(0f, IceSpriteTeleportPhase.Vanishing)]
        [TestCase(0.49f, IceSpriteTeleportPhase.Vanishing)]
        [TestCase(0.5f, IceSpriteTeleportPhase.Appearing)]
        [TestCase(0.99f, IceSpriteTeleportPhase.Appearing)]
        [TestCase(1f, IceSpriteTeleportPhase.Done)]
        [TestCase(99f, IceSpriteTeleportPhase.Done)]
        public void DissolveReform_PhaseBoundaries(float elapsed, IceSpriteTeleportPhase expected)
        {
            Assert.That(
                IceSpriteTeleport.PhaseAt(IceSpriteTeleportStyle.DissolveReform, elapsed, D, F),
                Is.EqualTo(expected));
        }

        [Test]
        public void DissolveReform_StaysAtSourceWhileVanishing()
        {
            Vector3 p = IceSpriteTeleport.PositionAt(
                IceSpriteTeleportStyle.DissolveReform, From, To, 0.25f, D, F);

            Assert.That(p, Is.EqualTo(From));
        }

        [Test]
        public void DissolveReform_SnapsToTargetOnceVanished()
        {
            Vector3 p = IceSpriteTeleport.PositionAt(
                IceSpriteTeleportStyle.DissolveReform, From, To, 0.5f, D, F);

            Assert.That(p, Is.EqualTo(To));
        }

        [Test]
        public void DissolveReform_TotalIsTwoDissolves()
        {
            Assert.That(
                IceSpriteTeleport.TotalDuration(IceSpriteTeleportStyle.DissolveReform, D, F),
                Is.EqualTo(1f).Within(0.0001f));
        }

        // ---- TrailFlight：中间多一段飞行 ----

        [TestCase(0f, IceSpriteTeleportPhase.Vanishing)]
        [TestCase(0.5f, IceSpriteTeleportPhase.InTransit)]
        [TestCase(0.89f, IceSpriteTeleportPhase.InTransit)]
        [TestCase(0.9f, IceSpriteTeleportPhase.Appearing)]
        [TestCase(1.4f, IceSpriteTeleportPhase.Done)]
        public void TrailFlight_PhaseBoundaries(float elapsed, IceSpriteTeleportPhase expected)
        {
            Assert.That(
                IceSpriteTeleport.PhaseAt(IceSpriteTeleportStyle.TrailFlight, elapsed, D, F),
                Is.EqualTo(expected));
        }

        [Test]
        public void TrailFlight_MidTransitIsHalfway()
        {
            // elapsed 0.7 → 飞行段过了 (0.7-0.5)/0.4 = 0.5
            Vector3 p = IceSpriteTeleport.PositionAt(
                IceSpriteTeleportStyle.TrailFlight, From, To, 0.7f, D, F);

            Assert.That(p.x, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void TrailFlight_TotalIncludesFlight()
        {
            Assert.That(
                IceSpriteTeleport.TotalDuration(IceSpriteTeleportStyle.TrailFlight, D, F),
                Is.EqualTo(1.4f).Within(0.0001f));
        }

        [Test]
        public void TrailFlight_ZeroFlightDegradesToDissolveReform()
        {
            // 防除零：f = 0 时不能崩，也不能卡在 InTransit
            Assert.That(
                IceSpriteTeleport.PhaseAt(IceSpriteTeleportStyle.TrailFlight, 0.5f, D, 0f),
                Is.EqualTo(IceSpriteTeleportPhase.Appearing));

            Assert.That(
                IceSpriteTeleport.TotalDuration(IceSpriteTeleportStyle.TrailFlight, D, 0f),
                Is.EqualTo(1f).Within(0.0001f));
        }

        // ---- Afterimage：本体不消失，直接在目标点 ----

        [TestCase(0f)]
        [TestCase(0.25f)]
        public void Afterimage_BodyIsAtTargetImmediately(float elapsed)
        {
            Assert.That(
                IceSpriteTeleport.PositionAt(IceSpriteTeleportStyle.Afterimage, From, To, elapsed, D, F),
                Is.EqualTo(To));

            Assert.That(
                IceSpriteTeleport.PhaseAt(IceSpriteTeleportStyle.Afterimage, elapsed, D, F),
                Is.EqualTo(IceSpriteTeleportPhase.Done));
        }

        [Test]
        public void Afterimage_TotalIsGhostFade()
        {
            Assert.That(
                IceSpriteTeleport.TotalDuration(IceSpriteTeleportStyle.Afterimage, D, F),
                Is.EqualTo(0.5f).Within(0.0001f));
        }

        // ---- 边界 ----

        [Test]
        public void NegativeElapsedIsTreatedAsStart()
        {
            Assert.That(
                IceSpriteTeleport.PhaseAt(IceSpriteTeleportStyle.DissolveReform, -1f, D, F),
                Is.EqualTo(IceSpriteTeleportPhase.Vanishing));
        }
    }
}
```

- [ ] **Step 2: 跑测试，确认它失败**

用 UnityMCP：`run_tests`，mode `EditMode`，filter `IceSpriteTeleportTests`。
或在 Unity 里 Window → General → Test Runner → EditMode → 跑该类。

预期：编译失败，`The type or namespace name 'IceSpriteTeleport' could not be found`。

- [ ] **Step 3: 写最小实现**

创建 `Assets/Scripts/Transitions/IceSpriteTeleport.cs`：

```csharp
using UnityEngine;

namespace MRBase.Transitions
{
    /// <summary>三种传送风格。测试台用来现场比较观感，不是业务分类。</summary>
    public enum IceSpriteTeleportStyle
    {
        /// <summary>原地消散，目标点重组。中间有一段谁都不在场。</summary>
        DissolveReform,

        /// <summary>消散后一束星屑沿路径飞到目标点再重组。</summary>
        TrailFlight,

        /// <summary>本体立刻到目标点，出发点留一个淡出的残影。</summary>
        Afterimage,
    }

    public enum IceSpriteTeleportPhase
    {
        Vanishing,
        InTransit,
        Appearing,
        Done,
    }

    /// <summary>
    /// 传送的时间轴推进与位置计算。纯函数，不碰引擎帧循环也不碰第三方类型，
    /// 所以能离机单测 —— 与 <see cref="GroundUpRevealController.CalculateRevealHeight"/> 同一套路。
    ///
    /// 编排层是 Assembly-CSharp 里的 IceSpritePresence，它引用不进任何 asmdef，
    /// 所以逻辑必须待在这边才测得到。
    /// </summary>
    public static class IceSpriteTeleport
    {
        public static float TotalDuration(
            IceSpriteTeleportStyle style, float dissolveDuration, float flightDuration)
        {
            float d = Mathf.Max(0f, dissolveDuration);

            switch (style)
            {
                case IceSpriteTeleportStyle.Afterimage:
                    return d;
                case IceSpriteTeleportStyle.TrailFlight:
                    return d + Mathf.Max(0f, flightDuration) + d;
                default:
                    return d + d;
            }
        }

        public static IceSpriteTeleportPhase PhaseAt(
            IceSpriteTeleportStyle style, float elapsed, float dissolveDuration, float flightDuration)
        {
            // Afterimage 的本体从头到尾都是实体，没有消散/重组段。
            if (style == IceSpriteTeleportStyle.Afterimage) return IceSpriteTeleportPhase.Done;

            float t = Mathf.Max(0f, elapsed);
            float d = Mathf.Max(0f, dissolveDuration);

            // f = 0 时飞行段是空区间，TrailFlight 自然退化成 DissolveReform。
            float f = style == IceSpriteTeleportStyle.TrailFlight
                ? Mathf.Max(0f, flightDuration)
                : 0f;

            if (t < d) return IceSpriteTeleportPhase.Vanishing;
            if (t < d + f) return IceSpriteTeleportPhase.InTransit;
            if (t < d + f + d) return IceSpriteTeleportPhase.Appearing;
            return IceSpriteTeleportPhase.Done;
        }

        public static Vector3 PositionAt(
            IceSpriteTeleportStyle style, Vector3 from, Vector3 to,
            float elapsed, float dissolveDuration, float flightDuration)
        {
            if (style == IceSpriteTeleportStyle.Afterimage) return to;

            float t = Mathf.Max(0f, elapsed);
            float d = Mathf.Max(0f, dissolveDuration);

            if (t < d) return from;

            if (style == IceSpriteTeleportStyle.TrailFlight)
            {
                float f = Mathf.Max(0f, flightDuration);
                if (f > 0f && t < d + f) return Vector3.Lerp(from, to, (t - d) / f);
            }

            return to;
        }
    }
}
```

- [ ] **Step 4: 跑测试，确认全绿**

同 Step 2 的跑法。预期：14 个 case 全 PASS。

如果 `DissolveReform_StaysAtSourceWhileVanishing` 之类的 `Is.EqualTo(From)` 因浮点比较报错，
改成逐分量 `Is.EqualTo(From.x).Within(0.0001f)` —— 这里没有算术，应当精确相等，报错说明实现动了值。

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Transitions/IceSpriteTeleport.cs \
        Assets/Scripts/Transitions/IceSpriteTeleport.cs.meta \
        Assets/Tests/EditMode/Transitions/IceSpriteTeleportTests.cs \
        Assets/Tests/EditMode/Transitions/IceSpriteTeleportTests.cs.meta
git commit -m "feat(ice-sprite-fx): 传送三风格的阶段与位置纯逻辑"
```

---

### Task 2: 编排组件与键盘驱动

把纯逻辑接到 INab 的 `Dissolver` 上，补 MasterKit 没有的三件事：弹性落位、闪光爆点、残影快照。
再加一个键盘驱动，让效果在编辑器里能反复触发。

这两个文件绑在一起交付 —— 没有驱动就验不了编排，拆开对评审没有意义。

**Files:**
- Create: `Assets/Scripts/IceSpriteFx/IceSpritePresence.cs`
- Create: `Assets/Scripts/IceSpriteFx/IceSpriteFxTestInput.cs`

**Interfaces:**
- Consumes: `MRBase.Transitions.IceSpriteTeleport`、`IceSpriteTeleportStyle`、`IceSpriteTeleportPhase`（Task 1）；
  第三方 `Dissolver`（`Assets/INab Studio/Dissolve-FX/Core/Scripts/Dissolver.cs`，全局命名空间，Assembly-CSharp）——
  用到的成员：`public void Materialize()`、`public void Dissolve()`、`public float duration`
- Produces:
  - `MRBase.IceSpriteFx.IceSpritePresence`：`public void Appear()`、`public void Vanish()`、`public void TeleportTo(Vector3 target)`、`public IceSpriteTeleportStyle style`

**这一步没有单元测试。** 它是 MonoBehaviour + 协程 + 第三方组件的胶水，
可测的部分已经在 Task 1 抽走了。验收靠 Task 3 的场景人工看。不要为了凑测试去 mock `Dissolver`。

**与设计文档第 7 节的一处偏差**：设计文档要求「传送进行中重复调用不会把状态机推乱」
用 EditMode 测试覆盖。做不到 —— 重入是**协程**状态，住在 MonoBehaviour 上，
而决策 3 把 MonoBehaviour 钉在了 Assembly-CSharp，测试 asmdef 引用不到。
纯逻辑层（Task 1）根本没有重入这个概念，那三个函数是无状态的。

所以这条改由 `StopRunning()` 在实现里保证，验收走 Task 3 Step 5 的人工用例
「传送途中再按 3」。如果要它自动化，得把编排层也搬进 asmdef —— 那要先给 INab 补 asmdef，
不属于本次范围。

- [ ] **Step 1: 写 IceSpritePresence**

创建 `Assets/Scripts/IceSpriteFx/IceSpritePresence.cs`：

```csharp
using System.Collections;
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

            Vanish();

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
```

- [ ] **Step 2: 写 IceSpriteFxTestInput**

创建 `Assets/Scripts/IceSpriteFx/IceSpriteFxTestInput.cs`：

```csharp
using MRBase.Transitions;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MRBase.IceSpriteFx
{
    /// <summary>
    /// 编辑器里反复触发特效用的键盘驱动。形状抄自 SacredRelicTrigger，
    /// 双分支是为了新旧 Input 后端都能跑。
    ///
    /// 1 = 出现   2 = 消失   3 = 传送到下一个锚点
    /// Q / W / E = 切传送风格（DissolveReform / TrailFlight / Afterimage）
    /// </summary>
    [RequireComponent(typeof(IceSpritePresence))]
    public sealed class IceSpriteFxTestInput : MonoBehaviour
    {
        [Tooltip("按 3 时在这些点之间轮着传送。至少放两个。")]
        [SerializeField] Transform[] anchors;

        IceSpritePresence _presence;
        int _nextAnchor;

        void Awake() => _presence = GetComponent<IceSpritePresence>();

        void NextTeleport()
        {
            if (anchors == null || anchors.Length == 0) return;

            _nextAnchor = (_nextAnchor + 1) % anchors.Length;
            Transform target = anchors[_nextAnchor];
            if (target != null) _presence.TeleportTo(target.position);
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame) _presence.Appear();
            if (keyboard.digit2Key.wasPressedThisFrame) _presence.Vanish();
            if (keyboard.digit3Key.wasPressedThisFrame) NextTeleport();

            if (keyboard.qKey.wasPressedThisFrame) _presence.style = IceSpriteTeleportStyle.DissolveReform;
            if (keyboard.wKey.wasPressedThisFrame) _presence.style = IceSpriteTeleportStyle.TrailFlight;
            if (keyboard.eKey.wasPressedThisFrame) _presence.style = IceSpriteTeleportStyle.Afterimage;
#else
            if (Input.GetKeyDown(KeyCode.Alpha1)) _presence.Appear();
            if (Input.GetKeyDown(KeyCode.Alpha2)) _presence.Vanish();
            if (Input.GetKeyDown(KeyCode.Alpha3)) NextTeleport();

            if (Input.GetKeyDown(KeyCode.Q)) _presence.style = IceSpriteTeleportStyle.DissolveReform;
            if (Input.GetKeyDown(KeyCode.W)) _presence.style = IceSpriteTeleportStyle.TrailFlight;
            if (Input.GetKeyDown(KeyCode.E)) _presence.style = IceSpriteTeleportStyle.Afterimage;
#endif
        }
    }
}
```

- [ ] **Step 3: 确认编译通过**

用 UnityMCP `read_console`（types `["error"]`），或看 Unity 状态栏。

预期：无错误。若报 `Dissolver` 找不到，说明文件被误放进了某个 asmdef 目录 ——
确认 `Assets/Scripts/IceSpriteFx/` 下**没有** `.asmdef` 文件。

- [ ] **Step 4: 提交**

```bash
git add Assets/Scripts/IceSpriteFx/
git commit -m "feat(ice-sprite-fx): 出现/消失/传送编排组件与键盘驱动"
```

---

### Task 3: 编辑器装配与测试场景

材质、VFX 克隆、场景搭建。这一步没有代码，是资产操作，产出物是「按 1/2/3 能看到效果」。

**Files:**
- Create: `Assets/IceSpriteFx/Materials/IceSprite_Dissolve.mat`
- Create: `Assets/IceSpriteFx/Materials/IceSprite_Afterimage.mat`
- Create: `Assets/IceSpriteFx/Vfx/IceSprite_Materialize.vfx`
- Create: `Assets/IceSpriteFx/Vfx/IceSprite_Dissolve.vfx`
- Create: `Assets/Scenes/IceSpriteFxTest.unity`

- [ ] **Step 1: 做溶解材质**

复制 `Assets/INab Studio/Dissolve-FX-MasterKit/Core URP/Dissolve Materials/Skinned Mesh Templates/1/Skinned Standard 1.mat`
到 `Assets/IceSpriteFx/Materials/IceSprite_Dissolve.mat`。

把 `_BaseMap` 换成 `Assets/Assets/Models/ice-sprite/textures/pet_501001.png`。

**噪声源模式两种都要试**（设计文档第 6 节的开放项）：

- `Use Triplanar UVs` = on，`Triplanar Space` = **Object**（不能是 World，否则冰灵移动时溶解带会滑）
- `Use Triplanar UVs` = off，走 `Guide Texture` + 模型 UV

冰灵是成品游戏角色，UV 大概率完整，预期 Guide Texture 更稳更便宜。以实际观感和帧时间为准，把选择写回设计文档第 6 节。

- [ ] **Step 2: 克隆两个 VFX 并绑定蒙皮渲染器**

复制这两个到 `Assets/IceSpriteFx/Vfx/`：

- `Skinned Mesh Templates/Standard/Skinned Standard Materialize Template.vfx` → `IceSprite_Materialize.vfx`
- `Skinned Mesh Templates/Standard/Skinned Standard Dissolve Template.vfx` → `IceSprite_Dissolve.vfx`

在各自的 VFX Graph 里把 Skinned Mesh 输入指向冰灵的 `SkinnedMeshRenderer`。

Axis 版本（`Skinned Axis Materialize / Dissolve Template.vfx`）同样复制一份留着 ——
决策 2 要的是能在 Inspector 里直接换，不写代码分支。

- [ ] **Step 3: 做残影材质**

新建 `Assets/IceSpriteFx/Materials/IceSprite_Afterimage.mat`，URP/Lit，Surface Type = **Transparent**。
`_BaseMap` 同样用 `pet_501001.png`，`_BaseColor` 调成偏冷的半透明色。

`IceSpritePresence.FadeAfterimage` 改的是 `material.color`（即 `_BaseColor`）的 alpha，
所以 shader 必须是 Transparent，Opaque 下改 alpha 不会有任何视觉变化。

- [ ] **Step 4: 搭测试场景**

新建 `Assets/Scenes/IceSpriteFxTest.unity`：

1. 一块地面 + 一盏方向光（够看清就行）
2. 拖入 `pet_501001.fbx`，材质换成 `IceSprite_Dissolve.mat`
3. 冰灵身上挂：
   - `Dissolver` —— `materials` 里加 `IceSprite_Dissolve.mat`，`duration` 设 0.8，`initialState` = Dissolved
   - `DissolverVFX` —— `materializeEffect` 指 `IceSprite_Materialize.vfx`，`dissolveEffect` 指 `IceSprite_Dissolve.vfx`，`materialToCopyFrom` 指 `IceSprite_Dissolve.mat`
   - `IceSpritePresence` —— `flash` 指一个子物体上的 Point Light（初始 intensity 0），`afterimageMaterial` 指 `IceSprite_Afterimage.mat`，`trail` 指一个拖尾 ParticleSystem
   - `IceSpriteFxTestInput` —— `anchors` 放两个空物体，相隔 3–4m
4. 拖尾 ParticleSystem：`Play On Awake` 关掉，形状小、速度低、寿命 0.3s 左右，颜色跟星屑一致

- [ ] **Step 5: 人工验收**

进 Play，逐条过：

| 按键 | 预期 |
|---|---|
| `1` | 星屑贴着模型表面浮现 → 本体扫成实体 → scale 弹一下回位 → 闪光收掉 |
| `2` | 反向：本体溶解成星屑飘散 |
| `Q` 然后 `3` | 原地消散，目标点重组，中间有一段谁都不在场 |
| `W` 然后 `3` | 消散后拖尾从 A 飞到 B，到点重组 |
| `E` 然后 `3` | 本体瞬间到 B，A 点留一个残影淡出 |
| 传送途中再按 `3` | 不卡死、不残留半截 scale、闪光不卡在高位 |

看到的问题按类别归位：观感问题调 `Dissolver` / `DissolverVFX` / 粒子的 Inspector，
时间轴问题回 Task 1 的纯逻辑加测试用例。**不要**把观感参数硬编码进 `IceSpritePresence`。

- [ ] **Step 6: 提交**

```bash
git add Assets/IceSpriteFx/ Assets/Scenes/IceSpriteFxTest.unity Assets/Scenes/IceSpriteFxTest.unity.meta
git commit -m "feat(ice-sprite-fx): 溶解材质、蒙皮星屑 VFX 与测试场景"
```

- [ ] **Step 7: 把噪声源模式的结论写回设计文档**

`docs/superpowers/specs/2026-08-07-ice-sprite-fx-design.md` 第 6 节「开放项：噪声源模式」
现在是待定。把 Step 1 的实测结论写进去 —— 选了哪种、为什么。

```bash
git add docs/superpowers/specs/2026-08-07-ice-sprite-fx-design.md
git commit -m "docs(ice-sprite-fx): 定下噪声源模式"
```

---

## 遗留

这些**不在**本次范围，别顺手做：

- 手势触发（`PalmsTogetherGesture` 已有 `Performed` / `Released`，接线是下一件事）
- 锚点 / 位置策略、跟随、常驻 idle 悬浮
- 上设备的性能测量。VFX Graph 是 compute 驱动，PICO / Quest 有实际成本；
  本项目曾为 bloom 从 7.5ms 压到 4.2ms（`eab3a0a`），渲染预算是紧的。转正前必测。
- 把 `IceSpritePresence` 搬进 asmdef。要做就得先给 INab 补 asmdef 或用接口隔开 `Dissolver`，
  现在不值得（决策 3）。
