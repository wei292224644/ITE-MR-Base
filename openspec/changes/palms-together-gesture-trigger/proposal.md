## Why

业务需要一个跨平台、与具体玩法解耦的「双手合十」语义触发器：识别成立后抛事件，由订阅方决定做什么。现有草稿 `PalmsTogetherGesture`（纯关节数学）已验证可行，但未入库、未挂场景。官方 `XRHandShape`/`XRHandPose` 路线更贴长期门面，但合十还需双手距离复合。本变更**同时实现两条路径**，运行时并行对比命中效果，用 HUD 做 A/B 观察；对外仍只暴露一套中立事件。

## What Changes

- 落地双手合十触发器，对外输出 `Performed` / `Released` / `IsHeld`（由当前选中的策略驱动）
- **路径 A（JointMath）**：现有纯关节数学（腕距、掌心相对、指尖同向、中指 curl + hold/迟滞）
- **路径 B（HandPoseComposite）**：左右各一份平掌 `XRHandShape`/`XRHandPose`（朝向对方）+ 腕距复合条件 + 同一套 hold/迟滞
- 运行时两条路径可并行求值；诊断 HUD **同时显示 A/B 命中状态**，并可切换「事件由哪条路径驱动」
- 数据源仅用 `XRHandSubsystem`（经 `MRContext.Hands`），不接厂商手势 API、不接业务逻辑
- EditMode 测试覆盖路径 A 几何判据；路径 B 在可测范围内补复合条件/距离门（资产 CheckConditions 依赖运行时关节事件时以集成/真机对照为主）
- 在核心场景挂载触发器实例
- **不做**：业务订阅（圣物苏醒等）、翻手/全握统一门面、近场（朝向圣物）门槛、全局事件总线

## Capabilities

### New Capabilities

- `palms-together-gesture`: 双手合十作为中立语义触发器的识别条件、双路径 A/B、事件契约与运行时可用性

### Modified Capabilities

- （无）主库 `openspec/specs/` 尚无对应能力；本变更为独立新能力，不修改 `mr-core-scene-cross-platform` 内进行中的 `hand-interaction` delta

## Impact

- 代码：`PalmsTogetherGesture`（门面/选路）、路径 A 判据、路径 B Pose 复合、相关 EditMode 测试、`DiagnosticsHud`（A/B 双行）、必要的 `XRHandShape`/`XRHandPose` 资产
- 运行时：依赖 `MRContext.Hands`；核心场景挂载；HUD 可见 A/B 命中与当前事件源
- 依赖：`com.unity.xr.hands`（FingerShape + Gestures 资产 API）
- 非目标：`SacredRelic*`、Marker、全量手势门面、`MRBase.Interaction` 程序集拆分

## Open Assumptions

- [DECIDED] 本变更交付触发器 + 事件 + 核心场景挂载 + 测试 + HUD 命中状态显示；不接任何业务订阅。HUD 仅显示是否命中（诊断），不算业务接线。（用户确认 A + HUD 补充）
- [DECIDED] 同时实现路径 A（纯关节数学）与路径 B（双 HandPose + 距离复合）；运行时并行对比，HUD 显示两边命中，事件源可切换。用户倾向 B，但要求实证 A/B。（用户确认）
- [DECIDED] 组件挂在 `MRCore.unity`（与 `MRContext` 同场景常驻），业务场景日后自行查找并订阅；本变更不提供全局静态事件总线。（用户确认）
- [DECIDED] 默认调参沿用草稿（`maxWristGap=0.10`、`holdSeconds=0.3`、`releaseSlack=1.4` 等）；真机精调不阻塞本变更验收，EditMode（路径 A）+ 双路径可跑/HUD 可见即主验收路径。（用户确认 A）
- [DECIDED] 能力名使用独立 `palms-together-gesture`，不并入进行中的 `hand-interaction` change，避免与 M1 捏合/触碰契约纠缠。（用户确认 A）
- [DECIDED] 默认事件源先选路径 A（JointMath），便于与既有测试对齐；A/B 对比靠 HUD。（用户确认 A）
- [DECIDED] 真机对比后选定路径 B（HandPoseComposite）为正式事件源；默认 `ActiveSource = HandPoseComposite`。路径 A 暂留作 HUD 对照，删除另议。（用户确认）
