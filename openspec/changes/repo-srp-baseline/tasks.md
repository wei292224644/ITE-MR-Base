## 1. 判据落地（先做，审计依赖它）

- [x] 1.1 创建 `openspec/constitution.md`，写入 SRP 变更轴条款：轴的定义（必须写成具体外部变化事件）、
      ≥2 条互不相干轴 = 违反、共享状态的轴合并为一条、行数/方法数/圈复杂度不作依据
- [x] 1.2 在同一条款下写入五条豁免：① Unity 生命周期不算轴（只计方法体内部驱动的事件种类）
      ② 装配点可知两边但运行期持有另计轴 ③ 单文件+不新增类+不动 public 面免轴清单 ④ 存量不倒查
      ⑤ 已论证的汇聚点（D11）
- [x] 1.3 为豁免 5 写入三条援引条件：收敛理由成文且可反驳（「拆开会让 轴数×文件数 变多」这类可算论证，
      不接受「集中管理更清晰」）、论证有可引用落点、轴仍须逐条登记。缺任一条即不得援引
- [x] 1.4 把条款写成 **structure criterion**（D2）：明确 `design.md` 必须含的清单段落标题、
      每个新增/修改类一行的格式、多轴新增类必须有编号决策。不得使用判断类表述
- [ ] 1.5 跑 D9 反向测试：深读 `Assets/Scripts/Common/StaticInstance.cs`（35 行）套判据数轴。
      期望一条轴、不违反。**若 ≥2 轴则停下**，另选样本或补写判据边界说明后再继续
      （注：原样本 `PlatformRuntime.cs` 已在 propose 阶段实测判出三轴，故换样本并增设豁免 5）
- [ ] 1.6 用一个已知违反项正向验证判据可用：对 `Assets/Scripts/Editor/BuildScript.cs`（640 行）
      套判据，期望判出一条轴（验证行数不干扰判定）
- [ ] 1.7 验证豁免 5 可用且不滥用：对 `Assets/Scripts/Platform/PlatformRuntime.cs` 套判据，
      期望判出三轴（passthrough 开法 / 重定位事件源 / MRUK 包行为）、命中豁免 5 不判违反、
      但三条轴仍须逐条登记进审计清单
- [ ] 1.8 运行 `openspec instructions analyze --change repo-srp-baseline --json`，确认
      `constitutionPresent: true` 且新条款出现在 `clauses[]` 里、level 为 MUST

## 2. 审计第一遍 — 全量浅扫（114 文件，零遗漏）

每个文件产出：轴数估计 + 一句话轴清单。只看 public 面、生命周期方法体内部驱动几件事、
字段里几组互不相干的状态。不读实现细节。

- [ ] 2.1 建立 `docs/architecture/srp-audit.md` 骨架：`find` 出的 114 个路径逐行列出（未扫的留空待填），
      并写入「已知未覆盖区」段落，四处分别标注性质——GsplatBench **待删**（永不补审）、
      SacredRelic **暂缓**（接回活场景必须补审）、`wu.yize.gsplat` **跨仓**、`Assets/Tests/` **判据不适用**
- [ ] 2.2 浅扫 `Assets/Scripts/Localization/`（20 文件 / 2498 行）
- [ ] 2.3 浅扫 `Assets/Scripts/IteHost/`（11 文件 / 1719 行）——含 handoff #7 的 `IteHostBootstrap`
- [ ] 2.4 浅扫 `Assets/Scripts/Core/`（9 文件 / 893 行）
- [ ] 2.5 浅扫 `Assets/Scripts/Editor/`（3 文件 / 970 行）+ `IceSpriteFx/`（3）+ `Transitions/`（2）
      + `Diagnostics/`（1）+ `Platform/`（1）+ `Common/`（1）
- [ ] 2.6 浅扫 `Packages/com.uality.ite-tour/Runtime/Core/`（22 文件 / 2686 行）
- [ ] 2.7 浅扫 `Packages/com.uality.ite-tour/Runtime/Components/`（18 文件 / 1755 行）
- [ ] 2.8 浅扫 `Packages/com.uality.ite-tour/Runtime/Internal/`（14）+ `Data/`（5）+ `Convert/`（3）
      + `Config/`（1）
- [ ] 2.9 **零遗漏核对**：`srp-audit.md` 里有记录的文件数必须等于 114；逐行比对 `find` 输出，
      差一个都要补
- [ ] 2.10 统计第一遍判出 ≥2 轴的类数量，与 proposal 中 `[ASSUMED]` 的 15–25 估计比对。
      若远超 25，先重估第 3 组的任务拆分再往下做

## 3. 审计第二遍 — 候选深读 + git 考古坐实

对第一遍判出 ≥2 轴的每个类，逐条轴找证据。

- [ ] 3.1 为每个候选类统计**消费者数**：`grep` 类名被引用的文件数（`FindFirstObjectByType` 这类
      服务定位也算消费者），记入清单
- [ ] 3.2 逐条轴做 git 考古（`git log -S` / `git log --follow -- <file>`），找到一次**单独触发该轴**
      的提交则记 commit hash；找不到则标 `[推测轴]`
- [ ] 3.3 把 `[推测轴]` 从排序用的坐实轴数里划掉；全部轴均为推测轴的类单独列入「疑似但未坐实」段落，
      不得悄悄丢弃
- [ ] 3.4 记录测试台子重复作为产品类违反的证据（已知一例：`IteHostBootstrapTests.cs:84-126` 的
      `HostFixture` 与 `IteHostCameraResolutionTests.cs:43` 的 `BuildHost`），归到对应产品类名下
- [ ] 3.5 按「坐实轴数 × 消费者数」排序，产出重构优先级清单
- [ ] 3.6 核对 `IteHostBootstrap` 的六条轴在考古后剩几条，更新它的实际排名（不预设第一名）

## 4. 存量 waiver 与门槛生效

- [ ] 4.1 为 `srp-audit.md` 的每个登记项配置 waiver：`principle` = SRP 条款 id，
      `reason` 指向该登记项。确认未登记的违反不会命中任何 waiver
- [ ] 4.2 对四个 in-flight change（`ite-tour-space-device` / `ite-scene-layout-convention` /
      `marker-anchor-axis-correction` / `xr-ui-interaction-unification`）各跑一次
      `/opsx:analyze`，确认它们不被新门槛报 CRITICAL
- [ ] 4.3 负向验证门槛真的能拦：临时造一个缺变更轴清单的 design（或在暂存副本上试），
      确认 analyze 报 **CRITICAL** 而非 WARNING。验完撤掉
- [ ] 4.4 正向验证豁免真的生效：确认一个单文件、不新增类、不动 public 面的 change 不因缺清单被拦

## 5. 收尾验证

- [ ] 5.1 `unity command run_tests --project-path . --mode EditMode` —— 必须 **389/389**，
      且**测试总数不变**（总数变了说明动了代码，违反 D8）
- [ ] 5.2 `git status` 核对改动面：只应有 `openspec/constitution.md`、
      `docs/architecture/srp-audit.md`、本 change 目录。`Assets/` 与 `Packages/` 下零改动
- [ ] 5.3 把 proposal 里仍未确认的 `[ASSUMED]` 逐条复核：能在实施中验掉的直接验掉并改写为结论，
      剩余的原样留在 proposal 中
- [ ] 5.4 用清单第一名起下一个 change（`/opsx:propose`），并在其 `design.md` 里**实际填一次**
      变更轴清单，作为门槛的首次真实演练
