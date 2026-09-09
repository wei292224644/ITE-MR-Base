# Handoff: ite-tour-space-integration 收尾

> Generated: 2026-09-09 15:37 +08:00
> Next session focus: 先收尾本 change（补提交规划产物、清临时场景、归档）；三条后续 change 只记账、不展开

## Goal

把 thirdDemo 的完整导览语义（扫码 + 区域触发，不是「永远全显示」）接到 `MR_Base` 的编辑器验收场景里。本 change 的实现与 Play 验收已做完。下一会话**先把这次修改收干净**（未跟踪产物入库、归档），三条产品后续 **只记名、等以后 `/opsx:propose`**。

## Current state

- OpenSpec change `ite-tour-space-integration`：**49/49 完成**，`openspec instructions apply` 状态 `all_done`，**尚未归档**。
- 分支：`openspec/unified-marker-tracking-contract`（工作叠在标记追踪合同分支上，不是独立分支）。
- 验收场景：`Assets/Scenes/IteTourSpace.unity`（**未**进 `EditorBuildSettings` / `BuildScript`，这是刻意的，见 design D9）。
- Play 证据：`openspec/changes/ite-tour-space-integration/play-evidence/`（7.1–7.6 截图 + `7.play-console.txt`）。
- 宿主 EditMode：`MRBase.Ite.Host.Tests` 上次 **20/20 绿**。
- 驱动层已改走 **Input System**（工程 `activeInputHandler=1`）。旧 `Input.GetKeyDown` 会每帧抛异常并打断假扫 `Tick`。
- **未跟踪**（归档前必须入库，不要跟下面「预先存在的脏文件」混在一起）：
  - `openspec/changes/ite-tour-space-integration/.openspec.yaml`
  - `openspec/changes/ite-tour-space-integration/proposal.md`
  - `openspec/changes/ite-tour-space-integration/design.md`
  - `openspec/changes/ite-tour-space-integration/probe-report.md`
  - `openspec/changes/ite-tour-space-integration/specs/`（三个 delta：`ite-content-acquisition` / `ite-tour-space-host` / `ite-tour-space-editor-harness`）
- **不要提交**的预先存在脏文件：`Assets/IceSpriteFx/Materials/IceSprite_Dissolve.mat`；已删除的 `openspec/changes/ite-render-smoke-scene/**`。
- 主 spec 目录目前只有 `openspec/specs/unified-marker-tracking-contract/spec.md`。本 change 的三条 delta **还没 sync**；归档时会问要不要 sync。
- 临时场景仍在：`Assets/Scenes/IteAcquireVerify.unity`（2.9 联网落盘验证用，可删）。

最近提交（节选）：`c712c50` 7.2–8.4 验收存档；`ac90141` Input System + Reanchor 日志；`eae8091` 7.1 首跑；`41655fc` 5.4 未注入路径。

## Key decisions

不要重开设计讨论。编号决策与理由只在这些文件里：

- [proposal.md](../../openspec/changes/ite-tour-space-integration/proposal.md)
- [design.md](../../openspec/changes/ite-tour-space-integration/design.md) — 尤其 D1 zip Strip/Preserve、D2 体积默认开、D3 会话只注入、D4 层级、D5 场景、D6 假扫位姿 1.5 m、D8 先修获取再接宿主、**D9 编辑器 ≠ 真机**、D10 从 MRCore 摘掉 ITE Host
- [follow-ups.md](../../openspec/changes/ite-tour-space-integration/follow-ups.md) — 8.2–8.4 与 Play 里记下但不改的行为
- [tasks.md](../../openspec/changes/ite-tour-space-integration/tasks.md) — 已全部勾选，含验收笔记

操作红线（沿用本仓库 CLAUDE / 用户约定）：

- **不要用 Unity MCP**。驱动 Editor 用 `unity command … --project-path /Users/wwj/Desktop/unity/MR_Base`，权限 `all`。
- 语言：简体中文。
- `commitMode` 曾为 `task`；收尾提交可以一次收规划产物，消息仍写 `task(ite-tour-space-integration): …`，**禁止** `git add -A`。
- `capture_game_view` 的相对路径会落到 `Assets/` 下。证据应放 `openspec/changes/…/play-evidence/`，用绝对路径或事后 `mv`，不要把 `openspec` 当 Unity 资源导入。

Play 里已经踩过、写在 follow-ups 里、**不要当 bug 重开**的行为：

- 体积 `BoxCollider.isTrigger = false`，靠相机胶囊 trigger 仍能进 `TourVolumeTrigger`。
- kinematic 相机**瞬移**进体积常常不派发 `OnTriggerEnter`（WASD 连续走位没这个问题）。
- 扫码移动的是 `AnchorRoot`，整场一起锚定；不要用 space json 的原始世界坐标找体积。
- 假扫 `feedSeconds=0.35` 会在同一次投喂里走出 `Reanchor consumesSecondAnchor`，这是 7.7 路径。

## Open questions / blockers

无实现 blocker。收尾时只需人确认两件事：

1. 未跟踪的 proposal/design/specs/probe-report **一并提交**（推荐）。
2. 归档时 **sync delta spec 进** `openspec/specs/`（推荐）。主库里还没有这三条 capability spec。

三条后续 change **不要在收尾会话里 `/opsx:propose`**，除非用户明确改口。没有新的内容包之前，第 3 条尤其不要做。

## Relevant artifacts

- `openspec/changes/ite-tour-space-integration/` — 本 change 全文
- `Assets/Scenes/IteTourSpace.unity` — 验收场景（Editor Rig + Debug HUD + ITE Host）
- `Assets/Scenes/IteAcquireVerify.unity` — 临时，可删
- `Assets/Scripts/IteHost/` — `IteHostBootstrap`、`IteMarkerBridge`、`IteEditorFly`、`IteEditorFakeScan`、`IteEditorHud`
- `Packages/com.uality.ite-tour/` — 包；`TourDirector.Reanchor` 现有 `[ITE] Reanchor …` 日志
- `Assets/Settings/ITE/IteRuntimeConfig.asset` — `sceneName=thirdDemo`
- 缓存：`~/Library/Application Support/响堂山/响堂山`（company/product 均为「响堂山」）；tour 版本键 `ite.tour.{id}.version`
- `docs/handoff/2026-08-04-ite-tour-package.md` — 更早的包迁移交接，**不要**当成当前进度

## Suggested next steps

### A. 先收尾这次（下一会话第一件事）

- [ ] 只 `git add` 上面列出的未跟踪规划产物（含 `specs/`、`probe-report.md`），提交。不要带 IceSpriteFx mat，不要带 `ite-render-smoke-scene` 删除。
- [ ] 删除 `Assets/Scenes/IteAcquireVerify.unity` 及其 `.meta`（若场景已打开，先切走）。
- [ ] `/opsx:archive` change `ite-tour-space-integration`；sync 三条 delta spec。
- [ ] 归档后确认 `openspec list` 不再把本 change 列为 active。

### B. 三条后续 change（只记账，以后再展开）

不要现在写 proposal。开的时候各自 `/opsx:propose`，并从 [follow-ups.md](../../openspec/changes/ite-tour-space-integration/follow-ups.md) 与 design D9 往下挖。建议 slug：

1. **`ite-tour-device-host`（优先）** — 真机接入  
   真实观测源注入 `IteHostBootstrap.AttachMarkerSession`（宿主仍不构造 session、不持有 `IMarkerObservationSource` 字段）；XR 相机替换 Editor Rig；`IteTourSpace` 产品版本进 build；290 MB 设备落盘与首启；编辑器绿 ≠ Quest/PICO 着色器/单通道立体。

2. **`ite-content-download-streaming`** — 下载形态  
   去掉 `DownloadHandlerBuffer` 全量入内存（151 MB zip 峰值约 300 MB）；空间场景包补版本校验（现在 `networkAvailable=true` 每次重下 358 KB 的 `thirdDemo.zip`）。

3. **`ite-unverified-ite-components`** — 等真实内容  
   `VideoPlane` / `PrimitiveModelRender` / `ApproximateTrigger` / `PlayAudioAction`。MUST NOT 造合成数据。`ApproximateTrigger` 近距离判定在包里本来就没实现。

产品 UI（扫码提示页、加载页、预览列表）仍是 Non-Goals，不在这三条里。

## Suggested skills

- `/opsx:archive` — **收尾的主技能**。change 名：`ite-tour-space-integration`。选 sync spec。
- `/opsx:handoff` — 本文件；不要再开一轮实现。
- `/opsx:propose` — **仅当用户要展开某条后续**时用，一次一条；先真机接入。
- `/opsx:apply` — 本 change 已 all_done，下一会话不要 apply，除非归档后的新 change。
- `.claude/skills/unity-cli/SKILL.md` — 若收尾时还要动 Editor（删场景、确认未进 build）。禁止 Unity MCP。
