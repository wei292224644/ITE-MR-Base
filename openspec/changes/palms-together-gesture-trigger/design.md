## Context

项目已有未入库草稿 `Assets/Scripts/Core/PalmsTogetherGesture.cs`（纯关节数学）与配套 EditMode 测试。手部数据统一走 `MRContext.Hands` → `XRHandSubsystem`。官方 `XRHandShape`/`XRHandPose` 适合单手指形+朝向；合十额外需要双手距离与同时性。用户要求两条路线都落地，运行时做 A/B 对比，再决定长期采用哪条。

约束：跨平台中立（禁止 `OVR*` / `PXR_*` / `XRCommonHandGestures`）；业务不进判定组件；核心场景常驻、业务 additive。

## Goals / Non-Goals

**Goals:**

- 双手合十成立 / 散开以边沿事件暴露，订阅方零业务耦合
- **双路径**：A = JointMath；B = HandPoseComposite（双 Pose + 腕距）
- 运行时并行求值；HUD 同时显示 A/B 命中；可切换事件驱动源
- 路径 A 判据可离机测
- 核心场景有可查找的触发器实例

**Non-Goals:**

- 接入圣物 / Marker / 玩法 UI 等任何业务订阅（诊断 HUD 除外）
- 翻手 / 全握等其它手势的统一门面（仅合十引入 Pose 用法作为 B 路径）
- 近场 / 朝向圣物等空间门控
- 新建 `MRBase.Interaction` 程序集（仍放 Core）
- 自动化「哪条更好」的评分系统（人工看 HUD / 真机感受）

## Decisions

### D1：双路径并行 + 可选事件源（A/B）

**决定**：

```
              ┌─ Path A JointMath ─────────┐
XRHandSubsystem┤                            ├→ 状态 A/B → HUD
              └─ Path B HandPoseComposite ─┘
                         │
              ActiveSource (A|B) → Performed / Released / IsHeld
```

- 路径 A：整理现有几何判据（腕 `rootPose`、掌心/指尖轴约定、中指 FullCurl、hold/迟滞）
- 路径 B：左/右 `XRHandPose`（Open/平掌 Shape + 相对对方腕 Transform 的朝向）AND 腕距 ≤ 阈值；hold/迟滞与 A 共用参数或平行一份可调副本
- 门面组件对外事件只跟 `ActiveSource`；两条 raw/held 状态都暴露给 HUD

**理由**：用户倾向官方 Pose 路线，同时认可先用纯数学验证；并行对比成本可控，避免过早赌一条。

**替代**：只做 A 或只做 B——否决，无法实证。

### D2：事件面 = C# `event Action`（Performed / Released）+ `IsHeld`

**决定**：不引入 UnityEvent、不引入全局静态总线。`IsHeld` 反映 **ActiveSource** 的保持态；另提供只读 `IsHeldA` / `IsHeldB`（或等价）供 HUD。

**理由**：可测；订阅方不感知双路径。全局总线留到 Interaction 门面再建。

### D3：姿态用 `rootPose`（腕）；路径 A 弯曲用中指 `FullCurl`

**决定**：不用可选 Palm 关节；路径 A 的 curl 取不到时按 0 放行。路径 B 的手指约束落在 Shape 资产里。

**理由**：`rootPose` 各 provider 必给；Palm 可靠度不一致。

### D4：挂载位置 = 核心场景，与 `MRContext` 同生命周期

**决定**：核心场景一个门面实例；路径 B 需要的左右手目标 Transform 从手部 tracking 根或 skeleton 解析（实现时写清），避免业务场景引用。

**理由**：手势是输入层能力，应常驻。

### D5：验收分层

**决定**：

- 路径 A：EditMode 几何用例为合并主门槛
- 路径 B：资产与复合逻辑尽量单测距离门；Pose `CheckConditions` 依赖关节更新时以 Play/真机 + HUD 对照验收
- 真机精调阈值不阻塞「双路径可跑 + HUD 可见」的交付

### D6：命中状态进既有 `DiagnosticsHud`

**决定**：HUD 至少显示：`Palms A=… B=… Active=…`（held 或 raw，实现选信息量足够的一种）。实例缺失时安全跳过。

**理由**：A/B 对比的主观察面就是设备内 HUD。

### D7：默认 ActiveSource = A

**决定**：开箱事件源为 JointMath；Inspector / 简易切换可改到 B。

**理由**：与既有测试及草稿行为对齐；对比不依赖默认事件源。若用户在假设确认中推翻，改默认即可。

## Risks / Trade-offs

- **[双手互遮导致误触/漏触]** → hold + releaseSlack；双路径阈值可分别调
- **[路径 B 依赖对方手 Transform]** → 手丢失时 Pose 目标失效，须与 tracked 判定一起失败，不能抛异常
- **[双路径阈值不可比]** → 尽量共用 `maxWristGap` / hold / slack；Shape 容差在资产侧单独调，并在 HUD/文档标明
- **[MRContext.Hands 启动晚]** → 每帧取 Hands 判空
- **[放在 Core 而非 Interaction]** → 迁移成本低，可用性优先
- **[维护两套实现]** → 明确是对照实验；选定胜者后可删败者（另开 change）

## Migration Plan

1. 整理路径 A + 测试入库
2. 新增路径 B 资产与复合检测
3. 门面并行求值 + ActiveSource
4. 核心场景挂载；HUD 显示 A/B
5. EditMode（A）+ Play 冒烟（A/B HUD）

回滚：移除场景组件与新增资产/脚本即可。

## Open Questions

见 proposal.md `## Open Assumptions` 中仍为 `[ASSUMED]` 的条目。
