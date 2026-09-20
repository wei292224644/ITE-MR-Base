# Probe Report: xr-ui-interaction-unification

> Generated: 2026-09-18
> Status: **PAUSED** — 用户主动中断（"ui/ux 暂时不探讨了"），转去处理 BuildScript 构建入口清理。可用 `/opsx:probe xr-ui-interaction-unification` 续聊，或直接 `/opsx:propose` 带着下面的开放假设生成产物。
> Summary: 4 组问答，2 条确认决策，多条开放假设（详见下）

## Confirmed decisions

### Scope & intent
- **Question**：这次 UI/UX 统一方案，范围是否只覆盖产品级、用户可见的 UI（导览菜单、tour 面板），不强制统一 `DiagnosticsHud`/`MarkerHookTestHud` 这类内部调试 HUD？
- **AI recommendation**：是——调试 HUD 图的是"能跑不挡视线"，统一规范对它没收益（现状：`MRSceneMenu`、`IteHmdPanel`/`IteEditorHud`、`DiagnosticsHud`、`MarkerHookTestHud` 四套 UI 各自为政，无统一层，对应 CLAUDE.md「PlatformRuntime 是唯一平台分支点」这条原则在 UI 侧还没落地）
- **User confirmation**：认可，未提出异议（讨论后转向了别的问题，未来续聊时应先复核这条仍然成立）

### Design — 输入交互层
- **Question**：现有 `MRCore.unity` 已挂 XRI 3.5.1 的 ray/poke interactor（`TrackedDeviceGraphicRaycaster` 等）。用户提到的三种展示方式——立体按压按钮、射线触发、手势——射线和按压是否本就是 XRI 统一喂给同一套 UI 目标的两种 interactor，不需要分别做两套 UI？
- **AI recommendation**：是，射线（far-field）+ poke（near-field）在 XRI 里天然是同一套输入抽象；手势（如 `PalmsTogetherGesture`）是脱离 raycaster 的独立识别，更适合做"呼出/收起面板"这类系统级触发，不是逐项选中
- **User confirmation**：认可这个区分；但明确表示「立体按压按钮」的视觉/物理保真度（真 3D 几何体+震动 vs uGUI 贴皮）**不是最优先级**，未继续深入

## Open assumptions [NEEDS CLARIFICATION]

- [ ] `[ASSUMED]` 「立体按压按钮」最终要不要做成真 3D 几何体+触觉反馈，还是先用 uGUI Button 顶上——用户明确说"不是最重要的"，但未给出替代优先级排序 — affects: design.md 的输入交互层选型
- [ ] `[ASSUMED]` 「翻掌呼出菜单」的手势基础形态**不能用单手翻掌/掌心朝脸**——这是结构性硬约束，不是概率性风险：Quest/PICO 的单手系统保留手势（对应手柄 Home 键那类"保底出口"）大概率两端都不可被 App 覆盖或吞掉（一般性判断，未查最新 Meta XR SDK / PICO PXR 文档验证，也未真机测试）— affects: proposal 里呼出手势的判定条件设计
- [ ] `[ASSUMED]` 呼出手势推荐做成**双手组合 pose**，复用/扩展 `PalmsTogetherGesture` 那套判定框架（JointMath + HandPoseComposite 双路径、hold 阈值、Performed/Released 事件），从架构上避开跟系统单手手势竞争——AI 提出但用户未最终确认，讨论被构建入口清理的问题打断 — affects: design.md 的手势层实现方案
- [ ] `[ASSUMED]` 「翻掌菜单」的产品定位——是**快捷动作呼出器**（跟头锁 `MRSceneMenu` 那种"一直可达"菜单互补、不互相替代），还是要**取代**现有头锁场景切换菜单——AI 建议前者（可达性场景不同），用户未表态 — affects: proposal 的功能范围与 `MRSceneMenu` 的去留
- [ ] `[ASSUMED]` Meta 官方是否真的开放"抑制系统手势 UI"的 API/manifest 开关，PICO 是否有等效机制——AI 判断"存在这类开关本身说明冲突是已知问题"，但没有查到具体 API 名称，需要读最新 SDK 文档或真机验证 — affects: 风险评估、是否需要一次真机 spike 才能定稿设计
- [ ] `[ASSUMED]` 本次统一方案是否要连带处理 `IteHost`（`IteHmdPanel`/`IteEditorHud`）——IteHost 是"宿主事件总线的形状"专属层，按 CLAUDE.md 的架构原则本不该被其他模块的 UI 规范污染，但它本身要不要遵循新的统一 UI 规范未讨论 — affects: L2 影响范围

## Suggested next step

- [ ] 续 `/opsx:probe xr-ui-interaction-unification`：优先敲定「呼出手势最终形态」和「翻掌菜单 vs 头锁菜单」两条开放假设，这两条决定 proposal 的核心范围
- [ ] 或直接 `/opsx:propose xr-ui-interaction-unification`，把上面 5 条开放假设原样带进 proposal.md，作为待定项而非默认结论
