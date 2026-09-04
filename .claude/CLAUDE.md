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

**Build** — always through `BuildScript` (`Assets/Scripts/Editor/BuildScript.cs`), never Unity's own Build Settings dialog (it skips loader/plugin setup and produces a black-screen or crashing package):

- Editor menu: `MRBase/Build/Quest`, `MRBase/Build/Pico`, `MRBase/Build/Dry Run Quest|Pico` (validates config, no packaging), `MRBase/Build/Marker Probe/*`, `MRBase/Build/Gsplat Bench/Quest`.
- Headless, both targets: `Tools/build-both.sh` (or `Tools/build-both.sh Quest` / `Pico` for one). Must be invoked as **two separate Unity processes** — switching Build Profile changes scripting defines and triggers a script recompile that would kill an in-progress build, so there is no single `BuildBoth()` entry point.
- One-off headless build: `Unity -batchmode -quit -nographics -projectPath . -executeMethod BuildScript.BuildQuest` (swap the method name for any `[MenuItem]` in `BuildScript.cs`).
- `UNITY=/path/to/Unity` overrides the Unity executable used by `build-both.sh`.

**Tests** — standard Unity Test Framework, EditMode only (see `Assets/Tests/EditMode/*`, one asmdef per module under test):

- Editor: Window > General > Test Runner.
- Headless: `Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml -logFile -`.
- Add `-assemblyNames MRBase.Core.Tests` (etc.) to run a single module's tests.

**openspec** — feature work in this repo is spec-driven (`openspec/` — `changes/` for in-flight initiatives, `specs/` for landed capability specs). Check `openspec/changes/<name>/design.md` for the numbered-decision log of an initiative before touching code it covers.

## Architecture

**Platform split is scripting-define-based, not scene-based.** `MRBASE_QUEST` / `MRBASE_PICO` come from the active Build Profile (`Assets/Settings/Build Profiles/{Quest,PICO}.asset`), set by `BuildScript`, not by hand in XR Plug-in Management. All per-platform branching funnels through `PlatformRuntime` (`Assets/Scripts/Platform/`) — do not add new `#if MRBASE_*` elsewhere; it was deliberately pulled out of scenes because per-scene platform objects don't scale with scene count.

**One persistent core scene, everything else additive.** `MRCore.unity` holds the XR rig and never unloads; `MRSceneDirector` (`Assets/Scripts/Core/MRSceneDirector.cs`) switches content scenes with `LoadSceneMode.Additive` (load new → set active → unload old). Never `LoadSceneMode.Single` — on Quest this silently kills passthrough (every other signal — ARSession state, camera clear flags, frame rate — still reads healthy) because it takes the core assembly down with it. Every build (including probe builds) boots through `MRCore` first; probes get their content scene additively loaded on top rather than having their own bootstrap path — a project decision, don't reintroduce a second startup path.

**Singletons**: `StaticInstance<T>` (`Assets/Scripts/Common/`) — a duplicate instance self-destructs rather than replacing the existing one (replacing it leaves a Unity "fake null" behind, which is worse to debug). `MRContext` (lookup only, e.g. `Camera`, `Origin`, `Hands`) and `MRSceneDirector` are the two long-lived singletons; `MRBootstrap` is a readiness gate that runs once and surfaces failures visibly (XR loader missing, no XR Origin, hand subsystem not up) — this project's failure modes are otherwise silent (black screen, no hands, no log), so don't add another silent-failure path here.

**Module layout** (each is its own asmdef under `Assets/Scripts/`, several with a matching `*.Tests` asmdef under `Assets/Tests/EditMode/`):

- `Common` — dependency-free helpers (`FetchUtils`, `FileUtils`, `StaticInstance<T>`).
- `Core` — bootstrap, scene director, XR context/anchor, gesture input (`PalmsTogetherGesture*`).
- `Platform` — the single cross-platform switch point (see above).
- `Localization` — marker/fiducial tracking: `IMarkerTrackingProvider` abstracts Quest vs. PICO marker sources (`Localization/Native/{Quest,Pico}MarkerProvider.cs`), feeding `AnchorRegistry`/`MarkerAnchorService`. Current tracking pipeline is AprilTag-based (native camera stream + `AprilTagDetectorCore`) after native ArUco/QR extrinsic pose approaches were tried and dropped — see recent git history and `openspec/changes/unified-marker-tracking-contract/design.md` before changing this path.
- `Transitions` — MR↔VR visual transition effects (ground-up reveal, ice-sprite teleport).
- `SacredRelic` (+ `SacredRelic/Editor`) — a specific demo feature (fracture/dust VFX narrative), largely self-contained.
- `Diagnostics` — in-headset HUD.
- `IteHost` — adapter layer for embedding this project inside the separate "Ite Tour" host app; keep host-specific event-bus shapes out of the other modules and confined here.
- `GsplatBench` — standalone Gaussian-splat perf-measurement rig (`BuildGsplatBenchQuest`). Deliberately does **not** boot through `MRCore`/the normal scene flow — it must measure renderer cost, not core-assembly overhead — and is scene-scoped and removable as a unit.
- `Editor` (`MRBase.Build.Editor`) — `BuildScript` only.

**Build gotchas the script guards against** (see comments in `BuildScript.cs` for the "why"): a project-wide audio Spatializer plugin setting silently makes one platform's build wrong; PICO requires `PXR_Settings` registered in `EditorBuildSettings` config objects or ~20 manifest entries silently fail to write with a "successful" build; both platforms' Android native plugins must be build-scoped or Gradle fails on duplicate `.so` names.

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
