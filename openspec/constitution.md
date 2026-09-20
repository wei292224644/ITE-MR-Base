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
