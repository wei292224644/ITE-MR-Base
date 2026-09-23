# ITE 导览状态机 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 `TourDirector` 里互相牵制的 5 个状态标志收成一个显式状态 `GuideState {Suspended, AwaitingScan, Anchored}`，修掉「摘下头显期间区域仍能唤醒 Tour、戴上后不要求重扫」的 bug。

**Architecture:** 新增纯逻辑状态核心 `TourGuide`（不碰 GameObject），持有状态、原因、在播 tourId、所在区域集合和帧末快照；每个输入返回一个 `GuideEffect`，由 `TourDirector` 落到 `IteTourObject` 上。三个策略函数（`TourScanPolicy` / `ScanPromptPolicy` / `TourRegionPolicy`）改为接收状态枚举；区域重选和提示重算都改为在帧末比较快照。宿主的重定位黄条改为跟随状态显示。

**Tech Stack:** Unity 6000.4.4f1，C#，NUnit（Unity Test Framework，EditMode），通过 `unity` CLI 操作用户已打开的 Editor。

**Spec:** `docs/superpowers/specs/2026-09-23-ite-guide-state-machine-design.md`

## Global Constraints

- 决策编号在代码注释里写成 `ite-guide-state-machine Dn`；区域门禁相关的写 `ite-scan-region-gate Dn`。不要写 `design D36`，不要引用 `openspec/`（已删除，不要读，也不要恢复）。
- Unity 操作只能通过 `unity` CLI，连用户已打开的 Editor。不开 headless Unity，不开第二个 Unity 进程，不打包、不装机。
- 注释用中文，风格与周围代码一致。
- 不在范围内，不要改：碰撞体事件重复、稳定器计时、扫码后被区域切走（spec §9）；区域体积尺寸（`IteTourObject.CreateTourObject` 里的 `/ 2`）。
- 工作区的基线里已有一批未提交的诊断日志，分别在 `TourDirector.cs`、`IteHmdPanel.cs`、`IteHostBootstrap.cs`。`TourDirector.cs` 按 Task 1 整体替换；`IteHmdPanel.cs` 的横幅日志按 Task 2 改写；`IteHostBootstrap.cs` 的提示日志保留原样。
- 在 Task 3 Step 4 得到用户明确同意之前，不要 commit、stage、stash、reset，也不要切分支。

## 如何编译和跑测试（所有 Task 通用）

编译：

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

## Review Focus

- **PICO 的重定位回调不一定在主线程上**：它来自原生回调 `PXR_Loader.XrEventDataBufferFunction`。改完之后 `RequireScan` 会停用 Tour，这要调 Unity API，只能在主线程执行。预期行为：回调里只记下请求，下一帧的 `Update` 里再处理。已放进 Task 2 Step 3。这一条 EditMode 测不了，列入真机验证。
- **摘下和扫码落在同一帧**：`IteHostBootstrap.Update` 先轮询佩戴状态，再推进扫码链路，所以摘下先生效，扫码被忽略。核心层由 `TourGuideTests.WhileSuspended_ScanIsIgnored` 覆盖；宿主侧的调用顺序这次不改。
- **戴上后 PICO 紧接着触发一次重定位**：进入等待扫码的原因会从 `HeadsetMounted` 变成 `Recentered`，黄条会出现。这可以接受，状态仍然正确，列入真机观察。
- **核心层要激活的 Tour 在效果层找不到**（`Find` 返回 null）：两边的在播状态会不一致。预期行为：打 `LogError`，不要静默吞掉，见 Task 1 Step 7 的 `Apply`。这需要真实的 `IteTourObject`，EditMode 测不了。
- **加载完成前提示就是「扫任意码」**：HMD 面板在初始化完成前只显示加载进度，看不到这条提示；编辑器 HUD 会显示。可以接受，不另加处理。

---

### Task 1: 包内状态机（状态词汇、策略函数、`TourGuide` 核心、`TourDirector` 效果层）

**Files:**
- Create: `Packages/com.uality.ite-tour/Runtime/Core/GuideState.cs`
- Create: `Packages/com.uality.ite-tour/Runtime/Core/TourGuide.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/TourIdLists.cs`（新增 `SameSet`）
- Replace: `Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs`
- Replace: `Packages/com.uality.ite-tour/Runtime/Core/ScanPromptPolicy.cs`
- Replace: `Packages/com.uality.ite-tour/Runtime/Core/TourRegionPolicy.cs`
- Replace: `Packages/com.uality.ite-tour/Runtime/Core/TourDirector.cs`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/IteRuntimeDriver.cs:16`
- Modify: `Packages/com.uality.ite-tour/Runtime/Core/IteRuntime.cs`（构造函数的事件转发、LoadAsync 末尾、`ActivateTour` / `RequireScan` / `SetHeadsetMounted`，以及新增的事件和属性）
- Create: `Packages/com.uality.ite-tour/Tests/Editor/TourGuideTests.cs`
- Replace: `Packages/com.uality.ite-tour/Tests/Editor/TourRegionPolicyTests.cs`
- Modify: `Packages/com.uality.ite-tour/Tests/Editor/TourIdListsTests.cs`、`TourScanPolicyTests.cs`、`ScanPromptPolicyTests.cs`

**Interfaces:**
- Produces（Task 2 要用）：
  - `enum GuideState { Suspended, AwaitingScan, Anchored }`
  - `enum GuideStateReason { ColdStart, HeadsetRemoved, HeadsetMounted, Recentered, HostRequested, Scanned }`
  - `IteRuntime.GuideState`（属性）、`IteRuntime.GuideStateReason`（属性）
  - `event Action<GuideState, GuideStateReason> IteRuntime.OnGuideStateChanged`
  - `IteRuntime.RequireScan(GuideStateReason reason = GuideStateReason.HostRequested)`

一个 Unity 程序集里只要有一处编译错误，整个程序集的测试都跑不起来。所以本 Task 的「红」就是 Step 2 的编译错误，而且错误必须**只**是缺少新 API；Step 8 编译通过、测试全绿才算「绿」。

- [ ] **Step 1: 先改测试**

(a) 新建 `Packages/com.uality.ite-tour/Tests/Editor/TourGuideTests.cs`：

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 导览状态核心（ite-guide-state-machine）。spec §3.2 的转换表逐条钉住，外加 PICO 实测
    /// 暴露的 bug：摘下头显期间区域仍能唤醒 Tour。
    /// </summary>
    public class TourGuideTests
    {
        private const IteSpaceScene.Tour.DisplayType Normal = IteSpaceScene.Tour.DisplayType.normal;
        private const IteSpaceScene.Tour.DisplayType Regional = IteSpaceScene.Tour.DisplayType.regionalTrigger;

        private static readonly Pose MarkerPose = new Pose(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f));

        private static TourDescriptor Tour(
            string id, IteSpaceScene.Tour.DisplayType type = Regional, bool secondAnchorAvailable = false)
            => new TourDescriptor { TourId = id, DisplayType = type, SecondAnchorAvailable = secondAnchorAvailable };

        private static List<TourDescriptor> Tours(params TourDescriptor[] tours) => new List<TourDescriptor>(tours);

        /// <summary>确定性挑选：总是第一个候选，让重选结果可断言。</summary>
        private static TourGuide NewGuide() => new TourGuide(ids => ids[0]);

        private static GuideEffect Frame(TourGuide guide, List<TourDescriptor> tours)
            => guide.EndOfFrame(() => tours, out _);

        /// <summary>相机在 tourId 的体积里扫它的码：走到 Anchored，tourId 在播。</summary>
        private static TourGuide AnchoredOn(string tourId, List<TourDescriptor> tours)
        {
            var guide = NewGuide();
            guide.SubmitVolumeTransition(tourId, VolumeTransition.Enter);
            Frame(guide, tours);
            guide.SubmitScan(tourId, MarkerPose, tours, out _);
            Frame(guide, tours);
            return guide;
        }

        // ---- 构造与扫码 ----

        [Test]
        public void Constructed_IsAwaitingScanForColdStart()
        {
            var guide = NewGuide();

            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.ColdStart));
            Assert.That(guide.ActiveTourId, Is.Null);
        }

        [Test]
        public void ScanWhileAwaiting_ActivatesWithPose_AndEntersAnchored()
        {
            var guide = NewGuide();

            var effect = guide.SubmitScan("t1", MarkerPose, Tours(Tour("t1")), out var decision);

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Activate));
            Assert.That(effect.ActivateTourId, Is.EqualTo("t1"));
            Assert.That(effect.AnchorPose.HasValue, Is.True);
            Assert.That(effect.AnchorPose.Value, Is.EqualTo(MarkerPose));
            Assert.That(effect.Deactivate, Is.False);
            Assert.That(guide.State, Is.EqualTo(GuideState.Anchored));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.Scanned));
            Assert.That(guide.ActiveTourId, Is.EqualTo("t1"));
        }

        [Test]
        public void Anchored_RescanActiveRegional_ReanchorsWithoutRebuild()
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
        }

        // ---- 摘下 / 戴上 ----

        [Test]
        public void HeadsetRemoved_FromAnchored_StopsTour_AndSuspends()
        {
            var guide = AnchoredOn("t1", Tours(Tour("t1")));

            var effect = guide.SetHeadsetMounted(false);

            Assert.That(effect.Deactivate, Is.True);
            Assert.That(guide.State, Is.EqualTo(GuideState.Suspended));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.HeadsetRemoved));
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
            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.HeadsetMounted));
        }

        [Test]
        public void HeadsetMounted_WhenNotSuspended_ChangesNothing()
        {
            var guide = NewGuide();
            int raised = 0;
            guide.StateChanged += (state, reason) => raised++;

            guide.SetHeadsetMounted(true);

            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.ColdStart));
            Assert.That(raised, Is.EqualTo(0));
        }

        /// <summary>
        /// PICO 实测（2026-09-23）：摘下期间应用照常运行、头显仍在追踪，区域进出事件不停。
        /// 原实现摘下只置 _paused，区域策略不看它，于是摘下期间区域唤醒了 Tour。
        /// </summary>
        [Test]
        public void WhileSuspended_EnteringRegion_NeverActivates()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);
            guide.SetHeadsetMounted(false);

            guide.SubmitVolumeTransition("t1", VolumeTransition.Exit);
            guide.SubmitVolumeTransition("t2", VolumeTransition.Enter);
            var effect = Frame(guide, tours);

            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(effect.Deactivate, Is.False);
            Assert.That(guide.ActiveTourId, Is.Null);
            Assert.That(guide.PendingTourIds, Is.EquivalentTo(new[] { "t2" }), "所在区域集合照常更新（I3）");
        }

        [Test]
        public void AfterRemount_EnteringRegion_NeverActivates()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);
            guide.SetHeadsetMounted(false);
            guide.SetHeadsetMounted(true);

            guide.SubmitVolumeTransition("t2", VolumeTransition.Enter);
            var effect = Frame(guide, tours);

            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.Null);
            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
        }

        [Test]
        public void WhileSuspended_ScanIsIgnored()
        {
            var guide = NewGuide();
            guide.SetHeadsetMounted(false);

            var effect = guide.SubmitScan("t1", MarkerPose, Tours(Tour("t1")), out var decision);

            Assert.That(decision.Action, Is.EqualTo(ScanAction.Ignore));
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.State, Is.EqualTo(GuideState.Suspended));
        }

        // ---- 运行中要求重扫 ----

        [Test]
        public void RequireScan_FromAnchored_StopsTour_AndAwaitsScan()
        {
            var guide = AnchoredOn("t1", Tours(Tour("t1")));

            var effect = guide.RequireScan(GuideStateReason.Recentered);

            Assert.That(effect.Deactivate, Is.True, "锚定作废后不能继续显示错位的内容（D2）");
            Assert.That(guide.State, Is.EqualTo(GuideState.AwaitingScan));
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.Recentered));
            Assert.That(guide.ActiveTourId, Is.Null);
        }

        [Test]
        public void RequireScan_WhileSuspended_StaysSuspended()
        {
            var guide = NewGuide();
            guide.SetHeadsetMounted(false);

            var effect = guide.RequireScan(GuideStateReason.Recentered);

            Assert.That(effect.Deactivate, Is.False);
            Assert.That(guide.State, Is.EqualTo(GuideState.Suspended), "摘下期间不能开始认扫码（D3）");
            Assert.That(guide.Reason, Is.EqualTo(GuideStateReason.HeadsetRemoved));
        }

        [Test]
        public void RequireScan_WhileAwaiting_UpdatesReasonOnce()
        {
            var guide = NewGuide();
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
            var guide = NewGuide();

            Assert.That(guide.TryActivateById("t1", out var effect), Is.False, "等待扫码时冒出在播 Tour 会违反 I1（D4）");
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.Null);
        }

        [Test]
        public void TryActivateById_WhenAnchored_SwitchesWithoutAnchoring()
        {
            var guide = AnchoredOn("t1", Tours(Tour("t1"), Tour("t2")));

            Assert.That(guide.TryActivateById("t2", out var effect), Is.True);
            Assert.That(effect.Deactivate, Is.True);
            Assert.That(effect.ActivateTourId, Is.EqualTo("t2"));
            Assert.That(effect.AnchorPose.HasValue, Is.False, "沿用现有锚定");
            Assert.That(guide.ActiveTourId, Is.EqualTo("t2"));
        }

        // ---- 帧末区域重选（D5）----

        [Test]
        public void Anchored_LeavingActiveRegion_SwitchesToRegionalInRange()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);
            guide.SubmitVolumeTransition("t2", VolumeTransition.Enter);
            Assert.That(Frame(guide, tours).ActivateTourId, Is.Null, "t1 仍在范围内，不换");

            guide.SubmitVolumeTransition("t1", VolumeTransition.Exit);
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.True);
            Assert.That(effect.ActivateTourId, Is.EqualTo("t2"));
            Assert.That(effect.AnchorPose.HasValue, Is.False, "区域唤醒沿用现有锚定");
            Assert.That(guide.ActiveTourId, Is.EqualTo("t2"));
        }

        [Test]
        public void Anchored_ExitAndReenterSameFrame_KeepsTour()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = AnchoredOn("t1", tours);
            guide.SubmitVolumeTransition("t2", VolumeTransition.Enter);
            Frame(guide, tours);

            guide.SubmitVolumeTransition("t1", VolumeTransition.Exit);
            guide.SubmitVolumeTransition("t1", VolumeTransition.Enter);
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.False, "集合净变化为零，不该切走 t1");
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.EqualTo("t1"));
        }

        [Test]
        public void Anchored_NothingPlayingAndNoRegionalInRange_NoEffect()
        {
            var tours = Tours(Tour("n1", Normal), Tour("n2", Normal));
            var guide = AnchoredOn("n1", tours);
            guide.SubmitVolumeTransition("n2", VolumeTransition.Enter);
            Frame(guide, tours);
            guide.SubmitVolumeTransition("n1", VolumeTransition.Exit);
            Assert.That(Frame(guide, tours).Deactivate, Is.True, "离开在播 normal 的区域：停用，范围内没有 regionalTrigger 可换");

            guide.SubmitVolumeTransition("n2", VolumeTransition.Exit);
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.False);
            Assert.That(effect.ActivateTourId, Is.Null);
        }

        /// <summary>
        /// 刚锚定完的那一帧，集合还是锚定前体积位置下的值（要等物理步产生进出事件），
        /// 不能据此切走刚扫的 Tour。
        /// </summary>
        [Test]
        public void JustAnchored_UnchangedSet_DoesNotReselect()
        {
            var tours = Tours(Tour("t1"), Tour("t2"));
            var guide = NewGuide();
            guide.SubmitVolumeTransition("t2", VolumeTransition.Enter);
            Frame(guide, tours);

            guide.SubmitScan("t1", MarkerPose, tours, out _); // 等待扫码不看区域：t1 不在集合里也激活
            var effect = Frame(guide, tours);

            Assert.That(effect.Deactivate, Is.False);
            Assert.That(effect.ActivateTourId, Is.Null);
            Assert.That(guide.ActiveTourId, Is.EqualTo("t1"));
        }

        // ---- 帧末快照（D6）与状态广播（D8）----

        [Test]
        public void EndOfFrame_ReportsChangeOnlyWhenSnapshotChanges()
        {
            var tours = Tours(Tour("t1"));
            var guide = NewGuide();

            guide.EndOfFrame(() => tours, out bool first);
            guide.EndOfFrame(() => tours, out bool idle);
            guide.SubmitVolumeTransition("t1", VolumeTransition.Enter);
            guide.EndOfFrame(() => tours, out bool afterEnter);
            guide.SetHeadsetMounted(false);
            guide.EndOfFrame(() => tours, out bool afterRemove);

            Assert.That(first, Is.True, "第一帧必须算一次提示");
            Assert.That(idle, Is.False);
            Assert.That(afterEnter, Is.True);
            Assert.That(afterRemove, Is.True);
        }

        [Test]
        public void StateChanged_CarriesReason()
        {
            var tours = Tours(Tour("t1"));
            var guide = NewGuide();
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

(b) 用下面的内容**整体替换** `Packages/com.uality.ite-tour/Tests/Editor/TourRegionPolicyTests.cs`：

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Uality.IteTour.Core;
using Uality.IteTour.Data;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 相机进出触发体积之后要不要换 Tour（ite-guide-state-machine D5）。集合增删（Apply）与
    /// 帧末重选判断（Decide）分开：进出事件只改集合，帧末按集合相对上一帧末的净变化判一次，
    /// 且只在 Anchored 下判。挑选（随机）归调用方。
    /// </summary>
    public class TourRegionPolicyTests
    {
        private const IteSpaceScene.Tour.DisplayType Normal = IteSpaceScene.Tour.DisplayType.normal;
        private const IteSpaceScene.Tour.DisplayType Regional = IteSpaceScene.Tour.DisplayType.regionalTrigger;
        private const IteSpaceScene.Tour.DisplayType Always = IteSpaceScene.Tour.DisplayType.alwaysDisplayed;

        private static TourDescriptor Tour(string id, IteSpaceScene.Tour.DisplayType type)
            => new TourDescriptor { TourId = id, DisplayType = type };

        private static List<TourDescriptor> Tours(params TourDescriptor[] tours) => new List<TourDescriptor>(tours);

        private static List<string> Pending(params string[] ids) => new List<string>(ids);

        private static ScanState Anchored(string activeTourId, params string[] pending)
            => new ScanState { State = GuideState.Anchored, ActiveTourId = activeTourId, PendingTourIds = Pending(pending) };

        // ---- 集合增删 ----

        [Test]
        public void Apply_OnEnter_AddsTour()
        {
            Assert.That(TourRegionPolicy.Apply(Pending(), "t1", VolumeTransition.Enter), Is.EquivalentTo(new[] { "t1" }));
        }

        [Test]
        public void Apply_OnExit_RemovesTour()
        {
            Assert.That(
                TourRegionPolicy.Apply(Pending("t1", "t2"), "t1", VolumeTransition.Exit),
                Is.EquivalentTo(new[] { "t2" }));
        }

        [Test]
        public void Apply_OnRepeatedEnter_DoesNotDuplicate()
        {
            Assert.That(TourRegionPolicy.Apply(Pending("t1"), "t1", VolumeTransition.Enter), Is.EquivalentTo(new[] { "t1" }));
        }

        [Test]
        public void Apply_DoesNotMutateTheIncomingSet()
        {
            var original = Pending("t1");

            TourRegionPolicy.Apply(original, "t2", VolumeTransition.Enter);

            Assert.That(original, Is.EquivalentTo(new[] { "t1" }), "纯函数不得改动传入集合");
        }

        [Test]
        public void Apply_NullSet_IsTreatedAsEmpty()
        {
            Assert.That(TourRegionPolicy.Apply(null, "t1", VolumeTransition.Enter), Is.EquivalentTo(new[] { "t1" }));
        }

        // ---- 帧末重选 ----

        [TestCase(GuideState.Suspended)]
        [TestCase(GuideState.AwaitingScan)]
        public void Decide_WhenNotAnchored_NeverReselects(GuideState notAnchored)
        {
            var state = new ScanState { State = notAnchored, PendingTourIds = Pending("t1") };

            var decision = TourRegionPolicy.Decide(state, Tours(Tour("t1", Regional)), Pending());

            Assert.That(decision.ShouldReselect, Is.False, "只有已定位才允许区域唤醒 Tour（I2）");
        }

        [Test]
        public void Decide_WhenSetUnchanged_DoesNotReselect()
        {
            var decision = TourRegionPolicy.Decide(
                Anchored(null, "t1", "t2"), Tours(Tour("t1", Regional), Tour("t2", Regional)), Pending("t2", "t1"));

            Assert.That(decision.ShouldReselect, Is.False, "按集合比较，顺序不同不算变化");
        }

        [Test]
        public void Decide_WhenActiveTourStillInRange_DoesNotReselect()
        {
            var decision = TourRegionPolicy.Decide(
                Anchored("t1", "t1", "t2"), Tours(Tour("t1", Regional), Tour("t2", Regional)), Pending("t1"));

            Assert.That(decision.ShouldReselect, Is.False, "当前 Tour 仍在范围内就不该被打断");
        }

        [Test]
        public void Decide_WhenActiveTourLeftRange_ReselectsAmongRegionalInRange()
        {
            var tours = Tours(
                Tour("r1", Regional),
                Tour("r2", Regional),   // 在播，刚离开
                Tour("n1", Normal),     // 类型不符
                Tour("a1", Always));    // 类型不符

            var decision = TourRegionPolicy.Decide(
                Anchored("r2", "r1", "n1", "a1"), tours, Pending("r1", "r2", "n1", "a1"));

            Assert.That(decision.ShouldReselect, Is.True);
            Assert.That(decision.ReselectCandidates, Is.EquivalentTo(new[] { "r1" }), "只有集合内的 regionalTrigger 才是候选");
        }

        [Test]
        public void Decide_WhenActiveTourLeftRange_AndNoRegionalInRange_ReselectsWithEmptyCandidates()
        {
            // 源实现的语义：当前 Tour 照样停用，只是没有新的可激活
            var decision = TourRegionPolicy.Decide(
                Anchored("t1", "n1"), Tours(Tour("t1", Regional), Tour("n1", Normal)), Pending("t1", "n1"));

            Assert.That(decision.ShouldReselect, Is.True);
            Assert.That(decision.ReselectCandidates, Is.Empty);
        }

        [Test]
        public void Decide_WhenNothingPlaying_AndNoRegionalInRange_DoesNothing()
        {
            var decision = TourRegionPolicy.Decide(Anchored(null, "n1"), Tours(Tour("n1", Normal)), Pending());

            Assert.That(decision.ShouldReselect, Is.False);
        }

        [Test]
        public void Decide_WhenNothingPlaying_AndRegionalInRange_Reselects()
        {
            var decision = TourRegionPolicy.Decide(Anchored(null, "r1"), Tours(Tour("r1", Regional)), Pending());

            Assert.That(decision.ShouldReselect, Is.True);
            Assert.That(decision.ReselectCandidates, Is.EquivalentTo(new[] { "r1" }));
        }

        /// <summary>
        /// 策略只给候选集，不替调用方挑。源实现用 OrderBy(Guid.NewGuid()) 随机取一个，
        /// 把随机性埋在 LINQ 链里——决策因此不可测，「为什么是随机」也无处说明。
        /// </summary>
        [Test]
        public void Decide_IsDeterministic_CandidateOrderFollowsTourOrder()
        {
            var tours = Tours(
                Tour("r0", Regional), Tour("r1", Regional), Tour("r2", Regional), Tour("r3", Regional));
            var state = Anchored("r0", "r3", "r1", "r2");
            var previous = Pending("r0", "r1", "r2", "r3");

            var first = TourRegionPolicy.Decide(state, tours, previous);
            var second = TourRegionPolicy.Decide(state, tours, previous);

            Assert.That(first.ReselectCandidates, Is.EqualTo(new[] { "r1", "r2", "r3" }));
            Assert.That(second.ReselectCandidates, Is.EqualTo(first.ReselectCandidates),
                "相同输入必须给出相同候选集；随机挑选是调用方的显式选择");
        }
    }
}
```

(c) `Packages/com.uality.ite-tour/Tests/Editor/TourIdListsTests.cs`：在 `Contains_NullList_IsFalse` 之后追加：

```csharp
        [Test]
        public void SameSet_IgnoresOrder()
        {
            Assert.That(TourIdLists.SameSet(new[] { "a", "b" }, new[] { "b", "a" }), Is.True);
        }

        [Test]
        public void SameSet_DifferentMembers_IsFalse()
        {
            Assert.That(TourIdLists.SameSet(new[] { "a" }, new[] { "a", "b" }), Is.False);
            Assert.That(TourIdLists.SameSet(new[] { "a", "c" }, new[] { "a", "b" }), Is.False);
        }

        [Test]
        public void SameSet_NullEqualsEmpty()
        {
            Assert.That(TourIdLists.SameSet(null, new string[0]), Is.True);
            Assert.That(TourIdLists.SameSet(null, new[] { "a" }), Is.False);
        }
```

(d) `Packages/com.uality.ite-tour/Tests/Editor/TourScanPolicyTests.cs`，逐条修改（行号以当前文件为准）：

1. 第 38–40 行：方法名 `Decide_WhenPaused_Ignores` 改为 `Decide_WhenSuspended_Ignores`；`new ScanState { Paused = true, ForcedScanPending = true }` 改为 `new ScanState { State = GuideState.Suspended }`。
2. 第 51、61、75、110 行：`new ScanState { ForcedScanPending = true }` 改为 `new ScanState { State = GuideState.AwaitingScan }`。
3. 第 68 行：`// ---- 强制扫码 ----` 改为 `// ---- 等待扫码定位 ----`。
4. 第 73 行：方法名 `Decide_ForcedScan_ActivatesAnyDisplayTypeAndClearsTheFlag` 改为 `Decide_AwaitingScan_ActivatesAnyDisplayType`；删掉第 81 行的 `Assert.That(decision.ClearsForcedScan, Is.True);`。
5. 第 85–91 行：方法名 `Decide_ForcedScan_IgnoresPendingTourFilter` 改为 `Decide_AwaitingScan_IgnoresPendingTourFilter`；注释 `// 强制扫码先于区域过滤：刚戴上头显时相机可能不在任何触发体积内` 改为 `// 等待扫码先于区域过滤：刚戴上头显时相机可能不在任何触发体积内`；初始化器里的 `ForcedScanPending = true,` 改为 `State = GuideState.AwaitingScan,`。
6. 第 99–108 行：注释里的「强制扫码激活 regionalTrigger 后」改为「等待扫码时激活 regionalTrigger 后」；方法名 `Decide_ForcedScanOnRegionalTrigger_DoesNotConsumeSecondAnchor_D14` 改为 `Decide_AwaitingScanOnRegionalTrigger_DoesNotConsumeSecondAnchor_D14`。
7. 第 119–126 行：注释 `ite-scan-region-gate D2：冷启动、重新戴上、追踪原点重置三者都落到强制扫码，此刻体积位置还没（重新）` 改为 `ite-scan-region-gate D2：冷启动、重新戴上、追踪原点重置都进入等待扫码定位，此刻体积位置还没（重新）`；方法名 `Decide_ForcedScan_ActivatesOutsideAllVolumes` 改为 `Decide_AwaitingScan_ActivatesOutsideAllVolumes`；`ForcedScanPending = true, PendingTourIds = InVolumes()` 改为 `State = GuideState.AwaitingScan, PendingTourIds = InVolumes()`。
8. **删除**测试 `Decide_ForcedScanWhileTourActive_ReanchorsItOutsideAllVolumes`，连同它上方的 `/// <summary>…</summary>` 注释（约第 178–202 行）。理由：I1 之后「等待扫码时有 Tour 在播」不可能出现；对应场景由 `TourGuideTests.RequireScan_FromAnchored_StopsTour_AndAwaitsScan` 覆盖。
9. 剩下的每个 `new ScanState {`（第 138、148、159、170、209、220、232、245、259、272、290 行）：在大括号里加上 `State = GuideState.Anchored,` 作为第一个成员。例如 `new ScanState { PendingTourIds = InVolumes("t2") }` 改为 `new ScanState { State = GuideState.Anchored, PendingTourIds = InVolumes("t2") }`。
10. 第 270 行：方法名 `Decide_AlwaysDisplayedTour_IgnoredOutsideForcedScan` 改为 `Decide_AlwaysDisplayedTour_IgnoredOnceAnchored`。

改完后执行 `grep -n "ForcedScan\|Paused\|ClearsForcedScan" Packages/com.uality.ite-tour/Tests/Editor/TourScanPolicyTests.cs`，应当没有输出。

(e) `Packages/com.uality.ite-tour/Tests/Editor/ScanPromptPolicyTests.cs`：

1. 第 36 行 `ForcedScanPending = true,` 改为 `State = GuideState.AwaitingScan,`。
2. 第 49、85 行：`ForcedScanPending = true` 改为 `State = GuideState.AwaitingScan`。
3. 第 64、74、99、111、124 行：在 `new ScanState {` 后加 `State = GuideState.Anchored,` 作为第一个成员。
4. 在 `Decide_WhenAScanIsRequiredOutsideAllVolumes_ShowsWithoutNamingTours` 之后插入：

```csharp
        /// <summary>头显摘下：没人在看，一律不提示——即使区域里有只能扫码进入的 normal Tour。</summary>
        [Test]
        public void Decide_WhenSuspended_Hides()
        {
            var state = new ScanState { State = GuideState.Suspended, PendingTourIds = Pending("n1") };

            var prompt = ScanPromptPolicy.Decide(state, Tours(Tour("n1", Normal)));

            Assert.That(prompt.State, Is.EqualTo(ScanPromptState.Hidden));
        }
```

改完后执行 `grep -n "ForcedScanPending" Packages/com.uality.ite-tour/Tests/Editor/ScanPromptPolicyTests.cs`，应当没有输出。

- [ ] **Step 2: 编译，确认「红」**

按「如何编译」执行。预期：编译失败，错误只涉及尚不存在的 `GuideState`、`GuideStateReason`、`TourGuide`、`GuideEffect`、`TourIdLists.SameSet`、`TourRegionPolicy.Apply`、`ScanState.State`，以及 `TourRegionPolicy.Decide` 的新签名。如果出现其他错误（拼写、语法等），先修测试，再重新确认。

- [ ] **Step 3: 新建 `Packages/com.uality.ite-tour/Runtime/Core/GuideState.cs`**

```csharp
namespace Uality.IteTour.Core
{
    /// <summary>
    /// 导览所处的状态（ite-guide-state-machine D1）。取代原来的 <c>_paused</c> 与
    /// <c>_forcedScanPending</c> 两个 bool：两者本是一个状态的两面，拆开之后各策略
    /// 各看一半——摘下头显期间区域仍能唤醒 Tour 就是这么漏出来的。
    ///
    /// 顺序刻意让 <c>default</c> 落在 <see cref="Suspended"/>：未初始化的状态什么都不做。
    /// </summary>
    public enum GuideState
    {
        /// <summary>头显摘下：不认扫码，区域不唤醒 Tour，无 Tour 在播。</summary>
        Suspended,

        /// <summary>
        /// 等待扫码定位，与冷启动相同：扫任一已装配 Tour 的码即激活并锚定（不看区域），
        /// 区域不唤醒 Tour，无 Tour 在播。
        /// </summary>
        AwaitingScan,

        /// <summary>已定位：区域门禁生效，区域可唤醒 regionalTrigger。</summary>
        Anchored,
    }

    /// <summary>进入当前状态的原因。宿主据此决定提示文案，例如重定位黄条（D8）。</summary>
    public enum GuideStateReason
    {
        ColdStart,
        HeadsetRemoved,
        HeadsetMounted,
        Recentered,
        HostRequested,
        Scanned,
    }
}
```

- [ ] **Step 4: `TourIdLists` 新增 `SameSet`**

在 `Packages/com.uality.ite-tour/Runtime/Core/TourIdLists.cs` 的 `Contains` 方法之后追加：

```csharp
        /// <summary>
        /// 两个集合的成员是否相同，不看顺序；null 视为空集。集合里的元素本就不重复
        /// （<see cref="TourRegionPolicy.Apply"/> 保证），所以比较数量与逐个包含就够了。
        /// </summary>
        public static bool SameSet(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            int countA = a?.Count ?? 0;
            int countB = b?.Count ?? 0;
            if (countA != countB)
            {
                return false;
            }

            for (int i = 0; i < countA; i++)
            {
                if (!Contains(b, a[i]))
                {
                    return false;
                }
            }

            return true;
        }
```

- [ ] **Step 5: 整体替换 `Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs`**

```csharp
using System.Collections.Generic;
using Uality.IteTour.Data;

namespace Uality.IteTour.Core
{
    /// <summary>一次标记扫描应当产生的动作。</summary>
    public enum ScanAction
    {
        /// <summary>什么都不做。</summary>
        Ignore,

        /// <summary>停用当前 Tour，激活目标 Tour，并按扫到的位姿锚定。</summary>
        Activate,

        /// <summary>保持当前 Tour，仅按新位姿重新锚定（不销毁重建内容）。</summary>
        Reanchor,
    }

    /// <summary>决策所需的 Tour 事实。刻意只带事实，不带 GameObject。</summary>
    public struct TourDescriptor
    {
        public string TourId;
        public IteSpaceScene.Tour.DisplayType DisplayType;

        /// <summary>该 Tour 当前是否还允许一次二次锚定。</summary>
        public bool SecondAnchorAvailable;
    }

    /// <summary>决策所需的导览状态快照。</summary>
    public struct ScanState
    {
        /// <summary>导览所处的状态（ite-guide-state-machine D1）。</summary>
        public GuideState State;

        public string ActiveTourId;

        /// <summary>
        /// 相机当前所在触发体积对应的 Tour 集合。<see cref="GuideState.Anchored"/> 下扫码只认这里面的码；
        /// 为空表示相机在所有体积外，此时扫码不生效（ite-scan-region-gate D1）。
        /// </summary>
        public IReadOnlyList<string> PendingTourIds;
    }

    public struct ScanDecision
    {
        public ScanAction Action;
        public string TourId;

        /// <summary>本次是否消耗目标 Tour 的二次锚定许可。</summary>
        public bool ConsumesSecondAnchor;

        public static ScanDecision Ignore => new ScanDecision { Action = ScanAction.Ignore };
    }

    /// <summary>
    /// 标记扫描的**纯决策**。无副作用、不碰 GameObject、不依赖帧或 async 时序。
    ///
    /// 源实现把这段逻辑摊在三个订阅同一事件的处理器里，其中第一个会清掉
    /// 「必须扫码」标志，导致第二个在同一次扫码中也会执行；而第二个是否生效
    /// 又取决于 <c>IteTourObject.Enable()</c> 里 <c>_canAnchor = true</c> 有没有
    /// 在 <c>await CreateTourScene()</c> 之后跑到——一个内容为空的 Tour 会同步
    /// 走完，于是走出另一套语义。详见 design D14。
    ///
    /// 本实现采用**内容异步加载路径**下的行为作为规范语义（真机上的常规情况），
    /// 把原先的偶然行为固化成契约。状态转换（等待扫码 → 已定位）不在这里，归
    /// <see cref="TourGuide"/>。
    /// </summary>
    public static class TourScanPolicy
    {
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
                // 等待扫码定位：不看区域、不看展示类型，任何匹配的 Tour 都激活（与源实现的强制扫码
                // 一致；不看区域见 ite-scan-region-gate D2）。
                //
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

            // 定位过之后只认相机所在触发体积的码，体积外一律不认（ite-scan-region-gate D1）。
            // 源实现在体积外不设限；现在只有上面的等待扫码能无视区域（D2）——
            // 那时体积还没按真实位姿锚定，站在哪都不算数。
            if (!TourIdLists.Contains(state.PendingTourIds, markerId))
            {
                return ScanDecision.Ignore;
            }

            switch (tour.DisplayType)
            {
                case IteSpaceScene.Tour.DisplayType.normal:
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

                    // 源实现此处是 ChangeTour(tour) 之后紧跟 tour.SecondAnchored()，
                    // 但那次置位会被 Enable() 续体里的 _canAnchor = true 覆盖掉——
                    // 是死代码。按 D14 所选语义，激活不消耗二次锚定许可。
                    return new ScanDecision
                    {
                        Action = ScanAction.Activate,
                        TourId = tour.TourId,
                        ConsumesSecondAnchor = false,
                    };

                // alwaysDisplayed 始终显示，不参与扫码切换（源实现两个分支都不匹配）
                default:
                    return ScanDecision.Ignore;
            }
        }

        private static bool TryFind(IReadOnlyList<TourDescriptor> tours, string tourId, out TourDescriptor found)
        {
            for (int i = 0; i < tours.Count; i++)
            {
                if (tours[i].TourId == tourId)
                {
                    found = tours[i];
                    return true;
                }
            }

            found = default;
            return false;
        }
    }
}
```

- [ ] **Step 6: 整体替换 `ScanPromptPolicy.cs` 和 `TourRegionPolicy.cs`**

`Packages/com.uality.ite-tour/Runtime/Core/ScanPromptPolicy.cs`：

```csharp
using System;
using System.Collections.Generic;
using Uality.IteTour.Data;

namespace Uality.IteTour.Core
{
    public enum ScanPromptState
    {
        Hidden,
        Visible,
    }

    public struct ScanPrompt
    {
        public ScanPromptState State;

        /// <summary>
        /// 需要用户去扫的 Tour。为空表示「随便扫哪个都行」——只出现在等待扫码定位
        /// （<see cref="GuideState.AwaitingScan"/>）时。
        /// </summary>
        public IReadOnlyList<string> TourIds;

        public static ScanPrompt Hidden => new ScanPrompt
        {
            State = ScanPromptState.Hidden,
            TourIds = Array.Empty<string>(),
        };

        public static ScanPrompt Visible(IReadOnlyList<string> tourIds) => new ScanPrompt
        {
            State = ScanPromptState.Visible,
            TourIds = tourIds ?? Array.Empty<string>(),
        };
    }

    /// <summary>
    /// 「要不要提示用户去扫码、提示哪几个 Tour」的**纯决策**。
    ///
    /// 源实现是个每 0.75 秒轮询的协程，直接调 <c>ScanPreviewUI.Instance.Show()/Hide()</c>。
    /// 决策是 ITE 业务，渲染不是——这里只留决策，渲染由宿主订阅广播自行处理（design D5）。
    /// </summary>
    public static class ScanPromptPolicy
    {
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

            // 定位过之后体积外扫码不生效，不提示（ite-scan-region-gate D3）
            if (state.PendingTourIds == null || state.PendingTourIds.Count == 0)
            {
                return ScanPrompt.Hidden;
            }

            // 范围内有 regionalTrigger：它会自动激活，不必提示
            if (HasPending(tours, state.PendingTourIds, IteSpaceScene.Tour.DisplayType.regionalTrigger))
            {
                return ScanPrompt.Hidden;
            }

            var normalTourIds = PendingIds(tours, state.PendingTourIds, IteSpaceScene.Tour.DisplayType.normal);

            return normalTourIds.Count > 0
                ? ScanPrompt.Visible(normalTourIds)
                : ScanPrompt.Hidden;
        }

        private static bool HasPending(
            IReadOnlyList<TourDescriptor> tours,
            IReadOnlyList<string> pending,
            IteSpaceScene.Tour.DisplayType displayType)
            => PendingIds(tours, pending, displayType).Count > 0;

        private static List<string> PendingIds(
            IReadOnlyList<TourDescriptor> tours,
            IReadOnlyList<string> pending,
            IteSpaceScene.Tour.DisplayType displayType)
        {
            var ids = new List<string>();
            if (tours == null)
            {
                return ids;
            }

            for (int i = 0; i < tours.Count; i++)
            {
                if (tours[i].DisplayType == displayType && TourIdLists.Contains(pending, tours[i].TourId))
                {
                    ids.Add(tours[i].TourId);
                }
            }

            return ids;
        }
    }
}
```

`Packages/com.uality.ite-tour/Runtime/Core/TourRegionPolicy.cs`：

```csharp
using System;
using System.Collections.Generic;
using Uality.IteTour.Data;

namespace Uality.IteTour.Core
{
    /// <summary>相机与 Tour 触发体积的进出。</summary>
    public enum VolumeTransition
    {
        Enter,
        Exit,
    }

    public struct RegionDecision
    {
        /// <summary>是否需要停用当前 Tour 并重新选一个。</summary>
        public bool ShouldReselect;

        /// <summary>
        /// 可供重选的 Tour（所在区域集合中的 <c>regionalTrigger</c>）。
        /// <see cref="ShouldReselect"/> 为 false 时为空。
        ///
        /// 刻意**只返回候选集，不替调用方挑**：源实现用
        /// <c>OrderBy(t =&gt; Guid.NewGuid())</c> 随机取一个，把随机性埋在 LINQ 链里，
        /// 既让决策不可测，也让「为什么是随机」无处说明。挑选交给调用方之后，
        /// 随机是一个显式的、可替换的选择。
        /// </summary>
        public IReadOnlyList<string> ReselectCandidates;

        public static RegionDecision None => new RegionDecision
        {
            ShouldReselect = false,
            ReselectCandidates = Array.Empty<string>(),
        };
    }

    /// <summary>
    /// 相机进出 Tour 触发体积的**纯决策**，分两半（ite-guide-state-machine D5）：
    /// <see cref="Apply"/> 把一次进出应用到「所在区域」集合上；<see cref="Decide"/> 在帧末按集合
    /// 相对上一帧末的净变化判一次要不要换 Tour。进出事件本身不再触发重选——同一帧里离开又
    /// 进入同一区域，净变化为零，不该切走在播的 Tour。
    /// </summary>
    public static class TourRegionPolicy
    {
        /// <summary>把一次进出应用到集合上，返回新集合。不改动传入集合；null 视为空集。</summary>
        public static IReadOnlyList<string> Apply(IReadOnlyList<string> current, string tourId, VolumeTransition transition)
        {
            var pending = current == null ? new List<string>() : new List<string>(current);

            if (transition == VolumeTransition.Enter)
            {
                if (!string.IsNullOrEmpty(tourId) && !pending.Contains(tourId))
                {
                    pending.Add(tourId);
                }
            }
            else
            {
                pending.Remove(tourId);
            }

            return pending;
        }

        /// <summary>
        /// 帧末结算：<paramref name="state"/> 的所在区域集合相对 <paramref name="previousTourIds"/>
        /// （上一帧末）有净变化时，要不要停掉在播的 Tour、从哪些 Tour 里重选。
        ///
        /// - 只在 <see cref="GuideState.Anchored"/> 下判（I2）：摘下与等待扫码时区域不能唤醒 Tour。
        /// - 集合没变不判：刚锚定完的那一帧，集合仍是锚定前体积位置下的值，据此判断会切走刚扫的 Tour。
        /// - 在播 Tour 仍在集合内就不动；否则候选是集合中的 regionalTrigger。在播为空且没有候选时什么都不做。
        /// </summary>
        public static RegionDecision Decide(
            ScanState state, IReadOnlyList<TourDescriptor> tours, IReadOnlyList<string> previousTourIds)
        {
            if (state.State != GuideState.Anchored)
            {
                return RegionDecision.None;
            }

            if (TourIdLists.SameSet(previousTourIds, state.PendingTourIds))
            {
                return RegionDecision.None;
            }

            if (TourIdLists.Contains(state.PendingTourIds, state.ActiveTourId))
            {
                return RegionDecision.None;
            }

            var candidates = RegionalTriggerTours(tours, state.PendingTourIds);
            if (string.IsNullOrEmpty(state.ActiveTourId) && candidates.Count == 0)
            {
                return RegionDecision.None;
            }

            return new RegionDecision { ShouldReselect = true, ReselectCandidates = candidates };
        }

        private static List<string> RegionalTriggerTours(
            IReadOnlyList<TourDescriptor> tours, IReadOnlyList<string> pending)
        {
            var candidates = new List<string>();
            if (tours == null)
            {
                return candidates;
            }

            for (int i = 0; i < tours.Count; i++)
            {
                if (tours[i].DisplayType == IteSpaceScene.Tour.DisplayType.regionalTrigger
                    && TourIdLists.Contains(pending, tours[i].TourId))
                {
                    candidates.Add(tours[i].TourId);
                }
            }

            return candidates;
        }
    }
}
```

- [ ] **Step 7: 新建 `TourGuide.cs`，整体替换 `TourDirector.cs`，改驱动与 `IteRuntime`**

(a) 新建 `Packages/com.uality.ite-tour/Runtime/Core/TourGuide.cs`：

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

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

        /// <summary>激活时的锚定位姿；null 表示沿用现有锚定（区域唤醒、直接激活）。</summary>
        public Pose? AnchorPose;

        /// <summary>只重新锚定、不重建内容的 Tour（regionalTrigger 二次锚定）；null 表示无。</summary>
        public string ReanchorTourId;

        public Pose ReanchorPose;

        /// <summary>重锚是否消耗该 Tour 的二次锚定许可。</summary>
        public bool ConsumesSecondAnchor;

        public static GuideEffect None => default;
    }

    /// <summary>
    /// 导览状态核心（ite-guide-state-machine D7）：状态、进入原因、在播 tourId、所在区域集合都在这里，
    /// 每个输入返回一个 <see cref="GuideEffect"/>，由 <see cref="TourDirector"/> 落到 IteTourObject 上。
    /// 不碰 GameObject，所以 spec §3.2 的每条转换都能在 EditMode 里测。
    ///
    /// 不变式：只有 <see cref="GuideState.Anchored"/> 下才可能有 Tour 在播（I1），也只有它允许区域
    /// 唤醒 Tour（I2）；所在区域集合在所有状态下都照常更新（I3）。
    /// </summary>
    public sealed class TourGuide
    {
        private readonly Func<IReadOnlyList<string>, string> _pick;

        // 集合只整体替换、从不原地修改，所以帧末快照可以直接持有同一个引用。
        private IReadOnlyList<string> _pendingTourIds = Array.Empty<string>();

        // 上一帧末的快照：区域重选（D5）与提示重算（D6）都以它为基准。
        private bool _hasFrame;
        private GuideState _frameState;
        private string _frameActiveTourId;
        private IReadOnlyList<string> _frameTourIds = Array.Empty<string>();

        /// <param name="pick">区域重选有多个候选时挑哪个。</param>
        public TourGuide(Func<IReadOnlyList<string>, string> pick)
        {
            _pick = pick ?? throw new ArgumentNullException(nameof(pick));
            State = GuideState.AwaitingScan;
            Reason = GuideStateReason.ColdStart;
        }

        public GuideState State { get; private set; }

        public GuideStateReason Reason { get; private set; }

        /// <summary>当前在播的 Tour；无则为 null。只有 Anchored 下可能非空（I1）。</summary>
        public string ActiveTourId { get; private set; }

        /// <summary>相机当前所在触发体积对应的 tourId 集合。</summary>
        public IReadOnlyList<string> PendingTourIds => _pendingTourIds;

        /// <summary>状态或进入原因变化时触发；两者都没变时不触发。</summary>
        public event Action<GuideState, GuideStateReason> StateChanged;

        public ScanState Snapshot() => new ScanState
        {
            State = State,
            ActiveTourId = ActiveTourId,
            PendingTourIds = _pendingTourIds,
        };

        /// <summary>摘下：停掉在播 Tour，进入 Suspended。戴上：只从 Suspended 进入 AwaitingScan，其他状态不变。</summary>
        public GuideEffect SetHeadsetMounted(bool mounted)
        {
            if (!mounted)
            {
                var effect = StopActive();
                Enter(GuideState.Suspended, GuideStateReason.HeadsetRemoved);
                return effect;
            }

            if (State == GuideState.Suspended)
            {
                Enter(GuideState.AwaitingScan, GuideStateReason.HeadsetMounted);
            }

            return GuideEffect.None;
        }

        /// <summary>
        /// 运行中直接进入等待扫码定位：停掉在播 Tour，与冷启动同一状态（D2）。
        /// Suspended 下不切换——摘下期间不能开始认扫码，戴上时本就会进入等待扫码（D3）。
        /// </summary>
        public GuideEffect RequireScan(GuideStateReason reason)
        {
            if (State == GuideState.Suspended)
            {
                return GuideEffect.None;
            }

            var effect = StopActive();
            Enter(GuideState.AwaitingScan, reason);
            return effect;
        }

        public GuideEffect SubmitScan(
            string markerId, Pose pose, IReadOnlyList<TourDescriptor> tours, out ScanDecision decision)
        {
            decision = TourScanPolicy.Decide(Snapshot(), tours, markerId);

            switch (decision.Action)
            {
                case ScanAction.Activate:
                    var effect = ActivateEffect(decision.TourId, pose);
                    if (State == GuideState.AwaitingScan)
                    {
                        Enter(GuideState.Anchored, GuideStateReason.Scanned);
                    }

                    return effect;

                case ScanAction.Reanchor:
                    return new GuideEffect
                    {
                        ReanchorTourId = decision.TourId,
                        ReanchorPose = pose,
                        ConsumesSecondAnchor = decision.ConsumesSecondAnchor,
                    };

                default:
                    return GuideEffect.None;
            }
        }

        /// <summary>相机进出某个 Tour 的触发体积：只更新集合（I3），要不要换 Tour 在帧末判（D5）。</summary>
        public void SubmitVolumeTransition(string tourId, VolumeTransition transition)
            => _pendingTourIds = TourRegionPolicy.Apply(_pendingTourIds, tourId, transition);

        /// <summary>不经扫码直接激活，沿用现有锚定。只在 Anchored 下可用（D4）。</summary>
        public bool TryActivateById(string tourId, out GuideEffect effect)
        {
            if (State != GuideState.Anchored)
            {
                effect = GuideEffect.None;
                return false;
            }

            effect = ActivateEffect(tourId, null);
            return true;
        }

        /// <summary>
        /// 帧末结算：集合有净变化且处于 Anchored 时做一次区域重选（D5）；<paramref name="changed"/>
        /// 报告（状态、在播、所在区域）相对上一帧末是否变化，供调用方决定要不要重算提示（D6）。
        /// </summary>
        /// <param name="tours">只在真要判区域时才取，避免每帧都分配 Tour 描述列表。</param>
        public GuideEffect EndOfFrame(Func<IReadOnlyList<TourDescriptor>> tours, out bool changed)
        {
            if (tours == null)
            {
                throw new ArgumentNullException(nameof(tours));
            }

            var effect = GuideEffect.None;
            var previous = _hasFrame ? _frameTourIds : _pendingTourIds;

            if (State == GuideState.Anchored && !TourIdLists.SameSet(previous, _pendingTourIds))
            {
                var region = TourRegionPolicy.Decide(Snapshot(), tours(), previous);
                if (region.ShouldReselect)
                {
                    effect = StopActive();
                    if (region.ReselectCandidates.Count > 0)
                    {
                        var pick = _pick(region.ReselectCandidates);
                        effect.ActivateTourId = pick;
                        ActiveTourId = pick;
                    }
                }
            }

            changed = !_hasFrame
                      || State != _frameState
                      || ActiveTourId != _frameActiveTourId
                      || !TourIdLists.SameSet(_frameTourIds, _pendingTourIds);

            _hasFrame = true;
            _frameState = State;
            _frameActiveTourId = ActiveTourId;
            _frameTourIds = _pendingTourIds;

            return effect;
        }

        private GuideEffect StopActive()
        {
            if (ActiveTourId == null)
            {
                return GuideEffect.None;
            }

            ActiveTourId = null;
            return new GuideEffect { Deactivate = true };
        }

        /// <summary>换 Tour 时先停旧的；同一个 Tour 由效果层只重新锚定并确保已启用（与原 Activate 一致）。</summary>
        private GuideEffect ActivateEffect(string tourId, Pose? pose)
        {
            var effect = new GuideEffect
            {
                Deactivate = ActiveTourId != null && ActiveTourId != tourId,
                ActivateTourId = tourId,
                AnchorPose = pose,
            };

            ActiveTourId = tourId;
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

(b) 整体替换 `Packages/com.uality.ite-tour/Runtime/Core/TourDirector.cs`：

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 扫描、区域进出与佩戴状态的**效果层**：输入交给 <see cref="TourGuide"/>（纯状态核心），
    /// 它返回的 <see cref="GuideEffect"/> 在这里落到 <see cref="IteTourObject"/> 上。
    ///
    /// 这一层刻意做得很薄——状态与所有判断都在 TourGuide 与三个策略函数里，这里只剩「照做」
    /// （ite-guide-state-machine D7）。与源实现的差异见 design D14：源实现把扫描逻辑摊在三个订阅
    /// 同一事件的处理器里、二次锚定许可依赖 await 时序，这里都由一次决策明确规定。
    /// </summary>
    public class TourDirector
    {
        private readonly IteTourAssembler _assembler;
        private readonly TourGuide _guide;

        private IteTourObject _activeTour;
        private ScanPrompt _lastPrompt = ScanPrompt.Hidden;

        /// <summary>Tour 被激活（先于内容构建完成）。</summary>
        public Action<string> TourActivated;

        public Action<string> TourDeactivated;

        /// <summary>扫码提示的显隐与内容发生变化时触发（design D5）。仅在变化时发。</summary>
        public Action<ScanPrompt> ScanPromptChanged;

        /// <summary>导览状态或进入原因变化时触发（ite-guide-state-machine D8）。</summary>
        public Action<GuideState, GuideStateReason> GuideStateChanged;

        /// <param name="pick">
        /// 区域重选有多个候选时挑哪个。缺省随机——源实现的 OrderBy(Guid.NewGuid())（design D14）。
        /// </param>
        public TourDirector(IteTourAssembler assembler, Func<IReadOnlyList<string>, string> pick = null)
        {
            _assembler = assembler ?? throw new ArgumentNullException(nameof(assembler));
            _guide = new TourGuide(pick ?? (ids => ids[UnityEngine.Random.Range(0, ids.Count)]));
            _guide.StateChanged += (state, reason) =>
            {
                Debug.Log($"[ITE] 状态 → {state}（原因={reason}）");
                GuideStateChanged?.Invoke(state, reason);
            };
        }

        /// <summary>把一个 Tour 的触发体积事件接进来。装配时逐个调用。</summary>
        public void Observe(IteTourObject tour)
        {
            if (tour != null)
            {
                tour.OnCameraVolumeTransition += SubmitVolumeTransition;
            }
        }

        public string ActiveTourId => _guide.ActiveTourId;

        public GuideState State => _guide.State;

        public GuideStateReason Reason => _guide.Reason;

        public IReadOnlyList<string> PendingTourIds => _guide.PendingTourIds;

        /// <summary>
        /// 运行中直接进入「等待扫码定位」：停掉在播 Tour，与冷启动同一状态（ite-guide-state-machine D2）。
        /// 摘下期间调用不切换（D3）。
        /// </summary>
        public void RequireScan(GuideStateReason reason) => Apply(_guide.RequireScan(reason));

        /// <summary>摘下：停掉在播 Tour，进入 Suspended。戴上：从 Suspended 进入 AwaitingScan。</summary>
        public void SetHeadsetMounted(bool mounted) => Apply(_guide.SetHeadsetMounted(mounted));

        /// <summary>
        /// 不经传感器直接激活指定 Tour（design D30），沿用现有锚定。只在 Anchored 下可用
        /// （ite-guide-state-machine D4）。找不到或状态不允许时返回 false。
        /// </summary>
        public bool ActivateById(string tourId)
        {
            if (_assembler.Find(tourId) == null)
            {
                Debug.LogError("[ITE] 找不到 Tour：" + tourId);
                return false;
            }

            if (!_guide.TryActivateById(tourId, out var effect))
            {
                Debug.LogWarning($"[ITE] 当前状态 {_guide.State} 下不能直接激活 Tour：{tourId}（需先扫码定位）");
                return false;
            }

            Apply(effect);
            return true;
        }

        public void SubmitMarkerScan(string markerId, Pose pose)
        {
            var before = _guide.Snapshot();
            var effect = _guide.SubmitScan(markerId, pose, Descriptors(), out var decision);

            // 真机上判断「为什么没反应」只能靠这一行：决策依据的状态与扫到的位姿都带上。
            Debug.Log(
                $"[ITE] 扫码 {markerId} → {decision.Action}" +
                $"（状态={before.State} 在播={before.ActiveTourId ?? "无"} 所在区域=[{JoinIds(before.PendingTourIds)}]）" +
                $" 位姿 pos={pose.position.ToString("F3")} rot={pose.rotation.eulerAngles.ToString("F1")}");

            Apply(effect);
        }

        /// <summary>
        /// 相机进出某个 Tour 的触发体积：只更新所在区域集合（I3），要不要换 Tour 在帧末按净变化
        /// 判一次（ite-guide-state-machine D5）。
        /// </summary>
        public void SubmitVolumeTransition(string tourId, VolumeTransition transition)
        {
            _guide.SubmitVolumeTransition(tourId, transition);
            Debug.Log($"[ITE] 区域 {transition} {tourId} → 所在区域=[{JoinIds(_guide.PendingTourIds)}]");
        }

        /// <summary>
        /// 帧末结算，由 <see cref="IteRuntimeDriver"/> 在 <c>LateUpdate</c> 调用：区域重选（D5），
        /// （状态、在播、所在区域）变了才重算扫码提示（D6）。
        ///
        /// 在帧末而不是在事件上结算：相邻体积之间移动时，退出 A 与进入 B 在同一物理步内发生，
        /// 按净变化判一次就直接从 A 换到 B，不会先停 A 再启 B。
        /// </summary>
        public void EndOfFrame()
        {
            var previousActive = _guide.ActiveTourId;
            var effect = _guide.EndOfFrame(Descriptors, out bool changed);

            if (effect.Deactivate || effect.ActivateTourId != null)
            {
                Debug.Log(effect.ActivateTourId != null
                    ? $"[ITE] 区域重选：{previousActive ?? "无"} → {effect.ActivateTourId}（沿用现有锚定）"
                    : $"[ITE] 区域重选：停用 {previousActive}，范围内无 regionalTrigger");
            }

            Apply(effect);

            if (changed)
            {
                EvaluateScanPrompt();
            }
        }

        /// <summary>
        /// 重算扫码提示，有变化才广播。源实现是个 0.75 秒轮询的协程，每次都直接调 UI 的
        /// Show/Hide（无论变没变）；这里只保留「算 + 变了才发」。
        /// </summary>
        private void EvaluateScanPrompt()
        {
            var prompt = ScanPromptPolicy.Decide(_guide.Snapshot(), Descriptors());

            if (SamePrompt(_lastPrompt, prompt))
            {
                return;
            }

            _lastPrompt = prompt;
            ScanPromptChanged?.Invoke(prompt);
        }

        private void Apply(GuideEffect effect)
        {
            if (effect.Deactivate)
            {
                DeactivateCurrent();
            }

            if (effect.ActivateTourId != null)
            {
                var tour = _assembler.Find(effect.ActivateTourId);
                if (tour == null)
                {
                    // 核心已记下它在播，效果层却找不到对象：不能静默，否则两边从此不一致
                    Debug.LogError("[ITE] 要激活的 Tour 不存在：" + effect.ActivateTourId);
                }
                else
                {
                    Activate(tour, effect.AnchorPose);
                }
            }

            if (effect.ReanchorTourId != null)
            {
                Reanchor(_assembler.Find(effect.ReanchorTourId), effect.ReanchorPose, effect.ConsumesSecondAnchor);
            }
        }

        private static bool SamePrompt(ScanPrompt a, ScanPrompt b)
        {
            if (a.State != b.State)
            {
                return false;
            }

            var left = a.TourIds ?? Array.Empty<string>();
            var right = b.TourIds ?? Array.Empty<string>();

            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static string JoinIds(IReadOnlyList<string> ids)
            => ids == null ? "" : string.Join(",", ids);

        private List<TourDescriptor> Descriptors()
        {
            var descriptors = new List<TourDescriptor>();

            foreach (var tour in _assembler.LiveTours)
            {
                if (tour == null)
                {
                    continue;
                }

                descriptors.Add(new TourDescriptor
                {
                    TourId = tour.TourId,
                    DisplayType = tour.DisplayType,
                    SecondAnchorAvailable = tour.CanSecondAnchor(),
                });
            }

            return descriptors;
        }

        /// <param name="pose">null 表示不重新锚定（区域唤醒与直接激活沿用现有位姿）。</param>
        private void Activate(IteTourObject tour, Pose? pose)
        {
            if (ReferenceEquals(_activeTour, tour))
            {
                if (pose.HasValue)
                {
                    tour.ChangeTourObjectTransform(pose.Value.position, pose.Value.rotation);
                }

                _ = tour.Enable();
                return;
            }

            DeactivateCurrent();

            _activeTour = tour;

            if (pose.HasValue)
            {
                tour.ChangeTourObjectTransform(pose.Value.position, pose.Value.rotation);
            }

            // 先广播再 Enable。已建树时 Enable 是空操作、不再派发组件侧 Loaded
            // （LoadTrigger 不能重放）；宿主超时由 IteRuntime 在 Activated 回调里补一次。
            TourActivated?.Invoke(tour.TourId);

            // 与源实现一致：不等内容构建完就返回，构建完成由 OnTourSceneLoaded 通知
            _ = tour.Enable();
        }

        private void Reanchor(IteTourObject tour, Pose pose, bool consumesSecondAnchor)
        {
            if (tour == null)
            {
                return;
            }

            tour.ChangeTourObjectTransform(pose.position, pose.rotation);

            if (consumesSecondAnchor)
            {
                tour.SecondAnchored();
            }

            Debug.Log("[ITE] Reanchor " + tour.TourId + (consumesSecondAnchor ? " consumesSecondAnchor" : ""));
        }

        private void DeactivateCurrent()
        {
            if (_activeTour == null)
            {
                return;
            }

            var tourId = _activeTour.TourId;

            _activeTour.Disable();
            _activeTour = null;

            TourDeactivated?.Invoke(tourId);
        }
    }
}
```

(c) `Packages/com.uality.ite-tour/Runtime/Core/IteRuntimeDriver.cs` 第 16 行：

```csharp
        private void LateUpdate() => Director?.FlushRegionTransitions();
```

改为

```csharp
        private void LateUpdate() => Director?.EndOfFrame();
```

(d) `Packages/com.uality.ite-tour/Runtime/Core/IteRuntime.cs`：

1. 构造函数中，在 `_director.ScanPromptChanged += prompt => OnScanPromptChanged?.Invoke(prompt);` 的下一行加：

```csharp
            _director.GuideStateChanged += (state, reason) => OnGuideStateChanged?.Invoke(state, reason);
```

2. 在 `public event Action<ScanPrompt> OnScanPromptChanged;` 的下一行加：

```csharp

        /// <summary>
        /// 导览状态或进入原因变化（ite-guide-state-machine D8）。宿主据此显示对应提示，
        /// 例如「视角已重定位」只在 <see cref="Core.GuideState.AwaitingScan"/> 且原因是
        /// <see cref="Core.GuideStateReason.Recentered"/> 时显示。
        /// </summary>
        public event Action<GuideState, GuideStateReason> OnGuideStateChanged;
```

3. `LoadAsync` 末尾的这两行：

```csharp
            // 冷启动后的第一次扫码无条件生效
            _director.RequireScan();
```

替换为：

```csharp
            // 冷启动状态由 TourGuide 构造时给出（AwaitingScan / ColdStart），这里不再额外要求重扫
            // （ite-guide-state-machine D9）。加载途中摘下头显时状态是 Suspended，戴上后照常进入等待扫码。
```

4. `ActivateTour` 的注释

```csharp
        /// <summary>
        /// 不经传感器直接激活指定 Tour（design D30）。没有真机与二维码时，
        /// 这是验证内容管线的唯一手段。找不到返回 false。
        /// </summary>
```

替换为：

```csharp
        /// <summary>
        /// 不经传感器直接激活指定 Tour（design D30），沿用现有锚定。只在已定位（Anchored）状态下可用，
        /// 其他状态返回 false（ite-guide-state-machine D4）。找不到也返回 false。
        /// </summary>
```

5. 在 `public string ActiveTourId => _director.ActiveTourId;` 之前加：

```csharp
        /// <summary>导览当前状态（ite-guide-state-machine D1）。</summary>
        public GuideState GuideState => _director.State;

        /// <summary>进入当前状态的原因。</summary>
        public GuideStateReason GuideStateReason => _director.Reason;

```

6. `RequireScan` 连同它的注释：

```csharp
        /// <summary>
        /// 宿主要求下一次扫码无条件生效。用于「上一次锚定不再可信」的场合——
        /// 例如系统重定位改了追踪原点：内容在世界坐标里没动，物理世界却整个转了过去，
        /// 不重扫就会一直偏着（design D18）。
        ///
        /// 与摘下/戴上不同：这里**不停用当前 Tour**，只是要求重扫。
        /// </summary>
        public void RequireScan() => _director.RequireScan();
```

替换为：

```csharp
        /// <summary>
        /// 运行中直接进入「等待扫码定位」：停掉在播的 Tour，状态与冷启动一致（ite-guide-state-machine D2）。
        /// 用于「上一次锚定不再可信」的场合——例如系统重定位改了追踪原点：内容在世界坐标里没动，
        /// 物理世界却整个转了过去，不重扫就会一直偏着（design D18），此时传
        /// <see cref="Core.GuideStateReason.Recentered"/>。头显摘下期间调用不切换状态，戴上时本就会要求扫码（D3）。
        /// </summary>
        public void RequireScan(GuideStateReason reason = GuideStateReason.HostRequested)
            => _director.RequireScan(reason);
```

7. `SetHeadsetMounted` 的注释 `/// <summary>宿主推入头显佩戴状态。摘下暂停导览，重新戴上要求重新扫码。</summary>` 替换为：

```csharp
        /// <summary>
        /// 宿主推入头显佩戴状态。摘下：停掉在播 Tour、不认扫码、区域不唤醒 Tour；戴上：进入等待扫码定位
        /// （ite-guide-state-machine §3.2）。
        /// </summary>
```

改完后执行 `grep -rn "ForcedScanPending\|FlushRegionTransitions\|ClearsForcedScan\|_forcedScanPending\|_paused\|_reselectPending\|_promptDirty" Packages/com.uality.ite-tour`，应当没有输出。

- [ ] **Step 8: 编译并跑测试，确认「绿」**

按「如何编译」执行，预期 `"failed":false,"errors":[]`。再跑 `A=Uality.IteTour.Tests`，预期 `{'Total': 301, 'Passed': 301, 'Failed': 0, ...}`，没有 FAIL 行。

301 的来历：原 276；`TourScanPolicyTests` 删 1 条（−1）；`ScanPromptPolicyTests` 加 1 条（+1）；`TourRegionPolicyTests` 从 12 条变成 14 条（+2）；`TourIdListsTests` 加 3 条（+3）；新增 `TourGuideTests` 20 条（+20）。

如果有失败：先判断是测试照 spec 写错了，还是实现错了；按 spec 修正，**不许为了过测试去改期望值**。确实需要改期望值时，在报告里写明理由。

- [ ] **Step 9: 自查**

- `git diff --stat` 里只出现 Files 列表中的文件，外加 Unity 为新文件生成的 `.meta`。
- 除第 7 步里明确要求的那一处，`IteRuntime.cs` 没有其他逻辑改动。

---

### Task 2: 宿主——重定位黄条跟随状态，重定位请求改到主线程处理

**Files:**
- Modify: `Assets/Scripts/IteHost/IteHmdPanel.cs`（删 `NotifyRecentered`；`TryHook` / `Unhook` 增加订阅；简化 `HandleScanPromptChanged`；新增 `ShowsRecenterBanner` 与 `HandleGuideStateChanged`）
- Modify: `Assets/Scripts/IteHost/IteDeviceMarkerRig.cs`（`HandleRecentered`、新增 `Update`、删除 `_panel`、加 using）
- Create: `Assets/Scripts/IteHost/Tests/EditMode/RecenterBannerTests.cs`

**Interfaces:**
- Consumes（来自 Task 1）：`GuideState`、`GuideStateReason`、`IteRuntime.OnGuideStateChanged`、`IteRuntime.GuideState`、`IteRuntime.GuideStateReason`、`IteRuntime.RequireScan(GuideStateReason)`。
- Produces：`public static bool IteHmdPanel.ShowsRecenterBanner(GuideState state, GuideStateReason reason)`。

- [ ] **Step 1: 先写测试**

新建 `Assets/Scripts/IteHost/Tests/EditMode/RecenterBannerTests.cs`：

```csharp
using NUnit.Framework;
using Uality.IteTour.Core;

namespace MRBase.Ite.Host.Tests
{
    /// <summary>
    /// 重定位黄条表达的是一个状态：「因为重定位，正在等待扫码」（ite-guide-state-machine D8）。
    /// 旧实现挂在扫码提示的变化上，只有收到一次 Visible 才清，于是会残留。
    /// </summary>
    public class RecenterBannerTests
    {
        [Test]
        public void Shows_WhileAwaitingScanBecauseOfRecenter()
        {
            Assert.That(IteHmdPanel.ShowsRecenterBanner(GuideState.AwaitingScan, GuideStateReason.Recentered), Is.True);
        }

        [TestCase(GuideState.Anchored, GuideStateReason.Scanned)]
        [TestCase(GuideState.Suspended, GuideStateReason.HeadsetRemoved)]
        [TestCase(GuideState.AwaitingScan, GuideStateReason.HeadsetMounted)]
        [TestCase(GuideState.AwaitingScan, GuideStateReason.ColdStart)]
        public void Hidden_Otherwise(GuideState state, GuideStateReason reason)
        {
            Assert.That(IteHmdPanel.ShowsRecenterBanner(state, reason), Is.False);
        }
    }
}
```

- [ ] **Step 2: 编译，确认「红」**

预期：编译失败，唯一的错误是 `IteHmdPanel` 没有 `ShowsRecenterBanner`。

- [ ] **Step 3: 改 `IteHmdPanel.cs` 和 `IteDeviceMarkerRig.cs`**

(a) `Assets/Scripts/IteHost/IteHmdPanel.cs`：

1. 删除整个 `NotifyRecentered()` 方法：

```csharp
        public void NotifyRecentered()
        {
            _recenterPending = true;
            Debug.Log("[ITE Host] 重定位横幅：显示");
        }
```

如果它上方有专属的 `/// <summary>` 注释，一并删除。

2. `TryHook()` 里，在 `_hookedRuntime.OnInitialized += HandleInitialized;` 之后加：

```csharp
            _hookedRuntime.OnGuideStateChanged += HandleGuideStateChanged;

            // 挂钩可能晚于状态变化，先同步一次当前状态
            HandleGuideStateChanged(runtime.GuideState, runtime.GuideStateReason);
```

3. `Unhook()` 里，在 `_hookedRuntime.OnInitialized -= HandleInitialized;` 之后加：

```csharp
            _hookedRuntime.OnGuideStateChanged -= HandleGuideStateChanged;
```

4. 把 `HandleScanPromptChanged` 整个替换为：

```csharp
        private void HandleScanPromptChanged(ScanPrompt prompt) => _prompt = prompt;

        /// <summary>
        /// 重定位黄条表达的是一个状态：「因为重定位，正在等待扫码」（ite-guide-state-machine D8）。
        /// 跟着状态走：扫码进入 Anchored（或摘下）即清掉，不会残留。
        /// </summary>
        public static bool ShowsRecenterBanner(GuideState state, GuideStateReason reason)
            => state == GuideState.AwaitingScan && reason == GuideStateReason.Recentered;

        private void HandleGuideStateChanged(GuideState state, GuideStateReason reason)
        {
            bool show = ShowsRecenterBanner(state, reason);
            if (show != _recenterPending)
            {
                Debug.Log("[ITE Host] 重定位横幅：" + (show ? "显示" : "清除"));
            }

            _recenterPending = show;
        }
```

(b) `Assets/Scripts/IteHost/IteDeviceMarkerRig.cs`：

1. 在 `using UnityEngine;` 下面加 `using Uality.IteTour.Core;`。
2. 把 `HandleRecentered()` 整个方法，以及紧随其后的 `private IteHmdPanel _panel;` 字段，替换为：

```csharp
        // PICO 的重定位事件来自原生回调（PXR_Loader.XrEventDataBufferFunction），不保证在主线程。
        // RequireScan 会停用在播的 Tour，要调 Unity API，只能在主线程——所以回调里只记下请求，
        // 下一帧 Update 再处理（ite-guide-state-machine D2 之后才需要：以前 RequireScan 只改一个 bool）。
        private volatile bool _recenterRequested;

        private void HandleRecentered() => _recenterRequested = true;

        private void Update()
        {
            if (!_recenterRequested)
            {
                return;
            }

            _recenterRequested = false;
            Debug.Log($"{LogPrefix} 追踪原点变化，上一次锚定作废，要求重新扫码。", this);

            // 黄条由 IteHmdPanel 跟随状态显示（D8），这里不再去找面板
            host?.Runtime?.RequireScan(GuideStateReason.Recentered);
        }
```

3. 执行 `grep -rn "NotifyRecentered\|_panel" Assets/Scripts/IteHost`，应当没有输出。

- [ ] **Step 4: 编译并跑两个程序集**

按「如何编译」执行，预期 `"failed":false,"errors":[]`。然后跑：
- `A=MRBase.Ite.Host.Tests`：预期 `{'Total': 35, 'Passed': 35, 'Failed': 0, ...}`（原 30 条，加 `RecenterBannerTests` 的 5 个用例）；
- `A=Uality.IteTour.Tests`：预期 301/301。

---

### Task 3: 文档收尾、回归与提交

**Files:**
- Modify: `docs/superpowers/specs/2026-09-23-ite-guide-state-machine-design.md`（文件头状态）
- Modify: `docs/superpowers/specs/2026-09-23-ite-scan-region-gate-design.md`（§5 的 `IteHmdPanel` 条目）

**Interfaces:** 无。

- [ ] **Step 1: 更新 spec 状态**

`docs/superpowers/specs/2026-09-23-ite-guide-state-machine-design.md` 第 3 行 `> 状态：待评审` 改为 `> 状态：已评审，已实现（待真机验证）`。

- [ ] **Step 2: 在区域门禁 spec 里标注黄条问题已解决**

`docs/superpowers/specs/2026-09-23-ite-scan-region-gate-design.md` §5 中，以「这次不修：宿主侧的修法是」开头的那一段末尾，追加：

```
（已由 `docs/superpowers/specs/2026-09-23-ite-guide-state-machine-design.md` D8 解决：黄条改为跟随导览状态显示。）
```

- [ ] **Step 3: 回归**

按「如何编译」执行，然后跑两个程序集：`Uality.IteTour.Tests` 预期 301/301，`MRBase.Ite.Host.Tests` 预期 35/35。只要有失败（包括看起来不是这次改动引起的），都按测试名原样报告。

执行 `git status --short`，预期改动只涉及：
- Task 1、Task 2 列出的文件，以及新文件的 `.meta`；
- 两份 spec 和本 plan；
- `Assets/Scripts/IteHost/IteHostBootstrap.cs`：这是基线里本来就有的诊断日志改动，一并提交。

- [ ] **Step 4: 提交（先征得用户同意）**

先问用户：是否现在提交？用户同意后再执行：

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git add Packages/com.uality.ite-tour/Runtime/Core/GuideState.cs Packages/com.uality.ite-tour/Runtime/Core/GuideState.cs.meta \
        Packages/com.uality.ite-tour/Runtime/Core/TourGuide.cs Packages/com.uality.ite-tour/Runtime/Core/TourGuide.cs.meta \
        Packages/com.uality.ite-tour/Runtime/Core/TourIdLists.cs Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs \
        Packages/com.uality.ite-tour/Runtime/Core/ScanPromptPolicy.cs Packages/com.uality.ite-tour/Runtime/Core/TourRegionPolicy.cs \
        Packages/com.uality.ite-tour/Runtime/Core/TourDirector.cs Packages/com.uality.ite-tour/Runtime/Core/IteRuntimeDriver.cs \
        Packages/com.uality.ite-tour/Runtime/Core/IteRuntime.cs \
        Packages/com.uality.ite-tour/Tests/Editor/TourGuideTests.cs Packages/com.uality.ite-tour/Tests/Editor/TourGuideTests.cs.meta \
        Packages/com.uality.ite-tour/Tests/Editor/TourRegionPolicyTests.cs Packages/com.uality.ite-tour/Tests/Editor/TourIdListsTests.cs \
        Packages/com.uality.ite-tour/Tests/Editor/TourScanPolicyTests.cs Packages/com.uality.ite-tour/Tests/Editor/ScanPromptPolicyTests.cs \
        Assets/Scripts/IteHost/IteHmdPanel.cs Assets/Scripts/IteHost/IteDeviceMarkerRig.cs Assets/Scripts/IteHost/IteHostBootstrap.cs \
        Assets/Scripts/IteHost/Tests/EditMode/RecenterBannerTests.cs Assets/Scripts/IteHost/Tests/EditMode/RecenterBannerTests.cs.meta \
        docs/superpowers/specs/2026-09-23-ite-guide-state-machine-design.md \
        docs/superpowers/specs/2026-09-23-ite-scan-region-gate-design.md \
        docs/superpowers/plans/2026-09-23-ite-guide-state-machine.md
git commit -m "$(cat <<'EOF'
refactor(ite): 导览状态收成显式状态机，修摘下后区域仍能唤醒 Tour

TourDirector 原来用 _paused / _forcedScanPending / _reselectPending /
_promptDirty 几个标志拼出状态，各策略各看一半：摘下只置 _paused，区域
策略只看 _forcedScanPending，于是摘下期间区域照样唤醒 Tour，戴上后看到
一个在播的 Tour、没有扫码提示（PICO 实测）。

- GuideState {Suspended, AwaitingScan, Anchored}，纯逻辑核心 TourGuide
  持有状态并返回动作，TourDirector 只照做
- 运行中 RequireScan 与冷启动同一状态，停掉在播 Tour
- 区域重选、提示重算改为帧末按快照净变化判一次
- 重定位黄条跟随状态；PICO 重定位回调改到主线程处理

设计见 docs/superpowers/specs/2026-09-23-ite-guide-state-machine-design.md

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
git show --stat HEAD
```

`git add` 之前，先用 `ls` 确认每个 `.meta` 文件确实存在；不存在的就从命令里去掉，不要编造路径。

- [ ] **Step 5: 真机验证清单（交给用户；用户说「打包」才打包）**

1. 扫码定位后摘下头显，拿着它走过几个区域，再戴上：没有 Tour 在播，显示「扫任意码」，日志里有 `[ITE] 状态 → Suspended` 和 `状态 → AwaitingScan（原因=HeadsetMounted）`，没有 `区域重选`；扫码后正常激活并锚定。
2. 头显放在桌上对着码摘下：日志里没有 `扫码 … → Activate`。
3. 触发一次追踪原点重置：在播 Tour 被停掉，出现黄条和「扫任意码」；日志有 `状态 → AwaitingScan（原因=Recentered）` 和 `重定位横幅：显示`；扫码后黄条消失（`重定位横幅：清除`）。
4. 观察戴上头显后 PICO 会不会紧跟着触发一次重定位（Review Focus 第 3 条）。
