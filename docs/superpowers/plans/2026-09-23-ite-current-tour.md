# ITE 区域队列与当前 Tour Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修掉 PICO 实测的两个区域问题：扫码锚定后被区域重选立刻切走；相机侧两个碰撞体让同一体积的进出成对重复、区域集合在第一次离开时就丢了这个 Tour。

**Architecture:** 按「一个问题一个模块」拆开：`RegionQueue`（按碰撞体计数、按进入先后排序的区域队列）、`RegionBaseline`（锚定结算窗口，区分「人动了」和「体积被锚定挪了」）、`CurrentTourRule`（当前 Tour 换成谁，不认识展示类型）、`TourAssembly.PlaysOnSelect / CanBeCurrent`（展示类型规则）、`TourScanPolicy` 与 `ScanPromptPolicy`（只看当前 Tour，不读队列）。`TourGuide` 只负责按顺序调用这些模块并合成 `GuideEffect`；`TourDirector` 把效果落到场景对象上；`IteRuntimeDriver` 用 `WaitForFixedUpdate` 通知物理步结束。

**Tech Stack:** Unity 6000.4.4f1，C#，NUnit（Unity Test Framework，EditMode），通过 `unity` CLI 操作用户已打开的 Editor。

**Spec:** `docs/superpowers/specs/2026-09-23-ite-current-tour-design.md`

## Global Constraints

- 代码注释里的决策编号写成 `ite-current-tour Dn`；沿用的旧决策写 `ite-guide-state-machine Dn` / `ite-scan-region-gate Dn` / `design Dn`（原样保留已有的）。不要引用 `openspec/`（已删除，不要读，也不要恢复）。
- Unity 操作只能通过 `unity` CLI，连用户已打开的 Editor。不开 headless Unity，不开第二个 Unity 进程，**不打包、不装机**。
- 注释用中文，风格与周围代码一致。
- 不在范围内，不要改：触发体积尺寸（`IteTourObject.CreateTourObject` 里的 `/ 2`）；`SceneRoles.IsCamera` 及其测试；XR rig 上的 `CharacterController`；区域门禁 spec 的 D5。
- 每个 Task 结束时在 `master` 上提交一次（仓库惯例），不 push。提交信息末尾加 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`。
- 新增 `.cs` 文件编译后 Unity 会生成同名 `.meta`，两者一起 `git add`；删除 `.cs` 时连同 `.meta` 一起 `git rm`。

## 如何编译和跑测试（所有 Task 通用）

编译（同时让 Unity 导入新文件、生成 `.meta`）：

```bash
cd /Users/wwj/Desktop/unity/MR_Base
unity command recompile --project-path .
for i in $(seq 1 40); do s=$(unity command recompile_status --project-path . 2>&1 | tail -1); case "$s" in *completed*|*up_to_date*) echo "$s"; break;; esac; sleep 3; done
```

成功时最后一行包含 `"failed":false,"errors":[]`；编译失败时 `errors` 里列出编译错误。

跑一个程序集，只列出失败项（`A` 取 `Uality.IteTour.Tests` 或 `MRBase.Ite.Host.Tests`）：

```bash
cd /Users/wwj/Desktop/unity/MR_Base
A=Uality.IteTour.Tests
unity command run_tests --project-path . --mode EditMode --filter "$A" --filter_type assembly --timeout 300 2>&1 | tail -1 > "$TMPDIR/r.json"
python3 - "$TMPDIR/r.json" "$A" <<'EOF'
import json,sys
raw=open(sys.argv[1]).read(); parts=raw.split('\t')
if len(parts)<3: print(sys.argv[2], "RAW:", raw[:300]); sys.exit()
j=json.loads(parts[2])
print(sys.argv[2], j.get("Summary"), j.get("error"))
for r in j.get("Results",[]):
    if r["Status"]!="Passed":
        print("  FAIL", r["FullName"], "|", (r["Message"] or "").strip().replace("\n"," ")[:200])
EOF
```

如果输出 `Cannot connect to Unity Editor Pipeline server`，每 3 秒重试 `unity command editor_status --project-path .`，最多 30 秒。仍然连不上就报 BLOCKED，不要自己启动 Unity。

C# 里「测试先失败」的表现是编译失败（新类型不存在）。这一步看到编译错误里提到新类型即可，不要为了让它「运行失败」去写桩代码。

## Review Focus

- **结算窗口一直关不上**（`IteRuntimeDriver` 的协程没跑：驱动对象被停用、`Time.timeScale = 0`）：当前 Tour 会一直不换。预期：`timeScale = 0` 时物理不跑、也不会有进出事件，没有东西丢失；驱动被意外停用则属于装配错误。Task 6 为开窗、关窗各打一行日志，真机上能看出窗口是否关上。EditMode 测不了协程，列入真机验证。
- **人站在体积里时体积被停用**（宿主调用 `SetTriggerVolumesActive(false)`）：Unity 不发 Exit，队列必须被清掉，否则当前 Tour 永远不释放。核心层由 Task 5 的 `ClearVolume_WhileInsideCurrent_ReleasesIt` 覆盖；`IteTourObject` 的通知接线（Task 6）EditMode 测不了，列入真机验证。
- **没有对应 Enter 的 Exit**（例如 Enter 发生在订阅之前）：必须被拒收并告警，计数不能变负，也不能因此释放当前 Tour。由 Task 1 的 `ExitWithoutEnter_IsRejected_AndCountNeverNegative` 和 Task 5 的 `OrphanExit_IsRejected_AndDoesNotReleaseCurrent` 覆盖。
- **锚定发生在物理阶段内**（宿主在物理回调里扫码或激活）：这一步的模拟可能已经跑完，窗口必须多等一步。由 Task 2 的 `BeginSettle_TwoSteps_FirstStepDoesNotClose` 和 Task 5 的 `ScanInsidePhysicsStep_WaitsOneMoreStep_BeforeCurrentCanMove` 覆盖。
- **`alwaysDisplayed` 被隐藏后再显示**：隐藏靠停用内容根 `_mainGroupObject`，其下的视频、音频、动画组件会随之停下；重新显示后应恢复正常播放。EditMode 测不了，列入真机验证。

## 文件结构

| 文件 | 动作 | 职责 |
|---|---|---|
| `Packages/com.uality.ite-tour/Runtime/Core/RegionQueue.cs` | 新建（Task 1），Task 5 迁入 `VolumeTransition` | 按碰撞体计数的有序区域队列 |
| `Packages/com.uality.ite-tour/Runtime/Core/RegionBaseline.cs` | 新建（Task 2） | 帧末基准与锚定结算窗口 |
| `Packages/com.uality.ite-tour/Runtime/Core/CurrentTourRule.cs` | 新建（Task 3） | 当前 Tour 换成谁 |
| `Packages/com.uality.ite-tour/Runtime/Core/TourAssembly.cs` | 修改（Task 3） | 新增 `PlaysOnSelect`、`CanBeCurrent` |
| `Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs` | 修改（Task 4、5） | `ScanState` 加 `CurrentTourId`、删 `PendingTourIds`；已定位后只认当前 Tour 的码 |
| `Packages/com.uality.ite-tour/Runtime/Core/ScanPromptPolicy.cs` | 修改（Task 4） | 只看当前 Tour |
| `Packages/com.uality.ite-tour/Runtime/Core/TourGuide.cs` | 重写（Task 5） | 按顺序调用各模块，合成 `GuideEffect` |
| `Packages/com.uality.ite-tour/Runtime/Core/TourRegionPolicy.cs` | 删除（Task 5） | 由上面三个新模块取代 |
| `Packages/com.uality.ite-tour/Runtime/Core/TourIdLists.cs` | 修改（Task 5） | 删掉不再使用的 `SameSet` |
| `Packages/com.uality.ite-tour/Runtime/Core/TourDirector.cs` | 修改（Task 5、6） | 效果落地、日志、物理步通知、`alwaysDisplayed` 显隐 |
| `Packages/com.uality.ite-tour/Runtime/Core/IteTourObject.cs` | 修改（Task 6） | 进出事件带碰撞体名字；体积停用通知；内容显隐 |
| `Packages/com.uality.ite-tour/Runtime/Core/IteTourAssembler.cs` | 修改（Task 6） | `SetAlwaysDisplayedVisible` |
| `Packages/com.uality.ite-tour/Runtime/Core/IteRuntimeDriver.cs` | 修改（Task 6） | `WaitForFixedUpdate` 循环 |
| `Packages/com.uality.ite-tour/Runtime/Core/IteRuntime.cs` | 修改（Task 6） | 物理模式检查；装配后同步显隐；文档 |
| `Packages/com.uality.ite-tour/Tests/Editor/RegionQueueTests.cs` | 新建（Task 1） | |
| `Packages/com.uality.ite-tour/Tests/Editor/RegionBaselineTests.cs` | 新建（Task 2） | |
| `Packages/com.uality.ite-tour/Tests/Editor/CurrentTourRuleTests.cs` | 新建（Task 3） | |
| `Packages/com.uality.ite-tour/Tests/Editor/TourAssemblyTests.cs` | 修改（Task 3） | |
| `Packages/com.uality.ite-tour/Tests/Editor/TourScanPolicyTests.cs` | 重写（Task 4） | |
| `Packages/com.uality.ite-tour/Tests/Editor/ScanPromptPolicyTests.cs` | 重写（Task 4） | |
| `Packages/com.uality.ite-tour/Tests/Editor/TourGuideTests.cs` | 重写（Task 5） | |
| `Packages/com.uality.ite-tour/Tests/Editor/TourRegionPolicyTests.cs` | 删除（Task 5） | |
| `Packages/com.uality.ite-tour/Tests/Editor/TourIdListsTests.cs` | 修改（Task 5） | 删掉 `SameSet` 的测试 |
| 三份旧文档 | 修改（Task 7） | 标注被取代的决策 |

---

### Task 1: `RegionQueue`——按碰撞体计数的有序区域队列

**Files:**
- Create: `Packages/com.uality.ite-tour/Runtime/Core/RegionQueue.cs`
- Test: `Packages/com.uality.ite-tour/Tests/Editor/RegionQueueTests.cs`

**Interfaces:**
- Consumes: 无
- Produces:
  - `public sealed class RegionQueue`
  - `IReadOnlyList<string> TourIds { get; }`：按计数 0→1 的先后排列；每次变化整体换成新数组，从不原地修改
  - `int CountOf(string tourId)`
  - `void Enter(string tourId)`：null 或空串忽略
  - `bool Exit(string tourId)`：计数为 0 时返回 false（拒收）
  - `void Clear(string tourId)`

- [ ] **Step 1: 写失败的测试**

新建 `Packages/com.uality.ite-tour/Tests/Editor/RegionQueueTests.cs`：

```csharp
using NUnit.Framework;
using Uality.IteTour.Core;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 区域队列（ite-current-tour D1、D2、D11）。相机侧有两个碰撞体（Main Camera 上的球、XR Origin 上的
    /// CharacterController 胶囊），要全部离开才算离开——PICO 实测（2026-09-23）的成对进出在这里回放。
    /// </summary>
    public class RegionQueueTests
    {
        [Test]
        public void Enter_AppendsInEntryOrder()
        {
            var queue = new RegionQueue();

            queue.Enter("a");
            queue.Enter("b");

            Assert.That(queue.TourIds, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(queue.CountOf("a"), Is.EqualTo(1));
        }

        [Test]
        public void SecondColliderEnter_CountsWithoutReordering()
        {
            var queue = new RegionQueue();

            queue.Enter("a");
            queue.Enter("b");
            queue.Enter("a");

            Assert.That(queue.TourIds, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(queue.CountOf("a"), Is.EqualTo(2));
        }

        /// <summary>PICO 14:36:36–39：qtcljiro 两次 Enter、两次 Exit，第一次 Exit 之后还有一个碰撞体在里面。</summary>
        [Test]
        public void TwoColliders_LeavesOnlyWhenBothExit()
        {
            var queue = new RegionQueue();
            queue.Enter("qtcljiro_zrx");
            queue.Enter("qtcljiro_zrx");

            Assert.That(queue.Exit("qtcljiro_zrx"), Is.True);
            Assert.That(queue.TourIds, Is.EqualTo(new[] { "qtcljiro_zrx" }), "还有一个碰撞体在体积里");

            Assert.That(queue.Exit("qtcljiro_zrx"), Is.True);
            Assert.That(queue.TourIds, Is.Empty);
            Assert.That(queue.CountOf("qtcljiro_zrx"), Is.EqualTo(0));
        }

        [Test]
        public void ReenterAfterLeaving_MovesToTail()
        {
            var queue = new RegionQueue();
            queue.Enter("a");
            queue.Enter("b");

            queue.Exit("a");
            queue.Enter("a");

            Assert.That(queue.TourIds, Is.EqualTo(new[] { "b", "a" }));
        }

        [Test]
        public void ExitWithoutEnter_IsRejected_AndCountNeverNegative()
        {
            var queue = new RegionQueue();

            Assert.That(queue.Exit("a"), Is.False);
            Assert.That(queue.CountOf("a"), Is.EqualTo(0));

            queue.Enter("a");
            Assert.That(queue.TourIds, Is.EqualTo(new[] { "a" }), "被拒的 Exit 没有留下欠账");
            Assert.That(queue.CountOf("a"), Is.EqualTo(1));
        }

        [Test]
        public void Clear_DropsTourRegardlessOfCount()
        {
            var queue = new RegionQueue();
            queue.Enter("a");
            queue.Enter("a");
            queue.Enter("b");

            queue.Clear("a");

            Assert.That(queue.TourIds, Is.EqualTo(new[] { "b" }));
            Assert.That(queue.CountOf("a"), Is.EqualTo(0));
        }

        [Test]
        public void Clear_UnknownTour_IsNoOp()
        {
            var queue = new RegionQueue();
            queue.Enter("a");
            var before = queue.TourIds;

            queue.Clear("zzz");
            queue.Clear(null);

            Assert.That(queue.TourIds, Is.SameAs(before));
        }

        [TestCase(null)]
        [TestCase("")]
        public void Enter_EmptyId_IsIgnored(string tourId)
        {
            var queue = new RegionQueue();

            queue.Enter(tourId);

            Assert.That(queue.TourIds, Is.Empty);
        }

        [Test]
        public void TourIds_IsReplacedNotMutated()
        {
            var queue = new RegionQueue();
            queue.Enter("a");
            var before = queue.TourIds;

            queue.Enter("b");
            queue.Exit("a");

            Assert.That(before, Is.EqualTo(new[] { "a" }), "帧末基准持有旧引用，不能被原地改掉");
        }
    }
}
```

- [ ] **Step 2: 编译，确认失败**

按「如何编译和跑测试」编译。Expected: 编译失败，错误提到 `RegionQueue` 找不到。

- [ ] **Step 3: 实现 `RegionQueue`**

新建 `Packages/com.uality.ite-tour/Runtime/Core/RegionQueue.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 人在哪些区域里、按什么先后（ite-current-tour D1、D2）。
    ///
    /// 每个 Tour 一个计数：相机侧每个碰撞体进入加 1、离开减 1，归零才算离开。相机侧不止一个碰撞体——
    /// Main Camera 上的球和 XR Origin 上的 CharacterController 胶囊都被 <see cref="SceneRoles.IsCamera"/>
    /// 认作相机，两者进出体积的时刻不同；只按第一次离开算，Tour 会在还有碰撞体在里面时就被移出（PICO 实测）。
    ///
    /// 列表按计数 0→1 的先后排列，离开后再进入排到队尾。不变式：计数 &gt; 0 ⟺ 在列表里。
    /// 不认识展示类型、导览状态与锚定。
    /// </summary>
    public sealed class RegionQueue
    {
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        // 只整体替换、从不原地修改：帧末基准（RegionBaseline）直接持有旧引用。
        private string[] _tourIds = Array.Empty<string>();

        public IReadOnlyList<string> TourIds => _tourIds;

        public int CountOf(string tourId)
            => tourId != null && _counts.TryGetValue(tourId, out var count) ? count : 0;

        public void Enter(string tourId)
        {
            if (string.IsNullOrEmpty(tourId))
            {
                return;
            }

            int count = CountOf(tourId);
            _counts[tourId] = count + 1;

            if (count == 0)
            {
                var next = new List<string>(_tourIds) { tourId };
                _tourIds = next.ToArray();
            }
        }

        /// <returns>
        /// false：计数已是 0，这次离开没有对应的进入，被拒收（ite-current-tour D11）。计数永远不会变负——
        /// 变负的计数会让下一次进入「抵消」掉，人明明在体积里，队列里却没有它。
        /// </returns>
        public bool Exit(string tourId)
        {
            int count = CountOf(tourId);
            if (count == 0)
            {
                return false;
            }

            if (count > 1)
            {
                _counts[tourId] = count - 1;
                return true;
            }

            Remove(tourId);
            return true;
        }

        /// <summary>体积被停用：Unity 不发离开，计数直接清零（ite-current-tour D11）。</summary>
        public void Clear(string tourId)
        {
            if (CountOf(tourId) > 0)
            {
                Remove(tourId);
            }
        }

        private void Remove(string tourId)
        {
            _counts.Remove(tourId);

            var next = new List<string>(_tourIds);
            next.Remove(tourId);
            _tourIds = next.ToArray();
        }
    }
}
```

- [ ] **Step 4: 编译并跑测试，确认通过**

编译；跑 `A=Uality.IteTour.Tests`。Expected: 编译成功，`RegionQueueTests` 全部通过，其他测试不受影响。

- [ ] **Step 5: 提交**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git add Packages/com.uality.ite-tour/Runtime/Core/RegionQueue.cs Packages/com.uality.ite-tour/Runtime/Core/RegionQueue.cs.meta \
        Packages/com.uality.ite-tour/Tests/Editor/RegionQueueTests.cs Packages/com.uality.ite-tour/Tests/Editor/RegionQueueTests.cs.meta
git commit -m "$(cat <<'EOF'
feat(ite): 新增按碰撞体计数的区域队列 RegionQueue

相机侧有两个碰撞体时，要全部离开才算离开（ite-current-tour D1、D2、D11）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: `RegionBaseline`——帧末基准与锚定结算窗口

**Files:**
- Create: `Packages/com.uality.ite-tour/Runtime/Core/RegionBaseline.cs`
- Test: `Packages/com.uality.ite-tour/Tests/Editor/RegionBaselineTests.cs`

**Interfaces:**
- Consumes: 无
- Produces:
  - `public sealed class RegionBaseline`
  - `IReadOnlyList<string> Baseline { get; }`：初始为空数组
  - `bool IsSettling { get; }`
  - `void BeginSettle(int physicsSteps)`：`physicsSteps < 1` 抛 `ArgumentOutOfRangeException`；窗口开着时取两者中较长的
  - `bool AfterPhysicsStep(IReadOnlyList<string> current)`：这一步关上窗口时返回 true，并把基准设为 `current`
  - `void Advance(IReadOnlyList<string> current)`：帧末把基准前移到 `current`

- [ ] **Step 1: 写失败的测试**

新建 `Packages/com.uality.ite-tour/Tests/Editor/RegionBaselineTests.cs`：

```csharp
using System;
using NUnit.Framework;
using Uality.IteTour.Core;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 这次区域变化是人动了，还是锚定把体积挪了（ite-current-tour D9）。锚定后第一次物理步里到达的进出
    /// 都算锚定造成的：关窗时并入基准，不算人移动。
    /// </summary>
    public class RegionBaselineTests
    {
        private static readonly string[] Empty = new string[0];

        [Test]
        public void Fresh_IsNotSettling_AndBaselineIsEmpty()
        {
            var baseline = new RegionBaseline();

            Assert.That(baseline.IsSettling, Is.False);
            Assert.That(baseline.Baseline, Is.Empty);
        }

        [Test]
        public void Advance_MovesBaseline()
        {
            var baseline = new RegionBaseline();

            baseline.Advance(new[] { "a" });

            Assert.That(baseline.Baseline, Is.EqualTo(new[] { "a" }));
        }

        /// <summary>PICO 14:36:31：锚定前在 ujf5 里，锚定后那一步物理里 ujf5 离开、hncx 进入。</summary>
        [Test]
        public void BeginSettle_OneStep_ClosesAfterFirstPhysicsStep_AndRebases()
        {
            var baseline = new RegionBaseline();
            baseline.Advance(new[] { "ujf5bo31_frb" });

            baseline.BeginSettle(1);
            Assert.That(baseline.IsSettling, Is.True);

            Assert.That(baseline.AfterPhysicsStep(new[] { "hncxtzfe_p4d" }), Is.True, "这一步关窗");
            Assert.That(baseline.IsSettling, Is.False);
            Assert.That(baseline.Baseline, Is.EqualTo(new[] { "hncxtzfe_p4d" }), "锚定造成的变化并入基准");
        }

        /// <summary>锚定发生在物理阶段内：这一步的模拟可能早于锚定就跑完了，要再等一步。</summary>
        [Test]
        public void BeginSettle_TwoSteps_FirstStepDoesNotClose()
        {
            var baseline = new RegionBaseline();

            baseline.BeginSettle(2);

            Assert.That(baseline.AfterPhysicsStep(new[] { "a" }), Is.False);
            Assert.That(baseline.IsSettling, Is.True);
            Assert.That(baseline.AfterPhysicsStep(new[] { "b" }), Is.True);
            Assert.That(baseline.Baseline, Is.EqualTo(new[] { "b" }));
        }

        [Test]
        public void AfterPhysicsStep_WhenNotSettling_LeavesBaselineAlone()
        {
            var baseline = new RegionBaseline();
            baseline.Advance(new[] { "a" });

            Assert.That(baseline.AfterPhysicsStep(new[] { "b" }), Is.False);
            Assert.That(baseline.Baseline, Is.EqualTo(new[] { "a" }), "窗口外基准只在帧末前移");
        }

        [Test]
        public void BeginSettle_WhileSettling_KeepsTheLongerWindow()
        {
            var baseline = new RegionBaseline();

            baseline.BeginSettle(2);
            baseline.BeginSettle(1);

            Assert.That(baseline.AfterPhysicsStep(Empty), Is.False);
            Assert.That(baseline.AfterPhysicsStep(Empty), Is.True);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void BeginSettle_NonPositiveSteps_Throws(int steps)
        {
            var baseline = new RegionBaseline();

            Assert.Throws<ArgumentOutOfRangeException>(() => baseline.BeginSettle(steps));
        }
    }
}
```

- [ ] **Step 2: 编译，确认失败**

编译。Expected: 编译失败，错误提到 `RegionBaseline` 找不到。

- [ ] **Step 3: 实现 `RegionBaseline`**

新建 `Packages/com.uality.ite-tour/Runtime/Core/RegionBaseline.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 这次区域变化是人动了，还是锚定把体积挪了（ite-current-tour D9）。
    ///
    /// 帧末拿「基准 → 当前队列」判断人有没有离开当前 Tour；基准是上一帧末的队列。锚定会把所有体积挪到
    /// 真实位置，下一个物理步里到达的进出是锚定造成的——这段时间叫结算窗口：窗口里的变化不拿来判断，
    /// 关窗时直接并入基准。
    ///
    /// 窗口按物理步计数、不按时长：本工程物理在 FixedUpdate 里自动模拟，每一步的顺序是
    /// FixedUpdate → 模拟（transform 改动此时同步进物理）→ OnTrigger* → WaitForFixedUpdate，
    /// 所以锚定后第一次 WaitForFixedUpdate 返回时，锚定造成的进出已全部到达。不认识展示类型与当前 Tour。
    /// </summary>
    public sealed class RegionBaseline
    {
        private IReadOnlyList<string> _baseline = Array.Empty<string>();
        private int _stepsToSettle;

        /// <summary>上一帧末（或关窗时）的队列。</summary>
        public IReadOnlyList<string> Baseline => _baseline;

        public bool IsSettling => _stepsToSettle > 0;

        /// <summary>
        /// 锚定挪动了体积：接下来 <paramref name="physicsSteps"/> 个物理步里到达的进出都算锚定造成的。
        /// 窗口已开着时取较长的那个。
        /// </summary>
        public void BeginSettle(int physicsSteps)
        {
            if (physicsSteps < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(physicsSteps), physicsSteps, "至少等一个物理步");
            }

            _stepsToSettle = Math.Max(_stepsToSettle, physicsSteps);
        }

        /// <summary>一个物理步的触发回调全部到达之后调用。这一步关上窗口时返回 true，基准设为 <paramref name="current"/>。</summary>
        public bool AfterPhysicsStep(IReadOnlyList<string> current)
        {
            if (_stepsToSettle == 0)
            {
                return false;
            }

            _stepsToSettle--;
            if (_stepsToSettle > 0)
            {
                return false;
            }

            _baseline = current ?? Array.Empty<string>();
            return true;
        }

        /// <summary>帧末：基准前移到 <paramref name="current"/>。</summary>
        public void Advance(IReadOnlyList<string> current) => _baseline = current ?? Array.Empty<string>();
    }
}
```

- [ ] **Step 4: 编译并跑测试，确认通过**

编译；跑 `A=Uality.IteTour.Tests`。Expected: `RegionBaselineTests` 全部通过。

- [ ] **Step 5: 提交**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git add Packages/com.uality.ite-tour/Runtime/Core/RegionBaseline.cs Packages/com.uality.ite-tour/Runtime/Core/RegionBaseline.cs.meta \
        Packages/com.uality.ite-tour/Tests/Editor/RegionBaselineTests.cs Packages/com.uality.ite-tour/Tests/Editor/RegionBaselineTests.cs.meta
git commit -m "$(cat <<'EOF'
feat(ite): 新增锚定结算窗口 RegionBaseline

锚定后第一个物理步里的进出算锚定造成的，关窗时并入基准（ite-current-tour D9）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: `CurrentTourRule` 与展示类型规则

**Files:**
- Create: `Packages/com.uality.ite-tour/Runtime/Core/CurrentTourRule.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/TourAssembly.cs`（在 `RetainsSceneWhenDeactivated` 之后追加两个方法）
- Test: `Packages/com.uality.ite-tour/Tests/Editor/CurrentTourRuleTests.cs`（新建）、`Packages/com.uality.ite-tour/Tests/Editor/TourAssemblyTests.cs`（追加）

**Interfaces:**
- Consumes: `TourIdLists.Contains(IReadOnlyList<string>, string)`（已有）
- Produces:
  - `public static class CurrentTourRule`，`static string Next(string current, IReadOnlyList<string> baseline, IReadOnlyList<string> queue)`
  - `TourAssembly.PlaysOnSelect(IteSpaceScene.Tour.DisplayType) : bool`：只有 `regionalTrigger` 为 true
  - `TourAssembly.CanBeCurrent(IteSpaceScene.Tour.DisplayType) : bool`：有触发体积的类型为 true（`alwaysDisplayed` 为 false）

- [ ] **Step 1: 写失败的测试**

新建 `Packages/com.uality.ite-tour/Tests/Editor/CurrentTourRuleTests.cs`：

```csharp
using NUnit.Framework;
using Uality.IteTour.Core;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 当前 Tour（C）换成谁（ite-current-tour D3、D4）：只有人离开 C 的体积才换，换成队尾；C 为空时取队尾。
    /// 不认识展示类型——队尾是 normal 也照取，播不播由别的模块决定。
    /// </summary>
    public class CurrentTourRuleTests
    {
        private static string[] Q(params string[] ids) => ids;

        [Test]
        public void NoCurrent_TakesTail()
        {
            Assert.That(CurrentTourRule.Next(null, Q(), Q("a", "b")), Is.EqualTo("b"));
        }

        [Test]
        public void NoCurrent_EmptyQueue_StaysEmpty()
        {
            Assert.That(CurrentTourRule.Next(null, Q("a"), Q()), Is.Null);
        }

        [Test]
        public void EnteringOtherRegions_KeepsCurrent()
        {
            Assert.That(CurrentTourRule.Next("a", Q("a"), Q("a", "b")), Is.EqualTo("a"));
        }

        [Test]
        public void LeavingOtherRegions_KeepsCurrent()
        {
            Assert.That(CurrentTourRule.Next("a", Q("a", "b"), Q("a")), Is.EqualTo("a"));
        }

        [Test]
        public void CurrentLeft_TakesTail()
        {
            Assert.That(CurrentTourRule.Next("a", Q("a", "b", "c"), Q("b", "c")), Is.EqualTo("c"));
        }

        [Test]
        public void CurrentLeft_EmptyQueue_BecomesEmpty()
        {
            Assert.That(CurrentTourRule.Next("a", Q("a"), Q()), Is.Null);
        }

        [Test]
        public void CurrentLeftIntoNeighbourInOneStep_SwitchesDirectly()
        {
            Assert.That(CurrentTourRule.Next("a", Q("a"), Q("b")), Is.EqualTo("b"));
        }

        /// <summary>锚定后人不在 C 的体积里：离开、进入别的区域都不换，直到人走进 C 再走出来。</summary>
        [Test]
        public void CurrentNeverInside_IsKept_WhateverElseChanges()
        {
            Assert.That(CurrentTourRule.Next("a", Q("b"), Q("b", "c")), Is.EqualTo("a"));
            Assert.That(CurrentTourRule.Next("a", Q("b"), Q()), Is.EqualTo("a"));
        }

        [Test]
        public void CurrentEntered_IsKept()
        {
            Assert.That(CurrentTourRule.Next("a", Q("b"), Q("b", "a")), Is.EqualTo("a"));
        }

        [Test]
        public void ExitAndReenterWithinFrame_KeepsCurrent()
        {
            Assert.That(CurrentTourRule.Next("a", Q("a", "b"), Q("b", "a")), Is.EqualTo("a"));
        }

        [Test]
        public void NullLists_AreTreatedAsEmpty()
        {
            Assert.That(CurrentTourRule.Next(null, null, null), Is.Null);
            Assert.That(CurrentTourRule.Next("a", null, null), Is.EqualTo("a"));
        }
    }
}
```

在 `Packages/com.uality.ite-tour/Tests/Editor/TourAssemblyTests.cs` 的类里（最后一个测试之后、类的右括号之前）追加：

```csharp
        [TestCase(IteSpaceScene.Tour.DisplayType.regionalTrigger, true)]
        [TestCase(IteSpaceScene.Tour.DisplayType.normal, false)]
        [TestCase(IteSpaceScene.Tour.DisplayType.alwaysDisplayed, false)]
        public void PlaysOnSelect_OnlyRegionalTrigger(IteSpaceScene.Tour.DisplayType displayType, bool expected)
        {
            Assert.That(TourAssembly.PlaysOnSelect(displayType), Is.EqualTo(expected),
                "碰撞永远不激活 normal（ite-current-tour D5）");
        }

        [TestCase(IteSpaceScene.Tour.DisplayType.regionalTrigger, true)]
        [TestCase(IteSpaceScene.Tour.DisplayType.normal, true)]
        [TestCase(IteSpaceScene.Tour.DisplayType.alwaysDisplayed, false)]
        public void CanBeCurrent_OnlyToursWithAVolume(IteSpaceScene.Tour.DisplayType displayType, bool expected)
        {
            Assert.That(TourAssembly.CanBeCurrent(displayType), Is.EqualTo(expected),
                "没有体积就永远离不开，不能当当前 Tour（ite-current-tour D10、I5）");
        }
```

- [ ] **Step 2: 编译，确认失败**

编译。Expected: 编译失败，错误提到 `CurrentTourRule`、`PlaysOnSelect`、`CanBeCurrent` 找不到。

- [ ] **Step 3: 实现**

新建 `Packages/com.uality.ite-tour/Runtime/Core/CurrentTourRule.cs`：

```csharp
using System.Collections.Generic;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 当前 Tour（C）换成谁（ite-current-tour D3、D4）。C 持有最高优先级：进入、离开别的区域都不影响它，
    /// 只有人离开 C 的体积（C 在基准里、不在当前队列里）才换成当前队列的队尾；C 为空时取队尾。
    ///
    /// 不认识展示类型与导览状态：队尾是 normal 也照取（它会挡住更早进入的 regionalTrigger），
    /// 选中之后播不播归 <see cref="TourAssembly.PlaysOnSelect"/>，什么时候判断归 <see cref="TourGuide"/>。
    /// 锚定后人本来就不在 C 的体积里时，C 不在基准里，于是保持到人走进去再走出来——不需要额外的标志。
    /// </summary>
    public static class CurrentTourRule
    {
        public static string Next(string current, IReadOnlyList<string> baseline, IReadOnlyList<string> queue)
        {
            bool left = TourIdLists.Contains(baseline, current) && !TourIdLists.Contains(queue, current);

            return string.IsNullOrEmpty(current) || left ? Tail(queue) : current;
        }

        private static string Tail(IReadOnlyList<string> queue)
            => queue == null || queue.Count == 0 ? null : queue[queue.Count - 1];
    }
}
```

在 `Packages/com.uality.ite-tour/Runtime/Core/TourAssembly.cs` 里，`RetainsSceneWhenDeactivated` 方法之后追加：

```csharp

        /// <summary>
        /// 被选为当前 Tour 后是否立即播放（ite-current-tour D5）：regionalTrigger 一进区域就播；
        /// normal 要扫到它的码才播——碰撞永远不激活 normal。
        /// </summary>
        public static bool PlaysOnSelect(IteSpaceScene.Tour.DisplayType displayType)
            => displayType == IteSpaceScene.Tour.DisplayType.regionalTrigger;

        /// <summary>
        /// 能不能当当前 Tour（ite-current-tour D10、I5）。当前 Tour 只在人离开它的体积时才换，没有体积的
        /// alwaysDisplayed 一旦当上就永远离不开；它的显示由导览状态决定（ite-current-tour D8），不需要当当前 Tour。
        /// </summary>
        public static bool CanBeCurrent(IteSpaceScene.Tour.DisplayType displayType)
            => AllowsTriggerVolume(displayType);
```

- [ ] **Step 4: 编译并跑测试，确认通过**

编译；跑 `A=Uality.IteTour.Tests`。Expected: `CurrentTourRuleTests` 与新增的 `TourAssemblyTests` 用例全部通过。

- [ ] **Step 5: 提交**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git add Packages/com.uality.ite-tour/Runtime/Core/CurrentTourRule.cs Packages/com.uality.ite-tour/Runtime/Core/CurrentTourRule.cs.meta \
        Packages/com.uality.ite-tour/Runtime/Core/TourAssembly.cs \
        Packages/com.uality.ite-tour/Tests/Editor/CurrentTourRuleTests.cs Packages/com.uality.ite-tour/Tests/Editor/CurrentTourRuleTests.cs.meta \
        Packages/com.uality.ite-tour/Tests/Editor/TourAssemblyTests.cs
git commit -m "$(cat <<'EOF'
feat(ite): 新增 CurrentTourRule 与展示类型规则

当前 Tour 只在人离开它时换成队尾；只有 regionalTrigger 选中即播，
alwaysDisplayed 不能当当前 Tour（ite-current-tour D3、D4、D5、D10）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: 扫码策略与提示策略只看当前 Tour

**Files:**
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/ScanPromptPolicy.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/TourGuide.cs`（只改 `Snapshot()` 一行，过渡用，Task 5 整体重写）
- Test: 重写 `Packages/com.uality.ite-tour/Tests/Editor/TourScanPolicyTests.cs`、`Packages/com.uality.ite-tour/Tests/Editor/ScanPromptPolicyTests.cs`

**Interfaces:**
- Consumes: `TourAssembly.CanBeCurrent`（Task 3）
- Produces:
  - `ScanState.CurrentTourId : string`（新增字段）。`ScanState.PendingTourIds` 本 Task 保留，Task 5 删除；本 Task 之后两个策略都不再读它。
  - `TourScanPolicy.Decide(ScanState, IReadOnlyList<TourDescriptor>, string)`：签名不变；等待扫码时扫到 `alwaysDisplayed` 返回 `ScanAction.Reanchor`（只锚定）
  - `ScanPromptPolicy.Decide(ScanState, IReadOnlyList<TourDescriptor>)`：签名不变

- [ ] **Step 1: 写失败的测试**

把 `Packages/com.uality.ite-tour/Tests/Editor/TourScanPolicyTests.cs` 整个替换为：

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 扫描决策是这个包里最容易在 PICO 上出问题、又最难在真机上复现的一段。
    /// D14 把它从「三个订阅者的相互作用 + `_canAnchor` 的 await 竞态」抽成纯函数，
    /// 就是为了让这组测试能够存在。已定位后只认当前 Tour 的码（ite-current-tour D6）。
    /// </summary>
    public class TourScanPolicyTests
    {
        private const IteSpaceScene.Tour.DisplayType Normal = IteSpaceScene.Tour.DisplayType.normal;
        private const IteSpaceScene.Tour.DisplayType Regional = IteSpaceScene.Tour.DisplayType.regionalTrigger;
        private const IteSpaceScene.Tour.DisplayType Always = IteSpaceScene.Tour.DisplayType.alwaysDisplayed;

        private static TourDescriptor Tour(
            string id,
            IteSpaceScene.Tour.DisplayType type = Normal,
            bool secondAnchorAvailable = false)
            => new TourDescriptor
            {
                TourId = id,
                DisplayType = type,
                SecondAnchorAvailable = secondAnchorAvailable,
            };

        private static List<TourDescriptor> Tours(params TourDescriptor[] tours) => new List<TourDescriptor>(tours);

        private static ScanState Anchored(string currentTourId, string activeTourId = null)
            => new ScanState { State = GuideState.Anchored, CurrentTourId = currentTourId, ActiveTourId = activeTourId };

        // ---- 前置门禁 ----

        [Test]
        public void Decide_WhenSuspended_Ignores()
        {
            var state = new ScanState { State = GuideState.Suspended };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1")), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
        }

        [TestCase(null)]
        [TestCase("")]
        public void Decide_WithoutMarkerId_Ignores(string markerId)
        {
            var state = new ScanState { State = GuideState.AwaitingScan };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1")), markerId);

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
        }

        [Test]
        public void Decide_WhenMarkerMatchesNoTour_Ignores()
        {
            var state = new ScanState { State = GuideState.AwaitingScan };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1")), "somethingElse");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
        }

        // ---- 等待扫码定位：唯一不受规则约束的入口 ----

        [TestCase(Normal)]
        [TestCase(Regional)]
        public void Decide_AwaitingScan_ActivatesToursWithAVolume(IteSpaceScene.Tour.DisplayType type)
        {
            var state = new ScanState { State = GuideState.AwaitingScan };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1", type)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(decision.TourId, Is.EqualTo("t1"));
        }

        /// <summary>ite-current-tour D10：alwaysDisplayed 只拿来锚定，不当当前 Tour、不当在播。</summary>
        [Test]
        public void Decide_AwaitingScan_AlwaysDisplayed_AnchorsWithoutActivating()
        {
            var state = new ScanState { State = GuideState.AwaitingScan };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("a1", Always)), "a1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Reanchor));
            Assert.That(decision.TourId, Is.EqualTo("a1"));
            Assert.That(decision.ConsumesSecondAnchor, Is.False);
        }

        [Test]
        public void Decide_AwaitingScan_IgnoresCurrentTour()
        {
            var state = new ScanState { State = GuideState.AwaitingScan, CurrentTourId = "other" };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1"), Tour("other")), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(decision.TourId, Is.EqualTo("t1"));
        }

        /// <summary>
        /// D14 所选语义，单独钉住：等待扫码时激活 regionalTrigger 后，本次扫码
        /// **不**消耗其二次锚定许可。
        ///
        /// 源实现中第二个处理器此刻会去查 CanSecondAnchor()，而 Enable() 尚未完成、
        /// _canAnchor 仍为 false，因此不消耗。内容为空的 Tour 会同步走完 Enable()
        /// 从而走出另一分支——那个偶然分支本次被规范掉了。
        /// </summary>
        [Test]
        public void Decide_AwaitingScanOnRegionalTrigger_DoesNotConsumeSecondAnchor_D14()
        {
            var state = new ScanState { State = GuideState.AwaitingScan };

            var decision = TourScanPolicy.Decide(
                state, Tours(Tour("t1", Regional, secondAnchorAvailable: true)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(decision.ConsumesSecondAnchor, Is.False);
        }

        // ---- 已定位：只认当前 Tour 的码（ite-current-tour D6）----

        [Test]
        public void Decide_Anchored_MarkerIsNotCurrent_Ignores()
        {
            var decision = TourScanPolicy.Decide(
                Anchored("t2", "t2"), Tours(Tour("t1", Regional), Tour("t2", Regional)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore),
                "当前 Tour 优先级最高：人站在 t1 的区域里扫 t1 的码也不认");
        }

        [Test]
        public void Decide_Anchored_NoCurrent_Ignores()
        {
            var decision = TourScanPolicy.Decide(Anchored(null), Tours(Tour("t1")), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
        }

        // ---- 当前 Tour 是 normal ----

        [Test]
        public void Decide_CurrentNormalNotPlaying_Activates()
        {
            var decision = TourScanPolicy.Decide(Anchored("t1"), Tours(Tour("t1", Normal)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(decision.TourId, Is.EqualTo("t1"));
        }

        [Test]
        public void Decide_CurrentNormalPlaying_Ignores()
        {
            var decision = TourScanPolicy.Decide(Anchored("t1", "t1"), Tours(Tour("t1", Normal)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
        }

        // ---- 当前 Tour 是 regionalTrigger ----

        [Test]
        public void Decide_CurrentRegionalNotPlaying_ActivatesWithoutConsumingSecondAnchor()
        {
            var decision = TourScanPolicy.Decide(
                Anchored("t1"), Tours(Tour("t1", Regional, secondAnchorAvailable: true)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(decision.ConsumesSecondAnchor, Is.False,
                "源实现此处的 SecondAnchored() 会被 Enable() 续体覆盖，是死代码（D14）");
        }

        [Test]
        public void Decide_CurrentRegionalPlayingWithAllowance_ReanchorsAndConsumesIt()
        {
            var decision = TourScanPolicy.Decide(
                Anchored("t1", "t1"), Tours(Tour("t1", Regional, secondAnchorAvailable: true)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Reanchor));
            Assert.That(decision.TourId, Is.EqualTo("t1"));
            Assert.That(decision.ConsumesSecondAnchor, Is.True,
                "重锚路径不触发 Enable()，所以这次消耗真正生效");
        }

        [Test]
        public void Decide_CurrentRegionalPlayingWithoutAllowance_Ignores()
        {
            var decision = TourScanPolicy.Decide(
                Anchored("t1", "t1"), Tours(Tour("t1", Regional, secondAnchorAvailable: false)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
        }

        // ---- alwaysDisplayed ----

        [Test]
        public void Decide_AlwaysDisplayed_IgnoredOnceAnchored()
        {
            var decision = TourScanPolicy.Decide(Anchored("a1"), Tours(Tour("a1", Always)), "a1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore),
                "alwaysDisplayed 不会是当前 Tour（I5）；万一是，也不参与扫码切换");
        }

        // ---- 重复推入 ----

        /// <summary>
        /// ITE 需要的是**可重复触发的原始事件流**，不是去重后的（design D13）。
        /// 同一个码连扫两次，第二次必须照常求值。
        /// </summary>
        [Test]
        public void Decide_IsPurelyStateDriven_SameMarkerTwiceIsNotDeduplicated()
        {
            var tours = Tours(Tour("t1", Regional, secondAnchorAvailable: true));
            var afterActivation = Anchored("t1", "t1");

            var first = TourScanPolicy.Decide(afterActivation, tours, "t1");
            var second = TourScanPolicy.Decide(afterActivation, tours, "t1");

            Assert.That(first.Action, Is.EqualTo(ScanAction.Reanchor));
            Assert.That(second.Action, Is.EqualTo(ScanAction.Reanchor),
                "相同状态下相同输入必须给出相同决策——去重是效果层的事，不是决策的事");
        }
    }
}
```

把 `Packages/com.uality.ite-tour/Tests/Editor/ScanPromptPolicyTests.cs` 整个替换为：

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 「要不要提示用户去扫码、提示哪个 Tour」。
    ///
    /// 源实现是个每 0.75 秒轮询的协程，直接调 <c>ScanPreviewUI.Instance.Show()/Hide()</c>——
    /// **决策**是 ITE 业务，**渲染**不是（design D5）。这里只留决策，渲染交给宿主订阅广播。
    ///
    /// 已定位后只看当前 Tour（ite-current-tour D7）：当前 Tour 是 normal 且还没播，才提示扫它。
    /// </summary>
    public class ScanPromptPolicyTests
    {
        private const IteSpaceScene.Tour.DisplayType Normal = IteSpaceScene.Tour.DisplayType.normal;
        private const IteSpaceScene.Tour.DisplayType Regional = IteSpaceScene.Tour.DisplayType.regionalTrigger;

        private static TourDescriptor Tour(string id, IteSpaceScene.Tour.DisplayType type)
            => new TourDescriptor { TourId = id, DisplayType = type };

        private static List<TourDescriptor> Tours(params TourDescriptor[] tours) => new List<TourDescriptor>(tours);

        private static ScanState Anchored(string currentTourId, string activeTourId = null)
            => new ScanState { State = GuideState.Anchored, CurrentTourId = currentTourId, ActiveTourId = activeTourId };

        /// <summary>已经有 Tour 在放，提示无条件收起——这条优先级最高。</summary>
        [Test]
        public void Decide_WhileATourIsPlaying_Hides()
        {
            var prompt = ScanPromptPolicy.Decide(Anchored("n1", "n1"), Tours(Tour("n1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }

        /// <summary>等待扫码定位：提示不带 Tour 名，因为哪个都行。</summary>
        [Test]
        public void Decide_AwaitingScan_ShowsWithoutNamingTours()
        {
            var state = new ScanState { State = GuideState.AwaitingScan };

            var prompt = ScanPromptPolicy.Decide(state, Tours(Tour("n1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Visible));
            Assert.That(prompt.TourIds, Is.Empty);
        }

        /// <summary>头显摘下：没人在看，一律不提示。</summary>
        [Test]
        public void Decide_WhenSuspended_Hides()
        {
            var state = new ScanState { State = GuideState.Suspended };

            var prompt = ScanPromptPolicy.Decide(state, Tours(Tour("n1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }

        /// <summary>当前 Tour 是 normal、还没播：只提示它，别的码扫了也不认（ite-current-tour D6）。</summary>
        [Test]
        public void Decide_CurrentNormalNotPlaying_ShowsOnlyIt()
        {
            var prompt = ScanPromptPolicy.Decide(
                Anchored("n1"), Tours(Tour("n1", Normal), Tour("n2", Normal), Tour("r1", Regional)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Visible));
            Assert.That(prompt.TourIds, Is.EqualTo(new[] { "n1" }));
        }

        [Test]
        public void Decide_CurrentRegional_Hides()
        {
            var prompt = ScanPromptPolicy.Decide(Anchored("r1"), Tours(Tour("r1", Regional)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }

        /// <summary>
        /// 已定位、没有当前 Tour（人在所有区域之外）：扫什么都不认，提示就是在叫人做无效操作
        /// （沿用 ite-scan-region-gate D3 的理由）。
        /// </summary>
        [Test]
        public void Decide_Anchored_NoCurrent_Hides()
        {
            var prompt = ScanPromptPolicy.Decide(Anchored(null), Tours(Tour("n1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }

        [Test]
        public void Decide_CurrentUnknown_Hides()
        {
            var prompt = ScanPromptPolicy.Decide(Anchored("ghost"), Tours(Tour("n1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }
    }
}
```

- [ ] **Step 2: 编译，确认失败**

编译。Expected: 编译失败，错误提到 `ScanState` 没有 `CurrentTourId`。

- [ ] **Step 3: 实现**

在 `Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs` 中：

(a) 把 `ScanAction.Reanchor` 的注释

```csharp
        /// <summary>保持当前 Tour，仅按新位姿重新锚定（不销毁重建内容）。</summary>
        Reanchor,
```

改为

```csharp
        /// <summary>
        /// 不换 Tour，仅按新位姿重新锚定（不销毁重建内容）。等待扫码时扫到 alwaysDisplayed 也走这里：
        /// 只锚定，不当当前 Tour（ite-current-tour D10）。
        /// </summary>
        Reanchor,
```

(b) 在 `ScanState` 里，`public GuideState State;` 之后、`public string ActiveTourId;` 之前插入：

```csharp

        /// <summary>
        /// 持有优先级的 Tour（ite-current-tour D3）。已定位后扫码只认它的码（ite-current-tour D6）。
        /// </summary>
        public string CurrentTourId;

```

(c) 把 `PendingTourIds` 字段上的注释改为：

```csharp
        /// <summary>过渡字段，两个策略都已不再读它；ite-current-tour 实施时随 TourGuide 重写一并删除。</summary>
```

(d) 把整个 `Decide` 方法替换为：

```csharp
        public static ScanDecision Decide(ScanState state, IReadOnlyList<TourDescriptor> tours, string markerId)
        {
            if (state.State == GuideState.Suspended || string.IsNullOrEmpty(markerId) || tours == null)
            {
                return ScanDecision.Ignore;
            }

            if (!TryFind(tours, markerId, out var tour))
            {
                return ScanDecision.Ignore;
            }

            if (state.State == GuideState.AwaitingScan)
            {
                // 等待扫码定位：唯一不受规则约束的入口——不看区域、不看当前 Tour，任何匹配的码都认
                // （ite-scan-region-gate D2）。
                //
                // alwaysDisplayed 只拿来锚定：它没有触发体积，当了当前 Tour 就永远离不开
                // （ite-current-tour D10）。
                if (!TourAssembly.CanBeCurrent(tour.DisplayType))
                {
                    return new ScanDecision
                    {
                        Action = ScanAction.Reanchor,
                        TourId = tour.TourId,
                        ConsumesSecondAnchor = false,
                    };
                }

                // ConsumesSecondAnchor 为 false 是 D14 的所选语义：源实现中第二个
                // 处理器此刻会去查 CanSecondAnchor()，而 Enable() 尚未完成、
                // _canAnchor 仍为 false，因此不消耗。
                return new ScanDecision
                {
                    Action = ScanAction.Activate,
                    TourId = tour.TourId,
                    ConsumesSecondAnchor = false,
                };
            }

            // 已定位：只认当前 Tour 的码（ite-current-tour D6，取代 ite-scan-region-gate D1）。
            // 当前 Tour 优先级最高：人站在别的 Tour 的区域里、扫别的码，一律不认。
            if (tour.TourId != state.CurrentTourId)
            {
                return ScanDecision.Ignore;
            }

            switch (tour.DisplayType)
            {
                case IteSpaceScene.Tour.DisplayType.normal:
                    // 当前 Tour 是 normal：还没播就扫它的码开始播；已在播则忽略
                    return tour.TourId == state.ActiveTourId
                        ? ScanDecision.Ignore
                        : new ScanDecision { Action = ScanAction.Activate, TourId = tour.TourId };

                case IteSpaceScene.Tour.DisplayType.regionalTrigger:
                    if (tour.TourId == state.ActiveTourId)
                    {
                        // 重锚路径不触发 Enable()，所以这里消耗的许可不会被覆盖回来 —— 真正生效。
                        return tour.SecondAnchorAvailable
                            ? new ScanDecision
                            {
                                Action = ScanAction.Reanchor,
                                TourId = tour.TourId,
                                ConsumesSecondAnchor = true,
                            }
                            : ScanDecision.Ignore;
                    }

                    // 按 D14 所选语义，激活不消耗二次锚定许可（源实现此处的 SecondAnchored()
                    // 会被 Enable() 续体里的 _canAnchor = true 覆盖掉，是死代码）。
                    return new ScanDecision
                    {
                        Action = ScanAction.Activate,
                        TourId = tour.TourId,
                        ConsumesSecondAnchor = false,
                    };

                // alwaysDisplayed 不会是当前 Tour（I5）；万一是，也不参与扫码切换
                default:
                    return ScanDecision.Ignore;
            }
        }
```

在 `Packages/com.uality.ite-tour/Runtime/Core/ScanPromptPolicy.cs` 中：

(e) 把类注释

```csharp
    /// <summary>
    /// 「要不要提示用户去扫码、提示哪几个 Tour」的**纯决策**。
```

改为

```csharp
    /// <summary>
    /// 「要不要提示用户去扫码、提示哪个 Tour」的**纯决策**。已定位后只看当前 Tour，不读区域队列
    /// （ite-current-tour D7）。
```

(f) 把 `Decide`、`HasPending`、`PendingIds` 三个方法整体替换为：

```csharp
        public static ScanPrompt Decide(ScanState state, IReadOnlyList<TourDescriptor> tours)
        {
            // 已经有 Tour 在放，提示无条件收起
            if (!string.IsNullOrEmpty(state.ActiveTourId))
            {
                return ScanPrompt.Hidden;
            }

            switch (state.State)
            {
                case GuideState.Suspended:
                    // 头显摘下：没人在看
                    return ScanPrompt.Hidden;

                case GuideState.AwaitingScan:
                    // 等待扫码定位（冷启动 / 重新戴上 / 追踪原点重置 / 宿主要求）：随便扫哪个都行，不报名字
                    return ScanPrompt.Visible(Array.Empty<string>());
            }

            // 已定位：只有当前 Tour 是 normal、还没播时才提示扫它（ite-current-tour D7，取代
            // ite-scan-region-gate D3）。别的码扫了也不认（ite-current-tour D6），提示别的就是在叫人做无效操作。
            return IsNormal(tours, state.CurrentTourId)
                ? ScanPrompt.Visible(new[] { state.CurrentTourId })
                : ScanPrompt.Hidden;
        }

        private static bool IsNormal(IReadOnlyList<TourDescriptor> tours, string tourId)
        {
            if (tours == null || string.IsNullOrEmpty(tourId))
            {
                return false;
            }

            for (int i = 0; i < tours.Count; i++)
            {
                if (tours[i].TourId == tourId)
                {
                    return tours[i].DisplayType == IteSpaceScene.Tour.DisplayType.normal;
                }
            }

            return false;
        }
```

改完后检查 `ScanPromptPolicy.cs` 顶部的 `using`：`System`（`Array.Empty`）、`System.Collections.Generic`、`Uality.IteTour.Data` 仍然需要，保留。

在 `Packages/com.uality.ite-tour/Runtime/Core/TourGuide.cs` 中（过渡，Task 5 整体重写）：

(g) 把 `Snapshot()`

```csharp
        public ScanState Snapshot() => new ScanState
        {
            State = State,
            ActiveTourId = ActiveTourId,
            PendingTourIds = _pendingTourIds,
        };
```

改为

```csharp
        public ScanState Snapshot() => new ScanState
        {
            State = State,

            // 过渡：旧模型里当前 Tour 就是在播的 Tour。ite-current-tour 实施中，TourGuide 重写时替换。
            CurrentTourId = ActiveTourId,
            ActiveTourId = ActiveTourId,
            PendingTourIds = _pendingTourIds,
        };
```

- [ ] **Step 4: 编译并跑测试，确认通过**

编译；跑 `A=Uality.IteTour.Tests`。Expected: 全部通过，包括尚未改动的 `TourGuideTests`（有了过渡映射，已定位后重扫在播 Tour 仍然是二次锚定）。

- [ ] **Step 5: 提交**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git add Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs Packages/com.uality.ite-tour/Runtime/Core/ScanPromptPolicy.cs \
        Packages/com.uality.ite-tour/Runtime/Core/TourGuide.cs \
        Packages/com.uality.ite-tour/Tests/Editor/TourScanPolicyTests.cs Packages/com.uality.ite-tour/Tests/Editor/ScanPromptPolicyTests.cs
git commit -m "$(cat <<'EOF'
feat(ite): 扫码与提示策略改为只看当前 Tour

已定位后只认当前 Tour 的码；只在当前 Tour 是 normal 且未播时提示它；
等待扫码时扫到 alwaysDisplayed 只锚定（ite-current-tour D6、D7、D10）。
两个策略不再读区域队列。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: `TourGuide` 改为按顺序调用各模块

**Files:**
- Rewrite: `Packages/com.uality.ite-tour/Runtime/Core/TourGuide.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/RegionQueue.cs`（迁入 `VolumeTransition`）
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs`（删 `ScanState.PendingTourIds`）
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/TourIdLists.cs`（删 `SameSet`）
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/TourDirector.cs`（跟上新签名）
- Delete: `Packages/com.uality.ite-tour/Runtime/Core/TourRegionPolicy.cs`（及 `.meta`）
- Rewrite: `Packages/com.uality.ite-tour/Tests/Editor/TourGuideTests.cs`
- Modify: `Packages/com.uality.ite-tour/Tests/Editor/TourIdListsTests.cs`
- Delete: `Packages/com.uality.ite-tour/Tests/Editor/TourRegionPolicyTests.cs`（及 `.meta`）

**Interfaces:**
- Consumes: `RegionQueue`（Task 1）、`RegionBaseline`（Task 2）、`CurrentTourRule.Next`、`TourAssembly.PlaysOnSelect / CanBeCurrent`（Task 3）、`TourScanPolicy.Decide` 与 `ScanState.CurrentTourId`（Task 4）
- Produces（Task 6 依赖）:
  - `GuideEffect.AlwaysDisplayedVisible : bool?`（null 表示不变）
  - `TourGuide(Func<bool> inPhysicsStep = null)`
  - `TourGuide.CurrentTourId`、`ActiveTourId`、`PendingTourIds`、`IsSettling`、`AlwaysDisplayedVisible : bool`
  - `int TourGuide.RegionCountOf(string tourId)`
  - `bool TourGuide.SubmitVolumeTransition(string tourId, VolumeTransition transition)`：Exit 被拒时返回 false
  - `void TourGuide.ClearVolume(string tourId)`
  - `bool TourGuide.TryActivateById(string tourId, IteSpaceScene.Tour.DisplayType displayType, out GuideEffect effect)`
  - `bool TourGuide.AfterPhysicsStep()`：这一步关窗时返回 true
  - `GuideEffect TourGuide.EndOfFrame(Func<IReadOnlyList<TourDescriptor>> tours, out bool changed)`：签名不变
  - `TourDirector(IteTourAssembler assembler)`（删掉 `pick` 参数）、`TourDirector.CurrentTourId`

- [ ] **Step 1: 写失败的测试**

把 `Packages/com.uality.ite-tour/Tests/Editor/TourGuideTests.cs` 整个替换为：

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 导览状态核心（ite-guide-state-machine；ite-current-tour）。状态转换逐条钉住；区域部分按 PICO 实测
    /// （2026-09-23）回放：扫码后被锚定引起的区域变化切走（问题 1）、双碰撞体成对进出（问题 2）。
    /// </summary>
    public class TourGuideTests
    {
        private const IteSpaceScene.Tour.DisplayType Normal = IteSpaceScene.Tour.DisplayType.normal;
        private const IteSpaceScene.Tour.DisplayType Regional = IteSpaceScene.Tour.DisplayType.regionalTrigger;
        private const IteSpaceScene.Tour.DisplayType Always = IteSpaceScene.Tour.DisplayType.alwaysDisplayed;

        private static readonly Pose MarkerPose = new Pose(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f));

        private static TourDescriptor Tour(
            string id, IteSpaceScene.Tour.DisplayType type = Regional, bool secondAnchorAvailable = false)
            => new TourDescriptor { TourId = id, DisplayType = type, SecondAnchorAvailable = secondAnchorAvailable };

        private static List<TourDescriptor> Tours(params TourDescriptor[] tours) => new List<TourDescriptor>(tours);

        private static GuideEffect Frame(TourGuide guide, List<TourDescriptor> tours)
            => guide.EndOfFrame(() => tours, out _);

        private static void Enter(TourGuide guide, params string[] tourIds)
        {
            foreach (var id in tourIds)
            {
                guide.SubmitVolumeTransition(id, VolumeTransition.Enter);
            }
        }

        private static void Exit(TourGuide guide, params string[] tourIds)
        {
            foreach (var id in tourIds)
            {
                guide.SubmitVolumeTransition(id, VolumeTransition.Exit);
            }
        }

        /// <summary>
        /// 站在 tourId 的体积里扫它的码，锚定后人仍在里面（那一步物理没有进出事件）：走到 Anchored，
        /// tourId 是当前 Tour 且在播，结算窗口已关。
        /// </summary>
        private static TourGuide AnchoredOn(string tourId, List<TourDescriptor> tours)
        {
            var guide = new TourGuide();
            Enter(guide, tourId);
            Frame(guide, tours);
            guide.SubmitScan(tourId, MarkerPose, tours, out _);
            Frame(guide, tours);
            guide.AfterPhysicsStep();
            Frame(guide, tours);
            return guide;
        }

        // ---- 构造与扫码 ----

        [Test]
        public void Constructed_IsAwaitingScanForColdStart()
        {
            var guide = new TourGuide();

            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.ColdStart));
            Assert.That(guide.CurrentTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.Null);
            Assert.That(guide.AlwaysDisplayedVisible, Is.False, "锚定前 alwaysDisplayed 不显示（ite-current-tour D8）");
        }

        [Test]
        public void ScanWhileAwaiting_ActivatesWithPose_EntersAnchored_OpensSettle()
        {
            var guide = new TourGuide();

            var effect = guide.SubmitScan("t1", MarkerPose, Tours(Tour("t1")), out var decision);

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(effect.ActivateTourId, Is.EqualTo("t1"));
            Assert.That(effect.AnchorPose.HasValue, Is.True);
            Assert.That(effect.AnchorPose.Value, Is.EqualTo(MarkerPose));
            Assert.That(effect.Deactivate, Is.False);
            Assert.That(effect.AlwaysDisplayedVisible, Is.EqualTo((bool?)true), "进入 Anchored 时显示 alwaysDisplayed");
            Assert.That(guide.State, Is.EqualTo(GuideState.Anchored));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.Scanned));
            Assert.That(guide.CurrentTourId, Is.EqualTo("t1"));
            Assert.That(guide.ActiveTourId, Is.EqualTo("t1"));
            Assert.That(guide.IsSettling, Is.True, "锚定挪动了体积（ite-current-tour D9）");
        }

        [Test]
        public void Anchored_RescanCurrentRegional_ReanchorsAndOpensSettle()
        {
            var tours = Tours(Tour("t1", Regional, secondAnchorAvailable: true));
            var guide = AnchoredOn("t1", tours);
            var newPose = new Pose(new Vector3(4f, 5f, 6f), Quaternion.identity);

            var effect = guide.SubmitScan("t1", newPose, tours, out _);

            Assert.That(effect.ReanchorTourId, Is.EqualTo("t1"));
            Assert.That(effect.ReanchorPose, Is.EqualTo(newPose));
            Assert.That(effect.ConsumesSecondAnchor, Is.True);
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(effect.Deactivate, Is.False);
            Assert.That(effect.AlwaysDisplayedVisible, Is.Null, "状态没变，显隐不动");
            Assert.That(guide.IsSettling, Is.True);
        }

        /// <summary>ite-current-tour D10：等待扫码时扫到 alwaysDisplayed，只锚定，当前 Tour 为空，之后取队尾。</summary>
        [Test]
        public void ScanAlwaysDisplayedWhileAwaiting_AnchorsOnly_ThenTailBecomesCurrent()
        {
            var tours = Tours(Tour("a1", Always), Tour("r1"));
            var guide = new TourGuide();

            var effect = guide.SubmitScan("a1", MarkerPose, tours, out _);

            Assert.That(effect.ReanchorTourId, Is.EqualTo("a1"));
            Assert.That(effect.ConsumesSecondAnchor, Is.False);
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.State, Is.EqualTo(GuideState.Anchored));
            Assert.That(guide.CurrentTourId, Is.Null, "alwaysDisplayed 不当当前 Tour（I5）");

            Enter(guide, "r1");
            guide.AfterPhysicsStep();
            var next = Frame(guide, tours);

            Assert.That(next.ActivateTourId, Is.EqualTo("r1"), "当前 Tour 为空时取队尾");
            Assert.That(guide.CurrentTourId, Is.EqualTo("r1"));
        }

        // ---- 摘下 / 戴上 ----

        [Test]
        public void HeadsetRemoved_FromAnchored_StopsTour_ClearsCurrent_HidesAlwaysDisplayed()
        {
            var guide = AnchoredOn("t1", Tours(Tour("t1")));

            var effect = guide.SetHeadsetMounted(false);

            Assert.That(effect.Deactivate, Is.True);
            Assert.That(effect.AlwaysDisplayedVisible, Is.EqualTo((bool?)false));
            Assert.That(guide.State, Is.EqualTo(GuideState.Suspended));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.HeadsetRemoved));
            Assert.That(guide.CurrentTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.Null);
        }

        [Test]
        public void HeadsetMounted_FromSuspended_AwaitsScan()
        {
            var guide = AnchoredOn("t1", Tours(Tour("t1")));
            guide.SetHeadsetMounted(false);

            var effect = guide.SetHeadsetMounted(true);

            Assert.That(effect.Deactivate, Is.False);
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(effect.AlwaysDisplayedVisible, Is.Null, "Suspended → AwaitingScan 都不显示，显隐不动");
            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.HeadsetMounted));
        }

        [Test]
        public void HeadsetMounted_WhenNotSuspended_ChangesNothing()
        {
            var guide = new TourGuide();
            int raised = 0;
            guide.StateChanged += (state, reason) => raised++;

            guide.SetHeadsetMounted(true);

            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.ColdStart));
            Assert.That(raised, Is.EqualTo(0));
        }

        /// <summary>PICO 实测（2026-09-23）：摘下期间头显仍在追踪，区域进出事件不停，但不能唤醒 Tour。</summary>
        [Test]
        public void WhileSuspended_EnteringRegion_NeverActivates()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);
            guide.SetHeadsetMounted(false);

            Exit(guide, "t1");
            Enter(guide, "t2");
            var effect = Frame(guide, tours);

            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(effect.Deactivate, Is.False);
            Assert.That(guide.ActiveTourId, Is.Null);
            Assert.That(guide.PendingTourIds, Is.EqualTo(new[] { "t2" }), "区域队列照常更新（I3）");
        }

        [Test]
        public void AfterRemount_EnteringRegion_NeverActivates()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);
            guide.SetHeadsetMounted(false);
            guide.SetHeadsetMounted(true);

            Enter(guide, "t2");
            var effect = Frame(guide, tours);

            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.Null);
            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
        }

        [Test]
        public void WhileSuspended_ScanIsIgnored()
        {
            var guide = new TourGuide();
            guide.SetHeadsetMounted(false);

            var effect = guide.SubmitScan("t1", MarkerPose, Tours(Tour("t1")), out var decision);

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.State, Is.EqualTo(GuideState.Suspended));
            Assert.That(guide.IsSettling, Is.False, "被忽略的扫码不挪体积");
        }

        // ---- 运行中要求重扫 ----

        [Test]
        public void RequireScan_FromAnchored_StopsTour_AndAwaitsScan()
        {
            var guide = AnchoredOn("t1", Tours(Tour("t1")));

            var effect = guide.RequireScan(GuideStateReason.Recentered);

            Assert.That(effect.Deactivate, Is.True, "锚定作废后不能继续显示错位的内容（ite-guide-state-machine D2）");
            Assert.That(effect.AlwaysDisplayedVisible, Is.EqualTo((bool?)false));
            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.Recentered));
            Assert.That(guide.CurrentTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.Null);
        }

        [Test]
        public void RequireScan_WhileSuspended_StaysSuspended()
        {
            var guide = new TourGuide();
            guide.SetHeadsetMounted(false);

            var effect = guide.RequireScan(GuideStateReason.Recentered);

            Assert.That(effect.Deactivate, Is.False);
            Assert.That(guide.State, Is.EqualTo(GuideState.Suspended), "摘下期间不能开始认扫码（ite-guide-state-machine D3）");
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.HeadsetRemoved));
        }

        [Test]
        public void RequireScan_WhileAwaiting_UpdatesReasonOnce()
        {
            var guide = new TourGuide();
            var raised = new List<GuideStateReason>();
            guide.StateChanged += (state, reason) => raised.Add(reason);

            guide.RequireScan(GuideStateReason.HostRequested);
            guide.RequireScan(GuideStateReason.HostRequested);

            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.HostRequested));
            Assert.That(raised, Is.EqualTo(new[] { GuideStateReason.HostRequested }), "状态与原因都没变时不重复广播");
        }

        // ---- 直接激活 ----

        [Test]
        public void TryActivateById_RefusedUnlessAnchored()
        {
            var guide = new TourGuide();

            Assert.That(guide.TryActivateById("t1", Regional, out var effect), Is.False,
                "等待扫码时冒出在播 Tour 会违反 I1（ite-guide-state-machine D4）");
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.Null);
        }

        [Test]
        public void TryActivateById_RefusesAlwaysDisplayed()
        {
            var guide = AnchoredOn("t1", Tours(Tour("t1"), Tour("a1", Always)));

            Assert.That(guide.TryActivateById("a1", Always, out _), Is.False, "I5（ite-current-tour D10）");
            Assert.That(guide.CurrentTourId, Is.EqualTo("t1"));
        }

        [Test]
        public void TryActivateById_WhenAnchored_SwitchesCurrentWithoutAnchoring()
        {
            var guide = AnchoredOn("t1", Tours(Tour("t1"), Tour("t2")));

            Assert.That(guide.TryActivateById("t2", Regional, out var effect), Is.True);
            Assert.That(effect.Deactivate, Is.True);
            Assert.That(effect.ActivateTourId, Is.EqualTo("t2"));
            Assert.That(effect.AnchorPose.HasValue, Is.False, "沿用现有锚定");
            Assert.That(guide.CurrentTourId, Is.EqualTo("t2"));
            Assert.That(guide.ActiveTourId, Is.EqualTo("t2"));
            Assert.That(guide.IsSettling, Is.False, "不挪体积，不开窗");
        }

        // ---- 真机回放 ----

        /// <summary>
        /// PICO 14:36:31（问题 1）：站在锚定前 ujf5 体积的位置扫 ujf5 的码（两个碰撞体都在里面）。锚定把体积
        /// 挪到真实位置，下一步物理里 ujf5 的两个碰撞体都离开、hncx 进入——这是锚定造成的，不能把刚扫的
        /// ujf5 切走。之后人真的走进 ujf5 再走出来，才换成队尾的 hncx。
        /// </summary>
        [Test]
        public void Replay_AnchorMovesVolumes_KeepsScannedTour_UntilPersonEntersAndLeavesIt()
        {
            var tours = Tours(Tour("ujf5bo31_frb"), Tour("hncxtzfe_p4d"));
            var guide = new TourGuide();
            Enter(guide, "ujf5bo31_frb", "ujf5bo31_frb");
            Frame(guide, tours);

            guide.SubmitScan("ujf5bo31_frb", MarkerPose, tours, out _);
            Assert.That(Frame(guide, tours).ActivateTourId, Is.Null, "扫码同一帧");

            Exit(guide, "ujf5bo31_frb", "ujf5bo31_frb");
            Enter(guide, "hncxtzfe_p4d");
            Assert.That(guide.AfterPhysicsStep(), Is.True, "锚定后第一步物理：关窗");
            var afterSettle = Frame(guide, tours);

            Assert.That(afterSettle.Deactivate, Is.False);
            Assert.That(afterSettle.ActivateTourId, Is.Null);
            Assert.That(guide.CurrentTourId, Is.EqualTo("ujf5bo31_frb"));
            Assert.That(guide.ActiveTourId, Is.EqualTo("ujf5bo31_frb"));

            Enter(guide, "ujf5bo31_frb");
            Assert.That(Frame(guide, tours).ActivateTourId, Is.Null, "走进当前 Tour：不变");

            Exit(guide, "ujf5bo31_frb");
            var leave = Frame(guide, tours);

            Assert.That(leave.Deactivate, Is.True);
            Assert.That(leave.ActivateTourId, Is.EqualTo("hncxtzfe_p4d"));
            Assert.That(leave.AnchorPose.HasValue, Is.False, "区域补位沿用现有锚定");
            Assert.That(guide.CurrentTourId, Is.EqualTo("hncxtzfe_p4d"));
        }

        /// <summary>
        /// PICO 14:36:36–39（问题 2）：qtcljiro 的两个碰撞体先后进入、先后离开。第一次离开后还有一个
        /// 碰撞体在里面，当前 Tour 不能被释放。
        /// </summary>
        [Test]
        public void Replay_TwoCameraColliders_CurrentReleasedOnlyWhenBothLeave()
        {
            var tours = Tours(Tour("qtcljiro_zrx"), Tour("r2"));
            var guide = AnchoredOn("qtcljiro_zrx", tours);
            Enter(guide, "qtcljiro_zrx", "r2");
            Frame(guide, tours);

            Exit(guide, "qtcljiro_zrx");
            var first = Frame(guide, tours);

            Assert.That(first.Deactivate, Is.False, "还有一个碰撞体在体积里");
            Assert.That(guide.CurrentTourId, Is.EqualTo("qtcljiro_zrx"));

            Exit(guide, "qtcljiro_zrx");
            var second = Frame(guide, tours);

            Assert.That(second.Deactivate, Is.True);
            Assert.That(second.ActivateTourId, Is.EqualTo("r2"));
        }

        // ---- 当前 Tour 优先（ite-current-tour D3、D4、D5）----

        [Test]
        public void EnteringOtherRegions_NeverInterruptsCurrent()
        {
            var tours = Tours(Tour("t1"), Tour("t2"), Tour("t3"));
            var guide = AnchoredOn("t1", tours);

            Enter(guide, "t2");
            Assert.That(Frame(guide, tours).ActivateTourId, Is.Null);
            Enter(guide, "t3");
            Assert.That(Frame(guide, tours).ActivateTourId, Is.Null);

            Assert.That(guide.CurrentTourId, Is.EqualTo("t1"));
            Assert.That(guide.ActiveTourId, Is.EqualTo("t1"));
        }

        [Test]
        public void LeavingCurrent_TailIsNormal_SelectsItWithoutPlaying()
        {
            var tours = Tours(Tour("r1"), Tour("r2"), Tour("n1", Normal));
            var guide = AnchoredOn("r1", tours);
            Enter(guide, "r2", "n1");
            Frame(guide, tours);

            Exit(guide, "r1");
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.True);
            Assert.That(effect.ActivateTourId, Is.Null, "碰撞永远不激活 normal（ite-current-tour D5）");
            Assert.That(guide.CurrentTourId, Is.EqualTo("n1"), "取队尾，不跳过 normal（ite-current-tour D4）");
            Assert.That(guide.ActiveTourId, Is.Null);
        }

        [Test]
        public void CurrentNormalAwaitingScan_OtherRegionAndOtherCode_DoNotTakeOver_OwnCodeActivates()
        {
            var tours = Tours(Tour("r1"), Tour("r2"), Tour("n1", Normal));
            var guide = AnchoredOn("r1", tours);
            Enter(guide, "n1");
            Frame(guide, tours);
            Exit(guide, "r1");
            Frame(guide, tours);
            Assert.That(guide.CurrentTourId, Is.EqualTo("n1"));

            Enter(guide, "r2");
            Assert.That(Frame(guide, tours).ActivateTourId, Is.Null, "当前 Tour 优先级最高");

            guide.SubmitScan("r2", MarkerPose, tours, out var other);
            Assert.That(other.Action, Is.EqualTo(ScanAction.Ignore), "只认当前 Tour 的码（ite-current-tour D6）");

            var effect = guide.SubmitScan("n1", MarkerPose, tours, out var own);

            Assert.That(own.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(effect.ActivateTourId, Is.EqualTo("n1"));
            Assert.That(effect.AnchorPose.HasValue, Is.True);
            Assert.That(guide.ActiveTourId, Is.EqualTo("n1"));
            Assert.That(guide.IsSettling, Is.True, "扫 normal 会重新锚定，开窗");
        }

        [Test]
        public void NoCurrent_TailNormal_IsSelectedButNeverPlayedByRegion()
        {
            var tours = Tours(Tour("t1"), Tour("n1", Normal));
            var guide = AnchoredOn("t1", tours);
            Exit(guide, "t1");
            Frame(guide, tours);
            Assert.That(guide.CurrentTourId, Is.Null);

            Enter(guide, "n1");
            var effect = Frame(guide, tours);

            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.CurrentTourId, Is.EqualTo("n1"));
            Assert.That(guide.ActiveTourId, Is.Null);
        }

        [Test]
        public void LeavingCurrentIntoNothing_Stops_ThenEnteringRegionPicksIt()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);

            Exit(guide, "t1");
            var stop = Frame(guide, tours);

            Assert.That(stop.Deactivate, Is.True);
            Assert.That(stop.ActivateTourId, Is.Null);
            Assert.That(guide.CurrentTourId, Is.Null);

            Enter(guide, "t2");
            var start = Frame(guide, tours);

            Assert.That(start.ActivateTourId, Is.EqualTo("t2"));
            Assert.That(guide.CurrentTourId, Is.EqualTo("t2"));
        }

        [Test]
        public void ExitAndReenterCurrentWithinFrame_KeepsIt()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);
            Enter(guide, "t2");
            Frame(guide, tours);

            Exit(guide, "t1");
            Enter(guide, "t1");
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.False);
            Assert.That(guide.CurrentTourId, Is.EqualTo("t1"));
            Assert.That(guide.PendingTourIds, Is.EqualTo(new[] { "t2", "t1" }), "重新进入排到队尾");
        }

        [Test]
        public void MovingFromCurrentIntoNeighbourInOneStep_SwitchesDirectly()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);

            Exit(guide, "t1");
            Enter(guide, "t2");
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.True);
            Assert.That(effect.ActivateTourId, Is.EqualTo("t2"));
        }

        // ---- 结算窗口与防御（ite-current-tour D9、D11）----

        /// <summary>在物理阶段内锚定（宿主在物理回调里扫码）：这一步的模拟可能已跑完，窗口多等一步。</summary>
        [Test]
        public void ScanInsidePhysicsStep_WaitsOneMoreStep_BeforeCurrentCanMove()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = new TourGuide(() => true);
            Enter(guide, "t1");
            Frame(guide, tours);
            guide.SubmitScan("t1", MarkerPose, tours, out _);

            Assert.That(guide.AfterPhysicsStep(), Is.False, "第一步可能在锚定之前就模拟完了");
            Exit(guide, "t1");
            Enter(guide, "t2");
            Assert.That(Frame(guide, tours).Deactivate, Is.False, "窗口还开着");

            Assert.That(guide.AfterPhysicsStep(), Is.True);
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.False, "锚定造成的离开并入基准");
            Assert.That(guide.CurrentTourId, Is.EqualTo("t1"));
        }

        [Test]
        public void OrphanExit_IsRejected_AndDoesNotReleaseCurrent()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);

            Assert.That(guide.SubmitVolumeTransition("t2", VolumeTransition.Exit), Is.False);
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.False);
            Assert.That(guide.CurrentTourId, Is.EqualTo("t1"));
            Assert.That(guide.PendingTourIds, Is.EqualTo(new[] { "t1" }));
        }

        /// <summary>体积被停用时 Unity 不发 Exit，由 ClearVolume 清零；人在里面时等同于离开。</summary>
        [Test]
        public void ClearVolume_WhileInsideCurrent_ReleasesIt()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);
            Enter(guide, "t1", "t2");
            Frame(guide, tours);

            guide.ClearVolume("t1");
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.True);
            Assert.That(effect.ActivateTourId, Is.EqualTo("t2"));
            Assert.That(guide.RegionCountOf("t1"), Is.EqualTo(0));
        }

        // ---- 帧末快照（ite-guide-state-machine D6）与状态广播（ite-guide-state-machine D8）----

        [Test]
        public void EndOfFrame_ReportsChangeOnlyWhenPromptSnapshotChanges()
        {
            var tours = Tours(Tour("t1"));
            var guide = new TourGuide();

            guide.EndOfFrame(() => tours, out bool first);
            guide.EndOfFrame(() => tours, out bool idle);
            Enter(guide, "t1");
            guide.EndOfFrame(() => tours, out bool afterEnter);
            guide.SetHeadsetMounted(false);
            guide.EndOfFrame(() => tours, out bool afterRemove);

            Assert.That(first, Is.True, "第一帧必须算一次提示");
            Assert.That(idle, Is.False);
            Assert.That(afterEnter, Is.False, "提示不再看区域队列（ite-current-tour D7）");
            Assert.That(afterRemove, Is.True);
        }

        [Test]
        public void StateChanged_CarriesReason()
        {
            var tours = Tours(Tour("t1"));
            var guide = new TourGuide();
            var raised = new List<(GuideState, GuideStateReason)>();
            guide.StateChanged += (state, reason) => raised.Add((state, reason));

            guide.SetHeadsetMounted(false);
            guide.SetHeadsetMounted(true);
            guide.SubmitScan("t1", MarkerPose, tours, out _);

            Assert.That(raised, Is.EqualTo(new[]
            {
                (GuideState.Suspended, GuideStateReason.HeadsetRemoved),
                (GuideState.AwaitingScan, GuideStateReason.HeadsetMounted),
                (GuideState.Anchored, GuideStateReason.Scanned),
            }));
        }
    }
}
```

把 `Packages/com.uality.ite-tour/Tests/Editor/TourIdListsTests.cs` 里的三个 `SameSet_*` 测试（`SameSet_IgnoresOrder`、`SameSet_DifferentMembers_IsFalse`、`SameSet_NullEqualsEmpty`）删掉，其余保留。

删除旧的区域策略测试：

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git rm -q Packages/com.uality.ite-tour/Tests/Editor/TourRegionPolicyTests.cs Packages/com.uality.ite-tour/Tests/Editor/TourRegionPolicyTests.cs.meta
```

- [ ] **Step 2: 编译，确认失败**

编译。Expected: 编译失败，错误提到 `TourGuide` 没有 `CurrentTourId` / `IsSettling` / `AfterPhysicsStep`、构造函数参数不对等。

- [ ] **Step 3: 删除 `TourRegionPolicy`，把 `VolumeTransition` 迁入 `RegionQueue.cs`**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git rm -q Packages/com.uality.ite-tour/Runtime/Core/TourRegionPolicy.cs Packages/com.uality.ite-tour/Runtime/Core/TourRegionPolicy.cs.meta
```

在 `Packages/com.uality.ite-tour/Runtime/Core/RegionQueue.cs` 里，`namespace Uality.IteTour.Core` 的左括号之后、`RegionQueue` 的类注释之前插入：

```csharp
    /// <summary>相机与 Tour 触发体积的进出。</summary>
    public enum VolumeTransition
    {
        Enter,
        Exit,
    }

```

- [ ] **Step 4: 删掉 `ScanState.PendingTourIds` 和 `TourIdLists.SameSet`**

在 `Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs` 的 `ScanState` 里，删掉 Task 4 标为「过渡字段」的那段注释和 `public IReadOnlyList<string> PendingTourIds;` 这一行。

把 `Packages/com.uality.ite-tour/Runtime/Core/TourIdLists.cs` 整个替换为：

```csharp
using System.Collections.Generic;

namespace Uality.IteTour.Core
{
    /// <summary>TourId 列表上的线性查找。</summary>
    public static class TourIdLists
    {
        public static bool Contains(IReadOnlyList<string> ids, string id)
        {
            if (ids == null || string.IsNullOrEmpty(id))
            {
                return false;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
```

- [ ] **Step 5: 重写 `TourGuide`**

把 `Packages/com.uality.ite-tour/Runtime/Core/TourGuide.cs` 整个替换为：

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using Uality.IteTour.Data;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// <see cref="TourGuide"/> 要求效果层执行的动作。字段可以同时出现：换 Tour 时既停用旧的、又激活新的。
    /// </summary>
    public struct GuideEffect
    {
        /// <summary>停用当前在播的 Tour。</summary>
        public bool Deactivate;

        /// <summary>要激活的 Tour；null 表示不激活。</summary>
        public string ActivateTourId;

        /// <summary>激活时的锚定位姿；null 表示沿用现有锚定（区域补位、直接激活）。</summary>
        public Pose? AnchorPose;

        /// <summary>只重新锚定、不换 Tour（regionalTrigger 二次锚定；等待扫码时扫到 alwaysDisplayed）；null 表示无。</summary>
        public string ReanchorTourId;

        public Pose ReanchorPose;

        /// <summary>重锚是否消耗该 Tour 的二次锚定许可。</summary>
        public bool ConsumesSecondAnchor;

        /// <summary>alwaysDisplayed 的显隐要变成什么；null 表示不变。只在进出 Anchored 时给出（ite-current-tour D8）。</summary>
        public bool? AlwaysDisplayedVisible;

        public static GuideEffect None => default;
    }

    /// <summary>
    /// 导览状态核心（ite-guide-state-machine D7；ite-current-tour D12）。持有导览状态、当前 Tour 与在播 Tour，
    /// 按顺序调用各规则模块，把结论合成一个 <see cref="GuideEffect"/> 交给 <see cref="TourDirector"/>。
    /// 规则本身不在这里：
    /// - 人在哪些区域：<see cref="RegionQueue"/>
    /// - 什么算人动了：<see cref="RegionBaseline"/>
    /// - 当前 Tour 换成谁：<see cref="CurrentTourRule"/>
    /// - 选中后播不播：<see cref="TourAssembly.PlaysOnSelect"/>
    /// - 扫码认不认：<see cref="TourScanPolicy"/>
    ///
    /// 不变式：I1 只有 Anchored 下才可能有 Tour 在播；I2 只有 Anchored 下区域才会换当前 Tour；I3 区域队列在
    /// 所有状态下都照常更新；I4 ActiveTourId 要么为空、要么等于 CurrentTourId；I5 当前 Tour 永远不是 alwaysDisplayed。
    /// 不碰 GameObject，所以每条规则的组合都能在 EditMode 里测。
    /// </summary>
    public sealed class TourGuide
    {
        private readonly Func<bool> _inPhysicsStep;
        private readonly RegionQueue _regions = new RegionQueue();
        private readonly RegionBaseline _baseline = new RegionBaseline();

        // 上一帧末的提示快照（ite-guide-state-machine D6）。提示不看区域队列（ite-current-tour D7），快照里也不放。
        private bool _hasFrame;
        private GuideState _frameState;
        private string _frameCurrentTourId;
        private string _frameActiveTourId;

        /// <param name="inPhysicsStep">
        /// 此刻是否在物理阶段内（效果层传 <c>() =&gt; Time.inFixedTimeStep</c>）。在物理阶段内锚定时，这一步的
        /// 模拟可能已经跑完，结算窗口要多等一步（ite-current-tour D9）。null 视为总是 false。
        /// </param>
        public TourGuide(Func<bool> inPhysicsStep = null)
        {
            _inPhysicsStep = inPhysicsStep ?? (() => false);
            State = GuideState.AwaitingScan;
            Reason = GuideStateReason.ColdStart;
        }

        public GuideState State { get; private set; }

        public GuideStateReason Reason { get; private set; }

        /// <summary>持有优先级的 Tour（ite-current-tour D3）；无则为 null。</summary>
        public string CurrentTourId { get; private set; }

        /// <summary>正在播的 Tour；无则为 null。只会是 null 或 <see cref="CurrentTourId"/>（I4）。</summary>
        public string ActiveTourId { get; private set; }

        /// <summary>相机所在区域，按进入先后排列（ite-current-tour D2）。</summary>
        public IReadOnlyList<string> PendingTourIds => _regions.TourIds;

        /// <summary>锚定结算窗口是否开着（ite-current-tour D9）。</summary>
        public bool IsSettling => _baseline.IsSettling;

        /// <summary>alwaysDisplayed 此刻该不该显示（ite-current-tour D8）。</summary>
        public bool AlwaysDisplayedVisible => ShowsAlwaysDisplayed(State);

        /// <summary>状态或进入原因变化时触发；两者都没变时不触发。</summary>
        public event Action<GuideState, GuideStateReason> StateChanged;

        public int RegionCountOf(string tourId) => _regions.CountOf(tourId);

        public ScanState Snapshot() => new ScanState
        {
            State = State,
            CurrentTourId = CurrentTourId,
            ActiveTourId = ActiveTourId,
        };

        /// <summary>摘下：停掉在播 Tour、清空当前 Tour，进入 Suspended。戴上：只从 Suspended 进入 AwaitingScan。</summary>
        public GuideEffect SetHeadsetMounted(bool mounted)
        {
            var before = State;
            var effect = GuideEffect.None;

            if (!mounted)
            {
                effect = ClearCurrent();
                Enter(GuideState.Suspended, GuideStateReason.HeadsetRemoved);
            }
            else if (State == GuideState.Suspended)
            {
                Enter(GuideState.AwaitingScan, GuideStateReason.HeadsetMounted);
            }

            return WithVisibility(effect, before);
        }

        /// <summary>
        /// 运行中直接进入等待扫码定位：停掉在播 Tour，与冷启动同一状态（ite-guide-state-machine D2）。
        /// Suspended 下不切换——摘下期间不能开始认扫码，戴上时本就会进入等待扫码（ite-guide-state-machine D3）。
        /// </summary>
        public GuideEffect RequireScan(GuideStateReason reason)
        {
            if (State == GuideState.Suspended)
            {
                return GuideEffect.None;
            }

            var before = State;
            var effect = ClearCurrent();
            Enter(GuideState.AwaitingScan, reason);
            return WithVisibility(effect, before);
        }

        public GuideEffect SubmitScan(
            string markerId, Pose pose, IReadOnlyList<TourDescriptor> tours, out ScanDecision decision)
        {
            decision = TourScanPolicy.Decide(Snapshot(), tours, markerId);

            GuideEffect effect;
            switch (decision.Action)
            {
                case ScanAction.Activate:
                    effect = Play(decision.TourId);
                    effect.AnchorPose = pose;
                    break;

                case ScanAction.Reanchor:
                    effect = new GuideEffect
                    {
                        ReanchorTourId = decision.TourId,
                        ReanchorPose = pose,
                        ConsumesSecondAnchor = decision.ConsumesSecondAnchor,
                    };
                    break;

                default:
                    return GuideEffect.None;
            }

            var before = State;
            if (State == GuideState.AwaitingScan)
            {
                Enter(GuideState.Anchored, GuideStateReason.Scanned);
            }

            // 两种动作都挪动了所有体积：接下来那一步物理里的进出是锚定造成的，不算人移动（ite-current-tour D9）。
            _baseline.BeginSettle(_inPhysicsStep() ? 2 : 1);

            return WithVisibility(effect, before);
        }

        /// <summary>
        /// 相机侧某个碰撞体进出某个 Tour 的体积：只更新区域队列（I3），当前 Tour 换不换在帧末判。
        /// </summary>
        /// <returns>false：没有对应进入的离开，被拒收（ite-current-tour D11）。</returns>
        public bool SubmitVolumeTransition(string tourId, VolumeTransition transition)
        {
            if (transition == VolumeTransition.Enter)
            {
                _regions.Enter(tourId);
                return true;
            }

            return _regions.Exit(tourId);
        }

        /// <summary>体积被停用：Unity 不发离开，计数直接清零（ite-current-tour D11）。</summary>
        public void ClearVolume(string tourId) => _regions.Clear(tourId);

        /// <summary>
        /// 不经扫码直接激活，沿用现有锚定。只在 Anchored 下可用（ite-guide-state-machine D4），
        /// alwaysDisplayed 不能激活（ite-current-tour D10）。
        /// </summary>
        public bool TryActivateById(string tourId, IteSpaceScene.Tour.DisplayType displayType, out GuideEffect effect)
        {
            if (State != GuideState.Anchored || !TourAssembly.CanBeCurrent(displayType))
            {
                effect = GuideEffect.None;
                return false;
            }

            effect = Play(tourId);
            return true;
        }

        /// <summary>一个物理步的触发回调全部到达之后调用。这一步关上结算窗口时返回 true（ite-current-tour D9）。</summary>
        public bool AfterPhysicsStep() => _baseline.AfterPhysicsStep(_regions.TourIds);

        /// <summary>
        /// 帧末结算：Anchored 且结算窗口已关时，按「基准 → 当前队列」判一次当前 Tour 换不换（ite-current-tour §5.2）；
        /// <paramref name="changed"/> 报告（状态、当前、在播）相对上一帧末是否变化，供调用方决定要不要重算提示
        /// （ite-guide-state-machine D6）。
        /// </summary>
        /// <param name="tours">只在当前 Tour 真的换了时才取，避免每帧都分配 Tour 描述列表。</param>
        public GuideEffect EndOfFrame(Func<IReadOnlyList<TourDescriptor>> tours, out bool changed)
        {
            if (tours == null)
            {
                throw new ArgumentNullException(nameof(tours));
            }

            var effect = GuideEffect.None;
            var queue = _regions.TourIds;

            if (State == GuideState.Anchored && !_baseline.IsSettling)
            {
                var next = CurrentTourRule.Next(CurrentTourId, _baseline.Baseline, queue);
                if (next != CurrentTourId)
                {
                    effect = ClearCurrent();
                    CurrentTourId = next;

                    if (next != null && PlaysOnSelect(tours(), next))
                    {
                        ActiveTourId = next;
                        effect.ActivateTourId = next;
                    }
                }
            }

            _baseline.Advance(queue);

            changed = !_hasFrame
                      || State != _frameState
                      || CurrentTourId != _frameCurrentTourId
                      || ActiveTourId != _frameActiveTourId;

            _hasFrame = true;
            _frameState = State;
            _frameCurrentTourId = CurrentTourId;
            _frameActiveTourId = ActiveTourId;

            return effect;
        }

        private static bool ShowsAlwaysDisplayed(GuideState state) => state == GuideState.Anchored;

        private static bool PlaysOnSelect(IReadOnlyList<TourDescriptor> tours, string tourId)
        {
            if (tours != null)
            {
                for (int i = 0; i < tours.Count; i++)
                {
                    if (tours[i].TourId == tourId)
                    {
                        return TourAssembly.PlaysOnSelect(tours[i].DisplayType);
                    }
                }
            }

            return false;
        }

        /// <summary>设为当前 Tour 并在播。换 Tour 时先停旧的；同一个 Tour 由效果层只重新锚定并确保已启用。</summary>
        private GuideEffect Play(string tourId)
        {
            var effect = new GuideEffect
            {
                Deactivate = ActiveTourId != null && ActiveTourId != tourId,
                ActivateTourId = tourId,
            };

            CurrentTourId = tourId;
            ActiveTourId = tourId;
            return effect;
        }

        /// <summary>清空当前 Tour；有在播的就停掉。</summary>
        private GuideEffect ClearCurrent()
        {
            CurrentTourId = null;

            if (ActiveTourId == null)
            {
                return GuideEffect.None;
            }

            ActiveTourId = null;
            return new GuideEffect { Deactivate = true };
        }

        /// <summary>进出 Anchored 时带上 alwaysDisplayed 的新显隐；没跨过这条线就不动（ite-current-tour D8）。</summary>
        private GuideEffect WithVisibility(GuideEffect effect, GuideState before)
        {
            if (ShowsAlwaysDisplayed(before) != AlwaysDisplayedVisible)
            {
                effect.AlwaysDisplayedVisible = AlwaysDisplayedVisible;
            }

            return effect;
        }

        private void Enter(GuideState state, GuideStateReason reason)
        {
            if (State == state && Reason == reason)
            {
                return;
            }

            State = state;
            Reason = reason;
            StateChanged?.Invoke(state, reason);
        }
    }
}
```

- [ ] **Step 6: `TourDirector` 跟上新签名**

在 `Packages/com.uality.ite-tour/Runtime/Core/TourDirector.cs` 中：

(a) 把构造函数及其 `<param name="pick">` 注释

```csharp
        /// <param name="pick">
        /// 区域重选有多个候选时挑哪个。缺省随机——与源实现的 OrderBy(Guid.NewGuid()) 一致。
        /// </param>
        public TourDirector(IteTourAssembler assembler, Func<IReadOnlyList<string>, string> pick = null)
        {
            _assembler = assembler ?? throw new ArgumentNullException(nameof(assembler));
            _guide = new TourGuide(pick ?? (ids => ids[UnityEngine.Random.Range(0, ids.Count)]));
```

改为

```csharp
        public TourDirector(IteTourAssembler assembler)
        {
            _assembler = assembler ?? throw new ArgumentNullException(nameof(assembler));

            // 在物理回调里锚定时，这一步的模拟可能已经跑完，结算窗口要多等一步（ite-current-tour D9）
            _guide = new TourGuide(() => Time.inFixedTimeStep);
```

(b) 在 `public string ActiveTourId => _guide.ActiveTourId;` 之前插入：

```csharp
        /// <summary>持有优先级的 Tour（ite-current-tour D3）；无则为 null。</summary>
        public string CurrentTourId => _guide.CurrentTourId;

```

(c) 把 `ActivateById` 的方法体替换为：

```csharp
        {
            var tour = _assembler.Find(tourId);
            if (tour == null)
            {
                Debug.LogError("[ITE] 找不到 Tour：" + tourId);
                return false;
            }

            if (!_guide.TryActivateById(tourId, tour.DisplayType, out var effect))
            {
                Debug.LogWarning(
                    $"[ITE] 不能直接激活 Tour：{tourId}（状态={_guide.State} 类型={tour.DisplayType}；" +
                    "需先扫码定位，alwaysDisplayed 不能直接激活）");
                return false;
            }

            Apply(effect);
            return true;
        }
```

并把它上方注释里的 `只在 Anchored 下可用（ite-guide-state-machine D4）。找不到或状态不允许时返回 false。` 改为 `只在 Anchored 下可用（ite-guide-state-machine D4），alwaysDisplayed 不能直接激活（ite-current-tour D10）。找不到或不允许时返回 false。`

(d) 在 `SubmitMarkerScan` 的日志里，把

```csharp
                $"（状态={before.State} 在播={before.ActiveTourId ?? "无"} 所在区域=[{JoinIds(before.PendingTourIds)}]）" +
```

改为

```csharp
                $"（状态={before.State} 当前={before.CurrentTourId ?? "无"} 在播={before.ActiveTourId ?? "无"} 所在区域=[{JoinIds(_guide.PendingTourIds)}]）" +
```

(e) 把 `SubmitVolumeTransition` 整个方法（含注释）替换为：

```csharp
        /// <summary>
        /// 相机侧某个碰撞体进出某个 Tour 的体积：只更新区域队列（I3），当前 Tour 换不换在帧末判（ite-current-tour §5.2）。
        /// </summary>
        public void SubmitVolumeTransition(string tourId, VolumeTransition transition)
        {
            if (!_guide.SubmitVolumeTransition(tourId, transition))
            {
                Debug.LogWarning($"[ITE] 区域 Exit {tourId} 没有对应的 Enter，已忽略（ite-current-tour D11）");
                return;
            }

            Debug.Log($"[ITE] 区域 {transition} {tourId} → 队列=[{JoinIds(_guide.PendingTourIds)}]");
        }
```

- [ ] **Step 7: 编译并跑测试，确认通过**

编译；跑 `A=Uality.IteTour.Tests`，再跑 `A=MRBase.Ite.Host.Tests`。Expected: 两个程序集全部通过。编译若报 `TourRegionPolicy`、`SameSet`、`PendingTourIds`（`ScanState` 上）、`pick` 的残留引用，用下面的命令找出来改掉：

```bash
cd /Users/wwj/Desktop/unity/MR_Base
grep -rn "TourRegionPolicy\|RegionDecision\|SameSet\|\.PendingTourIds =" Packages/com.uality.ite-tour Assets/Scripts | grep -v "\.meta:"
```

Expected: 没有输出。

- [ ] **Step 8: 提交**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git add Packages/com.uality.ite-tour/Runtime/Core/TourGuide.cs Packages/com.uality.ite-tour/Runtime/Core/RegionQueue.cs \
        Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs Packages/com.uality.ite-tour/Runtime/Core/TourIdLists.cs \
        Packages/com.uality.ite-tour/Runtime/Core/TourDirector.cs \
        Packages/com.uality.ite-tour/Tests/Editor/TourGuideTests.cs Packages/com.uality.ite-tour/Tests/Editor/TourIdListsTests.cs
git commit -m "$(cat <<'EOF'
refactor(ite): TourGuide 改为按顺序调用区域队列、结算窗口与当前 Tour 规则

- 当前 Tour 与在播分开，只有人离开它才换成队尾（ite-current-tour D2、D3、D4）
- 扫码后开结算窗口，锚定挪动体积造成的进出不算人移动（ite-current-tour D9）
- 进出 Anchored 时给出 alwaysDisplayed 显隐（ite-current-tour D8）
- 删除 TourRegionPolicy 与随机挑选、TourIdLists.SameSet、ScanState.PendingTourIds

修 PICO 实测问题 1（扫码后被切走）与问题 2（双碰撞体成对进出）的核心逻辑。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: 效果层接线——物理步通知、体积停用、`alwaysDisplayed` 显隐、诊断日志

**Files:**
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/IteTourObject.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/IteTourAssembler.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/TourDirector.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/IteRuntimeDriver.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/IteRuntime.cs`

**Interfaces:**
- Consumes: Task 5 的 `TourGuide` / `TourDirector` 接口
- Produces:
  - `IteTourObject.OnCameraVolumeTransition : Action<string, VolumeTransition, string>`（第三个参数是碰撞体名字）
  - `IteTourObject.OnVolumeCleared : Action<string>`
  - `IteTourObject.SetContentVisible(bool)`
  - `IteTourAssembler.SetAlwaysDisplayedVisible(bool)`
  - `TourDirector.SubmitVolumeTransition(string, VolumeTransition, string)`、`ClearVolume(string)`、`AfterPhysicsStep()`、`SyncAlwaysDisplayed()`

这一层全是 MonoBehaviour 与场景对象，EditMode 构造不出来（`IteTourObject` 依赖预制体上的序列化引用），所以这个 Task 的验收是：编译通过、两个程序集的已有测试全部通过、日志格式符合 spec §8。行为验证列入真机验证（见 Review Focus）。

- [ ] **Step 1: `IteTourObject`**

在 `Packages/com.uality.ite-tour/Runtime/Core/IteTourObject.cs` 中：

(a) 把

```csharp
        /// <summary>相机进出本 Tour 的触发体积。由编排层转给 <see cref="TourDirector"/>。</summary>
        public Action<string, VolumeTransition> OnCameraVolumeTransition;
```

改为

```csharp
        /// <summary>
        /// 相机进出本 Tour 的触发体积：(tourId, 进/出, 碰撞体名字)。由编排层转给 <see cref="TourDirector"/>。
        /// 碰撞体名字只用于日志：相机侧不止一个碰撞体（Main Camera 的球、XR Origin 的 CharacterController），
        /// 真机上靠它分辨是谁在进出（ite-current-tour §8）。
        /// </summary>
        public Action<string, VolumeTransition, string> OnCameraVolumeTransition;

        /// <summary>触发体积被停用。Unity 停用碰撞体时不发 OnTriggerExit，计数要另行清零（ite-current-tour D11）。</summary>
        public Action<string> OnVolumeCleared;
```

(b) 在 `NotifyVolumeTransition` 里把

```csharp
            OnCameraVolumeTransition?.Invoke(_tourId, transition);
```

改为（走到这里时 `other` 必然非空——`IsCamera` 对 null 返回 false，上面已经 return）

```csharp
            OnCameraVolumeTransition?.Invoke(_tourId, transition, other.name);
```

(c) 把 `SetVolumeObjectActive` 整个方法替换为：

```csharp
        public void SetVolumeObjectActive(bool isActive)
        {
            if (_scene.IsDestroyed) return;

            // alwaysDisplayed 不参与区域触发。决策收在 Tour 自己，避免
            // SetAllVolumesActive 把 ChangeDisplayType 刚关掉的体积重新打开。
            bool active = isActive && TourAssembly.AllowsTriggerVolume(_displayType);
            bool wasActive = _volumeObject.activeSelf;

            _volumeObject.SetActive(active);

            // Unity 停用碰撞体时不发 OnTriggerExit：人站在里面时，这个 Tour 会永远留在区域队列里
            // （ite-current-tour D11）。重新启用时，Unity 会对仍在重叠的碰撞体补发 Enter。
            if (wasActive && !active)
            {
                OnVolumeCleared?.Invoke(_tourId);
            }
        }

        /// <summary>
        /// 只切内容根的显隐，不拆内容树。alwaysDisplayed 按导览状态显隐用（ite-current-tour D8）。
        /// </summary>
        public void SetContentVisible(bool visible)
        {
            if (_scene.IsDestroyed) return;

            _mainGroupObject.SetActive(visible);
        }
```

- [ ] **Step 2: `IteTourAssembler`**

在 `Packages/com.uality.ite-tour/Runtime/Core/IteTourAssembler.cs` 的 `SetAllVolumesActive` 方法之后追加：

```csharp

        /// <summary>
        /// alwaysDisplayed 只在已定位时显示（ite-current-tour D8）。只切内容根的显隐，不拆内容树。
        /// </summary>
        public void SetAlwaysDisplayedVisible(bool visible)
        {
            foreach (var tour in _liveTours)
            {
                if (tour != null && tour.DisplayType == IteSpaceScene.Tour.DisplayType.alwaysDisplayed)
                {
                    tour.SetContentVisible(visible);
                }
            }
        }
```

- [ ] **Step 3: `TourDirector`**

在 `Packages/com.uality.ite-tour/Runtime/Core/TourDirector.cs` 中：

(a) 把 `Observe` 的方法体

```csharp
            if (tour != null)
            {
                tour.OnCameraVolumeTransition += SubmitVolumeTransition;
            }
```

改为

```csharp
            if (tour != null)
            {
                tour.OnCameraVolumeTransition += SubmitVolumeTransition;
                tour.OnVolumeCleared += ClearVolume;
            }
```

(b) 把 `SubmitMarkerScan` 整个方法替换为：

```csharp
        public void SubmitMarkerScan(string markerId, Pose pose)
        {
            var before = _guide.Snapshot();
            bool wasSettling = _guide.IsSettling;
            var effect = _guide.SubmitScan(markerId, pose, Descriptors(), out var decision);

            // 真机上判断「为什么没反应」只能靠这一行：决策依据的状态与扫到的位姿都带上。
            Debug.Log(
                $"[ITE] 扫码 {markerId} → {decision.Action}" +
                $"（状态={before.State} 当前={before.CurrentTourId ?? "无"} 在播={before.ActiveTourId ?? "无"} 所在区域=[{JoinIds(_guide.PendingTourIds)}]）" +
                $" 位姿 pos={pose.position.ToString("F3")} rot={pose.rotation.eulerAngles.ToString("F1")}");

            if (!wasSettling && _guide.IsSettling)
            {
                Debug.Log("[ITE] 锚定结算窗口打开：下一个物理步里的区域进出算锚定造成的（ite-current-tour D9）");
            }

            Apply(effect);
        }
```

(c) 把 Task 5 写的 `SubmitVolumeTransition` 整个方法替换为下面四个方法：

```csharp
        /// <summary>
        /// 相机侧某个碰撞体进出某个 Tour 的体积：只更新区域队列（I3），当前 Tour 换不换在帧末判（ite-current-tour §5.2）。
        /// </summary>
        /// <param name="colliderName">只用于日志：同一体积的计数到 2，说明两个碰撞体都在里面。</param>
        public void SubmitVolumeTransition(string tourId, VolumeTransition transition, string colliderName)
        {
            int before = _guide.RegionCountOf(tourId);

            if (!_guide.SubmitVolumeTransition(tourId, transition))
            {
                Debug.LogWarning($"[ITE] 区域 Exit {tourId}（{colliderName}）没有对应的 Enter，已忽略（ite-current-tour D11）");
                return;
            }

            Debug.Log(
                $"[ITE] 区域 {transition} {tourId}（{colliderName}，{before}→{_guide.RegionCountOf(tourId)}）" +
                $"队列=[{JoinIds(_guide.PendingTourIds)}]");
        }

        /// <summary>体积被停用：Unity 不发离开，计数直接清零（ite-current-tour D11）。</summary>
        public void ClearVolume(string tourId)
        {
            int before = _guide.RegionCountOf(tourId);
            _guide.ClearVolume(tourId);

            if (before > 0)
            {
                Debug.Log($"[ITE] 体积停用，清掉 {tourId} 的计数（{before}→0）队列=[{JoinIds(_guide.PendingTourIds)}]");
            }
        }

        /// <summary>
        /// 每个物理步的触发回调全部到达之后调用（<see cref="IteRuntimeDriver"/> 的 WaitForFixedUpdate 循环）。
        /// 锚定结算窗口在这里关上（ite-current-tour D9）。
        /// </summary>
        public void AfterPhysicsStep()
        {
            if (_guide.AfterPhysicsStep())
            {
                Debug.Log(
                    $"[ITE] 锚定结算完成：队列=[{JoinIds(_guide.PendingTourIds)}] 当前={_guide.CurrentTourId ?? "无"}");
            }
        }

        /// <summary>按当前状态设一次 alwaysDisplayed 的显隐。Tour 装配完成后由 IteRuntime 调用（ite-current-tour D8）。</summary>
        public void SyncAlwaysDisplayed() => _assembler.SetAlwaysDisplayedVisible(_guide.AlwaysDisplayedVisible);
```

(d) 把 `EndOfFrame` 整个方法（含注释）替换为：

```csharp
        /// <summary>
        /// 帧末结算，由 <see cref="IteRuntimeDriver"/> 在 <c>LateUpdate</c> 调用：当前 Tour 换不换
        /// （ite-current-tour §5.2），（状态、当前、在播）变了才重算扫码提示（ite-guide-state-machine D6）。
        ///
        /// 在帧末而不是在事件上结算：相邻体积之间移动时，离开 A 与进入 B 在同一物理步内发生，
        /// 按净变化判一次就直接从 A 换到 B，不会先停 A 再启 B。
        /// </summary>
        public void EndOfFrame()
        {
            var previous = _guide.CurrentTourId;
            var effect = _guide.EndOfFrame(_descriptors, out bool changed);
            var current = _guide.CurrentTourId;

            if (current != previous)
            {
                var why = previous == null ? "取队尾" : $"离开 {previous}，取队尾";
                var waiting = current != null && _guide.ActiveTourId != current ? "，等扫码" : "";
                Debug.Log(
                    $"[ITE] 当前 Tour：{previous ?? "无"} → {current ?? "无"}（{why}{waiting}）" +
                    $"队列=[{JoinIds(_guide.PendingTourIds)}]");
            }

            Apply(effect);

            if (changed)
            {
                EvaluateScanPrompt();
            }
        }
```

(e) 在 `Apply` 方法末尾（`ReanchorTourId` 那段 `if` 之后）追加：

```csharp

            if (effect.AlwaysDisplayedVisible.HasValue)
            {
                _assembler.SetAlwaysDisplayedVisible(effect.AlwaysDisplayedVisible.Value);
            }
```

(f) 把类注释第一句

```csharp
    /// 扫描、区域进出与佩戴状态的**效果层**：输入交给 <see cref="TourGuide"/>（纯状态核心），
```

改为

```csharp
    /// 扫描、区域进出、物理步与佩戴状态的**效果层**：输入交给 <see cref="TourGuide"/>（纯状态核心），
```

- [ ] **Step 4: `IteRuntimeDriver`**

把 `Packages/com.uality.ite-tour/Runtime/Core/IteRuntimeDriver.cs` 整个替换为：

```csharp
using System.Collections;
using UnityEngine;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 包自带的帧驱动。<see cref="IteRuntime"/> 在装配时创建一个，销毁时带走。
    ///
    /// 不做成「宿主每帧调 <c>ite.Tick()</c>」：忘了调不会报错，只是区域触发从此不再结算——
    /// 又一个只在真机上现形的静默失效（同 design D26 的 tag 问题）。包自己持有驱动，
    /// 宿主就没有忘的机会。
    /// </summary>
    internal class IteRuntimeDriver : MonoBehaviour
    {
        internal TourDirector Director;

        private void LateUpdate() => Director?.EndOfFrame();

        /// <summary>
        /// 每个物理步之后通知一次，给锚定结算窗口用（ite-current-tour D9）。Unity 每个物理步的顺序是
        /// FixedUpdate → 物理模拟 → OnTrigger* → WaitForFixedUpdate，所以这里返回时，这一步的区域进出
        /// 已经全部到达。编辑器非播放态不跑（Start 不会被调用），EditMode 测试直接调 AfterPhysicsStep。
        /// </summary>
        private IEnumerator Start()
        {
            var wait = new WaitForFixedUpdate();
            while (true)
            {
                yield return wait;
                Director?.AfterPhysicsStep();
            }
        }
    }
}
```

- [ ] **Step 5: `IteRuntime`**

在 `Packages/com.uality.ite-tour/Runtime/Core/IteRuntime.cs` 中：

(a) 在构造函数末尾，`_driver.Director = _director;` 之后追加：

```csharp

            // 锚定结算窗口靠 WaitForFixedUpdate 判定「锚定后那一步物理已经跑完」，只在 FixedUpdate 模拟下成立。
            // 模式被改掉时窗口时序失效，真机上只表现为扫码后内容被切走——必须出声（ite-current-tour D9）。
            if (Physics.simulationMode != SimulationMode.FixedUpdate)
            {
                Debug.LogError(
                    $"[ITE] Physics.simulationMode = {Physics.simulationMode}，区域结算要求 FixedUpdate。" +
                    "检查 Project Settings > Physics。");
            }
```

(b) 在 `LoadAsync` 的循环里，把

```csharp
                await _assembler.CreateAsync(tour, data);
```

改为

```csharp
                await _assembler.CreateAsync(tour, data);

                // alwaysDisplayed 在装配里就建好树并显示了；按当前导览状态收一次——锚定前不该看见它（ite-current-tour D8）
                _director.SyncAlwaysDisplayed();
```

(c) 把 `ActivateTour` 上方注释

```csharp
        /// 不经传感器直接激活指定 Tour（design D30），沿用现有锚定。只在已定位（Anchored）状态下可用，
        /// 其他状态返回 false（ite-guide-state-machine D4）。找不到也返回 false。
```

改为

```csharp
        /// 不经传感器直接激活指定 Tour（design D30），沿用现有锚定，它随即成为当前 Tour。只在已定位（Anchored）
        /// 状态下可用，其他状态返回 false（ite-guide-state-machine D4）；alwaysDisplayed 返回 false（ite-current-tour D10）；
        /// 找不到也返回 false。
```

(d) 把

```csharp
        /// <summary>相机当前所在触发体积对应的 tourId 集合。</summary>
        public IReadOnlyList<string> PendingTourIds => _director.PendingTourIds;
```

改为

```csharp
        /// <summary>相机当前所在触发体积对应的 tourId，按进入先后排列（ite-current-tour D2）。</summary>
        public IReadOnlyList<string> PendingTourIds => _director.PendingTourIds;
```

- [ ] **Step 6: 编译并跑测试，确认通过**

编译；跑 `A=Uality.IteTour.Tests`，再跑 `A=MRBase.Ite.Host.Tests`。Expected: 两个程序集全部通过（`IteRuntimeTests` 会构造 runtime，工程物理模式是 FixedUpdate，不会出现 `simulationMode` 的 LogError）。

再确认宿主没有用到旧的两参数回调：

```bash
cd /Users/wwj/Desktop/unity/MR_Base
grep -rn "OnCameraVolumeTransition" Assets/Scripts Packages/com.uality.ite-tour | grep -v "\.meta:"
```

Expected: 只出现在 `IteTourObject.cs`（声明与调用）和 `TourDirector.cs`（订阅）。

- [ ] **Step 7: 提交**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git add Packages/com.uality.ite-tour/Runtime/Core/IteTourObject.cs Packages/com.uality.ite-tour/Runtime/Core/IteTourAssembler.cs \
        Packages/com.uality.ite-tour/Runtime/Core/TourDirector.cs Packages/com.uality.ite-tour/Runtime/Core/IteRuntimeDriver.cs \
        Packages/com.uality.ite-tour/Runtime/Core/IteRuntime.cs
git commit -m "$(cat <<'EOF'
feat(ite): 效果层接上物理步通知、体积停用清零与 alwaysDisplayed 显隐

- 驱动在每个 WaitForFixedUpdate 后通知，关锚定结算窗口（ite-current-tour D9）
- 体积停用时清掉计数，Unity 不发 Exit（ite-current-tour D11）
- alwaysDisplayed 只在已定位时显示（ite-current-tour D8）
- 区域日志带碰撞体名字与计数变化，当前 Tour 变化写明原因（ite-current-tour §8）
- 物理模拟模式不是 FixedUpdate 时报错

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: 全量回归与旧文档标注

**Files:**
- Modify: `docs/superpowers/specs/2026-09-23-ite-scan-region-gate-design.md`
- Modify: `docs/superpowers/specs/2026-09-23-ite-guide-state-machine-design.md`
- Modify: `docs/handoff/2026-09-23-ite-region-trigger-issues.md`

**Interfaces:**
- Consumes: Task 1–6 的全部改动
- Produces: 无代码

- [ ] **Step 1: 全量回归**

编译；跑 `A=Uality.IteTour.Tests`，再跑 `A=MRBase.Ite.Host.Tests`。Expected: 两个程序集全部通过，无失败项。把两行 `Summary` 原样记下来，最后汇报时附上。

- [ ] **Step 2: 标注被取代的旧决策**

在 `docs/superpowers/specs/2026-09-23-ite-scan-region-gate-design.md` 中：

- 在 `### D1 定位后，人在所有区域之外时扫码一律忽略` 这一行的下一行插入一个空行和：
  `> 已被 `2026-09-23-ite-current-tour-design.md` 的 D6 取代：定位后只认当前 Tour 的码。`
- 在 `### D3 定位后人在所有区域之外时，不显示扫码提示` 这一行的下一行插入一个空行和：
  `> 已被 `2026-09-23-ite-current-tour-design.md` 的 D7 取代：定位后只在当前 Tour 是 normal 且未播时提示它。`

在 `docs/superpowers/specs/2026-09-23-ite-guide-state-machine-design.md` 中：

- 在 `### D5 区域重选改为「帧末按集合的净变化判一次」，去掉 `_reselectPending`` 这一行的下一行插入一个空行和：
  `> 已被 `2026-09-23-ite-current-tour-design.md` 的 D2（取队尾）、D3（在播优先）、D9（锚定结算窗口）取代。`
- 在以 `- **I1**：` 开头的那一行末尾追加：
  `（`alwaysDisplayed` 不受状态影响这一点，已被 ite-current-tour D8 修订：只在 `Anchored` 下显示。）`

在 `docs/handoff/2026-09-23-ite-region-trigger-issues.md` 中，第一行标题之后插入一个空行和：

`> 后续：两个问题的修复见 spec `docs/superpowers/specs/2026-09-23-ite-current-tour-design.md` 与计划 `docs/superpowers/plans/2026-09-23-ite-current-tour.md`。`

- [ ] **Step 3: 提交**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git add docs/superpowers/specs/2026-09-23-ite-scan-region-gate-design.md \
        docs/superpowers/specs/2026-09-23-ite-guide-state-machine-design.md \
        docs/handoff/2026-09-23-ite-region-trigger-issues.md
git commit -m "$(cat <<'EOF'
docs(ite): 标注被 ite-current-tour 取代的旧决策

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: 汇报并停下**

向用户汇报：两个程序集的 `Summary`；spec §10 的真机验证清单（出包要等用户说「打包」）。**不要打包、不要装机。**

---

## 补充（2026-09-24，整分支审查之后）

整分支审查（`c8383d6..8d53ed9`）查出 C1：Task 6 用停用内容根来隐藏 `alwaysDisplayed`，会销毁其中的视频（`VideoPlaneElement.OnDisable` 销毁 `VideoPlayer` 与 RenderTexture，没有 `OnEnable` 重建），还会让首屏效果在看不见的时候就放完。用户选方案 B，并把 M3 一并做。依据 spec D13、D14（`c9ab701`）。

Review Focus 第 5 条（`alwaysDisplayed` 隐藏后再显示）改由 Task 8 的测试覆盖，不再只靠真机。

### Task 8: `alwaysDisplayed` 按导览状态建树、拆树（spec D13）

**Files:**
- Create: `Packages/com.uality.ite-tour/Tests/Editor/AlwaysDisplayedLifecycleTests.cs`
- Modify: `Packages/com.uality.ite-tour/Tests/Editor/TourAssemblyTests.cs`（删 `RetainsSceneWhenDeactivated_OnlyAlwaysDisplayed`）
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/IteTourObject.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/TourAssembly.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/IteTourAssembler.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/IteRuntime.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/TourDirector.cs`（只改一段注释）

**Interfaces:**
- Consumes: `IteTourAssembler.CreateAsync`、`IteTourAssembler.TourCreated`、`IteTourAssembler.SetAlwaysDisplayedVisible(bool)`（Task 6）、`IteTourObject.IsSceneReady`、`IteTourObject.OnTourSceneLoaded`（既有）
- Produces: `SetAlwaysDisplayedVisible(bool)` 签名不变，语义改为「显示就建树、隐藏就拆树」；删除 `IteTourObject.SetContentVisible`、`TourAssembly.RetainsSceneWhenDeactivated`

- [ ] **Step 1: 写失败的测试**

新建 `Packages/com.uality.ite-tour/Tests/Editor/AlwaysDisplayedLifecycleTests.cs`：

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// alwaysDisplayed 可见 ⇔ 内容树已建好（ite-current-tour D13）。内容组件把 OnDisable 当拆除用
    /// （VideoPlaneElement 会销毁 VideoPlayer），所以隐藏只能拆树、重新显示只能重建。
    ///
    /// 走真实的 IteTourAssembler / IteTourObject：Tour 预制体在内存里搭（体积 + 内容根），内容是一个
    /// 空场景，装配、建树、拆树都同步完成，不需要 async 测试。
    /// </summary>
    public class AlwaysDisplayedLifecycleTests
    {
        private const IteSpaceScene.Tour.DisplayType Always = IteSpaceScene.Tour.DisplayType.alwaysDisplayed;
        private const IteSpaceScene.Tour.DisplayType Regional = IteSpaceScene.Tour.DisplayType.regionalTrigger;

        private GameObject _prefab;
        private GameObject _anchorRoot;
        private GameObject _camera;
        private IteTourAssembler _assembler;
        private readonly Dictionary<string, int> _loaded = new Dictionary<string, int>();

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("tour-prefab");

            var volume = new GameObject("Volume");
            volume.transform.SetParent(_prefab.transform);
            volume.AddComponent<BoxCollider>();

            var group = new GameObject("Group");
            group.transform.SetParent(_prefab.transform);

            var serialized = new SerializedObject(_prefab.AddComponent<IteTourObject>());
            serialized.FindProperty("_volumeObject").objectReferenceValue = volume;
            serialized.FindProperty("_mainGroupObject").objectReferenceValue = group;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            _anchorRoot = new GameObject("AnchorRoot");
            var tourRoot = new GameObject("TourRoot");
            tourRoot.transform.SetParent(_anchorRoot.transform);
            _camera = new GameObject("Camera");

            _assembler = new IteTourAssembler(_prefab, tourRoot.transform, _anchorRoot.transform, _camera.transform);

            // TourId 在 CreateTourObject 里才写入；回调触发时已经有值
            _loaded.Clear();
            _assembler.TourCreated += tour =>
                tour.OnTourSceneLoaded += () => _loaded[tour.TourId] = LoadedCount(tour.TourId) + 1;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_anchorRoot);
            Object.DestroyImmediate(_camera);
            Object.DestroyImmediate(_prefab);
        }

        [Test]
        public void Assembled_AlwaysDisplayed_IsNotBuilt()
        {
            var tour = Create("a1", Always);

            Assert.That(tour.IsSceneReady, Is.False, "锚定前看不见，就不该建树——首屏效果会在看不见时放完");
            Assert.That(LoadedCount("a1"), Is.EqualTo(0));
        }

        [Test]
        public void Shown_Builds_AndRaisesLoadedOnce()
        {
            var tour = Create("a1", Always);
            int before = LoadedCount("a1");

            _assembler.SetAlwaysDisplayedVisible(true);

            Assert.That(tour.IsSceneReady, Is.True);
            Assert.That(LoadedCount("a1"), Is.EqualTo(before + 1), "首屏效果在看得见时触发");
        }

        /// <summary>每个 Tour 装配完成后都会按当前状态同步一次，已定位时会反复「显示」。</summary>
        [Test]
        public void ShownTwice_BuildsOnce()
        {
            var tour = Create("a1", Always);
            int before = LoadedCount("a1");

            _assembler.SetAlwaysDisplayedVisible(true);
            _assembler.SetAlwaysDisplayedVisible(true);

            Assert.That(tour.IsSceneReady, Is.True);
            Assert.That(LoadedCount("a1"), Is.EqualTo(before + 1), "首屏效果不能叠放");
        }

        [Test]
        public void Hidden_TearsDown()
        {
            var tour = Create("a1", Always);
            _assembler.SetAlwaysDisplayedVisible(true);

            _assembler.SetAlwaysDisplayedVisible(false);

            Assert.That(tour.IsSceneReady, Is.False,
                "停用不是可恢复的隐藏（VideoPlaneElement.OnDisable 会销毁 VideoPlayer），只能拆树");
        }

        [Test]
        public void ShownAgain_Rebuilds_AndRaisesLoadedAgain()
        {
            var tour = Create("a1", Always);
            _assembler.SetAlwaysDisplayedVisible(true);
            _assembler.SetAlwaysDisplayedVisible(false);
            int before = LoadedCount("a1");

            _assembler.SetAlwaysDisplayedVisible(true);

            Assert.That(tour.IsSceneReady, Is.True);
            Assert.That(LoadedCount("a1"), Is.EqualTo(before + 1), "摘下再戴上、扫码后首屏效果重放");
        }

        /// <summary>回归守卫（改动前也成立）：别的类型由 TourDirector 按当前 Tour 激活，不受显隐影响。</summary>
        [Test]
        public void OtherDisplayTypes_AreUnaffected()
        {
            var regional = Create("r1", Regional);

            _assembler.SetAlwaysDisplayedVisible(true);

            Assert.That(regional.IsSceneReady, Is.False);
        }

        private int LoadedCount(string tourId) => _loaded.TryGetValue(tourId, out var count) ? count : 0;

        private IteTourObject Create(string tourId, IteSpaceScene.Tour.DisplayType displayType)
        {
            var tour = new IteSpaceScene.Tour
            {
                tourID = tourId,
                displayType = displayType,
                transform = new[]
                {
                    new[] { 1f, 0f, 0f, 0f },
                    new[] { 0f, 1f, 0f, 0f },
                    new[] { 0f, 0f, 1f, 0f },
                    new[] { 0f, 0f, 0f, 1f },
                },
                triggerVolume = new IteSpaceScene.Tour.TriggerVolume { width = 1f, height = 1f, depth = 1f },
            };

            // 必须有一个场景：ScenesOrder 为空时建树会 LogError（测试框架会判失败）
            var data = new Data.IteTour
            {
                Id = tourId,
                Assets = new Dictionary<string, Data.Assets.Asset>(),
                Scenes = new Dictionary<string, Data.Scene>
                {
                    ["s1"] = new Data.Scene { Entities = new Dictionary<string, Data.Entity>() },
                },
                ScenesOrder = new[] { "s1" },
            };

            var task = _assembler.CreateAsync(tour, data);
            Assert.That(task.IsCompleted, Is.True, "内容为空时装配应同步完成，用例前提不成立");
            return task.Result;
        }
    }
}
```

注意：在 `Uality.IteTour.Tests` 命名空间里，简单名 `IteTour` 会先解析成命名空间 `Uality.IteTour`，所以数据类一律写 `Data.IteTour`、`Data.Scene`、`Data.Entity`。

在 `Packages/com.uality.ite-tour/Tests/Editor/TourAssemblyTests.cs` 里删掉整个 `RetainsSceneWhenDeactivated_OnlyAlwaysDisplayed` 测试（含 `[Test]` 和它前面的空行）。

- [ ] **Step 2: 编译并跑测试，确认失败**

编译；跑 `A=Uality.IteTour.Tests`。Expected: 编译成功（新测试只用到已有接口）；`AlwaysDisplayedLifecycleTests` 里 5 个失败：`Assembled_AlwaysDisplayed_IsNotBuilt`、`Shown_Builds_AndRaisesLoadedOnce`、`ShownTwice_BuildsOnce`、`Hidden_TearsDown`、`ShownAgain_Rebuilds_AndRaisesLoadedAgain`；`OtherDisplayTypes_AreUnaffected` 通过（回归守卫）。其余测试全部通过。

- [ ] **Step 3: 实现**

(a) `IteTourObject.cs`，`CreateTourObject` 末尾，把

```csharp
            await LoadAssets(tourData.Assets);
            ChangeDisplayType(tour.displayType);

            // 源实现对 alwaysDisplayed 既不 Disable 也不 Enable，而内容树由 Enable 构建——
            // 于是「一直显示」的 Tour 内容永远是空的，且不报错（design D29）。
            if (tour.displayType == IteSpaceScene.Tour.DisplayType.alwaysDisplayed)
            {
                await Enable();
            }
            else
            {
                Disable();
            }
        }
```

改为

```csharp
            await LoadAssets(tourData.Assets);
            ChangeDisplayType(tour.displayType);

            // 装配只准备资源、不建树，所有展示类型一样。什么时候建由编排层决定：普通 Tour 在被激活时，
            // alwaysDisplayed 在进入已定位时（ite-current-tour D13）。design D29 曾让 alwaysDisplayed
            // 装配即建树（源实现对它既不 Enable 也不 Disable，内容永远是空的），但那样首屏效果会在
            // 锚定前、看不见的时候就放完。
            Disable();
        }
```

(b) `IteTourObject.cs`，删掉 Task 6 加的整个 `SetContentVisible` 方法（含上方 `/// <summary>` 注释）。

(c) `IteTourObject.cs`，`TearDownScene` 不再有保留内容树的特例，参数 `force` 随之删除。把

```csharp
        private void TearDownScene(bool force)
        {
            if (_scene.IsDestroyed) return;

            if (!force && TourAssembly.RetainsSceneWhenDeactivated(_displayType))
            {
                _canAnchor = false;
                return;
            }

            _scene.TearDown();
```

改为

```csharp
        private void TearDownScene()
        {
            if (_scene.IsDestroyed) return;

            _scene.TearDown();
```

并把 `Disable()` 里的 `TearDownScene(force: false);`、`Destroy()` 里的 `TearDownScene(force: true);` 都改为 `TearDownScene();`。

(d) `TourAssembly.cs`，删掉 `RetainsSceneWhenDeactivated` 方法及其 `/// <summary>停用当前导览时仍保留内容树。只有卸载才拆。</summary>` 注释（连同前面的空行）。

(e) `IteTourAssembler.cs`，把 `TourCreated` 的注释

```csharp
        /// <summary>
        /// Tour 实例已就位、内容尚未构建。订阅方在这一刻挂钩子。
        ///
        /// 时机必须在 <c>CreateTourObject</c> 之前：<c>alwaysDisplayed</c> 的 Tour 会在
        /// 那里面就把内容建完并触发 <c>OnTourSceneLoaded</c>（design D29），事后再订就晚了。
        /// </summary>
```

改为

```csharp
        /// <summary>
        /// Tour 实例已就位、内容尚未构建。订阅方在这一刻挂钩子：早于 <c>CreateTourObject</c>，
        /// 之后的体积进出、体积停用、建树完成都不会漏。
        /// </summary>
```

再把 Task 6 加的 `SetAlwaysDisplayedVisible`（含注释）整个替换为：

```csharp
        /// <summary>
        /// alwaysDisplayed 只在已定位时显示（ite-current-tour D8），可见 ⇔ 内容树已建好（ite-current-tour D13）：
        /// 显示就建树，隐藏就拆树。不能只停用内容根——内容组件把 OnDisable 当拆除用（VideoPlaneElement
        /// 会销毁 VideoPlayer），重新启用回不来。重复调用是空操作（Enable / Disable 都幂等）。
        /// </summary>
        public void SetAlwaysDisplayedVisible(bool visible)
        {
            foreach (var tour in _liveTours)
            {
                if (tour == null || tour.DisplayType != IteSpaceScene.Tour.DisplayType.alwaysDisplayed)
                {
                    continue;
                }

                if (visible)
                {
                    Show(tour);
                }
                else
                {
                    tour.Disable();
                }
            }
        }

        /// <summary>
        /// 发出即走，不让调用方等建树（ite-current-tour D13）。建树抛错必须出声：没人 await 的 Task 里的
        /// 异常不会进日志，内容就这样静默地空着。
        /// </summary>
        private static async void Show(IteTourObject tour)
        {
            try
            {
                await tour.Enable();
            }
            catch (Exception e)
            {
                Debug.LogException(e, tour);
            }
        }
```

(f) `IteRuntime.cs`，把 `TourActivated` 的订阅

```csharp
            _director.TourActivated += id =>
            {
                OnTourActivated?.Invoke(id);

                // alwaysDisplayed 在装配时已经建树并派发过组件侧 OnTourSceneLoaded。
                // 再次 Activate 不再重建，组件事件不能重放（LoadTrigger 会再跑一遍）。
                // 宿主超时监视仍需要一次 Loaded，由这里补发给宿主。
                var tour = _assembler.Find(id);
                if (tour != null && tour.IsSceneReady)
                {
                    OnTourSceneLoaded?.Invoke(id);
                }
            };
```

改为（D10 之后 `alwaysDisplayed` 不会被激活，别的类型被激活时内容一定已拆，补发分支走不到）

```csharp
            _director.TourActivated += id => OnTourActivated?.Invoke(id);
```

把

```csharp
            // 必须在 CreateTourObject 之前挂钩：alwaysDisplayed 的 Tour 在那里面就把内容
            // 建完并触发 OnTourSceneLoaded（design D29）
```

改为

```csharp
            // 早于 CreateTourObject 挂钩，之后的体积事件与建树完成都不会漏（见 IteTourAssembler.TourCreated）
```

把

```csharp
                // alwaysDisplayed 在装配里就建好树并显示了；按当前导览状态收一次——锚定前不该看见它（ite-current-tour D8）
```

改为

```csharp
                // 已定位之后才装配完的 alwaysDisplayed 在这里建树；未定位时它们本就没建，调用无副作用（ite-current-tour D13）
```

(g) `TourDirector.cs`，`Activate` 里把

```csharp
            // 先广播再 Enable。已建树时 Enable 是空操作、不再派发组件侧 Loaded
            // （LoadTrigger 不能重放）；宿主超时由 IteRuntime 在 Activated 回调里补一次。
```

改为

```csharp
            // 先广播再 Enable：宿主据此开始等 OnTourSceneLoaded。换过来的 Tour 内容一定已拆
            // （ite-current-tour D13 之后没有停用时保留内容树的类型），Enable 必然重建并派发 Loaded。
```

- [ ] **Step 4: 编译并跑测试，确认通过**

编译；跑 `A=Uality.IteTour.Tests`，再跑 `A=MRBase.Ite.Host.Tests`。Expected: 两个程序集全部通过，`AlwaysDisplayedLifecycleTests` 6 个全过。

再确认没有残留：

```bash
cd /Users/wwj/Desktop/unity/MR_Base
grep -rn "RetainsSceneWhenDeactivated\|SetContentVisible\|force: " Packages/com.uality.ite-tour Assets/Scripts | grep -v "\.meta:"
```

Expected: 没有输出。

- [ ] **Step 5: 提交**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
P=Packages/com.uality.ite-tour
git add $P/Tests/Editor/AlwaysDisplayedLifecycleTests.cs $P/Tests/Editor/AlwaysDisplayedLifecycleTests.cs.meta \
        $P/Tests/Editor/TourAssemblyTests.cs \
        $P/Runtime/Core/IteTourObject.cs $P/Runtime/Core/TourAssembly.cs $P/Runtime/Core/IteTourAssembler.cs \
        $P/Runtime/Core/IteRuntime.cs $P/Runtime/Core/TourDirector.cs
git commit -m "$(cat <<'EOF'
fix(ite): alwaysDisplayed 按导览状态建树拆树，不再停用内容根

停用内容根会触发内容组件的 OnDisable（VideoPlaneElement 销毁 VideoPlayer），
重新显示后视频面是白板；首屏效果也在锚定前、看不见时就放完了。
改为可见即已建树：进入已定位时建，离开时拆（ite-current-tour D13）。
删除 RetainsSceneWhenDeactivated、SetContentVisible 与 IteRuntime 里走不到的补发分支。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

### Task 9: 帧驱动在 `OnEnable` 启动物理步协程（spec D14）

**Files:**
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/IteRuntimeDriver.cs`

**Interfaces:**
- Consumes: `TourDirector.AfterPhysicsStep()`（Task 6）
- Produces: 无

**这个 Task 没有先失败的测试**：EditMode 不跑 MonoBehaviour 的生命周期和协程，为此引入 PlayMode 测试框架不划算。验收是编译通过、已有测试全部通过、残留检查为空，行为靠真机日志（spec §10 第 6 条：每次扫码后都有「锚定结算完成」）。

- [ ] **Step 1: 实现**

把 `IteRuntimeDriver.cs` 里 Task 6 加的

```csharp
        /// <summary>
        /// 每个物理步之后通知一次，给锚定结算窗口用（ite-current-tour D9）。Unity 每个物理步的顺序是
        /// FixedUpdate → 物理模拟 → OnTrigger* → WaitForFixedUpdate，所以这里返回时，这一步的区域进出
        /// 已经全部到达。编辑器非播放态不跑（Start 不会被调用），EditMode 测试直接调 AfterPhysicsStep。
        /// </summary>
        private IEnumerator Start()
        {
```

改为

```csharp
        /// <summary>
        /// 在 OnEnable 而不是 Start 里启动（ite-current-tour D14）：对象停用时 Unity 会停掉它上面的协程，
        /// 而 Start 只跑一次，再启用就回不来——LateUpdate 照常恢复，结算窗口却从此关不上，当前 Tour
        /// 再也不换，且不报错。编辑器非播放态不跑（OnEnable 不会被调用），EditMode 测试直接调 AfterPhysicsStep。
        /// </summary>
        private void OnEnable() => StartCoroutine(NotifyPhysicsSteps());

        /// <summary>
        /// 每个物理步之后通知一次，给锚定结算窗口用（ite-current-tour D9）。Unity 每个物理步的顺序是
        /// FixedUpdate → 物理模拟 → OnTrigger* → WaitForFixedUpdate，所以这里返回时，这一步的区域进出
        /// 已经全部到达。
        /// </summary>
        private IEnumerator NotifyPhysicsSteps()
        {
```

方法体不变。

- [ ] **Step 2: 编译并跑测试**

编译；跑 `A=Uality.IteTour.Tests`、`A=MRBase.Ite.Host.Tests`、`A=MRBase.Build.Editor.Tests`。Expected: 全部通过。

```bash
cd /Users/wwj/Desktop/unity/MR_Base
grep -n "IEnumerator Start\|void Start" Packages/com.uality.ite-tour/Runtime/Core/IteRuntimeDriver.cs
```

Expected: 没有输出。

- [ ] **Step 3: 提交**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git add Packages/com.uality.ite-tour/Runtime/Core/IteRuntimeDriver.cs
git commit -m "$(cat <<'EOF'
fix(ite): 帧驱动在 OnEnable 启动物理步协程

用 Start 启动时，驱动对象停用再启用后协程永久消失，结算窗口从此关不上、
当前 Tour 再也不换且不报错（ite-current-tour D14）。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: 汇报并停下**

向用户汇报三个程序集的 `Summary`，以及 spec §10 的真机验证清单（第 1–7 条）。**不要打包、不要装机。**
