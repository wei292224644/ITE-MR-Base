# Handoff: PICO 扫码定位有偏移（gizmo 不在码中心）＋ marker-rescan 收尾

> Generated: 2026-09-24 17:51 CST
> Next session focus: PICO 的定位有一点偏移，截图看 gizmo 的原点没有落在码的正中心。先找根因，再谈修法。

## Goal

1. **主问题**：PICO 上扫码定位后，`AnchorRoot` 下的 `AxisGizmo` 原点（三轴交点）没有落在打印码的正中心。要找出偏移来自哪一环并修掉。
2. **收尾**：marker-rescan 已完成，PICO 真机验证已通过；还剩 Quest 真机验证，以及本地提交要不要 push。

## Current state

### PICO 定位偏移：只截过一帧，还没开始查

- 头显上装的是 `4136f50` 的 PICO 包（marker-rescan 全部改动），应用在跑（`pidof com.uality.xiangtangshan`）。
- 17:51 前后截了一帧：`<scratchpad>/pico_gizmo.png`，4320×2160，左右眼并排；局部放大图是 `<scratchpad>/pico_gizmo_crop.png`，取的是全分辨率的 x 700–1600、y 800–1700。
  - scratchpad 是 `/private/tmp/claude-501/-Users-wwj-Desktop-unity-MR-Base/2b542251-e8dd-46c9-8804-e8b82af2adca/scratchpad/`，随时可能被清掉。需要时按下面「命令」一节重截。
- 这一帧（左眼）能看到：
  - 桌上的打印码是 `ujf5bo31_frb`，AprilTag（tagStandard41h12），前面挡着场景菜单和 Tour 名字的大字标签；
  - gizmo 原点在码黑色图案中心的**右侧约 120 像素（全分辨率）**，高度大致相同；
  - 码的黑色图案宽约 420 像素，所以偏移约为图案宽度的 0.3，**粗估 3–4 cm，方向沿红轴（X，画面右侧）**；
  - 右眼被文字挡住，看不清码。
- **这只是一帧，还不能下结论。** 截图是合成器的输出：透视层和应用渲染层在截图里对不对得上，未必等于人眼看到的效果。好在用户亲眼看到了偏移，所以偏移本身是真的。

### marker-rescan：已完成，PICO 已验证

- spec：`docs/superpowers/specs/2026-09-24-marker-rescan-design.md`（D1–D8）。
- 计划：`docs/superpowers/plans/2026-09-24-marker-rescan.md`，三个任务都已完成。
- 提交在本地 `master`，`766d7e1..4136f50`，**没有 push**；`master` 比 `origin/master` 领先 27 个提交，其中也包括前面 ite-current-tour 那一批。
- EditMode 测试：`Uality.IteTour.Tests` 334/334、`MRBase.Ite.Host.Tests` 42/42、`MRBase.Localization.Tests` 45/45。
- 终审（新开的审查者）：
  - Critical 1（冷启动时提示在加载期间就可见，导致扫不上）按用户选的方案 B 修了，即 spec D8 和 `ScanPromptGate`；
  - Important（丢失、放行没有日志）也修了；
  - 4 个 Minor 暂缓，见下面「Open questions」。
- PICO 真机日志（17:44–17:46）确认了：
  - 冷启动时，加载完成的同一刻补发提示并放行；
  - 一直盯着码不会自动 Reanchor；
  - 每次 Reanchor 之前都先有「标记丢失」；
  - 4 次摘下再戴上，都在 0.4–1.6 秒内扫上。
  - 没测到的：normal Tour 的两项（日志里只出现过 `ujf5bo31_frb`），以及休眠。

## Key decisions

- marker-rescan 的全部取舍见 spec D1–D8，**不要重新讨论**：
  - 每次出现只提交一次；
  - 丢失时长全局 3 秒，即重扫门槛；
  - 扫码提示可见时放行；
  - 在播时扫当前 Tour 的码一律 Reanchor；
  - 删除二次锚定许可；
  - 同帧落选的码重新判稳；
  - 加载完成前不对外发提示。
- 标记的轴约定和 ITE 的内容锚点换算（`MarkerFrame.ToContentAnchor`，`Euler(270,180,0)`，**只做旋转**）已在真机上验证过，见 `.claude/CLAUDE.md` 的 Localization 一节。
- `PlatformOffsetConfig` 两端都是 identity（`Assets/Settings/ITE/PlatformOffsetConfig.asset`）。它只用来补「贴纸和锚点之间的物理偏移」。**不要拿它去掩盖求解误差**，除非查明偏移确实是物理上的、而且固定不变。
- 用户规则：改平台行为前，先在对话里给出选项和推荐，由用户拍板；改业务规则要先写 spec。「打包」只在用户说的时候做，一次只打一轮。

## Open questions / blockers

### 偏移来自哪一环？以下假设都没验证过

PICO 的位姿链是这样的：相机帧和 `frame.pose` → AprilTag 检测 → `PlanarPoseSolver`（单应，相机系）→ `PicoEnterpriseCameraPose.ComposeWorld`（换到世界系）→ 防抖平滑 → 桥接加偏移（identity）→ `IteRuntime.SubmitMarkerScan` → `MarkerFrame.ToContentAnchor`（只转不移）。

1. **相机外参没有乘进去**。`PicoFiducialObservationSource.cs:253` 写着「官方样例只打印外参，不乘进 FrameTarget（design D13）」。如果 `frame.pose` 是头部或设备中心的位姿，而不是左 RGB 相机的光心，结果就会差一个相机相对头部的平移，一般是几厘米，和截图的量级吻合。启动日志里有一行 `[PicoFiducialObservationSource] extrinsics L=...`，先读出这个平移量来对比。
2. **透视画面本身和真实世界没对齐**。PICO 的透视画面按某个假设深度重投影，近处（桌面，不到 1 米）可能错位。**这一条和手柄错位的交接文档（`docs/handoff/2026-09-24-pico-controller-pose-mismatch.md`）可能是同一个根因**：手柄由 PICO 运行时追踪，和我们的求解链无关，它也对不上。如果手柄和 gizmo 偏移的方向、大小相近，那就是显示或透视的问题，不是标记求解的问题。**建议先做这个对照。**
3. **内参和分辨率对不上**。内参按 `GetCameraParametersNewfor4U(1280, 960)` 取，如果实际出帧有裁剪或缩放，主点（cx、cy）就会偏。表现是：码在画面中心附近偏得少、在画面边缘偏得多。
4. **码的尺寸不对**。`tagSizeMeters = 0.0889`（检测四边形的边长）。尺寸错了，距离就会按比例错，斜着看时会表现成横向偏移。量一下打印出来的实际尺寸。
5. **帧位姿锁存的时机**：`latestFramePose` 和缓冲区快照是不是同一帧。头动的时候偏移会变大，静止时应该没有偏移。
- 区分这几种的办法：请用户把菜单关掉，站着不动，从正面、左侧、右侧、近处、远处各截一张；再各截一张手柄对比。
  - **偏移在码的坐标系里固定不变** → 求解或尺寸的问题（假设 3、4）；
  - **偏移随视角变化，但在头部坐标系里固定** → 外参或透视的问题（假设 1、2）。

### 其他未决

- **Quest 真机验证 marker-rescan**（spec §8）。最要紧的一项：移开视线 3 秒以上后，日志里要出现「[ITE Host] 标记丢失」。如果不出现，说明码离开视野后 MRUK 仍一直报在追踪，Quest 上就无法重扫。
- 本地 27 个提交要不要 push、怎么 push，用户还没决定。
- marker-rescan 暂缓的 4 个 Minor：
  - 同一帧里「扫码被接受」和「RequireScan」同时发生时，放行会丢；
  - `SubmitMarkerScan` 的公开文档没写「一次调用就是一次有意的扫描」；
  - `lostAfterSeconds` 没有下限；
  - 「提示可见时 `Rearm`」这处接线没有自动化测试。
- PICO 手柄错位：`docs/handoff/2026-09-24-pico-controller-pose-mismatch.md`，还没开始查，而且这个文件还没提交。
- 区域门禁 spec 的 D5 是暂停中的话题，**不要追问**。

## Relevant artifacts

- `Assets/Scripts/Localization/Native/PicoFiducialObservationSource.cs`：取流、内参（约 240–260 行）、外参日志（253 行）、帧位姿锁存（约 290 行）、`TrySolveWorldPose`（约 440 行）。
- `Assets/Scripts/Localization/PlanarPoseSolver.cs`：单应求位姿，`ToUnityCameraSpace`（CLAUDE.md 里的 D30）。
- `Assets/Scripts/Localization/PicoEnterpriseCameraPose.cs`：`ComposeWorld`，把相机系的位姿换到 Unity 追踪系（D13/D14，见提交 `7db60d3`）。
- `Assets/Scripts/Localization/Tests/EditMode/PlanarPoseSolverTests.cs`、`PicoEnterpriseCameraPoseTests.cs`：已有的求解测试，改求解前先读。
- `Assets/Scripts/Localization/Native/AxisGizmo.cs`：`IteTour.unity` 里 `AnchorRoot` 下的 gizmo（`authoringHanded`），就是截图里的三轴。
- `Packages/com.uality.ite-tour/Runtime/Core/MarkerFrame.cs`：`ToContentAnchor`，只转不移。
- `Assets/Settings/ITE/PlatformOffsetConfig.asset`：两端都是 identity。
- `Assets/Scenes/MarkerHookTest.unity` 和 `MarkerHookTestRig`：探针场景，直接画出契约坐标系（不经过 ITE），可以用来把问题隔离到求解链。打包菜单是 `MRBase/Build/Marker Hook Test/...`，**打包前要用户同意**。
- `docs/handoff/2026-09-24-pico-controller-pose-mismatch.md`：手柄错位，可能和本问题同一个根因。
- `.claude/CLAUDE.md`：标记轴约定、打包、测试、`unity` CLI 的约定。

## 命令（设备与日志）

- adb：`/Applications/Unity/Hub/Editor/6000.4.4f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb`
- PICO 序列号 `PA9410MGL5140677G`（A9210）；Quest 3 序列号 `2G0YC1ZF7Z0SC3`。
- 截图：`adb -s PA9410MGL5140677G exec-out screencap -p > <scratchpad>/pico.png`
  - 缩小：`sips -Z 1600 in.png --out out.png`；
  - 局部裁剪：`sips -c <h> <w> --cropOffset <y> <x> in.png --out out.png`（这台机器没有 PIL）。
- 日志：`adb -s <serial> logcat -d -v time -s Unity`。
  - 求解相关的行以 `[PicoFiducialObservationSource]` 开头，包括 intrinsics、extrinsics 和每 30 次的检出率；
  - 扫码相关的行是 `[ITE] 扫码 … 位姿 pos=… rot=…`。
- 打包：`unity command eval --project-path . 'UnityEditor.EditorApplication.ExecuteMenuItem("MRBase/Build/Queue Pico"); return "queued";'`。**只有用户说「打包」才做**，打包时只接目标设备。
- 编译和测试：自己触发（`unity command recompile` + `recompile_status` 轮询，然后 `run_tests`），**不要每轮都请用户按 Cmd+R**（见 memory `unity-pipeline-server-port-bind-failure`）。就绪检查用 `unity command editor_status --project-path .`。

## Suggested next steps

- [ ] 先读启动日志里的 `extrinsics L=` 和 `intrinsics` 两行，估算左 RGB 相机相对头部的平移量，看是否和 3–4 cm 的偏移吻合（假设 1）。
- [ ] 请用户关掉场景菜单、站着不动，截正面、左侧、右侧、近处、远处各一张，再截一张手柄；判断偏移是固定在码的坐标系里，还是固定在头部坐标系里，并和手柄的错位对照（假设 2）。
- [ ] 用 superpowers:systematic-debugging 走完 Phase 1，确认是哪一环；修法如果改到平台或求解行为，先在对话里给出选项和推荐。
- [ ] 需要隔离时，提议打 MarkerHookTest 探针包（等用户同意）。
- [ ] 顺带问用户：Quest 上的 marker-rescan 验证什么时候做；本地提交要不要 push。

## Suggested skills

- `superpowers:systematic-debugging`：偏移是 bug，先确认根因，再提修法。
- `unity-cli`：在用户已打开的 Editor 里查场景、组件、配置。不要开 headless，不要开第二个 Unity 进程。
- `superpowers:verification-before-completion`：声称修好之前，必须拿到真机截图（多个视角）作为证据。
- `superpowers:brainstorming`：如果修法会改变求解或平台行为，走 spec → plan 的流程。
- 注意：`openspec/` 已被用户删除，**不要用** `/opsx:propose`、`/opsx:apply` 这类依赖 `openspec/` 的命令。spec 和计划写到 `docs/superpowers/` 下。
