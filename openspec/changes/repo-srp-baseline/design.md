## Context

仓库目前没有 `openspec/constitution.md`。`.claude/CLAUDE.md` 有一节「决策原则：以架构最优为判据」，
但它约束的是**决定怎么做**（结构优先于省事、偏离要记编号决策），没有任何条款回答
「一个类该对几件事负责」。结果是每次「顺手加个字段」都合规。

`IteHostBootstrap.cs`（350 行）是这个缺口的当前最大产物——一个类同时对六类外部变化负责，
且已经长成场景服务台：`IteEditorHud.cs:38`、`IteEditorFakeScan.cs:75`、`IteHmdPanel.cs:65`、
`IteDeviceMarkerRig.cs:57` 四个组件各自 `FindFirstObjectByType<IteHostBootstrap>()` 再各取一片。
测试侧的代价同样可见：`IteHostBootstrapTests.cs:84-126` 的 `HostFixture`（33 行，`SerializedObject`
反射写五个私有字段）与 `IteHostCameraResolutionTests.cs:43` 的 `BuildHost` 是同一个台子写了两遍。

本 change 的前身是 handoff `2026-09-20-ite-host-bootstrap-split.md` 的 #7（拆这个类）。probe 第 3 问后
范围扩为整仓，#7 降为审计对象之一。

**约束**
- 审计范围 ≈ 114 个 `.cs` / 约 13600 行，逐个深读不可行。
- 本仓部分失败模式只在头显上可见（passthrough 黑屏、手部不上报），大范围同时重构无法二分定位。
- `389/389` EditMode 是唯一自动化基线；它只能证明"最后是绿的"。
- `/opsx:analyze` 的能力边界已实测（见 D2），门槛设计必须迁就它。

## Goals / Non-Goals

**Goals**
- 立一条**可执行、可证伪**的 SRP 判据，成文于 `openspec/constitution.md`。
- 让判据卡到**增量**上：新 change 的 `design.md` 必须交变更轴清单，`analyze` 能真的拦住。
- 产出全仓审计清单，每条轴带 commit 证据，给出可依据的重构优先级。
- 存量不因判据生效而卡死正常开发。

**Non-Goals**
- **不修改任何产品代码**。审计出的违反项各自另起 change。
- 不拆 `IteHostBootstrap`（它进清单，排名由证据定）。
- 不审 GsplatBench、SacredRelic、`wu.yize.gsplat` submodule、`Assets/Tests/`。
- 不重议 handoff 的 #2（已否决）、#3 / #6（用户暂停，本 change 不触碰）。
- 不把 `.claude/CLAUDE.md` 既有原则整体搬进 constitution（见 D10）。

## Decisions

### D1 — 度量用「变更轴计数」，不用行数、方法数或圈复杂度

**选了什么**：对一个类列出「哪一类**外部变化**会逼它改」。每条轴必须能写成一句具体事件
（「PICO 佩戴状态 API 变了」），不能是「逻辑变了」。共享状态、必须同时改的候选轴算一条。

**替代方案与否决理由**
- **行数**：本仓有现成反例——`BuildScript.cs` 640 行只有「打包流程变」一条轴，拆了反而把
  一条流程散到多个文件；`IteHostBootstrap.cs` 350 行六条轴。行数与问题无关。
- **方法数 / 圈复杂度**：它们度量「难读」。本次要解决的是「意图不清」，而意图不清的根因是
  一个类被多个不相干的理由驱动——那正是变更轴，不是复杂度。
- **决定性理由**：变更轴**可证伪**。一条轴若在 git 历史里从未单独触发过修改，它就是臆想的轴，
  不计数。这让审计排名能被反驳，而不是 AI 的主观排序。

**正向验证记录（apply 任务 1.6，2026-09-20）：通过，且过程本身验证了合并规则与推测轴规则。**
浅扫 `BuildScript.cs` 初判出三条候选轴（出包流程 / 入口增减 / adb 装机），与「一条轴」的预期不符。
考古后证据反转：

- 三次「加出包入口」的提交（`1959911` / `f8f935a` / `9d7b902`）**每次都同时改了 profile/loader/define**，
  入口与流程在历史上从未分开改过 → 按合并规则记作**一条**轴「出包配置变」；
- adb 装机（`InstallAndLaunch:270` / `ResolveAdb:300`）只在 `3079a7a` 里随一次大改动一起变过，
  从未单独触发 → `[推测轴]`，不计入坐实轴数。

结论：**1 条坐实轴，不违反**。这条记录的价值在于它推翻了 AI 的浅读初判——正是 D6 第二遍存在的理由。

### D2 — 条款写成 structure criterion，不写成 judgment criterion

**选了什么**：constitution 里的 SRP 条款表述为**机械可检查的结构要求**——`design.md` 是否含固定标题的
变更轴清单段、清单是否为每个新增/修改类各占一行、多轴新增类是否有对应编号决策。

**为什么（实测得出，改变了原设计）**：probe 阶段曾假设「analyze 能拦住」。已读
`.claude/skills/openspec-analyze-change/SKILL.md` 确认：analyze 确实读 constitution
（`openspec instructions analyze --change` 返回 `constitutionPresent` / `clauses[]` / `waivers[]`），
CRITICAL 会 advisory-block（`SKILL.md:68`）。**但 `SKILL.md:37` 规定
`judgment without concrete evidence → downgrade to WARNING (never CRITICAL)`。**

**替代方案与否决理由**
- **写成 judgment 条款**（「类应职责单一」）：会被自动降级为 WARNING，永远拦不住——等于软判据，
  而用户明确要硬门槛。否决。
- **在 analyze 之外另造检查脚本**：为单一用途造工具，且要维护一个 C# 解析器才能数轴。
  本仓已有的 change 流程足够承载，否决（判据的执行者是写 design 的人，不是解析器）。

**代价**：门槛检查的是「清单在不在、格式对不对」，不是「轴数对不对」。数轴的准确性靠人。
这是接受的——机械门槛的作用是让轴清单**无法被省略**，判断本身本来就该由人做并留下记录。

### D3 — 阈值为 ≥2 条轴，不放宽到 ≥3

**选了什么**：≥2 条互不相干的轴即违反。

**替代方案与否决理由**：≥3 的阈值会放过「装配 + 运行期状态推进」这种最常见的两轴形态——
而它恰是本仓最典型的病灶（`IteHostBootstrap.Update:226-249`）。放宽阈值等于把判据的主要用途排除掉。
probe 中已向用户明示 ≥2 会判很多类违反，用户确认不放宽。

**缓解**：靠 D4 的存量 waiver 与「单文件豁免」，而不是靠抬高阈值。

### D4 — 存量不倒查由 waiver 机制承载，不改写条款文字

**选了什么**：既有违反项登记进 `srp-audit.md`，并在 analyze 的 `waivers[]` 中以
`principle = SRP 条款 id`、`reason = 指向 srp-audit.md 的登记项` 的形式豁免。
命中 waiver 时 analyze 输出 NOTE（`SKILL.md:36`）而非 CRITICAL。

**替代方案与否决理由**
- **放宽条款文字**（如「仅对新增类生效」）：条款会失去对「修改既有类时继续加轴」的约束力——
  正是六条轴长出来的路径。且 `SKILL.md:17` 明确「Constitution is non-negotiable during analyze，
  On MUST violations 调整计划，不要重新解释或删除条款」。否决。
- **不做豁免，靠人忽略 CRITICAL**：advisory-block 会挡住 apply，每个碰老代码的 change 都要手动放行；
  「习惯性忽略 CRITICAL」会让门槛整体失效。否决。

**配套约束**：未登记的违反 **不**自动获得豁免——先补登记。否则 waiver 变成万能通行证。

### D5 — 审计清单落 `docs/architecture/srp-audit.md`，不落 change 目录

**选了什么**：判据进 `openspec/constitution.md`；清单进 `docs/architecture/srp-audit.md`；
change 目录只放 proposal / design / tasks。

**为什么**：生命周期不匹配。change 一归档就整体进 `changes/archive/`（先例：
`archive/2026-09-08-unified-marker-tracking-contract/`，现在要靠路径考古才读到）。而清单是十几个后续
change 要反复引用、逐条勾掉、随代码更新行号的**活文档**。沉进 archive 它就变成另一份没人看的架构文档
——正是本 change 要治的病。`docs/` 已有长期架构文档的先例
（`mr-to-vr-transition-effects.md`、`unity-multi-scene-seamless-transition.md`）。

**替代方案**：清单留在 change 里、change 长期不归档。否决：一个永不归档的 change 会挂在
`openspec status` 里污染所有后续状态判断，且它没有"完成"的定义。

### D6 — 审计两遍法，第二遍必须做 git 考古

**选了什么**
- 第一遍**全量浅扫**（114 文件全覆盖）：只看 public 面、生命周期方法体内部驱动几件事、字段里几组
  互不相干的状态。产出轴数估计 + 一句话轴清单，不读实现细节。
- 第二遍**候选深读**（≥2 轴者）：每条轴要么找到一次单独触发它的提交（记 commit hash），
  要么标 `[推测轴]`。坐实不了的轴从排序用的计数里划掉。

**替代方案与否决理由**：跳过 git 考古、全部标 `[推测轴]`，快一半以上。否决理由是 D1 的立足点——
判据的价值在「可证伪」，而可证伪只在第二遍兑现。已有现成范例证明这遍有效：
`2026-07-30 fix(sacred-relic): align dust emission with the shader's dissolve field` 一眼坐实了
「shader 侧改动逼状态机改」这条轴。没有第二遍，清单就是 AI 的主观排名——换个形式重演「意图不清」。

**排序公式**：坐实轴数 × 消费者数。消费者数用 `grep` 统计类名被引用的文件数（`FindFirstObjectByType`
这种服务定位也算消费者）。理由：轴数衡量它多久被打扰一次，消费者数衡量拆它时的影响面。

### D7 — 审计范围的四处排除，各有独立理由

| 排除项 | 理由 |
|---|---|
| `Assets/Scripts/GsplatBench/`（1827 行） | `.claude/CLAUDE.md` 记明 removable as a unit；`BuildScript` 入口已于 2026-09-18 移除。给待删代码做架构，报告和代码一起进垃圾桶 |
| `Assets/Scripts/SacredRelic/`（1615 行） | 用户定为不审。记为「**暂缓，启用则补审**」而非待删：当前不活跃（独占 `SacredRelicDemo.unity`、无其他场景引用、最后实质改动 `2026-08-03`），日后接回导览或任何活场景必须补审 |
| `Packages/wu.yize.gsplat/` | git submodule（独立仓 `gsplat-unity`），改它要跨仓 PR，不该混进本仓 change |
| `Assets/Tests/` | 测试的职责判据与产品代码不同（一个 fixture 服务多个用例本就正当） |

**测试代码的特殊处置**：不审，但**记录**重复台子，当作对应**产品类**违反的证据——
两处为同一个类各搭一套等价 fixture，说明那个类的构造路径不可直接调用，这是产品侧的结构问题。

**强制要求**：`srp-audit.md` 必须显式列出这四处「已知未覆盖区」，不得把覆盖范围表述为「全仓」。
否则下一个读清单的人会以为 114 就是全部。

### D8 — 本 change 零产品代码改动

**选了什么**：只产出两份文档。第一名的重构另起 change。

**为什么**：20387 行同时动会让 `389/389` 失去二分能力；且本仓一部分故障只在头显复现，大改后真机出问题
无法回溯。把「立判据」和「用判据改代码」分开，也让判据本身能被独立审阅——判据错了，代价是改文档，
不是回滚重构。这与 `.claude/CLAUDE.md`「迁移既有代码的默认是行为等价，让迁移与修 bug 可分离」同构。

**验收**：本 change 完成后 EditMode 仍为 389/389，且**测试总数不变**（总数变了说明动了代码）。

### D9 — 判据须通过一次反向测试才算落地；样本改为 `StaticInstance.cs`

**选了什么**：用 `Assets/Scripts/Common/StaticInstance.cs`（35 行，CLAUDE.md 定为 dependency-free helper）
做反向测试样本，期望结果是**一条轴、不违反**。

**为什么要反向测试**：一条只会判「违反」的判据没有信息量。豁免 1（生命周期不算轴）与豁免 2
（装配点可知两边）正是为了不误判 Unity 惯用形状，反向测试是它们唯一的验证手段。

**实测结果（apply 任务 1.5，2026-09-20）：通过。** `StaticInstance.cs` 判出**一条**轴——
「单例重复实例的语义变」（`Awake:9-27`：重复实例自毁而非顶掉，避免留下 Unity 伪 null）。
另一个候选轴「测试绑定需求变」（`BindInstanceForTesting:34`）与它共享 `_instance`，
改存储必须同时改，按合并规则记作同一条。判为不违反——判据能判出「不违反」，具备信息量。

**原样本被换掉的原因（propose 阶段实测，推翻了原假设）**：原定样本
`Assets/Scripts/Platform/PlatformRuntime.cs`（198 行）经深读判出**三条**互不相干的轴——

| 轴 | 位置 | 证据 |
|---|---|---|
| passthrough 开法变 | `EnablePassthrough:85`（Quest `ARSession`/`ARCameraManager` vs PICO `EnableVideoSeeThrough` + premultiplied alpha） | — |
| 系统重定位事件源变 | `HookRecenter:147` / `Recentered:142` | `d790dcd refactor(platform): 系统重定位的平台分叉归 PlatformRuntime` ← 单独触发，**已坐实** |
| MRUK 包行为变 | `SilenceMetaGlobalHookOnPico:69`（按名字找 Meta 的 `internal` 类型） | 代码注释记 2026-09-17 PICO 实测 |

`_recenterHooked` 只服务第二条，不构成共享状态，三条不能合并。这不是判据误判，而是暴露了一个
真实边界——见 D11。

### D11 — 增设豁免 5「已论证的汇聚点」，并用「收敛理由」限制它

**选了什么**：刻意把多条轴收在一处的类，若收敛理由成文，不判为违反。援引它须同时满足三条：
① 写出「拆开会让 `轴数 × 承载文件数` 变多」这类具体论证（不接受「集中管理更清晰」）；
② 论证有可引用的落点（`.claude/CLAUDE.md` / `spec.md` / 某 change 的 `design.md`）；
③ **轴仍逐条登记**——豁免免除的是「判为违反」，不免除轴清单与证据。

**为什么**：`PlatformRuntime` 的三条轴是被**刻意**收进来的，CLAUDE.md 已成文
「全部平台分叉的唯一落点」「do not add new `#if MRBASE_*` elsewhere」，拆开就是把 `#if MRBASE_*`
散回各处、轴数 × 文件数变多，并直接违反既有架构决定。若判据把这种「刻意汇聚」与
`IteHostBootstrap` 那种「偶然堆积」判成同一件事，审计清单的第一名可能是个结构正确的类——
判据就失去指导力了。

**替代方案与否决理由**
- **不加豁免，`PlatformRuntime` 算违反**（进清单排队重构）：会把 CLAUDE.md 定下的唯一落点列成待重构项。
  否决——判据不该指使人去违反更上层的架构决定。
- **只换反向测试样本，边界不动**：`PlatformRuntime` 按存量登记就过去了。否决——下次有人**新写**
  一个汇聚点时，门槛会拦它而没有条款可依，判据留下一块说不清的地带。

**已知风险**：任何多轴类都可能自称「汇聚点」。三条援引条件就是防线，其中第 ① 条最关键——
它要求论证是**可反驳的**（拆开后文件数是否真的变多，能算）；第 ③ 条保证豁免不产生信息黑洞。

### D10 — constitution 只写这一条 SRP 条款

**选了什么**：constitution 只放 SRP 变更轴判据（含四条豁免），不把 `.claude/CLAUDE.md` 的
「以架构最优为判据」整节搬进去。

**为什么**：analyze 每次要遍历 `clauses[]` 逐条判，条款越多、判断类条款越多，噪声越大，
真正要拦的那条越容易被淹在 WARNING 里。CLAUDE.md 那节是**写给读它的人**的判断指引（判断类，
按 D2 的机制注定降级为 WARNING），不适合做机械门槛。两者分工：CLAUDE.md 管怎么想，
constitution 管什么必须写下来。

**代价**：constitution 与 CLAUDE.md 会有一处重叠——「偏离要记编号决策」。接受：
SRP 条款要求的是**特定一段清单**，是前者的机械化子集，重叠处不矛盾。

## Risks / Trade-offs

- **门槛拦住"只想快改一行"的 change** → 单文件豁免（单文件、不新增类、不动 public 面免清单）。
  这条豁免的措辞在 proposal 中登记为 `[ASSUMED]`，用户未审阅原文。
- **机械门槛只验清单存在、不验轴数正确** → D2 已接受。真实防线是清单公开可审 + 多轴新增类必须写
  编号决策；写得出理由的多轴类本来就该放过。
- **审计清单随代码漂移**（行号、类名过期） → 清单是活文档，waiver 的 `reason` 指向具体登记项，
  登记项失效时 waiver 也就命不中，会在下一次 analyze 暴露。
- **waiver 被滥用成万能通行证** → 未登记的违反不自动豁免；waiver 必须逐条指向登记项。
- **第一遍浅扫漏文件** → 任务以文件清单驱动（`find` 出的 114 个路径逐个勾），不是"扫主要的"。
  验收条件写明零遗漏。
- **`[推测轴]` 比例过高导致排名失真** → 若某类全部轴都是推测轴，它排序权重为 0，
  不进优先级前列；`srp-audit.md` 需单独列出这类"疑似但未坐实"的条目，而不是悄悄丢掉。
- **判据误判 Unity 惯用形状** → 豁免 1 / 2 / 5 + D9 反向测试。若反向测试失败，先修判据再继续审计
  （已发生一次：原样本 `PlatformRuntime` 判出三轴，据此增设豁免 5，见 D11）。
- **豁免 5 被当成万能挡箭牌** → 三条援引条件（收敛理由成文且可反驳、有引用落点、轴仍逐条登记）。
  审计时若发现某类援引豁免 5 但拿不出 ① 的可算论证，按违反处理。
- **候选数量远超预估**（proposal 登记为 `[ASSUMED]` 的 15–25 个）→ 第一遍结束后即可知实数，
  超出时重估第二遍的任务拆分，不硬赶。

## Migration Plan

判据生效是一次**流程**变更，没有代码回滚问题：

1. 先落 constitution（含四条豁免），跑 D9 反向测试；反向测试不通过则先修判据。
2. 再做审计（两遍），产出 `srp-audit.md`，同时把既有违反项登记为技术债。
3. 为审计出的存量违反项配置 waiver，使 in-flight 的四个 change
   （`ite-tour-space-device` / `ite-scene-layout-convention` / `marker-anchor-axis-correction` /
   `xr-ui-interaction-unification`）不被新门槛卡住。
4. 回滚方式：删除 `openspec/constitution.md` 即让门槛整体失效（`constitutionPresent: false` →
   analyze 跳过该 pass，`SKILL.md:28`）。`srp-audit.md` 可独立留存，它本身无副作用。

## 变更轴清单

无（本 change 不新增、不修改任何类）。

按 constitution `SRP-1` 的 structure criterion 第 1 项自举：纯文档 change 也须保留本段落，
标题必须正好是 `## 变更轴清单`，内容写明无类改动。此段同时示范清单在 `design.md` 中的位置。

## Open Questions

- ~~D9 的反向测试样本 `PlatformRuntime.cs` 是否真为一条轴~~ —— **已验：三条轴**，样本换为
  `StaticInstance.cs`，并据此增设豁免 5（D11）。新样本本身仍未深读，见 proposal 的 `[ASSUMED]`。
- 第二遍候选实际数量（估 15–25）——第一遍结束才知。
- 单文件豁免的措辞是否需要用户逐字审阅——proposal 中已登记 `[ASSUMED]`。
- ITE 包内违反项后续提 change 时，是否需要先通读 D1–D35——倾向需要，未确认。
