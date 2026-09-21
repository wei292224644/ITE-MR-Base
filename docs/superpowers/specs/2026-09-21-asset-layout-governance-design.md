# 目录与 asmdef 约定（asset-layout-governance）—— 设计

> 状态：已评审，待实施
> 日期：2026-09-21
> 范围：**只定规矩 + 加机械校验**。存量代码与资源不搬（唯一例外见 §6）。不碰 `Packages/`，不碰 `openspec/`。

## 1. 目标

工程当前的感受是"脚本混乱、资源乱放、测试随便摆、Editor 脚本堆一起"。本设计把这件事收敛成三件可交付的东西：

- 一套 **feature-first** 的目录约定，写进 `.claude/CLAUDE.md`
- 一个 **EditMode 结构测试**，让约定里机械可判的部分由编译/测试强制，不靠自觉
- 一份 **豁免表**，承载已知的存量违规，同时充当债务清单

**不在本次范围内**：把存量代码/资源搬进新结构（`Assets/Scripts/` 原地不动）、把模块升级成 `Packages/` 下的嵌入式 UPM 包、第三方插件目录的任何整理、资源摆放的机械校验。

## 2. 现状（读码与目录扫描所得，非推测）

问题不是"没有架构"——11 个模块里 10 个已有 asmdef，划分是清楚的。问题是**两套归类法在打架**：代码按模块（`Assets/Scripts/<Module>` + asmdef），资源按杂项（`Assets/IceSpriteFx/`、`Assets/Gestures/`、`Assets/Prefabs/ITE`）。同一个功能被劈成两半。

| 症状 | 证据 |
|---|---|
| 功能被劈两半 | `Assets/Scripts/IceSpriteFx` ↔ `Assets/IceSpriteFx/{Materials,Vfx}`；`Scripts/GsplatBench` ↔ `Assets/GsplatBench/Settings` |
| 反向：资源跑进代码目录 | `Scripts/Transitions/` 下躺着 `GroundUpRevealDemo.mat` 与 `Shaders/` |
| 有代码没 asmdef | `Scripts/IceSpriteFx` 的 3 个 `.cs` 落入 `Assembly-CSharp` |
| Editor 混装 | `MRBase.Build.Editor` 里躺着 `IteSceneSetup.cs`（ITE 功能工具，非构建），已与 CLAUDE.md 写的 "Editor — BuildScript only" 不符 |
| 测试摆放不一致 | `Tests/EditMode/` 根目录散着 8 个 Localization 测试 + asmdef，其余模块在子目录 |
| 场景平铺 | 生产场景 `MRCore`/`IteTour` 与探针 `MarkerHookTest`/`BloomTest`/`GsplatBench` 同一层 |

**裸 `.cs` 的损害已经被付过账，且写在注释里。** `Assembly-CSharp` 自动引用所有 asmdef 程序集，反向不行——asmdef 引用不了裸 `.cs`：

```
Assets/Scripts/Transitions/IceSpriteTeleport.cs:30
    /// 编排层是 Assembly-CSharp 里的 IceSpritePresence，它引用不进任何 asmdef，
```

`MRBase.Transitions` 够不着 `IceSpritePresence`，于是纯函数逻辑被迫抽进 asmdef、编排层留在裸程序集。**这个依赖方向是缺失的 asmdef 逼出来的，不是设计选的。**

## 3. 目标结构

```
Assets/
  _Project/                     ← 自己的东西全在这里（下划线排到最顶）
    Features/
      <Feature>/                ← 一个功能一个文件夹，自包含
        Runtime/                  代码 + MRBase.<Feature>.asmdef
        Editor/                   该功能的编辑器工具 + MRBase.<Feature>.Editor.asmdef
        Tests/EditMode/           该功能的测试 + MRBase.<Feature>.Tests.asmdef
        Art/ Prefabs/ Scenes/ Settings/   ← 按需建，没有就不建
    Core/                       ← MRCore.unity、Common、Platform、Bootstrap（跨功能骨架）
    Settings/                   ← Build Profiles、URP pipeline 等工程级配置
  Scripts/                      ← 存量，原地不动，逐步迁出
  Oculus/ XR/ XRI/ Samples/ Plugins/ TextMesh Pro/ INab Studio/ …   ← 第三方，永久范围外
```

## 4. 约定条文

写进 `.claude/CLAUDE.md` 新增一节。**只有条文 1 和条文 2 的前半句**由 §5 的测试机械执行，其余是文档指引。

1. **每个 feature 一个 asmdef，不许有裸 `.cs` 掉进 `Assembly-CSharp`。**
2. **Editor 代码只能在自己 feature 的 `Editor/` 下**，归属名字以 `.Editor` 结尾的 asmdef。`MRBase.Build.Editor` 只装 `BuildScript` + `ManifestGuard`。
   - 机械可判的只有"Editor 代码在 `Editor/` 目录里"。**"属于正确的 feature"判不了**——`IteSceneSetup.cs` 该归 ITE 还是归构建，需要人读代码才知道。因此 §2 记录的那条 Editor 混装是**文档级债务**，测试不会报它，也不进豁免表（豁免表只登记测试确实会报的项）。
3. **测试紧贴 feature**，与被测代码同目录树。`Assets/Tests/` 不再存在。
4. **`_Project/` 下不新建 `Resources/` 文件夹。** 它无条件全量进包、不可剥离。工程级 `Assets/Resources/`（SDK 生成的 `PXR_*`/`OVR*`）在范围外，不管。
5. **场景归属 feature**，只有 `MRCore.unity` 这类跨功能骨架在 `_Project/Core/Scenes/`。探针场景跟着各自 feature 走。

### 适用范围（白名单）

校验与约定**只看两个目录**：

```
Assets/_Project/**     ← 新结构
Assets/Scripts/**      ← 存量
```

其余 `Assets/*` 一律范围外。

**范围外 ≠ 豁免。** 豁免表是债务清单，登记"自己的、违规的、将来要还的"；第三方不是债，永远不该被管。两者混在一起会让清单失真。

## 5. 执行方式

三个产出：

| # | 产出 | 落点 |
|---|---|---|
| 1 | 约定条文 | `.claude/CLAUDE.md` 新增一节 |
| 2 | 机械校验 | `Assets/Scripts/Editor/Tests/EditMode/LayoutConventionTests.cs` + `MRBase.Build.Editor.Tests.asmdef` |
| 3 | 豁免表 | 挨着测试的 `layout-waivers.txt`，一行一路径，`#` 起头写理由 |

校验只检三件 100% 机械可判的事：

- 白名单内每个 `.cs` 都被某个 asmdef 覆盖（向上找最近的 `.asmdef`，找不到即落入 `Assembly-CSharp`）
- 路径含 `/Editor/` 的 `.cs` 必须落在名字以 `.Editor` 结尾的 asmdef 里
- 反向：`.Editor` asmdef 里不能有 `/Editor/` 之外的 `.cs`。抓的是"asmdef 取名 `Foo.Editor` 却放在 feature 根目录"——那样它会连 Runtime 代码一起吞进仅编辑器程序集，出包时整个 feature 静默消失

落地时豁免表只有一条：`Assets/Scripts/IceSpriteFx/`（3 个裸 `.cs`）。

## 6. 唯一一次存量搬迁：测试

`Assets/Tests/` 整个消失，5 个测试 asmdef 移到各自模块旁：

```
Assets/Tests/EditMode/Core/        → Assets/Scripts/Core/Tests/EditMode/
Assets/Tests/EditMode/Ite/         → Assets/Scripts/IteHost/Tests/EditMode/
Assets/Tests/EditMode/*.cs（根上 8 个） → Assets/Scripts/Localization/Tests/EditMode/
Assets/Tests/EditMode/SacredRelic/ → Assets/Scripts/SacredRelic/Tests/EditMode/
Assets/Tests/EditMode/Transitions/ → Assets/Scripts/Transitions/Tests/EditMode/
```

asmdef 名字一个不改，`-assemblyNames MRBase.Core.Tests` 照跑。纯 `git mv`（含 `.meta`），无代码改动。

**落点是 `Assets/Scripts/<Module>/`，不是 `_Project/`。** feature 目录尚不存在（代码还在老位置），搬进 `_Project/` 会让测试与被测代码分居，比现状更糟。将来某模块整体迁入 `_Project/Features/` 时，测试自然跟着走。

## 7. 编号决策

### D1 — 落点是 `Assets/_Project/`，不是 `Packages/` 下的嵌入式 UPM 包

**选了**：Assets 内的 feature 文件夹。
**替代**：把 `Localization`/`Transitions`/`Common` 升级成 `Packages/<name>`，与既有的 `wu.yize.gsplat`、`com.uality.ite-tour` 同构。
**否决理由**：包化的收益是跨工程复用，而这些模块当前只服务 MR_Base。为不存在的复用需求付出资源引用与 Editor 工具的额外约束，是为省事之外的另一种过度设计。工程内已有包化范式，将来真要复用时升级路径是通的。

### D2 — 先定规矩、存量不动，而非一次性重构

**选了**：只写约定 + 加校验，存量挂豁免表。
**替代**：一个 change 把所有代码与资源挖到新结构。
**否决理由**：存量迁移与定规矩是两条可分离的轴。合并会让一次巨大 diff 同时承担"结构是否正确"和"行为是否等价"两个待验证命题，出问题时无法二分。

### D3 — 适用范围用白名单，不用黑名单

**选了**：只扫 `Assets/_Project/**` 与 `Assets/Scripts/**`。
**替代**：列出第三方目录（`INab Studio/`、`Oculus/`、`Samples/` …）排除。
**否决理由**：零维护。以后装任何插件，它往 `Assets/` 根扔的东西自动在范围外；黑名单每装一个插件漏一次，且漏了才发现。规范与校验器两头都不必点具体插件的名。

### D4 — 校验只检代码侧，资源摆放只写文档

**选了**：只检 asmdef 覆盖、Editor 代码位置、Editor asmdef 纯度。
**替代**：同时校验资源目录归属。
**否决理由**：一个 `.mat` 该归哪个 feature，机器判不出来，硬编规则只会制造假阳性并训练出"随手加豁免"的习惯。与 `openspec/constitution.md` 的"只收录能机械检查的要求"同一原则。

### D5 — 约定条文落 `.claude/CLAUDE.md`，不落 `openspec/specs/`

**选了**：CLAUDE.md 新增一节。
**替代**：作为能力 spec 落 `openspec/specs/asset-layout-governance/spec.md`，与 `srp-axis-governance` 同构。
**否决理由**：两点。其一，往 `openspec/specs/` 写 spec 应走 propose→apply→sync-specs 的完整流程，手写塞入是绕过治理。其二，CLAUDE.md 每次会话加载，是唯一能在"新代码被写出来之前"影响落点的位置——而本设计的全部价值正在于此。`srp-axis-governance` 进 openspec 是因为"变更轴"需要人在每个 change 里判断；目录结构不需要。

### D6 — 不新增 constitution 条款

**选了**：只靠 EditMode 测试执行。
**替代**：同时加一条 constitution 条款，由 `/opsx:analyze` 每个 change 检查。
**否决理由**：目录结构由测试完全覆盖，条款只会重复。且 `/opsx:analyze` 只在走 openspec 流程时触发——随手加文件恰恰是最容易乱的场景，那时它不在。

### D7 — 校验测试寄生在 `MRBase.Build.Editor.Tests`，不新建治理模块

**选了**：放 `Assets/Scripts/Editor/Tests/EditMode/`。
**替代**：新建 `MRBase.Governance` 模块。
**否决理由**：`MRBase.Build.Editor` 已装着 `ManifestGuard`（构建期守卫），`LayoutConventionTests` 是提交期守卫，同属"工程级机械守卫"一条轴。为一个测试文件新建模块，成本高于收益。测试 asmdef 独立，不违反条文 2。

### D8 — 门禁加严：`includePlatforms` 与 asmdef 名字双向一致（2026-09-21 追加）

**背景**：§5 原本只要求检 asmdef 的**名字**。最终 review 指出这留了个缺口——规则 3 的报错信息声称要防"Runtime 代码被吞进仅编辑器程序集、出包静默消失"，但只看名字抓不到真正造成它的那个形状：一个 `includePlatforms: ["Editor"]` 却不叫 `.Editor` 的 asmdef。

**选了**：把约束升级成**双向等价** —— `includePlatforms` 恰好是 `["Editor"]` ⟺ 名字以 `.Editor` 或 `.Tests` 结尾。落成两条新规则：

- `EditorOnlyAssemblyIsNamedEditor`：仅编辑器程序集，名字没有 `.Editor` / `.Tests` 后缀 → 违规
- `EditorNamedAssemblyIsEditorOnly`：名字 `.Editor`，`includePlatforms` 不是恰好 `["Editor"]` → 违规

**替代方案一**：只加前一条（review 点名的那条）。
**否决理由**：后一条抓的场景**更常见**。Unity 新建 asmdef 时 `includePlatforms` 默认为空（= 所有平台），要限定 Editor 得手动去勾——"建了 `Foo.Editor.asmdef` 但忘了勾"是默认行为下的自然失误，而前一条要求先做对一半（勾了平台但名字没跟上）。两条共用同一次 JSON 解析，边际成本接近零。

**替代方案二**：维持 §5 原状，把缺口记为已知残留。
**否决理由**：测试**声称**的保障与**实际**保障不符，正是本仓决策原则里"用碰巧能跑替代明确规定"的形态。报错信息写着防灾难 A，实际只防了灾难 A 的一半。

**三个实现决定**：

1. **违规单位是 asmdef，不是它覆盖的 `.cs`。** 一个坏 asmdef 报一条、豁免也只需写一条；挂到每个 `.cs` 上会让一个错误刷出 N 条。`EveryWaiverIsStillNeeded` 无需改动——它比对的是 `RawViolations()` 的路径集合，asmdef 路径进去后自动兼容。
2. **`.Tests` 两边都不强制。** PlayMode 测试跑在真机上，`includePlatforms` 本就该是空；强制它们 Editor-only 会在引入第一个 PlayMode 测试时误报。
3. **"仅编辑器"定义为 `includePlatforms` 恰好只有 `["Editor"]`。** 空数组是"所有平台"不是"仅编辑器"；`["Editor","Android"]` 也不是——那样 Editor 代码会跟着进 Android 包。

**不做**：`.asmref` 识别（全仓无）、`excludePlatforms` 的对称检查（无真实失效场景）。

**落地验证**：加严后现存 18 个白名单内 asmdef **零误报**（8 个 `["Editor"]` 的全部以 `.Editor` / `.Tests` 结尾，10 个空 `includePlatforms` 的全部不以 `.Editor` 结尾）。两个方向各造一个反例，均精确点亮对应规则且不误伤其余四条。测试数 393 → 395。

## 8. 与在飞 change 的边界

`openspec/changes/ite-scene-layout-convention` 管 ITE 两个场景**内部**根层对象如何分组；本设计管场景**文件放在哪个目录**。仅在条文 5 处接触，不冲突，互不阻塞。

## 9. 验收

- EditMode 全量回归：搬迁本身**不改变测试总数**，搬完仍是 389/389；`LayoutConventionTests` 新增的用例另计，最终总数为 389 + 新增用例数
- `LayoutConventionTests` 在当前工程上通过（豁免表含 `IceSpriteFx` 一条）
- 手工造一个裸 `.cs`（白名单内、无 asmdef 覆盖）→ 测试必须红
- 手工把一个非 Editor 的 `.cs` 放进 `Editor/` 目录 → 测试必须红
- 两个平台 `MRBase/Build/Dry Run Quest|Pico` 通过（确认测试搬迁没动到构建路径）
