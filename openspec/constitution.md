# Project Constitution: MR_Base

> Version: 1.0 · Last amended: 2026-09-20

本文件是本仓的**计划层不变量**，`/opsx:analyze` 每次逐条检查。
只收录**能机械检查**的要求；判断类的架构指引写在 `.claude/CLAUDE.md`，不进本文件
（理由见 `openspec/changes/repo-srp-baseline/design.md` D2 / D10）。

判据全文（轴的定义、五条豁免、汇聚点援引条件、waiver 机制）见本文件末尾的**判据全文**一节。
条款只是它的机械检查面。

## I. 变更轴清单必须出现在 design 里 (MUST)

每个 change 的 `design.md` MUST 含一个标题为 `## 变更轴清单` 的段落。
该 change 新增或修改的**每个**类 MUST 在清单里各占一行，标出轴数与每条轴的一句话描述。
不新增也不修改任何类的 change（如纯文档 change）MUST 保留该段落，内容写明无类改动。
满足**豁免 3**（单文件 + 不新增类 + 不改动任何 `public` 成员）的 change 可免该段落，
但 MUST 在 `proposal.md` 或 `design.md` 里**显式声明**一行 `适用豁免 3：单文件、不新增类、不动 public 面`。
声明是必需的——检查是机械的，读不出改动范围，只读得出有没有这句话；**默认不豁免**。

- CRITERION[structure]: `design.md` 中存在标题恰为 `## 变更轴清单` 的段落，且该段落为每个新增/修改类各列一行（行内含类型名、`新增` 或 `修改`、轴数）；或 `proposal.md` / `design.md` 中存在 `适用豁免 3` 字样。
  PASS: `## 变更轴清单` 段落下有 `| IteMarkerBridge | 修改 | 1 | 会话注入时序变 |`；或纯文档 change 写「无（本 change 不新增、不修改任何类）」；或改一行的 change 写「适用豁免 3：单文件、不新增类、不动 public 面」。
  FAIL: 既无 `## 变更轴清单` 段落也无 `适用豁免 3` 声明；或标题写成 `## 本 change 自身的变更轴清单`；或新增了两个类但清单只列一行。

## II. 新增的多轴类必须有编号决策 (MUST)

变更轴清单中任何标注为 `新增` 且**轴数 ≥2** 的类，`design.md` MUST 有一条编号决策
（`### D<n> —` 开头）提到该类型名，写明为什么允许它承担多条轴。
援引**豁免 5**（已论证的汇聚点）的行，MUST 在该行或其编号决策中给出一个**具名可计算量**
及其在「合」与「拆」两种形态下的数值，并给出可引用的落点位置。

- CRITERION[structure]: 清单中每个 `新增` 且轴数 ≥2 的行，其类型名出现在某条 `### D<n> —` 编号决策的正文里；声明命中豁免 5 的行，其编号决策含一个量名与两个数值。
  PASS: 清单有 `| PlatformRuntime | 新增 | 3 | ... |`，且 `### D11 — …PlatformRuntime…` 写明「新增一个平台要改的分叉文件数：合 1、拆 3」与落点 `.claude/CLAUDE.md` Architecture 节。
  FAIL: 新增一个 3 轴类，design 里无任何编号决策提到它；或只写「集中管理更清晰」而无可计算量与数值。

---

# 判据全文

条款 I / II 是本判据的机械检查面。判断部分（数几条轴）由人做，由清单公开可审兜住。

## 轴的定义

判定一个类是否违反单一职责，**只**使用**变更轴计数**：列出哪一类**外部变化**会逼这个类修改。
每条轴必须能写成一句**具体事件**：

- ✅「PICO 佩戴状态 API 变了」「ITE 包的事件签名变了」「出包配置变了」
- ❌「这里的逻辑会变」——无法证伪，不是轴

**合并规则**：两条候选轴若共享状态、必须同时修改，记作**一条**轴。

**阈值**：**≥2 条互不相干的轴 = 违反 SRP。**

**不作依据的度量**：行数、方法数、圈复杂度 MUST NOT 用于违反判定。本仓实测反例——
`Assets/Scripts/Editor/BuildScript.cs` 640 行只有一条坐实轴「出包配置变」；
`Assets/Scripts/IteHost/IteHostBootstrap.cs` 350 行六条候选轴。行数与轴数不相关，甚至反向。

**可证伪要求**：每条轴必须有**可引用的凭据**才算坐实，否则标记 `[推测轴]`，不计入排序用的坐实轴数。
凭据满足以下**任一**条即可：

1. **历史证据**：git 上有一次单独触发该轴的提交（记 commit hash）。
2. **消费面证据**：存在一个外部消费者，只取用该轴的 public 面。多个消费者各自只依赖一条轴的表面，
   这就证明了轴的独立性——这是**现在时**的证据，可随时复核。
   例：`IteEditorHud` 只取 `Runtime`+`MarkerBridge`、`IteEditorFakeScan` 只取 `AttachMarkerSession`、
   `IteDeviceMarkerRig` 只取 `StartRuntimeAsync`、`IteHmdPanel` 只取 `Failed`。
3. **实测记录证据**：代码注释或某个 `design.md` 记有该轴的独立实测事件（含日期与现象）。
   例：`PlatformRuntime.SilenceMetaGlobalHookOnPico` 的 2026-09-17 PICO 实测。

**为什么不能只认 git 历史**（2026-09-20 实测得出）：本仓 2026-07-27 起共 236 个提交，
候选类各自只被改过 3–7 次，代码基本一次写成。实测 `IteHostBootstrap` 的六条轴全部塌进
`df057ec` 与 `0b71471` 两个提交，无一条被单独触发；`IteTourObject` 四条塌进 3 个提交。
只认历史的话坐实轴数普遍为 0，排序公式全体归零，判据自废。
**git 的沉默不是「轴不存在」的证据，只说明这段代码还没被改第二次。**

## 五条豁免

**豁免 1 — Unity 生命周期不算轴。**
`Awake` / `Start` / `Update` / `OnDestroy` 等宿主调用约定本身不计为轴；
只计其**方法体内部**驱动的事件种类。
例：`IteHostBootstrap.Update` 里同时跑佩戴轮询、桥 Tick、超时计时——`Update` 不计轴，
这三件事按各自所属的外部变化分别计入对应轴。

**豁免 2 — 装配点可知两边，但装配 ≠ 运行期持有。**
跨边界装配类允许同时引用两侧类型（ITE 包 design D2 已定此形状，见
`Assets/Scripts/IteHost/IteHostBootstrap.cs` 类注释），**装配本身算一条轴**。
但装配完成后仍每帧驱动状态的部分**另计轴**。

**豁免 3 — 单文件豁免。**
**单文件 + 不新增类 + 不改动任何 `public` 成员**，三条全满足的改动，不要求提交变更轴清单。
保留 `public` 面这条的理由：动 public 面 = 动消费者契约，而轴的定义就是「外部变化透进来的口」。

**豁免 4 — 存量不倒查。**
本判据生效前既存的违反项不判为违反 constitution，只登记为技术债，登记处为
`docs/architecture/srp-audit.md`。实现机制见下方 waiver 一节。

**豁免 5 — 已论证的汇聚点。**
刻意把多条轴收在一处的类，若其**收敛理由成文**，不判为违反。援引条件见下。

## 豁免 5 的三条援引条件（缺任一条即不得援引）

1. **收敛理由成文且可反驳**。必须给出一个**具名的可计算量**，并写出它在「合」与「拆」两种形态下的
   数值，证明拆开会让这个量变差。**不接受**「集中管理更清晰」「放一起更好维护」这类无法计算的说法。

   合法的量举例（不限于这两个，但必须同样能算）：
   - **新增一个同类变体时需改动的文件数**。`PlatformRuntime` 适用此量：三条轴各自都带
     `#if MRBASE_*`（全文 8 处），合则加第三个平台改 **1** 个文件，拆则改 **3** 个。
   - **轴数 × 承载文件数**。注意此量对等分拆分不敏感（1 文件 3 轴 = 3，3 文件各 1 轴 = 3），
     选它之前先确认它在本案例上真能区分两种形态。

2. **论证有可引用落点**。必须记在 `.claude/CLAUDE.md`、该能力的 `spec.md`，或某个 change 的
   `design.md` 里，能被引用到具体位置。

3. **轴仍须逐条登记**。豁免免除的是「判为违反」，**不免除**轴清单——每条轴照样列出并附
   commit 证据或 `[推测轴]` 标记。

**已确认成立的一例**（2026-09-20 实测）：`Assets/Scripts/Platform/PlatformRuntime.cs`

| 轴 | 证据 |
|---|---|
| passthrough 开法变 | `2f8ba63 refactor(platform): move passthrough setup out of the scene` |
| 系统重定位事件源变 | `d790dcd refactor(platform): 系统重定位的平台分叉归 PlatformRuntime` |
| MRUK 包行为变 | 仅在 `7f69499` 中附带修，未单独触发 → `[推测轴]` |

2 条坐实轴，本应判违反。三条援引条件逐条核对：
① 可计算量 = **新增一个平台要改的分叉文件数**，合 = 1，拆 = 3；
② 落点 = `.claude/CLAUDE.md` Architecture 节「全部平台分叉的唯一落点」「do not add new
`#if MRBASE_*` elsewhere」；③ 三条轴已在上表逐条登记。
**命中豁免 5，不判违反。**

## Waiver（存量不倒查的实现机制）

存量违反**不**通过放宽或删除条款文字来放过。改用 waiver：

**落点（2026-09-20 实测）**：waiver 写在**该 change 自己的** `openspec/changes/<name>/.openspec.yaml` 里。
根目录 `.openspec.yaml` 与 `openspec/config.yaml` 都**不被读取**（实测均返回 `waivers: []`）。
形如：

```yaml
schema: spec-driven
created: YYYY-MM-DD
waivers:
  - principle: I
    reason: "存量违反已登记：docs/architecture/srp-audit.md · IteTourObject（4 坐实轴）"
```

- `principle` 必须等于对应条款 id（`I` 或 `II`）；
- `reason` 必须指向 `docs/architecture/srp-audit.md` 中对应的登记项；
- **未登记的违反不自动获得豁免**——先补登记，否则按新增违反处理。

**waiver 是按 change 配的，不是全局开关。** 每个碰到存量违反项的 change 各自写一条，
写的时候必须点名是清单里的哪一项——这正是想要的摩擦：豁免可见、可追溯，不会一次性放过所有人。

依据：`.claude/skills/openspec-analyze-change/SKILL.md:17`「Constitution is non-negotiable during
analyze. On MUST violations, adjust the plan — do NOT reinterpret or delete clauses.」
