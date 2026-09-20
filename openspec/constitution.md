# MR_Base Constitution

本文件是本仓的**计划层不变量**。`/opsx:analyze` 每次会逐条检查这里的条款。

条款只收录**能机械检查**的要求（structure criterion）。判断类的架构指引写在 `.claude/CLAUDE.md`，
不进本文件——理由见 `openspec/changes/repo-srp-baseline/design.md` D2 / D10。

---

## SRP-1 — 单一职责按「变更轴」判定

**Level**: MUST

### 判据

判定一个类是否违反单一职责，**只**使用**变更轴计数**。

**轴的定义**：列出哪一类**外部变化**会逼这个类修改。每条轴必须能写成一句**具体事件**，例如：

- ✅「PICO 佩戴状态 API 变了」
- ✅「ITE 包的事件签名变了」
- ✅「打包流程变了」
- ❌「这里的逻辑会变」——无法证伪，不是轴

**合并规则**：两条候选轴若共享状态、必须同时修改，记作**一条**轴。

**阈值**：**≥2 条互不相干的轴 = 违反 SRP。**

**不作依据的度量**：行数、方法数、圈复杂度 **MUST NOT** 用于违反判定。
本仓有现成反例——`Assets/Scripts/Editor/BuildScript.cs` 640 行只有「打包流程变」一条轴；
`Assets/Scripts/IteHost/IteHostBootstrap.cs` 350 行有六条轴。行数与问题无关。

**可证伪要求**：一条轴若在 git 历史里从未**单独触发**过修改，标记为 `[推测轴]`，
不计入用于排序的坐实轴数。判据的结论必须能被 git 历史反驳。

### 豁免

以下五条限定判据的适用边界。

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
本条款生效前既存的违反项不判为违反 constitution，只登记为技术债，登记处为
`docs/architecture/srp-audit.md`。实现机制见 SRP-1 的 waiver 一节。

**豁免 5 — 已论证的汇聚点。**
刻意把多条轴收在一处的类，若其**收敛理由成文**，不判为违反。援引条件见下。

### 豁免 5 的三条援引条件（缺任一条即不得援引）

1. **收敛理由成文且可反驳**。必须写出「拆开会让 `轴数 × 承载文件数` 变多」这一类**可计算**的论证。
   **不接受**「集中管理更清晰」「放一起更好维护」这类不可反驳的说法。
2. **论证有可引用落点**。必须记在 `.claude/CLAUDE.md`、该能力的 `spec.md`，或某个 change 的 `design.md` 里，
   能被引用到具体位置。
3. **轴仍须逐条登记**。豁免免除的是「判为违反」，**不免除**轴清单——每条轴照样列出并附
   commit 证据或 `[推测轴]` 标记。

**已确认成立的一例**：`Assets/Scripts/Platform/PlatformRuntime.cs` 判出三条轴
（passthrough 开法变 / 系统重定位事件源变 / MRUK 包行为变），而 `.claude/CLAUDE.md` 已成文
「全部平台分叉的唯一落点」「do not add new `#if MRBASE_*` elsewhere」——拆开即把 `#if MRBASE_*`
散回各处，轴数 × 文件数变多。命中豁免 5，不判违反；三条轴仍登记在审计清单里。

### Criterion（structure，机械检查）

**criterionType**: `structure`

检查对象：change 的 `design.md`。按顺序机械核对四项，**不做主观判断**：

1. **段落存在**。`design.md` 含且仅含一个标题为 `## 变更轴清单` 的段落。
   - 豁免：该 change 满足豁免 3（单文件 + 不新增类 + 不动 `public` 面）时，本项跳过。
   - 该 change 不新增也不修改任何类时（如纯文档 change），段落仍须存在，内容写「无（本 change 不新增、不修改任何类）」。
2. **每类一行**。该 change 新增或修改的**每个**类，在清单里各占一行，格式为：

   ```
   | <类型全名> | <新增|修改> | <轴数> | <每条轴一句话，用 / 分隔> |
   ```

3. **多轴新增类须有编号决策**。清单中任何标注为 `新增` 且轴数 ≥2 的行，
   `design.md` 里必须有一条编号决策（`### D<n> —` 开头）提到该类型名。
4. **援引豁免 5 须指明落点**。任何声明命中豁免 5 的行，该行或其编号决策中必须出现一个可引用位置
   （文件路径、`spec.md` 或 `design.md` 的章节号）。

以上四项皆可由「文本里有没有这段、有几行、行里有没有这个名字」确定。
**本条款不检查轴数判断是否正确**——那由人做、由清单公开可审兜住。

### Waiver（存量不倒查的实现机制）

存量违反**不**通过放宽或删除本条款文字来放过。改用 waiver：

- waiver 的 `principle` 必须等于 `SRP-1`；
- waiver 的 `reason` 必须指向 `docs/architecture/srp-audit.md` 中对应的登记项；
- **未登记的违反不自动获得豁免**——先补登记，否则按新增违反处理。

依据：`.claude/skills/openspec-analyze-change/SKILL.md:17`「Constitution is non-negotiable during
analyze. On MUST violations, adjust the plan — do NOT reinterpret or delete clauses.」
