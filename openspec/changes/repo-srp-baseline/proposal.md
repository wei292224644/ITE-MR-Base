## Why

仓库里多数类的职责边界不清——一个类同时被多个互不相干的理由驱动修改，读代码读不出意图。
最典型的 `IteHostBootstrap`（350 行）同时对六类外部变化负责：场景装配契约、包事件签名、
PICO 佩戴通道、网络判定策略、会话注入时序、失败呈现。这不是单点问题：没有任何成文判据说明
"一个类该对几件事负责"，所以每次"顺手加个字段"都合规，六条轴是这样攒出来的，清掉一次三个月后照样长回来。

因此本次要解决的不是"拆某个类"，而是**缺判据**：立一条可执行、可证伪的 SRP 判据并卡在 change 流程上，
同时产出一份全仓审计清单，让后续每次重构有优先级可依。

## What Changes

- 新增 `openspec/constitution.md`，写入 **SRP 变更轴判据**：对一个类列出「哪一类外部变化会逼它改」，
  **≥2 条互不相干的轴 = 违反**。附五条豁免（Unity 生命周期不算轴；装配点可知两边但不可持有运行期状态；
  单文件、不新增类、不动 public 面的改动免轴清单；存量不倒查；已论证的汇聚点——见 design D11）。
- **BREAKING（流程层面）**：此后 `/opsx:propose` 产出的每个 `design.md` 必须含一段结构化的**变更轴清单**；
  新增 ≥2 轴的类必须记一条编号决策说明为什么可以。`/opsx:analyze` 会对缺失的清单报 CRITICAL 并 advisory-block。
  这会拦下一部分原本能直接 apply 的 change。
- 新增 `docs/architecture/srp-audit.md`：审计范围内 **114 个 .cs** 的逐类轴数记录、每条轴的 commit 证据、
  以及按「坐实轴数 × 消费者数」排出的重构优先级清单。这是**活文档**，后续 change 逐条勾掉并更新。
- **本 change 不修改任何产品代码。** 审计出的违反项各自另起 change。
- handoff `2026-09-20-ite-host-bootstrap-split.md` 的 **#7**（拆 `IteHostBootstrap`）被本 change 吸收为
  审计对象之一，不再是独立议题；它是否排第一由审计的 commit 证据决定，本 change 不预设。

## Capabilities

### New Capabilities
- `srp-axis-governance`: SRP 变更轴判据的定义、豁免边界、design 门槛的机械可检查形态、
  waiver 承载存量不倒查、以及审计清单的产出与维护要求。

### Modified Capabilities
<!-- 无。现有四条 spec（ite-content-acquisition / ite-tour-space-editor-harness /
     ite-tour-space-host / unified-marker-tracking-contract）都是运行时能力，本 change 不改其需求。 -->

## Impact

**新增文件**
- `openspec/constitution.md`（判据，长期有效，不随 change 归档）
- `docs/architecture/srp-audit.md`（活清单，不放 change 目录——change 一归档就整体进 `changes/archive/`，
  先例 `archive/2026-09-08-unified-marker-tracking-contract/`，清单沉进去即死）

**审计覆盖**（只读，≈114 个 .cs / 约 13600 行）
- `Assets/Scripts/` 九个模块：Localization(2498) / IteHost(1719) / Editor(970) / Core(893) /
  IceSpriteFx(396) / Transitions(310) / Diagnostics(246) / Platform(198) / Common(35)
- `Packages/com.uality.ite-tour/Runtime/` 六层：Core(2686) / Components(1755) / Internal(1109) /
  Data(526) / Convert(176) / Config(53)

**明确不覆盖**
- `Assets/Scripts/GsplatBench/`（1827 行）— CLAUDE.md 记明 removable as a unit，BuildScript 入口已于
  2026-09-18 移除；给待删代码做架构，报告和代码一起进垃圾桶
- `Assets/Scripts/SacredRelic/`（1615 行）— 用户定为不审
- `Packages/wu.yize.gsplat/` — git submodule（独立仓 `gsplat-unity`），改它要跨仓 PR
- `Assets/Tests/` — 测试的职责判据与产品代码不同（一个 fixture 服务多个用例本就正当）。
  但审计**记录**测试台子重复（例：`IteHostBootstrapTests.cs:84-126` 的 `HostFixture` 与
  `IteHostCameraResolutionTests.cs:43` 的 `BuildHost` 是同一台子写两遍）当作对应**产品类**违反的证据

**受影响的流程**
- `/opsx:propose`（design 多一段必填清单）、`/opsx:analyze`（新增一条 MUST 条款）
- 既有 in-flight change（`ite-tour-space-device` / `ite-scene-layout-convention` /
  `marker-anchor-axis-correction` / `xr-ui-interaction-unification`）不倒查

**不受影响**
- 运行时行为、构建流程、`389/389` EditMode 测试基线（本 change 不动代码，测试数不应变化）

## Open Assumptions

### 已在 propose 阶段验证掉的

- **`[已验证 — 推翻了原假设，改变了判据]`** probe 报告假设「`PlatformRuntime.cs` 判为一条轴」，
  用作判据的反向测试样本。深读后判出**三条**互不相干的轴：passthrough 开法变（`EnablePassthrough:85`）、
  系统重定位事件源变（`HookRecenter:147`，由 `d790dcd` 单独触发，**已坐实**）、MRUK 包行为变
  （`SilenceMetaGlobalHookOnPico:69`，注释记 2026-09-17 PICO 实测）；`_recenterHooked` 只服务第二条，
  三条不能合并。经用户确认，据此**增设豁免 5「已论证的汇聚点」**（design D11），并把反向测试样本
  换为 `Assets/Scripts/Common/StaticInstance.cs`。`PlatformRuntime` 命中豁免 5、不判违反，
  但三条轴仍须逐条登记。

- **`[已验证 — 结论改变了设计]`** probe 报告假设「`/opsx:analyze` 能拦住不符合项」。已读
  `.claude/skills/openspec-analyze-change/SKILL.md`：`analyze` 确实读 constitution
  （`openspec instructions analyze --change` 返回 `constitutionPresent` / `clauses[]` / `waivers[]`），
  CRITICAL 会 advisory-block。**但 `SKILL.md:37` 规定 `judgment without concrete evidence →
  downgrade to WARNING (never CRITICAL)`**——判断类条款拦不住。故 SRP 条款必须写成
  **structure criterion**（机械可检查：design.md 是否含该段、每个类是否一行），见 design D2。
  同时 `waivers[]`（`principle`/`reason`）正是「存量不倒查」的承载机制，见 design D4。

### 仍未确认的

以下为 AI 未经确认的假设，显式带进后续实施：

- **`[已验证]`** 第二遍候选数量：第一遍浅扫 **114/114** 完成（零遗漏核对通过），判出 **24 个候选**
  （≥2 候选轴，含已命中豁免 5 的 `PlatformRuntime`）。落在原估 **15–25** 区间内，第 3 组任务拆分无需重估。
- **`[已验证]`** 反向测试样本 `StaticInstance.cs`：已深读，判出**一条**轴
  （`BindInstanceForTesting` 与 `Awake` 共享 `_instance`，按合并规则并入）。D9 反向测试通过。
      （若它也 ≥2 轴，需再换样本；35 行的 helper 出错概率低，故未在 propose 阶段深读）
- **`[已验证]`** `IteHostBootstrap` 的六条轴已逐条取证：**3 条坐实**（场景装配契约 ← `IteSceneSetup`、
  会话注入 ← `IteEditorFakeScan`+`IteDeviceMarkerRig`、失败呈现 ← `IteHmdPanel`），
  **3 条 `[推测轴]`**（包事件转发 / PICO 佩戴通道 / 网络判定——三者全 private 无消费者，
  且 `df057ec`、`0b71471` 均非单独触发）。得分 3 × 6 = 18，**排第 3**，不是第一名
  （第一是 `IteTourObject` 4 × 15 = 60）。「不预设第一名」兑现。
- [ ] `[ASSUMED]` ITE 包内违反项的**修改**受 `ite-tour-space-device/design.md` D1–D35 约束，提 change 前需
      先读那条决策链。审计本身不受限 — 影响：后续包内 change 的前置条件
- **`[已确认]`** `SacredRelic/` 记为「**暂缓，启用则补审**」，不当作待删代码。`srp-audit.md` 须写明：
  未审原因是当前不活跃（1615 行、独占 `SacredRelicDemo.unity`、无其他场景引用、最后实质改动
  `2026-08-03`）；若日后接回导览或任何活场景，**必须补审**。与 GsplatBench（待删，永不进审计）区别对待。
- **`[已确认]`** 测试代码不审，但重复 fixture **算对应产品类的证据**，记在该产品类名下并参与判定。
  依据：两处为同一个类各搭等价台子，说明其构造路径不可直接调用
  （`HostFixture` 要 `SerializedObject` 反射写五个私有字段才立得起来）——这是产品侧结构问题。
- **`[已确认]`** 单文件豁免（豁免 3）的措辞定为**单文件 + 不新增类 + 不动 `public` 面**，三条全满足才免。
  用户在 propose 阶段逐字确认。保留 `public` 面这条的理由：动 public 面 = 动消费者契约，而轴的定义就是
  「外部变化透进来的口」——`IteHostBootstrap` 的六条轴正是一次次加 public 成员攒出来的。
  接受的代价：给现有类加一个 public 方法这类常见改动也要交清单（只需一行）。
- [ ] `[ASSUMED]` 本 change 的 capability 命名为 `srp-axis-governance`，且判据属于「能力」而非纯文档 —
      propose 阶段 AI 自定 — 影响：`specs/srp-axis-governance/spec.md` 的路径与命名
- **`[已确认]`** constitution 只写这一条 SRP 条款（含五条豁免），不把 `.claude/CLAUDE.md` 的
  「以架构最优为判据」搬进去。见 design D10。分工：CLAUDE.md 管怎么想（判断类，且每 session 自动加载），
  constitution 管什么必须写下来（structure 类，只在 analyze 时读）。
