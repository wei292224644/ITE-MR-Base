## ADDED Requirements

### Requirement: 变更轴是本仓 SRP 的唯一度量

本仓判定一个类是否违反单一职责，SHALL 只使用**变更轴计数**：列出哪一类**外部变化**会逼这个类修改，
每条轴 MUST 能写成一句具体事件（例「PICO 佩戴状态 API 变了」），MUST NOT 写成「逻辑变了」这类无法证伪的描述。
若两条候选轴共享状态、必须同时改，它们 SHALL 记作一条轴。

行数、方法数、圈复杂度 MUST NOT 用作违反判定的依据。

#### Scenario: 行数大但只有一条坐实轴，判为不违反
- **WHEN** 审计 `Assets/Scripts/Editor/BuildScript.cs`（640 行）
- **THEN** 出包入口与平台配置 SHALL 合并为一条轴「出包配置变」
      （历史上三次加入口都同时改了 profile/loader/define，从未分开改过）
- **AND** adb 装机 SHALL 标记 `[推测轴]`（只在 `3079a7a` 随大改动变过，未单独触发）
- **AND** 判定为**不违反**（1 条坐实轴），不进重构清单

#### Scenario: 行数中等但多轴，判为违反
- **WHEN** 审计 `Assets/Scripts/IteHost/IteHostBootstrap.cs`（350 行）
- **THEN** 列出场景装配契约、包事件签名、PICO 佩戴通道、网络判定、会话注入时序、失败呈现六条候选轴
- **AND** 判定为**违反**，进重构清单

#### Scenario: 无法写成具体事件的轴不计数
- **WHEN** 某条候选轴只能表述为「这里的逻辑会变」
- **THEN** 该轴 MUST 从计数中剔除，并在审计记录里说明剔除原因

### Requirement: 违反阈值为两条互不相干的轴

一个类若被判出 **≥2 条互不相干的变更轴**，SHALL 判定为违反 SRP。

#### Scenario: 恰好两轴即违反
- **WHEN** 某类被坐实两条互不相干的轴
- **THEN** 判定为违反，进审计清单

#### Scenario: 单轴不违反
- **WHEN** 某类只有一条轴
- **THEN** 判定为不违反

### Requirement: 五条豁免限定判据的适用边界

判据 SHALL 附带以下五条豁免，且豁免 MUST 成文于 `openspec/constitution.md`：

1. **Unity 生命周期不算轴**：`Awake`/`Start`/`Update`/`OnDestroy` 等宿主调用约定本身 MUST NOT 计为轴；
   只计其**方法体内部**驱动的事件种类。
2. **装配点可知两边**：跨边界装配类允许同时引用两侧类型（ITE 包 design D2 已定此形状），装配本身算一条轴。
   但**装配 ≠ 运行期持有**：装配完成后仍每帧驱动状态的部分 SHALL 另计轴。
3. **单文件豁免**：单文件、不新增类、不改动任何 `public` 成员的改动 MUST NOT 被要求提交变更轴清单。
4. **存量不倒查**：判据生效前既存的违反项 SHALL NOT 判为违反 constitution，只登记为技术债。
5. **已论证的汇聚点**：刻意把多条轴收在一处的类，若其收敛理由成文，SHALL NOT 判为违反。

#### Scenario: Update 内部驱动三件事，计三条轴而非一条
- **WHEN** 审计 `IteHostBootstrap.Update`（佩戴轮询 + 桥 Tick + 超时计时）
- **THEN** `Update` 本身不计轴
- **AND** 其内部三件事按各自所属的外部变化分别计入对应轴

#### Scenario: 装配点持有运行期状态，装配之外另计轴
- **WHEN** 某装配类在装配完成后仍每帧推进会话状态
- **THEN** 「装配」算一条轴
- **AND** 运行期状态推进 SHALL 另计为独立的轴

#### Scenario: 改一行不需要轴清单
- **WHEN** 某 change 只改一个文件、不新增类、不动 public 面
- **THEN** 其 `design.md` MUST NOT 被要求含变更轴清单
- **AND** `/opsx:analyze` 不得因缺该清单而报 CRITICAL

### Requirement: 汇聚点豁免必须由成文的收敛理由支撑

豁免 5 MUST NOT 凭「这个类是汇聚点」的自称成立。援引它的类 SHALL 满足全部三条：

- **收敛理由成文**：MUST 给出一个**具名的可计算量**，并写出它在「合」与「拆」两种形态下的数值。
  MUST NOT 只写「集中管理更清晰」这类无法计算的说法。所选的量 MUST 能真正区分两种形态
  ——`轴数 × 承载文件数` 对等分拆分不敏感（1 文件 3 轴 = 3 文件各 1 轴 = 3），
  选它前须确认它在本案例上有区分力；
- **理由有落点**：论证 MUST 记在 `.claude/CLAUDE.md`、该能力的 `spec.md` 或某个 change 的 `design.md` 里，
  能被引用到具体位置；
- **轴仍需逐条登记**：豁免免除的是「判为违反」，MUST NOT 免除轴清单——每条轴照样列出并附证据。

#### Scenario: 平台分叉汇聚点援引豁免成立
- **WHEN** 审计 `Assets/Scripts/Platform/PlatformRuntime.cs`，判出三条轴
      （passthrough 开法变 / 系统重定位事件源变 / MRUK 包行为变）
- **AND** 给出可计算量「新增一个平台要改的分叉文件数」：合 = 1，拆 = 3（三条轴各自都带 `#if MRBASE_*`）
- **AND** 落点为 `.claude/CLAUDE.md` Architecture 节「全部平台分叉的唯一落点」
      「do not add new `#if MRBASE_*` elsewhere」
- **THEN** 命中豁免 5，SHALL NOT 判为违反
- **AND** 三条轴 MUST 仍逐条登记在 `srp-audit.md`

#### Scenario: 自称汇聚点但无收敛理由，豁免不成立
- **WHEN** 某多轴类主张自己是汇聚点，但找不到成文的收敛理由，或理由只是「集中管理更清晰」
- **THEN** MUST NOT 命中豁免 5，按违反处理

#### Scenario: 豁免不免除轴清单
- **WHEN** 某类命中豁免 5
- **THEN** 其每条轴 MUST 仍在审计清单里列出并附 commit 证据或 `[推测轴]` 标记

### Requirement: design 门槛以机械可检查的结构形态存在

constitution 中的 SRP 条款 SHALL 以 **structure criterion** 表述，MUST NOT 依赖判断类表述。

理由是机制性的：`.claude/skills/openspec-analyze-change/SKILL.md:37` 规定
`judgment without concrete evidence → downgrade to WARNING (never CRITICAL)`，
判断类条款因此无法 advisory-block。

每个 change 的 `design.md` SHALL 含一段固定标题的变更轴清单，清单 MUST 为该 change 新增或修改的每个类
各占一行，并标出轴数。

#### Scenario: 缺清单段落被拦
- **WHEN** 某 change 新增了类，但 `design.md` 无变更轴清单段落
- **AND** 该 change 不适用单文件豁免
- **THEN** `/opsx:analyze` SHALL 报 CRITICAL 并 advisory-block

#### Scenario: 新增多轴类但无编号决策被拦
- **WHEN** 清单中某个**新增**类标出 ≥2 轴
- **AND** `design.md` 无对应编号决策说明为何可以
- **THEN** `/opsx:analyze` SHALL 报 CRITICAL

#### Scenario: 清单齐备则放行
- **WHEN** 清单存在、每个新增/修改类各一行、多轴新增类均有编号决策
- **THEN** 该条款检查通过

### Requirement: 存量违反通过 waiver 承载而非改写条款

「存量不倒查」SHALL 由 `analyze` 的 waiver 机制实现：waiver 的 `principle` MUST 等于 SRP 条款 id，
`reason` MUST 指向 `docs/architecture/srp-audit.md` 中对应的登记项。
MUST NOT 通过放宽或删除条款文字来放过存量。

#### Scenario: 碰到已登记存量违反项的 change 不被卡死
- **WHEN** 某 change 修改了审计清单上已登记的违反类，但未在本次消除其多轴
- **THEN** 该违反 SHALL 命中 waiver，以 NOTE 形式呈现而非 CRITICAL

#### Scenario: 未登记的存量不自动获得豁免
- **WHEN** 某类违反但不在 `srp-audit.md` 的登记项中
- **THEN** MUST NOT 自动豁免；先补登记，否则按新增违反处理

### Requirement: 审计清单覆盖全范围且每条轴附证据

`docs/architecture/srp-audit.md` SHALL 满足：

- 审计范围内**每一个** `.cs` 文件都有轴数记录，零遗漏；
- 每个 ≥2 轴的类，其**每一条**轴 MUST 附三类凭据之一，或显式标记 `[推测轴]`：
  ① commit hash（该轴曾单独触发过修改）；② 只取用该轴 public 面的外部消费者（文件与取用点）；
  ③ 带日期与现象的独立实测记录；
- 清单 SHALL 按「坐实轴数 × 消费者数」排序；
- 文档 MUST 显式列出**已知未覆盖区**（GsplatBench、SacredRelic、`wu.yize.gsplat` submodule、`Assets/Tests/`），
  MUST NOT 把覆盖范围表述为「全仓」。

#### Scenario: 坐实不了的轴被划掉
- **WHEN** 某条候选轴三类凭据（历史 / 消费面 / 实测记录）一个都拿不出
- **THEN** 该轴 SHALL 标记 `[推测轴]`
- **AND** MUST NOT 计入用于排序的坐实轴数

#### Scenario: 年轻仓库里靠消费面坐实
- **WHEN** 某类的多条轴全部在同一两个提交中引入，无一条被单独触发
- **AND** 存在多个外部消费者，各自只取用其中一条轴的 public 面
      （如 `IteEditorHud`→`Runtime`、`IteEditorFakeScan`→`AttachMarkerSession`）
- **THEN** 这些轴 SHALL 凭消费面证据坐实
- **AND** MUST NOT 因 git 历史沉默而全部降级为 `[推测轴]`

#### Scenario: 判据须能通过反向测试
- **WHEN** 用一个已知职责单一的类（`Assets/Scripts/Common/StaticInstance.cs`，35 行，
      CLAUDE.md 定为 dependency-free helper）套用判据
- **THEN** 结果 SHALL 为一条轴、不违反
- **AND** 若结果为 ≥2 轴，MUST 另选反向测试样本或补写判据边界说明

#### Scenario: 测试台子重复记作产品类的证据
- **WHEN** 发现两处测试为同一产品类各自搭建等价 fixture
- **THEN** 该重复 SHALL 记作对应**产品类**违反 SRP 的证据
- **AND** MUST NOT 记作测试代码自身的违反

### Requirement: 本变更不修改产品代码

本 change SHALL 只产出 `openspec/constitution.md` 与 `docs/architecture/srp-audit.md`，
MUST NOT 修改 `Assets/` 或 `Packages/` 下任何产品代码或测试代码。审计出的违反项各自另起 change。

#### Scenario: 测试基线不变
- **WHEN** 本 change 完成后运行 EditMode 测试
- **THEN** 结果 SHALL 仍为 389/389 通过
- **AND** 测试总数 MUST NOT 变化
