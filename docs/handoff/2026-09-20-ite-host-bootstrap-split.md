# Handoff: ITE 模块化整改（下一步：拆 `IteHostBootstrap`）

> Generated: 2026-09-20
> Next session focus: #7 —— 把 `IteHostBootstrap` 的七项职责拆开

## Goal

让 ITE 相关代码（包 `Packages/com.uality.ite-tour` + 宿主 `Assets/Scripts/IteHost`）
在模块边界、职责划分、平台分叉三方面站得住。本次会话按一份架构评审的排序依次执行，
已完成 3 项、明确否决 1 项、2 项被阻塞，剩下 #7。

## Current state

工作树干净（除下方「未提交」列出的既有改动），全量 EditMode **389/389 绿**。
本次会话的五个提交（都在 `master`，与仓库既有习惯一致）：

```
46acb5c docs(ite-tour-space-device): 记入 D33–D35（并带上既有的 D31）
d790dcd refactor(platform): 系统重定位的平台分叉归 PlatformRuntime
eb13913 refactor(ite): 锚定几何提成纯函数 TourAnchoring
903b374 fix(ite): 桌面假扫码修复三处静默失效，并挡在设备包之外
12fb50c refactor(ite): 两个 ITE 场景按分组 + harness 预制体重组
```

架构评审的七项，当前状态：

| # | 事项 | 状态 |
|---|---|---|
| 1 | 锚定几何提成纯函数 | ✅ `eb13913`，见 design D33 |
| 2 | Core ↔ Components 双向依赖 | ❌ 细读后**否决**，理由见下 |
| 3 | `MarkerFrame` 的固定旋转取值 | ⏸ 用户暂停，勿主动追问 |
| 4 | 桌面脚手架不进设备包 | ✅ `903b374`，见 design D34 |
| 5 | 重定位分叉归 `PlatformRuntime` | ✅ `d790dcd`，见 design D35（未上机验） |
| 6 | `Tour` / `Entity` 的 `FlipRotY` 不一致 | ⏸ 与 #3 同链，需真机 |
| 7 | 拆 `IteHostBootstrap` | ⬜ **下一步** |

### #7 的现状（下一个 session 的起点）

`Assets/Scripts/IteHost/IteHostBootstrap.cs`，350 行，21 个方法，七项职责：
装配校验、运行时创建、相机解析（`ResolveCamera:143`）、网络探针
（`IsNetworkAvailable:166` / `SetNetworkProbe:179`）、会话绑定
（`AttachMarkerSession:80` / `TryBindMarkerSession:251`）、事件转发
（`HookRuntimeEvents:264` + 十个 `Handle*:300-331`，其中八个是 `static` 只为打日志）、
佩戴状态（经 `HeadsetPresenceAdapter`，由 `Update:226` 驱动）。

评审当时的判断是「每项都薄，还没到痛的时候」，最容易剥离的是**事件转发**那一块。
下一个 session 若要动，先确认它现在是否真的碍事，而不是为拆而拆。

## Key decisions

三条已写进 `openspec/changes/ite-tour-space-device/design.md`（D33 / D34 / D35），
不要在新 session 里重新论证，直接读那三节。其中 D34 记了一个反面实测：
**Editor-only asmdef 装不了 MonoBehaviour**（`AddComponent` 返回 null，场景组件变 Missing），
所以桌面脚手架走 `#if UNITY_EDITOR`。

**#2 被否决的理由**（未写进 design.md，因为没有改动落地，记在这里）：
组件向 Core 要的只有四样——`GetAsset(id)`、`ElementPrefabs`、`OnTourSceneLoaded`、`transform`，
面很窄。但 `BaseComponent.Awake` 用 `GetComponentInParent<IteTourObject>()` 服务定位；
改成注入就必须让 `IteTourObject.CreateEntity` 先建 inactive 对象 → `AddComponent` →
塞 context → `SetActive(true)`，等于把内容构建路径的正确性押在激活顺序上——正是
`.claude/CLAUDE.md` 点名的「行为依赖隐式因素：调用顺序」那一类，而该路径今天零测试覆盖。
造 `ITourContentContext` 之类的接口同样不成立（单一实现的接口是明确拒绝的形状）。
结论：要动就连 `IteTourObject` 的初始化顺序一起重新设计，那比 #2 本身大得多。

**场景组织约定**（已落地，新 session 改场景时要遵守）：根层只有
`-- Management --`（共享 `IteTourRig` 预制体）与 `-- Harness --`（设备/编辑器 harness 预制体），
且分组头与预制体根必须 `localToWorldMatrix == identity`。这两条由
`Assets/Scripts/IteHost/Tests/EditMode/IteSceneLayoutTests.cs` 守住。

## Open questions / blockers

- **#7 值不值得做**：需要先给出「它现在怎么碍事」的具体证据，否则按本仓库的原则
  （结构最优，但不为拆而拆）应该继续放着。
- **#3 / #6 被暂停**：两者都要真机才能定。相关调查证据已存档，见下方 artifacts。
  用户已明确暂停，**新 session 不要主动追问**。
- **D35 未上机验证**：重定位路径两端都只在头显上才走得到，下次出包时一并验。
- `Assets/Settings/ITE/IteRuntimeConfig.asset` 的 `sceneName` 在本次会话期间从
  `thirdDemo` 变成了 `DazuRockCarvings`（非本次工作所改）。若要复现桌面验收流程，
  先确认这个值是不是你要的场景。

## Relevant artifacts

- `openspec/changes/ite-tour-space-device/design.md` — D33/D34/D35（本次）与 D30/D31/D32（既有），
  改锚定、平台分叉、脚手架之前必读
- `Packages/com.uality.ite-tour/Runtime/Core/TourAnchoring.cs` + `Tests/Editor/TourAnchoringTests.cs` —
  锚定链现在的唯一几何实现与它的轴向守卫
- `Assets/Scripts/IteHost/Tests/EditMode/IteSceneLayoutTests.cs` — 两个 ITE 场景的结构约定
- `Assets/Scripts/Platform/PlatformRuntime.cs` — 全部平台分叉的唯一落点（现含重定位）
- `openspec/changes/marker-anchor-axis-correction/probe-report.md` — #3 的全部证据
  （含真实场景 JSON 的实测数值、两个工程做法的逐行对比）。**未跟踪**，被暂停
- `openspec/changes/ite-scene-layout-convention/probe-report.md` — 场景重组的决策过程。**未跟踪**
- `Assets/Scripts/IteHost/IteHostBootstrap.cs` — #7 的对象本体

### 未提交的既有改动（不是本次工作）

`.claude/CLAUDE.md`、`.gitignore`、`DevAgentSettings.asset`、`BuildScript.cs`、
`IteRuntimeConfig.asset`、`MarkerHookTestRig.cs`、`AxisGizmo.cs`、`MarkerFrame.cs`(+测试)、
`IteRuntime.cs`，以及 `openspec/changes/` 下三个未跟踪目录。其中
`MarkerFrame` / `AxisGizmo` / `IteRuntime` 那一组属于 #3 的轴向线。

## Suggested next steps

- [ ] 先判断 #7 是否值得做：列出它当前造成的具体成本（改一处要动哪些无关职责、
      测试为什么难写），拿不出成本就维持现状并在 design.md 里记一条「暂不拆」
- [ ] 若做：从**事件转发**剥起（`HookRuntimeEvents` + 十个 `Handle*`，八个是纯日志 static），
      它与其余六项职责没有状态耦合，是唯一能独立搬走的一块
- [ ] 任何改动后跑 `unity command run_tests --project-path . --mode EditMode`，
      基线是 389/389
- [ ] Unity 操作一律经 `unity` CLI 打进用户正开着的 Editor（见 `.claude/CLAUDE.md`），
      不另起进程、不手改场景 YAML

## Suggested skills

- `/opsx:probe marker-anchor-axis-correction` — **仅当用户主动重启 #3 时**；
  报告已在仓库里，接着问物理摆放那两问即可
- `/opsx:propose` — 若 #7 决定要做且改动面超出单文件重构，先出 proposal/design/tasks
- 直接动手即可（不必建 change）——用户在本次会话中明确表达过这个偏好，#7 属于此类
- `unity-cli` — 任何触及场景、预制体、资产或跑测试的操作
