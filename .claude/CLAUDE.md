# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Hindsight memory

This repo has a Hindsight coding-agent memory bank (`coding-agent::MR_Base`). Reach for it before re-deriving project history, past decisions, or conventions from git/code:

- `hindsight_search_knowledge_pages(query)` — first stop for project questions (components, conventions, past decisions, initiatives). Credit results with `> 🧠 From Hindsight memory (<page>): ...`.
- `hindsight_list_knowledge_pages` / `hindsight_read_knowledge_page` — read a page in full; follow `[[page:<id>]]` links.
- `hindsight_reflect(query)` — deep synthesis over the whole bank for "why" questions and exact decided values; slower, use deliberately.
- `hindsight_capture_initiative(title, summary)` — right after a plan is agreed, before writing code, for a new feature/initiative (skip for bug fixes/chores). Call again with `relates_to_page_id` when the plan materially changes.
- `hindsight_ingest_document(title, content)` — save an external document or durable findings (not the current conversation — that's captured automatically at session end).
- `hindsight_diagnose` / `hindsight_sync_status` — when memory/reflect/tools seem broken or stale: reports resolved bank, config, and whether seeding/deepen/pages are caught up.
- If a memory turns out wrong or stale, fix it: `hindsight_ingest_document` titled `Correction: <topic>` stating what memory claimed, what's true now, and the evidence — never just silently ignore it.

<!-- CODEGRAPH_START -->
## CodeGraph

In repositories indexed by CodeGraph (a `.codegraph/` directory exists at the repo root), reach for it BEFORE grep/find or reading files when you need to understand or locate code:

- **MCP tool** (when available): `codegraph_explore` answers most code questions in one call — the relevant symbols' verbatim source plus the call paths between them, including dynamic-dispatch hops grep can't follow. Name a file or symbol in the query to read its current line-numbered source. If it's listed but deferred, load it by name via tool search.
- **Shell** (always works): `codegraph explore "<symbol names or question>"` prints the same output.

If there is no `.codegraph/` directory, skip CodeGraph entirely — indexing is the user's decision.
<!-- CODEGRAPH_END -->

## Project

Unity 6 (6000.4.4f1) MR/VR project targeting two headsets from one codebase: Meta Quest (AR Foundation / OpenXR) and PICO (PXR SDK). `Packages/wu.yize.gsplat` is a git submodule (separate repo `gsplat-unity`) providing a custom 3D Gaussian Splatting renderer.

## Commands

**Driving Unity — always `unity` CLI + the `unity-cli` skill, always inside the user's open Editor.** Every Unity operation (build, tests, scene/asset edits, inspecting Editor state) goes through the `unity` CLI against the user's running Editor, following `.claude/skills/unity-cli/SKILL.md`. Do **not** use a Unity MCP server.

**Builds run in that Editor — never headless, never a cloned project, never a background task.** A second Unity process competes for the machine's memory (it has been OOM-killed mid-session) and builds a copy of the project rather than what the Editor has, so its result proves nothing about the user's project.

- `unity status --project-path <repo>` → the Editor must be `ready`. If it won't connect, check Safe Mode (`unity pipeline list`) before anything else.
- Inspect / act: `unity command eval --project-path <repo> '<C#>'` (list everything with `unity command`).
- Build: queue a `BuildScript` menu item from inside the Editor, e.g. `unity command eval --project-path <repo> 'UnityEditor.EditorApplication.ExecuteMenuItem("MRBase/Build/Queue Quest"); return "queued";'` — `Queue*` defers the build until the CLI call returns, then auto-installs and launches on device via adb once the build succeeds. Follow progress with `unity command console` / `get_console_logs` and wait for `[BuildScript] 构建成功` in `~/Library/Logs/Unity/Editor.log`.
- The headless recipes below are only for when **no** Editor is open on this project.

**Build** — always through `BuildScript` (`Assets/Scripts/Editor/BuildScript.cs`), never Unity's own Build Settings dialog (it skips loader/plugin setup and produces a black-screen or crashing package):

- Editor menu: `MRBase/Build/Quest`, `MRBase/Build/Pico` (both auto-install + launch via adb on success), `MRBase/Build/Queue Quest|Pico` (queued variants), `MRBase/Build/Dry Run Quest|Pico` (validates config, no packaging).
- Headless, both targets: `Tools/build-both.sh` (or `Tools/build-both.sh Quest` / `Pico` for one). Must be invoked as **two separate Unity processes** — switching Build Profile changes scripting defines and triggers a script recompile that would kill an in-progress build, so there is no single `BuildBoth()` entry point.
- One-off headless build: `Unity -batchmode -quit -nographics -projectPath . -executeMethod BuildScript.BuildQuest` (swap the method name for any `[MenuItem]` in `BuildScript.cs`).
- `UNITY=/path/to/Unity` overrides the Unity executable used by `build-both.sh`.

**Tests** — standard Unity Test Framework, EditMode only (see 各模块 `<Module>/Tests/EditMode/*`, one asmdef per module under test):

- Editor: Window > General > Test Runner.
- Headless: `Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml -logFile -`.
- Add `-assemblyNames MRBase.Core.Tests` (etc.) to run a single module's tests.

**openspec** — feature work in this repo is spec-driven (`openspec/` — `changes/` for in-flight initiatives, `changes/archive/` for landed ones, `specs/` for capability specs). Check that change's `design.md` for the numbered-decision log before touching code it covers.

## Architecture

**Platform split is scripting-define-based, not scene-based.** `MRBASE_QUEST` / `MRBASE_PICO` come from the active Build Profile (`Assets/Settings/Build Profiles/{Quest,PICO}.asset`), set by `BuildScript`, not by hand in XR Plug-in Management. All per-platform branching funnels through `PlatformRuntime` (`Assets/Scripts/Platform/`) — do not add new `#if MRBASE_*` elsewhere; it was deliberately pulled out of scenes because per-scene platform objects don't scale with scene count.

**One persistent core scene, everything else additive.** `MRCore.unity` holds the XR rig and never unloads; `MRSceneDirector` (`Assets/Scripts/Core/MRSceneDirector.cs`) switches content scenes with `LoadSceneMode.Additive` (load new → set active → unload old). Never `LoadSceneMode.Single` — on Quest this silently kills passthrough (every other signal — ARSession state, camera clear flags, frame rate — still reads healthy) because it takes the core assembly down with it. Every build (including probe builds) boots through `MRCore` first; probes get their content scene additively loaded on top rather than having their own bootstrap path — a project decision, don't reintroduce a second startup path.

**Singletons**: `StaticInstance<T>` (`Assets/Scripts/Common/`) — a duplicate instance self-destructs rather than replacing the existing one (replacing it leaves a Unity "fake null" behind, which is worse to debug). `MRContext` (lookup only, e.g. `Camera`, `Origin`, `Hands`) and `MRSceneDirector` are the two long-lived singletons; `MRBootstrap` is a readiness gate that runs once and surfaces failures visibly (XR loader missing, no XR Origin, hand subsystem not up) — this project's failure modes are otherwise silent (black screen, no hands, no log), so don't add another silent-failure path here.

**Module layout** (each is its own asmdef under `Assets/Scripts/`, several with a matching `*.Tests` asmdef under 各自模块的 `Tests/EditMode/`):

- `Common` — dependency-free helpers (`StaticInstance<T>`).
- `Core` — bootstrap, scene director, XR context/anchor, gesture input (`PalmsTogetherGesture*`).
- `Platform` — the single cross-platform switch point (see above).
- `Localization` — marker/fiducial tracking. Platform sources implement `IMarkerObservationSource` only (`QuestObservationSource` via MRUK QRCode, `PicoFiducialObservationSource` via native camera + `AprilTagDetectorCore`). The sole event source is `MarkerTrackingSession` (`Open`/`Poll`/`Tick`, time-based lost hysteresis). Native PICO ArUco, self-rolled QR pose, `IMarkerTrackingProvider`, and `AnchorRegistry` were tried and dropped. `MarkerHookTest` is the in-headset probe for this path (build via `MRBase/Build/Marker Hook Test/Queue Quest`); its rig draws an RGB axis gizmo (X red / Y green / Z blue, each bar only on the + side) with X/Y/Z text labels. See `openspec/specs/unified-marker-tracking-contract/spec.md` and `openspec/changes/archive/2026-09-08-unified-marker-tracking-contract/design.md` before changing this path.
  - **Marker axis convention (verified in headset 2026-09-18, Quest).** The intended marker frame, as the ITE content author sees it looking at the printed code: **X → right edge of the print, Y → perpendicular to the print (out of the printed face), Z → bottom edge of the print.** This is a **right-handed** frame (ITE exports right-handed; `ConvertToLeftHanded` flips Z on import). In Unity the same frame is therefore **X → right edge, Y → out of the face, Z → *top* edge** — a plain rotation, no mirror. Never decompose `eulerAngles`, negate a component and rebuild with `Quaternion.Euler` — that jumps between equivalent Euler forms and made the axes jitter in the headset.
  - The marker contract (`MarkerObservation.Pose`) is, on **both** platforms (verified 2026-09-18): **X → left edge, Y → top edge, Z → out of the printed face**. Quest forwards MRUK unmodified; PICO gets there via `PlanarPoseSolver.ToUnityCameraSpace` (D30).
  - The conversion to the ITE content anchor is `Euler(270,180,0)`, done **once, inside the ITE package**: `MarkerFrame.ToContentAnchor`, applied in `IteRuntime.SubmitMarkerScan` (design D32 of `ite-tour-space-device`). `PlatformOffsetConfig` stays identity on both platforms — it's only for physical sticker-vs-anchor offsets. Don't mirror content (negative scale flips meshes/text) — the Z flip is already done by `ConvertToLeftHanded`.
  - `AxisGizmo` (`Localization/Native`) draws a frame in-headset. Under `AnchorRoot` in `IteTour.unity` it runs with `authoringHanded` (Z flipped back) so it shows the author's convention; `MarkerHookTestRig` draws the plain contract frame (so there, red = print **left** is correct).
- `Transitions` — MR↔VR visual transition effects (ground-up reveal, ice-sprite teleport).
- `SacredRelic` (+ `SacredRelic/Editor`) — a specific demo feature (fracture/dust VFX narrative), largely self-contained.
- `Diagnostics` — in-headset HUD.
- `IteHost` — adapter layer for embedding this project inside the separate "Ite Tour" host app; keep host-specific event-bus shapes out of the other modules and confined here.
- `GsplatBench` — standalone Gaussian-splat perf-measurement rig. Deliberately does **not** boot through `MRCore`/the normal scene flow — it must measure renderer cost, not core-assembly overhead — and is scene-scoped and removable as a unit. `BuildScript.cs` menu entry removed 2026-09-18.
- `Editor` (`MRBase.Build.Editor`) — `BuildScript` + `ManifestGuard`。旁边的
  `MRBase.Build.Editor.Tests` 装 `LayoutConventionTests`（目录约定的提交期守卫）。
  `IteSceneSetup.cs` 目前也在这里，但它是 ITE 的功能工具、不是构建 —— 属已知的
  文档级债务，机械校验判不出来，见目录与 asmdef 约定一节。

**目录与 asmdef 约定（feature-first）。** 新代码进 `Assets/_Project/Features/<Feature>/`，
一个功能一个文件夹，代码与资源同居：

```
Assets/_Project/Features/<Feature>/
  Runtime/       代码 + MRBase.<Feature>.asmdef
  Editor/        该功能的编辑器工具 + MRBase.<Feature>.Editor.asmdef
  Tests/EditMode/  该功能的测试 + MRBase.<Feature>.Tests.asmdef
  Art/ Prefabs/ Scenes/ Settings/   按需建，没有就不建
```

`Assets/_Project/Core/` 放跨功能骨架（`MRCore.unity`、`Common`、`Platform`、`Bootstrap`）。
`Assets/Scripts/` 是存量，原地不动、逐步迁出 —— **不要**为了"统一"去批量搬它。

五条条文：

1. **每个 feature 一个 asmdef，不许有裸 `.cs` 掉进 `Assembly-CSharp`。**
   `Assembly-CSharp` 自动引用所有 asmdef 程序集，反向不行，所以裸 `.cs` 是单向死路：
   任何 asmdef 里的代码都够不着它（`Assets/Scripts/Transitions/IceSpriteTeleport.cs:30`
   的注释就是这笔账）。
2. **Editor 代码只能在自己 feature 的 `Editor/` 下**，归属名字以 `.Editor` 结尾的 asmdef。
   `MRBase.Build.Editor` 只装 `BuildScript` + `ManifestGuard`。
3. **测试紧贴被测代码**，与它同一棵树。`Assets/Tests/` 已不存在，不要重建。
4. **`_Project/` 下不新建 `Resources/`。** 它无条件全量进包、不可剥离。
   工程级 `Assets/Resources/`（SDK 生成的 `PXR_*`/`OVR*`）在范围外，不管。
5. **场景归属 feature**，只有 `MRCore.unity` 这类跨功能骨架在 `_Project/Core/Scenes/`。

**适用范围是白名单**：只有 `Assets/_Project/**` 与 `Assets/Scripts/**` 受管辖，
其余 `Assets/*` 一律在外 —— `Oculus/`、`INab Studio/`、`Samples/`、`XR/`、`XRI/`、
`Plugins/`、`TextMesh Pro/`、`Resources/` 都不检，装新插件也不用回来改清单。
**范围外 ≠ 豁免**：豁免表登记的是"自己的、违规的、将来要还的"，第三方不是债。

条文 1、2 由 `Assets/Scripts/Editor/Tests/EditMode/LayoutConventionTests.cs` 机械执行
（`MRBase.Build.Editor.Tests`，与 `ManifestGuard` 同轴：一个守构建期，一个守提交期）。
判不出的部分不检：一个 `.mat` 该归哪个 feature、`IteSceneSetup.cs` 该归 ITE 还是归构建，
都需要人读代码 —— 硬编规则只会制造假阳性。这类是文档级债务，不进豁免表。
存量违规登记在 `Assets/Scripts/Editor/Tests/EditMode/layout-waivers.txt`，还完债就删行。

设计与七条编号决策见 `docs/superpowers/specs/2026-09-21-asset-layout-governance-design.md`。

**Build gotchas the script guards against** (see comments in `BuildScript.cs` for the "why"): a project-wide audio Spatializer plugin setting silently makes one platform's build wrong; PICO requires `PXR_Settings` registered in `EditorBuildSettings` config objects or ~20 manifest entries silently fail to write with a "successful" build; both platforms' Android native plugins must be build-scoped or Gradle fails on duplicate `.so` names.

**Quest: never ship a `Development` build.** On Horizon OS v207 (runtime 207.218.0) a Development build hangs inside XR startup — main thread blocked in `StartXRSDK`, no scene ever loads, the headset sits on the loading screen with no error. Dropping `AllowDebugging` alone does not fix it; the identical content as Release starts fine (verified 2026-09-11). Any Quest build or probe entry must therefore build Release — `Debug.Log` still reaches logcat.

## 决策原则：以架构最优为判据

技术决策按**结构是否正确**来判，不按**实现是否省事**来判。以架构师视角思考，不以"最短 diff"视角思考。

**在"改动最小"与"结构更正确"之间，选后者。** 用结构性理由论证选择，不用工作量论证。「照搬更快」不是理由；「照搬保住了可审阅性」才是理由，而且要能被更强的结构理由推翻。

**迁移既有代码的默认是行为等价**——让迁移与修 bug 保持可分离，这本身是结构理由。但下列情形下，重构优先于照搬：

- 行为依赖**隐式**因素：调用顺序、标志位被谁先清、`await` 会不会真的挂起
- 同一份代码因数据不同走出**不同语义**（而非不同结果）
- 跨平台会走出不同时序，且故障只能在真机上复现
- 信任边界缺输入校验、缺防止数据丢失的错误处理

**拒绝为省事而留下的形状**：把宿主事件总线的形状留在包里、为单一实现造接口、把一个决策藏在多个处理器的相互作用中、用"碰巧能跑"替代"明确规定"。

**每个偏离既有行为的决定，在 `design.md` 里记成一条编号决策**，写清：选了什么语义、替代方案是什么、为什么否决。偏离不记录等于没发生过。

**不确定时给选项 + 推荐，由人拍板。** 不要因为某条路更快就默认走它，也不要因为怕麻烦而不提更优解。
