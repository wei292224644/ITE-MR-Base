# Probe Report: repo-srp-baseline

> Generated: 2026-09-20
> Summary: 9 questions · 9 decisions · 8 open assumptions
> 入口：`/opsx:probe @docs/handoff/2026-09-20-ite-host-bootstrap-split.md`（handoff 的 #7）
> 转向：probe 第 3 问后范围从「拆一个类」扩为「整仓 SRP 基线」，change 由
> `ite-host-bootstrap-split` 改名为 `repo-srp-baseline`，#7 被吸收为审计对象之一。

## Confirmed decisions

### Scope & intent

**Q1 — 这次定的是「#7 拆不拆」还是「已定要拆，只定怎么拆」？**

- **AI 推荐**：先判断值不值得。证据：handoff 建议的切口（剥「事件转发」，八个纯日志 `static`）
  对四个消费者零影响，350 → 300 行，是按方法数切而非按耦合面切。
- **用户确认**：先判断。

**Q2/Q3 — 判据与真实诉求**

- **AI 推荐**（初）：成本 B（测试台子重复）是真痛，成本 A（服务台）还不是。
- **用户确认**：诉求不是这两条——是「很多代码架构和意图不清晰，没有采用单一职责原则来构建代码」。
- **AI 据此撤回上述犹豫**，改用 SRP 原始定义（一个模块只对一类变更负责）重扫
  `IteHostBootstrap`，得六条互不相干的变更轴，判定**该拆成立**：

  | 变更轴 | 位置 |
  |---|---|
  | 场景装配契约变 | 六个 `[SerializeField]` + `TryCreateRuntime:90` |
  | 包事件签名变 | `HookRuntimeEvents:264` + 十个 `Handle*:300-331` |
  | 平台佩戴通道变（PICO API） | `_picoPresence` + `HeadsetPresenceAdapter:122-125` |
  | 网络判定策略变 | `IsNetworkAvailable:166` / `SetNetworkProbe:179` |
  | 会话注入时序变 | `AttachMarkerSession:80` / `TryBindMarkerSession:251` |
  | 失败呈现策略变 | `Failed:212` / `RaiseFailure` / `Update:234-249` 超时 |

**Q3 — 范围：只 IteHost，还是整仓？**

- **AI 推荐**：只 `Assets/Scripts/IteHost/`。证据：包对宿主零知识是 D2 定的；handoff 记录
  #2 被否决时已实测动包内 `Core ↔ Components` 必须连 `IteTourObject` 初始化顺序重设计。
- **用户确认**：**整个仓库**。（推荐被否决）

**Q4 — 整仓整改的形状**

- **AI 推荐**：(B) 先立判据 + 全仓审计，再按排名逐个小 change。证据：
  ① `openspec/constitution.md` 不存在（已查），只清扫存量则三个月后照样长出新的 350 行类；
  ② 20387 行同时动会让 `389/389` 基线失去二分能力，且部分失败模式只在头显可见；
  ③ 体量排名 ≠ SRP 违反排名（`BuildScript` 640 行一条轴，`IteHostBootstrap` 350 行六条轴）。
- **用户确认**：可以（选 B）。

### Impact

**Q6 — 审计覆盖哪些代码**

- **AI 推荐**：审 `Assets/Scripts/` 除 GsplatBench 外十个模块 + `com.uality.ite-tour/Runtime/` 六层；
  排除 GsplatBench（CLAUDE.md 记明 removable as a unit，BuildScript 菜单入口已于 2026-09-18 移除）、
  `Packages/wu.yize.gsplat`（git submodule，独立仓）、`Assets/Tests/`。
  对 `SacredRelic/`（1615 行）与 ITE 包，AI 倾向都审。
- **用户确认**：`SacredRelic/` **不审**；ITE 包 **审**。
- **最终范围**：`Assets/Scripts/` 九个模块（Localization / IteHost / Editor / Core / IceSpriteFx /
  Transitions / Diagnostics / Platform / Common）+ `com.uality.ite-tour/Runtime/` 六层
  ≈ **114 个 .cs / 约 13600 行**。
- **测试代码的处置**：不审。但审计**记录**测试台子重复（例：`IteHostBootstrapTests.cs:84-126`
  的 `HostFixture` 33 行与 `IteHostCameraResolutionTests.cs:43` 的 `BuildHost` 是同一台子写两遍），
  当作对应**产品类**违反 SRP 的证据，不算测试自身违反。

**Q9 — 产物落在哪**

- **AI 推荐**：清单放 `docs/architecture/srp-audit.md`，不放 change 目录。证据：change 一归档就整体
  进 `changes/archive/`（先例：`archive/2026-09-08-unified-marker-tracking-contract/`），而清单是
  十几个后续 change 要反复引用并逐条勾掉的活文档，沉进 archive 即死。
- **用户确认**：行，就这么分。
- **分工定案**：
  - `openspec/constitution.md` — 判据本身。长期有效，不随 change 走。
  - `docs/architecture/srp-audit.md` — 114 文件轴数记录 + 排序清单 + 每条轴的 commit 证据。活文档。
  - `openspec/changes/repo-srp-baseline/` — 只放 proposal / design / tasks。归档后不留活内容。

### Design

**Q5 — SRP 判据用什么度量**

- **AI 推荐**：**变更轴计数**。定义：对一个类，列出哪一类**外部变化**会逼它改；每条轴必须写成一句
  具体事件（"PICO 佩戴状态 API 变了"），不能是"逻辑变了"；轴之间若共享状态、必须同时改，算一条。
  **≥2 条互不相干的轴 = 违反 SRP。**
  否决的度量：行数（`BuildScript` 640/一轴 vs `IteHostBootstrap` 350/六轴，不相关）、
  方法数与圈复杂度（度量「难读」，而用户诉求「意图不清」的根因是多个不相干理由驱动同一个类）。
  选变更轴的决定性理由：**可证伪**——若一条轴在 git 历史里从未单独触发过修改，它是臆想的轴，不计数。
- **用户确认**：认同（含 ≥2 阈值，未放宽到 ≥3）。
- **配套豁免（随判据一并写进 constitution）**：
  1. **Unity 生命周期不算轴**。`Awake/Update/OnDestroy` 是宿主调用约定；只看其**内部**驱动几件事。
  2. **装配点允许知道两边**（ITE 包 design D2，`IteHostBootstrap.cs:13`）。但**装配 ≠ 运行期持有**：
     装配完还每帧驱动状态（`Update:226-249` 的佩戴轮询 + 桥 Tick + 超时计时）另算轴。

### Success criteria

**Q7 — 审计怎么做才算做完**

- **AI 推荐**：两遍法 + 要 git 证据（甲）。
- **用户确认**：甲。
- **做法定案**：
  - **第一遍 全量浅扫**（114 文件全覆盖）：每类只看 public 面、生命周期方法内部驱动几件事、
    字段里几组互不相干的状态。产出轴数估计 + 一句话轴清单。不读实现细节。
  - **第二遍 候选深读**（≥2 轴的类）：每条轴要么在 git 历史里找到一次**单独触发它改动**的提交，
    要么标 `[推测轴]`。坐实不了的轴从计数里划掉。
  - 否决的 (乙)：跳过 git 考古全标 `[推测轴]`，快一半但排名可信度降级为 AI 直觉。
    否决理由：判据里「可证伪」那条只有第二遍才兑现；
    `2026-07-30 fix(sacred-relic): align dust emission with the shader's dissolve field`
    就是现成范例——一眼坐实「shader 侧改动逼状态机改」这条轴。
- **验收信号（done 的定义）**：
  1. 114 个文件**全部**有轴数记录，零遗漏（不是"扫了主要的"）。
  2. 每个 ≥2 轴的类，每条轴附 `commit hash` 或 `[推测轴]` 标记。
  3. 清单按「坐实轴数 × 消费者数」排序，第一名当场可起 change。
  4. `openspec/constitution.md` 落地判据，并用一个**已知不违反**的类做反向测试，确认判据不误判
     ——选 `Assets/Scripts/Platform/PlatformRuntime.cs`（198 行，CLAUDE.md 定为「全部平台分叉的
     唯一落点」，应判为一条轴）。

### Failure modes

**Q8 — constitution 的约束力**

- **AI 推荐**：(甲) 硬门槛。证据：
  ① 用户诉求本身是「以后别再长出来」，软判据只管存量，而六条轴是每次「顺手加个字段」攒的；
  ② 这仓已有同构硬约束在跑且有效——`.claude/CLAUDE.md` 的「每个偏离记成编号决策」攒出了 D1–D35，
  handoff 里 #2 的否决推理能被复用正是靠它：**写下来才算发生过**；
  ③ 成本落在写 design 时（几行字），不落在改代码时。
- **用户确认**：甲，豁免写进去，存量不倒查。
- **门槛定案**：
  - `/opsx:propose` 生成的每个 change，`design.md` 必须列出新增/修改类的**轴清单**；
    新增 ≥2 轴的类必须记一条编号决策说明为什么可以。`/opsx:analyze` 的 constitution alignment 拦不符合项。
  - **单文件豁免**：单文件、不新增类、不动 public 面的改动免轴清单。
  - **存量不倒查**：constitution 生效后，审计清单上的既有违反项**不算违反 constitution**，
    只算已登记的技术债——否则每个碰到老代码的 change 都会被自己的门槛卡死。

### 本次 probe 未处理（明确排除）

- handoff 的 **#3 / #6**（marker 轴向线）——用户此前已明确暂停，本次未触碰、未追问。
- handoff 的 **#2**（Core ↔ Components 双向依赖）——已否决，理由记在 handoff 里，本次不重议。
- handoff 的 **D35 未上机验证**——与本 change 无关，留待下次出包。

## Open assumptions [NEEDS CLARIFICATION]

- [ ] `[ASSUMED]` 第二遍深读的候选数量估为 **15–25 个类**，纯属未验证估计；第一遍浅扫完才知真实数量，
      若远超 25 个，工作量与 tasks 拆分都要重估 — 影响：tasks.md 的任务粒度与工期
- [ ] `[ASSUMED]` `/opsx:analyze` 的 constitution alignment 检查确实会读 `openspec/constitution.md`
      并**能拦住**不符合项。AI 未读 analyze 的实现，只依据其 skill 描述 — 影响：design.md 的门槛机制
      （若 analyze 拦不住，甲档只能靠人工 review 兜，需改写门槛条款）
- [ ] `[ASSUMED]` `PlatformRuntime.cs` 判为一条轴。仅据 `.claude/CLAUDE.md` 的描述，未深读该文件 —
      影响：constitution 的反向测试样本（若它其实 ≥2 轴，得另选样本或说明判据边界）
- [ ] `[ASSUMED]` `IteHostBootstrap` 的六条轴由浅读得出，**未做 git 考古坐实**；按判据，其中可能有
      `[推测轴]` 要被划掉 — 影响：srp-audit.md 里它的排名（本报告不预设它是第一名）
- [ ] `[ASSUMED]` ITE 包内违反项的**修改**受 D1–D35 约束，提 change 前需先读那条决策链。审计本身不受限 —
      影响：后续包内 change 的前置条件
- [ ] `[ASSUMED]` `SacredRelic/` 排除是因为「不审」，但用户**未说它是否待删**；若日后启用需补审 —
      影响：srp-audit.md 的覆盖声明（须显式记「已知未覆盖区」而非假装 114 就是全仓）
- [ ] `[ASSUMED]` 测试代码「不审、但重复台子当产品类违反的证据」这条约定由 AI 提出，用户未逐条确认 —
      影响：srp-audit.md 的证据规则
- [ ] `[ASSUMED]` 单文件豁免的确切措辞「不新增类、不动 public 面」由 AI 拟定，用户认了"豁免写进去"
      但未审阅措辞 — 影响：constitution 的豁免条款文字

## Suggested next step

- [ ] 运行 `/opsx:propose repo-srp-baseline` 生成 proposal / design / tasks（会读本报告）
- [ ] proposal 需覆盖三份产物的分工（constitution / srp-audit.md / change 目录）与两遍法的任务拆分
- [ ] design 需把上面八条 `[ASSUMED]` 落成待验项，尤其第 2 条（analyze 能否真的拦住）应在写门槛条款前验掉
