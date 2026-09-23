# ITE 扫码区域门禁 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 定位过一次之后，只有人站在某个 Tour 的区域里，扫这个 Tour 的码才生效；「必须扫码」状态（冷启动、重新戴上、追踪原点重置）下不看区域。

**Architecture:** 规则全部收在 ITE 包的两个纯决策函数里：`TourScanPolicy.Decide`（扫码生不生效）和 `ScanPromptPolicy.Decide`（提示显示什么）。两者都只读 `ScanState` 快照，没有副作用。宿主、识别链路、区域进出逻辑都不改。

**Tech Stack:** Unity 6000.4.4f1，C#，NUnit（Unity Test Framework，EditMode），`unity` CLI 连接用户正在打开的 Editor。

**Spec:** `docs/superpowers/specs/2026-09-23-ite-scan-region-gate-design.md`

## Global Constraints

- 只改 `Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs`、`Packages/com.uality.ite-tour/Runtime/Core/ScanPromptPolicy.cs` 和它们在 `Packages/com.uality.ite-tour/Tests/Editor/` 下的测试。宿主（`Assets/Scripts/IteHost`）不动。
- 触发体积尺寸（`IteTourObject.CreateTourObject` 里的 `/ 2`）不动（spec D4）。
- 不读、不恢复 `openspec/`（用户已删除）。代码里引用本次决策时，写 `ite-scan-region-gate D1/D2/D3`，不写 `design D36`。
- Unity 操作一律用 `unity` CLI，在用户打开的 Editor 里执行；不开 headless Unity，不开第二个 Unity 进程。
- 工作区里还有两处和本 plan 无关的改动：`Packages/com.uality.ite-tour/Runtime/Internal/RoundedBoxUI.shader`（另一个修复）、`Assets/Resources/DevAgentSettings.asset`（用户自己的改动）。**不要把它们放进本 plan 的提交。**
- 提交只在用户明确同意后执行。

## 如何跑测试（所有 Task 通用）

编译（改完 `.cs` 之后先做这一步）：

```bash
cd /Users/wwj/Desktop/unity/MR_Base
unity command recompile --project-path .
for i in $(seq 1 40); do s=$(unity command recompile_status --project-path . 2>&1 | tail -1); case "$s" in *completed*|*up_to_date*) echo "$s"; break;; esac; sleep 3; done
```

预期最后一行包含 `"failed":false,"errors":[]`。

跑一个程序集并只列出失败项（`$A` 换成 `Uality.IteTour.Tests` 或 `MRBase.Ite.Host.Tests`）：

```bash
cd /Users/wwj/Desktop/unity/MR_Base
A=Uality.IteTour.Tests
unity command run_tests --project-path . --mode EditMode --filter "$A" --filter_type assembly --timeout 300 2>&1 | tail -1 > "$TMPDIR/r.json"
python3 - "$TMPDIR/r.json" "$A" <<'EOF'
import json,sys
j=json.loads(open(sys.argv[1]).read().split('\t')[2])
print(sys.argv[2], j.get("Summary"), j.get("error"))
for r in j.get("Results",[]):
    if r["Status"]!="Passed":
        print("  FAIL", r["FullName"], "|", (r["Message"] or "").strip().replace("\n"," ")[:200])
EOF
```

`--filter_type` 只接受 `testName` / `assembly` / `category`，不支持正则。

## Review Focus

- 定位后人站在两个重叠的区域里，扫其中任何一个 Tour 的码都应生效 → Task 1 Step 5 加测试。
- 追踪原点重置时 Tour 仍在播、人在所有区域之外，扫这个在播 Tour 的码应该重新锚定它（D2 的主场景：原点重置不会停掉 Tour）→ Task 1 Step 5 加测试。
- 人在 t1 的区域里扫 t2 的码 → 忽略。已有测试 `Decide_WhenMarkerOutsidePendingTours_Ignores` 覆盖，不新增。
- 定位后人在只有 `alwaysDisplayed` Tour 的区域里 → 不提示。已有测试 `Decide_WhenNothingActionableIsInRange_Hides` 覆盖，不新增。
- 第一次锚定后，触发体积被挪到真实位置，区域集合要等下一个物理步才更新；锚定后立刻在区域里扫码，可能因为集合还没更新而被拒。这是物理时序，EditMode 纯函数测不到，列入 Task 3 的真机验证项。

---

## 当前状态说明

写 spec 之前，代码已经按 spec 改过了（未提交）。当时的过程是：先改测试，跑一次确认 4 条新测试按预期失败（`Expected: Ignore But was: Activate` ×2，`Expected: Hidden But was: Visible` ×2），再改实现，全部通过（ITE 包 274/274，宿主 30/30）。下面标 `[x]` 的步骤就是已经做完的，每一步都附了当时的结果。没做完的是：Review Focus 要求的两条测试、把注释和测试名里的 `D36` 改成指向新 spec、最终回归和提交。

---

### Task 1: 扫码判定的区域门禁（spec D1、D2）

**Files:**
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs`（`ScanState.PendingTourIds` 的注释；`Decide` 里原「集合为空不设限」那段判断）
- Test: `Packages/com.uality.ite-tour/Tests/Editor/TourScanPolicyTests.cs`

**Interfaces:**
- Consumes: `TourIdLists.Contains(IReadOnlyList<string> ids, string id)`，`ids` 为 null 或 `id` 为空时返回 `false`（`Packages/com.uality.ite-tour/Runtime/Core/TourIdLists.cs`，本 plan 不改它）。
- Produces: `TourScanPolicy.Decide(ScanState, IReadOnlyList<TourDescriptor>, string)` 签名不变。行为变化：`ForcedScanPending == false` 且 `PendingTourIds` 不含该码（包括为空或为 null）时，返回 `ScanDecision.Ignore`。

- [x] **Step 1: 先改测试**

在 `TourScanPolicyTests` 里加了辅助方法 `InVolumes(params string[])`；新增三条测试：强制扫码时在所有区域外 → `Activate`，定位后在所有区域外 → `Ignore`，定位后 `PendingTourIds == null` → `Ignore`；删掉了 `Decide_WhenPendingToursEmpty_DoesNotFilter`；给区域内分支的既有用例补上 `PendingTourIds = InVolumes("t1")`。

- [x] **Step 2: 跑测试，确认失败**

结果：`TourScanPolicyTests.Decide_AfterAnchoring_OutsideAllVolumes_Ignores_D36` 和 `..._WithoutVolumeSet_Ignores_D36` 失败，报 `Expected: Ignore But was: Activate`。其余通过。

- [x] **Step 3: 改实现**

`TourScanPolicy.Decide` 里，原来的

```csharp
if (state.PendingTourIds != null
    && state.PendingTourIds.Count > 0
    && !TourIdLists.Contains(state.PendingTourIds, markerId))
```

改成

```csharp
if (!TourIdLists.Contains(state.PendingTourIds, markerId))
```

- [x] **Step 4: 跑测试，确认通过**

结果：`Uality.IteTour.Tests` 274/274 通过。

- [ ] **Step 5: 加 Review Focus 的两条测试**

在 `TourScanPolicyTests.cs` 里、`Decide_AfterAnchoring_WithoutVolumeSet_Ignores_D36` 那个方法后面加：

```csharp
        /// <summary>重叠区域：相机同时在 t1、t2 的体积里，两个码都认。</summary>
        [Test]
        public void Decide_InsideOverlappingVolumes_AcceptsTheOtherTour()
        {
            var state = new ScanState { ActiveTourId = "t1", PendingTourIds = InVolumes("t1", "t2") };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1"), Tour("t2")), "t2");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(decision.TourId, Is.EqualTo("t2"));
        }

        /// <summary>
        /// 追踪原点重置（ite-scan-region-gate D2）不停用当前 Tour，只要求重扫。
        /// 此刻相机相对错位的体积在哪都不可信——在所有体积外扫在播 Tour 的码，
        /// 必须走激活路径把它重新锚定，而不是按「normal 已激活」忽略掉。
        /// </summary>
        [Test]
        public void Decide_ForcedScanWhileTourActive_ReanchorsItOutsideAllVolumes()
        {
            var state = new ScanState
            {
                ForcedScanPending = true,
                ActiveTourId = "t1",
                PendingTourIds = InVolumes(),
            };

            var decision = TourScanPolicy.Decide(state, Tours(Tour("t1", Normal)), "t1");

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(decision.TourId, Is.EqualTo("t1"));
            Assert.That(decision.ClearsForcedScan, Is.True);
        }
```

这两条测的是现有代码已经具备、spec 要求保持的行为，加完就应该直接通过。它们的作用是把这两个行为钉住，以后谁改坏了会变红，不是用来驱动新实现的。
- 如果以后有人把区域判断挪到强制扫码分支之前，第二条会变红。
- 如果以后有人把 `Contains` 换成「只比较第一个元素」之类的写法，第一条会变红。

- [ ] **Step 6: 把 `D36` 引用改成指向新 spec**

`TourScanPolicy.cs` 里 `ScanState.PendingTourIds` 的注释，改成：

```csharp
        /// <summary>
        /// 相机当前所在触发体积对应的 Tour 集合。非强制扫码只认这里面的码；
        /// 为空表示相机在所有体积外，此时扫码不生效（ite-scan-region-gate D1）。
        /// </summary>
```

`Decide` 里区域判断上方的注释，改成：

```csharp
            // 定位过之后只认相机所在触发体积的码，体积外一律不认（ite-scan-region-gate D1）。
            // 源实现在体积外不设限；现在只有上面的强制扫码能无视区域（D2）——
            // 那时体积还没按真实位姿锚定，站在哪都不算数。
```

`TourScanPolicyTests.cs`：
- 测试方法名去掉 `_D36` 后缀：`Decide_ForcedScan_ActivatesOutsideAllVolumes`、`Decide_AfterAnchoring_OutsideAllVolumes_Ignores`、`Decide_AfterAnchoring_WithoutVolumeSet_Ignores`。
- `Decide_ForcedScan_ActivatesOutsideAllVolumes` 上方注释的开头 `D36：` 改成 `ite-scan-region-gate D2：`。
- 分节注释 `// ---- 区域门禁（D36）：定位过之后，只认相机所在体积的码 ----` 改成 `// ---- 区域门禁（ite-scan-region-gate D1）：定位过之后，只认相机所在体积的码 ----`。
- 断言消息 `"源实现在体积外不设限；D36 起定位过之后必须走进该 tour 的体积"` 改成 `"源实现在体积外不设限；ite-scan-region-gate D1 起定位过之后必须走进该 tour 的体积"`。

- [ ] **Step 7: 编译并跑测试，确认通过**

按「如何跑测试」先编译，再跑 `A=Uality.IteTour.Tests`。
预期：`{'Total': 276, 'Passed': 276, 'Failed': 0, ...}`，没有 FAIL 行。
确认残留：`grep -n "D36" Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs Packages/com.uality.ite-tour/Tests/Editor/TourScanPolicyTests.cs` 没有输出。

---

### Task 2: 扫码提示与区域门禁保持一致（spec D3）

**Files:**
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/ScanPromptPolicy.cs`（`ScanPrompt.TourIds` 的注释；`Decide` 里「必须扫码或不在任何区域 → 提示扫任意码」那段）
- Test: `Packages/com.uality.ite-tour/Tests/Editor/ScanPromptPolicyTests.cs`

**Interfaces:**
- Consumes: 同一个 `ScanState` 快照（`ForcedScanPending`、`ActiveTourId`、`PendingTourIds`）。
- Produces: `ScanPromptPolicy.Decide(ScanState, IReadOnlyList<TourDescriptor>)` 签名不变。行为变化：`ForcedScanPending == false`、没有 Tour 在播，且 `PendingTourIds` 为空或为 null 时，返回 `ScanPrompt.Hidden`（原来是 `Visible(空列表)`）。消费方（`IteHmdPanel`、`IteEditorHud`、`IteHostBootstrap`）不用改。

- [x] **Step 1: 先改测试**

`Decide_WhenNotInsideAnyVolume_ShowsWithoutNamingTours` 改成「定位后不在任何区域 → 不提示」；新增「定位后 `PendingTourIds == null` → 不提示」；新增「必须扫码时在所有区域外 → 提示扫任意码」。

- [x] **Step 2: 跑测试，确认失败**

结果：`ScanPromptPolicyTests.Decide_AfterAnchoring_NotInsideAnyVolume_Hides_D36` 和 `..._WithoutVolumeSet_Hides_D36` 失败，报 `Expected: Hidden But was: Visible`。

- [x] **Step 3: 改实现**

原来的

```csharp
if (state.ForcedScanPending || state.PendingTourIds == null || state.PendingTourIds.Count == 0)
{
    return ScanPrompt.Visible(Array.Empty<string>());
}
```

拆成两段：

```csharp
if (state.ForcedScanPending)
{
    return ScanPrompt.Visible(Array.Empty<string>());
}

if (state.PendingTourIds == null || state.PendingTourIds.Count == 0)
{
    return ScanPrompt.Hidden;
}
```

`ScanPrompt.TourIds` 的注释也改了：「为空表示随便扫哪个都行」只在强制扫码时出现。

- [x] **Step 4: 跑测试，确认通过**

结果：与 Task 1 Step 4 是同一次运行，274/274 通过。

- [ ] **Step 5: 把 `D36` 引用改成指向新 spec**

`ScanPromptPolicy.cs` 里的注释

```csharp
            // 定位过之后体积外扫码不生效（design D36），不提示
```

改成

```csharp
            // 定位过之后体积外扫码不生效，不提示（ite-scan-region-gate D3）
```

`ScanPromptPolicyTests.cs`：
- 测试方法名去掉 `_D36` 后缀：`Decide_AfterAnchoring_NotInsideAnyVolume_Hides`、`Decide_AfterAnchoring_WithoutVolumeSet_Hides`、`Decide_WhenAScanIsRequiredOutsideAllVolumes_ShowsWithoutNamingTours`。
- 注释 `体积外扫码一律不生效（D36），` 改成 `体积外扫码一律不生效（ite-scan-region-gate D3），`。

- [ ] **Step 6: 编译并跑测试，确认通过**

按「如何跑测试」先编译，再跑 `A=Uality.IteTour.Tests`。
预期：`{'Total': 276, 'Passed': 276, 'Failed': 0, ...}`。
确认残留：`grep -rn "D36" Packages/com.uality.ite-tour` 没有输出。

---

### Task 3: 回归、提交与真机验证清单

**Files:**
- 不改代码。

**Interfaces:**
- Consumes: Task 1、Task 2 的全部改动。
- Produces: 一次只包含本 plan 改动的提交，外加一份交给用户的真机验证清单。

- [ ] **Step 1: 跑两个程序集的回归**

按「如何跑测试」分别跑 `A=Uality.IteTour.Tests` 和 `A=MRBase.Ite.Host.Tests`。
预期：`Uality.IteTour.Tests` 276/276，`MRBase.Ite.Host.Tests` 30/30，都没有 FAIL 行。
有任何失败（包括不是本次改动引起的），都按测试名原样报告，不要跳过。

- [ ] **Step 2: 检查改动范围**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git status --short -- Packages/com.uality.ite-tour docs/superpowers
git diff --stat -- Packages/com.uality.ite-tour/Runtime/Core Packages/com.uality.ite-tour/Tests/Editor
```

预期改动的只有 `TourScanPolicy.cs`、`ScanPromptPolicy.cs`、`TourScanPolicyTests.cs`、`ScanPromptPolicyTests.cs`，外加两份新文件：spec 和本 plan。`RoundedBoxUI.shader` 会出现在第一条命令的结果里，**不要加进这次提交**。

- [ ] **Step 3: 提交（用户明确同意后才执行）**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git add Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs \
        Packages/com.uality.ite-tour/Runtime/Core/ScanPromptPolicy.cs \
        Packages/com.uality.ite-tour/Tests/Editor/TourScanPolicyTests.cs \
        Packages/com.uality.ite-tour/Tests/Editor/ScanPromptPolicyTests.cs \
        docs/superpowers/specs/2026-09-23-ite-scan-region-gate-design.md \
        docs/superpowers/plans/2026-09-23-ite-scan-region-gate.md
git commit -m "$(cat <<'EOF'
feat(ite): 定位后扫码须在该 tour 的触发体积内

强制扫码（冷启动 / 重新戴上 / 追踪原点重置）仍不看区域；定位过之后
相机在所有体积外时扫码一律忽略、扫码提示隐藏。源实现在体积外不设限，
此处按产品要求偏离，决策见 docs/superpowers/specs/2026-09-23-ite-scan-region-gate-design.md。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
git show --stat HEAD
```

预期：`git show --stat` 只列出上面 6 个文件。

- [ ] **Step 4: 把真机验证清单交给用户（不打包、不安装，除非用户要求）**

照 spec §7 列出，另加 Review Focus 最后一条：
1. 定位后走出所有区域扫码 → 没反应，也没有提示；走进某个 Tour 的区域扫它的码 → 生效。
2. 摘下后戴上，在区域外扫码 → 生效并重新锚定。
3. 站在编辑器里画的区域边缘试扫，记录能不能扫（区域实际只有一半大小，为 spec D4 的后续决定积累依据）。
4. 第一次锚定后立刻在另一个 Tour 的区域里扫它的码，看会不会被误拒（区域集合要等下一个物理步才更新）。
