# Handoff: PICO 手柄虚拟位置与真实手柄对不上（插入问题）＋ ITE 当前 Tour 真机收尾

> Generated: 2026-09-24 16:03 CST
> Next session focus: 先查 PICO 上手柄的虚拟位置为什么和真实手柄对不上（先取 PICO 截图看）；之后回到 PICO 上的 ITE 真机验证。

## Goal

1. **插入问题（优先）**：PICO 上，虚拟手柄（模型 / 射线）的位置和透视画面里真实手柄的位置对不上。要找到根因，并修好。
2. **收尾**：ite-current-tour（区域队列与当前 Tour）已在 Quest 真机上验证过，还剩 PICO 真机验证没做。

## Current state

**PICO 手柄问题：还没开始查。**
- 头显上装的是 `aa9b579` 的 PICO 包（`Builds/Pico/MR_Base.apk`，13:50 构建）。它是用 adb 手动装上并启动的：构建结束时 PICO 没接上，`BuildScript` 装机失败，只生成了 APK。
- 上一个会话在 16:01 截过一张 PICO 画面：`/private/tmp/claude-501/-Users-wwj-Desktop-unity-MR-Base/2b542251-e8dd-46c9-8804-e8b82af2adca/scratchpad/pico_shot.png`，4320×2160，左右眼并排。这个临时目录随时可能被清掉，需要时用下面「命令」一节重新截。
- 截图里看到的：
  - 透视画面中，右手拿着真实的 PICO 手柄；
  - 一条细白射线从左上方连到真实手柄顶端附近；
  - 左眼画面里有一条绿色射线，从画面左边缘水平射入；
  - 前面是场景选择菜单（BloomTest … IteTour），背景有大块紫、粉、绿色线框；
  - **单看这一帧，分辨不出虚拟手柄模型是否偏了**：画面里没看到和真实手柄分开的虚拟模型。
- 手柄相关配置的线索（还没细看）：
  - `MRCore.unity` 用的 XR rig 是 `Assets/Assets/Prefabs/Rig/XROrigin_Base.prefab`；
  - 它嵌套了 XRI 3.5.1 sample 的 `Assets/Samples/XR Interaction Toolkit/3.5.1/Hands Interaction Demo/Prefabs/XR Origin Hands (XR Rig).prefab`，里面有 Left Hand / Right Hand，手柄对象在更里一层嵌套的 XRI Starter Assets rig 里；
  - 平台分支统一放在 `PlatformRuntime`（`Assets/Scripts/Platform/`），见 `.claude/CLAUDE.md`。

**ite-current-tour：代码已完成，已在 Quest 真机验证，PICO 还没测。**
- 设计：`docs/superpowers/specs/2026-09-23-ite-current-tour-design.md`，决策 D1–D14。
- 计划：`docs/superpowers/plans/2026-09-23-ite-current-tour.md`，Task 1–9 全部完成。
- 提交在本地 `master`，**没有 push**：`310a4e1..aa9b579`（`git log 310a4e1^..aa9b579`）。
- EditMode 测试结果：`Uality.IteTour.Tests` 330/330、`MRBase.Ite.Host.Tests` 35/35、`MRBase.Build.Editor.Tests` 6/6。
- Quest 真机日志确认了以下几点：
  - 扫码后不被切走（问题 1）；
  - 同一区域计数会到 2，来源是 XR Origin 和 Main Camera 两个碰撞体（问题 2 的根因）；
  - 离开当前 Tour 的区域才切换到最后进入的区域；
  - 只认当前 Tour 的码；
  - 摘下眼镜时进入 Suspended；
  - 计数能回到 0。
- 另外修了 Quest 摘下再戴上后扫不了码的问题：`93b3e14`、`aa9b579`，只改了 `Assets/Scripts/Localization/Native/QuestObservationSource.cs`，已在真机验证：
  - 根因：49804c1 只在 `InputFocusAcquired` 时重建 MRUK QR 追踪器。会话重开后，`OVRPlugin.hasInputFocus` 一直是 false，这个事件不会来。
  - 现在的做法：摘下期间应用被暂停过（`OnApplicationPause(true)` 就说明会话停过），戴上时才重建一次。短暂摘下不重建，已经认出的码继续保留。
  - 验证结果：休眠后戴上 1.7 秒扫码生效；用户确认「没有什么问题了」。

## Key decisions

- ite-current-tour 的全部取舍（计数、队尾、在播优先、结算窗口、只认当前 Tour 的码、`alwaysDisplayed` 生命周期等）见 spec D1–D14，**不要重新讨论**。
- D13（2026-09-24，用户选方案 B）：`alwaysDisplayed` 可见即已建树。进入已定位时建树，离开时拆树。停用内容根会让 `VideoPlaneElement` 销毁视频播放器，不能用。
- D14：帧驱动在 `OnEnable` 启动 `WaitForFixedUpdate` 协程。
- Quest 扫码修复，用户选了方案 A（事件触发＋暂停标记），A 已经够用，**不做方案 C**（自定义 OpenXRFeature 监听会话）。
- 用户确认没感觉到「离开重叠区域时，某个 Tour 被激活约 45 毫秒又停掉」这个现象，视为**维持现状**，不加稳定等待。

## Open questions / blockers

- **PICO 手柄错位**：
  - 截一帧看不出错位有多大。需要让用户把手柄拿稳，连续截图或录屏；也可以在 Editor 里查 PICO 下手柄模型、射线的 TrackedPoseDriver / XRI 输入绑定。
  - 要查：用的是 grip 位姿还是 aim 位姿；手柄模型本身有没有偏移；PICO 的 OpenXR / PXR 手柄交互配置文件是否匹配；有没有被 `PlatformRuntime` 或 rig 缩放影响。
  - 还要确认这是新出现的问题，还是一直都有。可以比较 `git log` 中 XRI/PICO SDK 依赖的变更，比如 `5cb80a2`。
- **PICO 上的 ITE 验证还没做**，逐条对照 spec §10 真机验证清单。PICO 用 AprilTag 识别（`PicoFiducialObservationSource`），今天的 Quest 扫码修复对它不起作用，摘下再戴上后能否扫码要单独确认。
- 场景里有没有 `alwaysDisplayed` Tour 不确定：日志里从没出现过，所以 D13 还没在真机上验证过。
- 这批本地提交要不要 push、怎么 push（直接推 `master` 还是开分支提 PR），用户还没决定。
- `~/Library/Logs/Unity/Editor.log` 已经约 3 GB，一直在刷 `Unable to load font face for [PingFang SC]`（Game 视图渲染时）和 `Curl error 35`。可能拖慢 Editor，查不查由用户决定。
- 规则层面的观察，用户还没表态：同一张码（earyserh）的位置被 3–4 个区域覆盖，当前 Tour 经常被切到别的区域，这时扫这张码会被 D6 忽略；而防抖只在码重新出现、或位置变化超过 5 cm / 1° 时才会再次提交扫码。已经验证的部分见上一个会话的对照表；**改规则前必须先写 spec**。
- 暂缓的小问题：旧状态机 spec 的状态表、D6、D7、接口表还没标注「已被取代」；`TourDirector.cs:11`、`IteTourObject.cs:18` 的注释已经过时。
- 区域门禁 spec 的 D5 仍在「待确认」，**是暂停中的话题，不要追问**。

## Relevant artifacts

- `docs/superpowers/specs/2026-09-23-ite-current-tour-design.md`：当前 Tour 设计（D1–D14、§10 真机验证清单）
- `docs/superpowers/plans/2026-09-23-ite-current-tour.md`：实施计划（Task 1–9，含审查后补的部分）
- `docs/handoff/2026-09-23-ite-region-trigger-issues.md`：PICO 最初报的两个区域问题
- `Assets/Scripts/Localization/Native/QuestObservationSource.cs`：Quest QR 追踪重建逻辑（`93b3e14`、`aa9b579`）
- `Assets/Assets/Prefabs/Rig/XROrigin_Base.prefab`：MRCore 的 XR rig，手柄问题从这里查起
- `Assets/Scripts/Platform/`：`PlatformRuntime`，唯一允许按平台分支的地方
- `Assets/Scripts/Editor/BuildScript.cs`：打包；装机时不指定设备，装到当时唯一连着的那台
- `.claude/CLAUDE.md`：打包、测试、`unity` CLI 的约定

## 命令（设备与日志）

- adb：`/Applications/Unity/Hub/Editor/6000.4.4f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb`
- PICO 序列号 `PA9410MGL5140677G`（A9210）；Quest 3 序列号 `2G0YC1ZF7Z0SC3`。两台的日志缓存都已调成 16 MB（`logcat -G 16M`），重启后会恢复默认。
- 截图：`adb -s PA9410MGL5140677G exec-out screencap -p > <scratchpad>/pico.png`，看之前先用 `sips -Z 1600` 缩小。
- 日志：`adb -s <serial> logcat -d -v time -s Unity`。ITE 的行以 `[ITE]` 开头，Quest 扫码链路的行以 `[QuestObservationSource]`、`MRUK Shared` 开头。
- 打包：`unity command eval --project-path . 'UnityEditor.EditorApplication.ExecuteMenuItem("MRBase/Build/Queue Pico"); return "queued";'`，**只有用户说「打包」才做**。打包时只接目标设备。
- `unity` CLI 触发重编译后，常出现 Pipeline Server 绑不上端口、连接断开的情况（见 memory `unity-pipeline-server-port-bind-failure`）。改完代码请用户在 Editor 里按一次 `Cmd+R`，之后只跑 `run_tests` / `recompile_status`。

## Suggested next steps

- [ ] 连上 PICO，确认应用在运行（`pidof com.uality.xiangtangshan`）。请用户把手柄拿稳、放在视野中央，连续截 2–3 张图。对比透视画面里的真实手柄和虚拟模型、射线的起点，估计偏移的方向和大小。
- [ ] 顺着 `XROrigin_Base.prefab`，查清 PICO 下手柄模型、射线用的位姿来源（TrackedPoseDriver / XRI input action 的 grip 或 aim）和交互配置文件，找出根因。用 superpowers:systematic-debugging 走完 Phase 1 再提修法。
- [ ] 修法如果改到平台行为，先在对话里给出方案和推荐，由用户拍板；涉及业务规则的，先写 spec。
- [ ] 手柄问题处理完后，回到 PICO 上的 ITE 验证：对照 spec §10 逐条看，并单独确认摘下再戴上后能否扫 AprilTag。
- [ ] 问用户这批本地提交要不要 push、怎么 push。

## Suggested skills

- `superpowers:systematic-debugging`：手柄错位是个 bug，先找根因再修。
- `unity-cli`：在用户已打开的 Editor 里查 rig、prefab、XRI/PXR 配置（不要开 headless，不要开第二个 Unity 进程）。
- `superpowers:brainstorming`：如果修法会改变平台行为或业务规则，走完 spec → plan 的流程。
- `superpowers:verification-before-completion`：声称修好之前，必须拿到真机截图或日志作为证据。
- 注意：`openspec/` 已被用户删除，**不要用** `/opsx:propose`、`/opsx:apply` 这类依赖 `openspec/` 的命令。spec 和计划写到 `docs/superpowers/` 下。
