# ITE 区域队列与当前 Tour（ite-current-tour）—— 设计

> 状态：已评审（对话中逐节确认），待写实施计划
> 日期：2026-09-23
> 范围：ITE 包里「人在哪些区域」「哪个 Tour 持有优先级」「扫码认哪个码」「提示什么」「`alwaysDisplayed` 何时显示」这几条规则，以及它们的模块划分；宿主侧只动诊断日志。
> 来源：`docs/handoff/2026-09-23-ite-region-trigger-issues.md` 的问题 1、问题 2。

## 1. 目标

1. 区域集合如实反映人在哪些区域里：同一个体积不会因为相机侧有多个碰撞体就被提前判成「已离开」（问题 2）。
2. 扫哪个码就显示哪个 Tour，不会因为锚定把体积挪了位置而被立刻切走（问题 1）。
3. 规则按「一个问题一个模块」拆开：改其中一条规则，只动一个模块，其他行为不受影响（用户要求，2026-09-23）。

## 2. 现状与根因

### 2.1 问题 2：同一体积的进出事件成对重复（静态证据完整，未上机确认）

相机侧有两个碰撞体都被当成「相机」：

| 碰撞体 | 位置 | 形状 |
|---|---|---|
| `SphereCollider` + kinematic `Rigidbody`（`IteDeviceMarkerRig.AttachCameraTrigger` 挂上去的） | Main Camera | 头部位置、半径 0.15 的球 |
| `CharacterController` | `XROrigin_Base` 根节点，是相机的祖先 | 半径 0.1、高约 1.36 的胶囊，从地面到头顶；`Locomotion/XRBodyTransformer`（`m_UseCharacterControllerIfExists: true`）让它跟着头移动 |

`SceneRoles.IsCamera` 把 `camera.IsChildOf(candidate)` 也算作相机（`SceneRoles.cs:24`，`CameraColliderMatchTests` 第 73 行把这一点写成了测试），所以 rig 根节点上的胶囊也算相机。CharacterController 进出静态 trigger 时会触发 `OnTriggerEnter/Exit`。球和胶囊形状不同，进出体积的时刻也不同，与真机日志的三种现象都对得上：加载时同一时刻两次 Enter；同一体积的 Exit 相隔 2 秒以上；Enter 和 Exit 都成对出现。

编辑器验收用的 `IteEditorHarness.prefab` 相机上只有一个胶囊，也没有 CharacterController 祖先，所以 Editor 里复现不了。

危害：`TourRegionPolicy.Apply` 在 Enter 时去重添加，在**第一次** Exit 就把 Tour 移出集合。实际上还有一个碰撞体在体积里，集合里却已经没有它了。

### 2.2 问题 1：扫码锚定后被区域重选立刻切走

锚定会移动 AnchorRoot/TourRoot，所有触发体积随之挪到真实位置。下一个物理步里，锚定引起的 Exit/Enter 到达；帧末按 `TourRegionPolicy.Decide` 的规则（在播 Tour 不在集合里，就从集合中的 `regionalTrigger` 里重选），刚扫的 Tour 被换掉。状态机 spec（`2026-09-23-ite-guide-state-machine-design.md`）D5 只保证了**锚定那一帧**不切换，下一个物理步照样会切。

问题 1 和问题 2 互相牵连：14:36:31 那次的 `Enter hncxtzfe_p4d`，可能只是胶囊从地面那一段碰进了 hncx 的体积。

## 3. 规则模型（用户梳理，2026-09-23）

**等待扫码**（冷启动；摘下后重新戴上；追踪原点重置；宿主要求重扫）
- 忽略所有碰撞。
- 扫任意码，就激活对应的 Tour 并锚定，然后进入「已定位」。这是唯一一个不受规则约束的入口。

**已定位**
- `alwaysDisplayed` 只在这个状态下显示。
- 有一个**当前 Tour**（下文记作 C），它持有最高优先级。首次扫码后，C 就是被扫的那个 Tour。
- **C 什么时候换**：只有人离开 C 的体积时才换（锚定引起的离开不算），换成队列里最后进入的 Tour，不区分 `regionalTrigger` 和 `normal`；队列为空时 C 为空。C 为空时取队尾。
- **C 什么时候播**：C 是 `regionalTrigger` 就立即播放；C 是 `normal` 就先提示「扫 C」，扫到 C 的码才播放。在这期间人走进别的区域，C 不变。
- **扫码只认 C 的码**，其他码一律忽略。

**不变式**（沿用状态机 spec 的 I1–I3，新增两条）
- **I4**：`ActiveTourId` 要么为空，要么等于 `CurrentTourId`。
- **I5**：C 永远不会是 `alwaysDisplayed` 的 Tour。

## 4. 决策

### D1 区域成员按碰撞体计数

**选了什么**：每个 Tour 一个计数。凡是 `SceneRoles.IsCamera` 认作相机的碰撞体，进入这个 Tour 的体积就加 1，离开就减 1。计数大于 0 就算人在这个区域里，归零才算离开。`IsCamera` 本身不改。

**后果**：「在区域里」的意思变成相机球体和 XR Origin 胶囊（从地面到头顶）**任意一个**碰到体积。

**替代方案**：
- 几何判定：每帧判断头部这一点落在哪些体积里，不再使用触发事件。否决（用户选择计数，2026-09-23）。
- 只认一个由宿主指定的碰撞体：多个碰撞体的问题解决了，但锚定时序问题（D9）依旧存在，而且要改宿主与包之间的接口。否决。

### D2 队列按进入先后排序，补位取队尾，删除随机挑选

**选了什么**：计数从 0 变成 1 时，tourId 追加到队尾；从 1 变成 0 时，从队列里删掉；从 1 变成 2 时顺序不变。离开后再进入，排到队尾。需要补位时取队尾。`TourGuide` 和 `TourDirector` 构造函数里的 `pick` 参数一起删掉。

**为什么**：最后进入的那个区域就是人刚走进去的地方，这是用户规定的语义（「选择最后进入的、并且还在区域内的那个 Tour」）。随机挑选既无法预测，也无法测试。

**替代方案**：保留随机挑选（源实现是 `OrderBy(Guid.NewGuid())`）。否决。

### D3 当前 Tour 与在播 Tour 分开；在播优先

**选了什么**：`TourGuide` 同时持有 `CurrentTourId`（持有优先级的 Tour）和 `ActiveTourId`（正在播的 Tour），两者满足 I4。只有人**离开** C 的体积时才换 C；进入别的区域、离开别的区域都不影响 C。

**为什么**：`normal` Tour 有「被选中了但还没播」这个阶段（D5），只用一个在播字段表达不了。用户的规则是：Tour 一旦被激活，优先级就最高，除非人主动离开。

**与现状的差异**：状态机 spec 的 D5 是「在播 Tour 不在集合里就重选」，改成「在播 Tour 离开集合才重选」。锚定后人本来就不在 C 的体积里时，C 保持不变，直到人走进去再走出来（D9）。

### D4 补位不区分展示类型

**选了什么**：补位取队尾，不管它是 `regionalTrigger` 还是 `normal`。队尾是 `normal` 时，它会挡住更早进入的 `regionalTrigger`：不播放，提示扫它的码。

**替代方案**：跳过 `normal`，取最靠后的 `regionalTrigger`。否决（用户确认，2026-09-23）。

### D5 碰撞只选 C，播不播由展示类型决定

**选了什么**：
- 碰撞（区域进出）只做两件事：更新队列；在规则允许时更换 C。
- C 被选中之后要不要立即播放，只由展示类型决定，集中在 `TourAssembly.PlaysOnSelect(displayType)`：`regionalTrigger` 立即播放，`normal` 等扫码。
- **碰撞永远不会激活 `normal`。**

| 来源 | `regionalTrigger` | `normal` | `alwaysDisplayed` |
|---|---|---|---|
| 扫码，等待扫码状态 | 激活并锚定 | 激活并锚定 | 锚定，C 为空（D10） |
| 扫码，已定位（只认 C 的码，D6） | 已在播：二次锚定；否则：激活 | 激活 | 忽略（不可能是 C，I5） |
| 碰撞，选为 C | 立即播放 | 提示扫码，扫到才播放 | 没有体积 |
| 碰撞，离开 C | 停播，重新选 C | 同左 | — |

### D6 已定位后扫码只认 C 的码（取代区域门禁 spec D1）

**选了什么**：`TourScanPolicy` 在已定位状态下：扫到的码不是 C 的，一律忽略；是 C 的，按上表处理。`ScanState` 删掉 `PendingTourIds`，增加 `CurrentTourId`，扫码策略不再读队列。

**与现状的差异**：原来是「所在区域里任何一个 Tour 的码都认」。现在人站在 N 的区域里、而 C 是 R 时，扫 N 的码会被忽略。

**替代方案**：扫码不受在播优先的约束。否决（用户梳理的流程：「只接受当前 tour 的码」）。

### D7 扫码提示只看 C（取代区域门禁 spec D3）

**选了什么**：`ScanPromptPolicy` 的规则：
- 等待扫码：提示「扫任意码」（不变）；
- 摘下头显：隐藏（不变）；
- 已定位：C 是 `normal` 且未播，提示 `[C]`；其他情况隐藏。

提示策略也不再读队列。

**与现状的差异**：原来是「没有 Tour 在播、区域里也没有 `regionalTrigger` 时，列出区域内所有 `normal`」。

### D8 `alwaysDisplayed` 只在已定位状态下显示

> 显示规则不变；「隐藏只切 `activeSelf`、不拆内容树」这一实现方式已被 D13 取代（2026-09-24，整分支审查 C1）。

**选了什么**：进入已定位时显示全部 `alwaysDisplayed`，离开已定位时隐藏。隐藏只切换 Tour 对象的 `activeSelf`，不拆内容树（沿用 `RetainsSceneWhenDeactivated`）。时机：
- Tour 装配完成后（`CreateAsync` 返回时）按当前状态设一次；
- 进入或离开已定位时，对全部 `alwaysDisplayed` 设一次。

装配过程中，`Enable()` 建树时对象是激活的，因此在装配完成前可能短暂可见，这与现状相同。

**为什么**：用户的流程规定 `alwaysDisplayed` 在首次扫码激活之后才出现。现状是加载完就显示：锚定前位置不对，摘下再戴上后也仍按旧锚定显示。这修订了状态机 spec I1 中「`alwaysDisplayed` 不受状态影响」的说明。

**实现位置**：状态转换时，`TourGuide` 在 `GuideEffect` 里给出显隐（新增一个字段）；`TourDirector` 负责落到对象上。「已定位就显示」这条规则只写在 `TourGuide` 里。

### D9 锚定结算窗口：锚定引起的区域变化不算人移动

**选了什么**：
- **开窗**：`TourGuide` 发出任何会移动体积的效果时开窗。这类效果包括：带锚定位姿的激活（首次扫码、已定位后扫 `normal` 的码）和二次锚定。开窗取代 `SubmitScan` 里现有的 `_frameTourIds = _pendingTourIds`。
- **窗口期间**：进出事件照常更新计数和队列（I3），但帧末不评估 C 要不要换。
- **关窗**：锚定之后第一次 `WaitForFixedUpdate` 返回时关窗，并把基准设为当前队列。
- **关窗之后**：帧末照常按「基准 → 当前」评估。

**为什么时点是确定的**：本工程物理按 FixedUpdate 自动模拟，`autoSyncTransforms=0`，`Fixed Timestep` 为 0.02 秒。Unity 每个物理步的顺序是：FixedUpdate → 物理模拟（transform 改动在这里同步进物理）→ `OnTrigger*` → `WaitForFixedUpdate`。所以锚定后第一次 `WaitForFixedUpdate` 返回时，锚定造成的进出事件已经全部到达。帧里没有物理步时，窗口顺延到下一个有物理步的帧，这期间也不会有任何进出事件。

**防御**：
- 锚定发生在物理阶段内时（`Time.inFixedTimeStep` 为真，例如宿主在物理回调里调用了激活），这一步的模拟可能已经跑完，所以多等一个物理步再关窗。这个判断放在效果层，`TourGuide` 保持纯逻辑。
- 运行时启动时检查 `Physics.simulationMode == SimulationMode.FixedUpdate`，不满足就报 `LogError`。否则窗口时序不成立，只会表现为真机上静默失效。

**替代方案**：
- 锚定引起的离开也算离开，立即补位。否决（用户选择，2026-09-23）：扫码后仍会被切走。
- 用固定时长作宽限期：依赖帧率和物理步长，两个平台时序不同，否决。

### D10 首次扫码扫到 `alwaysDisplayed` 的码：锚定，但 C 为空；`ActivateTour` 拒绝 `alwaysDisplayed`

**选了什么**：
- 等待扫码状态下扫到 `alwaysDisplayed` Tour 的码：用它锚定并进入已定位，但 C 为空、不播放任何 Tour。之后按 C 为空的规则补位（取队尾）。
- `IteRuntime.ActivateTour(id)` 遇到 `alwaysDisplayed` 时返回 false 并打日志。

**为什么**：`alwaysDisplayed` 没有触发体积，一旦成为 C，就永远「离不开」，C 会被它一直占着，直到摘下头显或要求重扫。它的显示由 D8 按状态决定，不需要成为 C。这条决策保证 I5。

**与现状的差异**：现在它会被设为在播 Tour。旧规则下，下一次区域变化就会把它换掉（它不在任何区域集合里）；对它调用 `Disable()` 时，因为 `RetainsSceneWhenDeactivated`，内容保留、不会被隐藏，所以这个问题一直没有暴露。在新的在播优先规则（D3）下，它会一直占着 C。

### D11 计数的防御

- **多余的 Exit**（计数为 0 时收到 Exit）：`RegionQueue` 拒收并返回 false，效果层打警告。计数永远不会变成负数。
- **体积被停用**：Unity 在碰撞体停用时不发 Exit。`IteTourObject.SetVolumeObjectActive(false)` 另发一条「清除」通知，把这个 Tour 的计数清零，效果等同于人离开。体积重新启用时，Unity 会对仍在重叠的碰撞体补发 Enter。
- **相机侧碰撞体被销毁**（`IteDeviceMarkerRig.OnDestroy`）：只在 ITE 整体卸载时发生，这时 runtime 也一起销毁，不处理。

### D12 模块划分：一个问题一个模块

| 模块 | 回答的问题 | 输入 | 输出 | 刻意不知道 |
|---|---|---|---|---|
| `RegionQueue`（新增） | 人在哪些区域里，按什么先后 | 进入 / 离开 / 清除某个 tourId | 有序 tourId 列表 | 展示类型、导览状态、锚定 |
| `RegionBaseline`（新增） | 这次变化是人动了，还是锚定挪了体积 | 开窗、物理步结束、帧末取值 | 基准队列和当前队列（窗口内不输出） | 展示类型、C |
| `CurrentTourRule`（新增，取代 `TourRegionPolicy`） | C 该换成谁 | C、基准队列、当前队列 | 新的 C | 展示类型、导览状态 |
| `TourAssembly.PlaysOnSelect`（新增一条） | 被选为 C 后是否立即播放 | 展示类型 | bool | — |
| `TourScanPolicy`（修改） | 这次扫码认不认 | 导览状态、C、在播、扫到的码 | 激活 / 二次锚定 / 忽略 | 队列 |
| `ScanPromptPolicy`（修改） | 提示什么 | 导览状态、C、在播 | 扫码提示 | 队列 |
| `TourGuide`（瘦身） | 按什么顺序调用上面这些模块 | 各种输入 | `GuideEffect` | 规则细节 |
| `TourDirector`（小改） | 把效果落到场景对象上 | `GuideEffect` | — | 规则 |

**关键解耦点**
- 只有 `CurrentTourRule` 读队列。区域对行为的影响只经过「C 是谁」这一个出口；`ScanState` 删掉 `PendingTourIds`，两个策略从结构上就无法依赖队列。
- `CurrentTourRule` 不认识展示类型。展示类型只出现在 `TourAssembly`、扫码策略、提示策略这三处。
- `TourGuide` 只负责按顺序调用，不包含规则。

**改一条规则只动哪里**

| 要改的规则 | 只改这个模块 |
|---|---|
| 计数、多个碰撞体 | `RegionQueue` |
| 什么算人动了（结算窗口） | `RegionBaseline` |
| 补位、优先级 | `CurrentTourRule` |
| 哪种展示类型一选中就播放 | `TourAssembly` |
| 扫码门禁 | `TourScanPolicy` |
| 扫码提示 | `ScanPromptPolicy` |
| 摘戴头显、要求重扫、`alwaysDisplayed` 显隐 | `TourGuide` 的状态转换 |

### D13 `alwaysDisplayed` 按导览状态建树、拆树（取代 D8 的实现方式，修订 design D29）

**选了什么**：规则只有一条——`alwaysDisplayed` 可见 ⇔ 内容树已建好。
- 进入已定位：对全部 `alwaysDisplayed` 调 `Enable()` 建树。建完派发 `OnTourSceneLoaded`，LoadTrigger 的首屏效果在这时触发。
- 离开已定位（摘下、要求重扫）：调 `Disable()`，完整拆树。
- 装配时不再建树：`CreateTourObject` 对所有展示类型都以 `Disable()` 收尾，删掉 `alwaysDisplayed` 装配即 `await Enable()` 的分支（design D29 的做法）。
- 删除 `TourAssembly.RetainsSceneWhenDeactivated`：`alwaysDisplayed` 不再是「停用时保留内容树」的特例，和 `normal`、`regionalTrigger` 走同一条 `Enable` / `Disable` 路径。
- 时机沿用 D8：`GuideEffect.AlwaysDisplayedVisible` 在进出已定位时给出；每个 Tour 装配完成后按当前状态设一次（覆盖「已定位之后才装配完」的 Tour）。
- `Enable()` 在这里是发出即走，不再挂在加载链上等。建树抛错时必须打 `LogException`，不能变成没人观察的 Task 异常。
- 建到一半就离开已定位：沿用 `TourSceneLifecycle` 的世代作废（既有机制，`TourSceneLifecycleTests` 已覆盖），拆树会作废进行中的构建。
- 资源不重载：`LoadAssets` 只在装配时做一次，`ReleaseAssets` 只在卸载（`Destroy`）时做。重建只实例化实体、跑各组件的 `Constructor`（视频要重新 `Prepare`）。

**随之删除**：`IteTourObject.SetContentVisible`（D8 的实现；spec 原写 `SetShown` 切 Tour 对象 `activeSelf`，实现改成切内容根，这个偏离当时没记，一并在此了结）；`IteRuntime` 在 `TourActivated` 时给已建好树的 Tour 补发 `OnTourSceneLoaded` 的分支——D10 之后 `alwaysDisplayed` 不会被激活，别的类型被激活时内容一定还没建好，这个分支走不到。

**为什么**：内容组件把 `OnDisable` 当拆除用，停用内容根不是可恢复的隐藏。
- `VideoPlaneElement.OnDisable` 销毁 `VideoPlayer` 并释放 RenderTexture，二者只在 `Constructor` 里建、没有 `OnEnable` 重建：重新显示后视频面是白板，点击无反应。
- Spin、局部动画、`AutoRotate` 在 `OnDisable` 里复位，显隐动画协程停在半途。
- LoadTrigger 的首屏效果只在建树时派发一次：按 D8 的做法，冷启动时 `alwaysDisplayed` 在看不见的时候建树、放完首屏效果，然后才被隐藏。

改动前 `alwaysDisplayed` 从不被停用，所以 D8 的做法是回归（整分支审查 C1，已核实）。让「可见」与「已建树」等价，生命周期由一条规则决定，不依赖每个组件（包括以后加的）都能扛住停用。

**替代方案**：
- A：让 `VideoPlaneElement` 对停用对称（停用只暂停，销毁挪到 `OnDestroy`，启用时恢复）。只修了视频，首屏效果仍被吞，而且要求以后每个组件都扛住停用。否决（用户选择，2026-09-24）。
- D8 原做法（停用 Tour 对象或内容根）：即上面的回归。否决。

**代价**：
- 每次重新定位（摘下再戴上、重定位后再扫码）都重建一次内容，首屏效果每次都重放——和 `normal` / `regionalTrigger` 每次激活都重建一致。
- 宿主收到 `alwaysDisplayed` 的 `OnTourSceneLoaded` 从加载期挪到定位之后，而且每次定位都会收到。宿主 `IteHostBootstrap` 只对「刚激活、正在等」的 Tour 处理这个事件，其余只打日志，不受影响。
- 加载链不再等 `alwaysDisplayed` 建树，加载完成得更早；建树失败不再让加载链失败，改为打 `LogException`。

### D14 帧驱动在 `OnEnable` 启动物理步协程

**选了什么**：`IteRuntimeDriver` 在 `OnEnable` 里启动 `WaitForFixedUpdate` 循环（D9 的关窗通知），不再用 `Start`。对象停用时 Unity 自动停掉协程，再启用时重新启动。

**为什么**：用 `Start` 启动时，驱动对象停用再启用后协程永久消失，`LateUpdate` 却照常恢复。之后每次锚定，结算窗口都关不上，当前 Tour 再也不换，而且不报错——只是日志里少了「锚定结算完成」那一行（整分支审查 M3）。

**替代方案**：加看门狗（窗口持续超过若干帧就报错）。不做：已知的原因已经修掉，看门狗防的是推测出来的原因；真机上出现窗口长时间不关时再加。

**验证**：EditMode 不跑协程，只能靠真机日志（§10 第 6 条）。

## 5. 数据流

```
物理 Enter/Exit、体积停用 ──▶ RegionQueue
移动体积的效果 ── 开窗 ──▶ RegionBaseline ◀── 物理步结束（WaitForFixedUpdate）
                                   │ 帧末：基准队列 + 当前队列（窗口内不输出）
                                   ▼
                           CurrentTourRule ──▶ 新的 C
                                   ▼
               TourAssembly.PlaysOnSelect：立即播放 / 等扫码
                                   ▼
扫码 ─▶ TourScanPolicy ──▶ GuideEffect ──▶ TourDirector 执行
（状态, C, 在播）──▶ ScanPromptPolicy ──▶ 提示
```

### 5.1 `CurrentTourRule`

```
Next(C, 基准, 当前):
  C 为空                         → 当前队列的队尾（队列为空则为空）
  C 在基准里，且不在当前队列里   → 当前队列的队尾（队列为空则为空）
  否则                           → C
```

- 只在已定位、且结算窗口关闭时评估（I2）。
- 同一帧里离开 A 又进入 A：帧末 A 仍在队列里，C 不变（保留状态机 spec D5 的这条行为），但 A 会排到队尾。
- 同一物理步里从 A 走到 B：C 直接从 A 换成 B，中间不会先停播。
- C 走进去再走出来：走进去时 C 不在基准里，C 不变；走出来时 C 在基准里、不在当前队列里，于是补位。

### 5.2 帧末的执行顺序（`TourGuide.EndOfFrame`）

1. `RegionBaseline` 给出（基准，当前）；不在已定位状态，或者窗口开着时，跳过第 2–3 步。
2. `CurrentTourRule` 算出新的 C。
3. C 变化时：停掉在播的 Tour（如果有）；新的 C 满足 `PlaysOnSelect` 就激活它（沿用现有锚定，不带位姿），否则只记下 C。
4. 基准前移到当前队列。
5. 快照（状态、C、在播）有变化，就重算提示（沿用状态机 spec D6，快照里去掉队列）。

## 6. 对外接口变化

| 接口 | 变化 |
|---|---|
| `IteRuntime.ActivateTour(string)` | 已定位时把 C 和在播都设为这个 id；遇到 `alwaysDisplayed` 返回 false（D10） |
| `IteRuntime.PendingTourIds` | 类型不变，语义改为按进入先后排序的队列（D2） |
| `ScanState` | 删除 `PendingTourIds`，新增 `CurrentTourId`（D6、D12） |
| `TourDirector(assembler, pick)` / `TourGuide(pick)` | 删除 `pick` 参数（D2） |
| `TourDirector` | 新增 `CurrentTourId`、`AfterPhysicsStep()`（D9） |
| `TourRegionPolicy`、`RegionDecision` | 删除，由 `RegionQueue` / `RegionBaseline` / `CurrentTourRule` 取代 |
| `IteTourObject.OnCameraVolumeTransition` | 增加碰撞体名字参数（只用于日志）；新增「体积清除」通知（D11）。显隐不另设接口，走 `Enable()` / `Disable()`（D13） |
| `IteRuntimeDriver` | 新增常驻协程，每次 `WaitForFixedUpdate` 后调用 `Director.AfterPhysicsStep()`（D9），在 `OnEnable` 启动（D14） |
| `TourAssembly.RetainsSceneWhenDeactivated` | 删除（D13） |
| `IteRuntime.OnTourSceneLoaded` | `alwaysDisplayed` 的这个事件从加载期挪到每次进入已定位后（D13） |
| `TourAssembly` | 新增 `PlaysOnSelect(displayType)`（D5） |

宿主（`Assets/Scripts/IteHost`）不需要改代码：`IteEditorHud` 读的是 `ActiveTourId` 和 `PendingTourIds`，这两个类型都不变。

## 7. 行为变化清单

1. 相机侧有多个碰撞体时，要所有碰撞体都离开，区域才算离开（D1，修问题 2）。
2. 锚定后，刚扫的 Tour 不会因为锚定引起的区域变化被切走（D9，修问题 1）。
3. 进入别的区域不会打断在播或已选中的 Tour；只有离开它才会切换（D3）。
4. 补位从随机挑选改为取队尾，而且不区分展示类型（D2、D4）。
5. 已定位后只认 C 的码（D6）。
6. 已定位后只在 C 是 `normal` 且未播时提示，只提示 C（D7）。
7. `alwaysDisplayed` 在等待扫码和摘下状态下隐藏（D8）：离开已定位时拆树，进入已定位时建树，首屏效果在看得见时触发（D13）。
8. 首次扫到 `alwaysDisplayed` 的码只做锚定；`ActivateTour` 拒绝 `alwaysDisplayed`（D10）。
9. 帧驱动被停用再启用后，结算窗口仍能关上（D14）。

## 8. 诊断日志

- 区域进出：`[ITE] 区域 Enter ujf5 (Main Camera, 0→1) 队列=[…]`，带碰撞体名字和计数变化。有了这一行，上机就能确认 §2.1 的根因。
- 多余的 Exit：警告，带碰撞体名字。
- 结算窗口开、关各打一行，关窗时带上 rebase 后的队列。
- C 变化：`[ITE] 当前 Tour：A → B（离开 A，队尾）`，说明原因；B 是 `normal` 时注明「等扫码」。
- 启动时 `simulationMode` 不是 FixedUpdate：`LogError`。

## 9. 测试

EditMode 纯逻辑测试，放在 `Packages/com.uality.ite-tour/Tests/Editor/`。

**按模块**
- `RegionQueue`：计数加减；顺序；「计数 > 0 ⟺ 在列表里」；多余 Exit 被拒；清除。按真机日志回放双碰撞体：`qtcljiro` 要等两个碰撞体都离开才出队。
- `RegionBaseline`：窗口内的变化在帧末看不到；关窗时 rebase；物理阶段内开窗时多等一步。
- `CurrentTourRule`：用表格覆盖 §5.1 的每个分支。
- `TourAssembly.PlaysOnSelect`：`regionalTrigger` 为 true，`normal` 为 false。
- `TourScanPolicy`：已定位时只认 C 的码；等待扫码时认任意码；等待扫码时扫到 `alwaysDisplayed`（D10）。
- `ScanPromptPolicy`：C 是 `normal` 且未播，提示 `[C]`；其他已定位情况隐藏；等待扫码时「扫任意码」。

**`TourGuide` 组合**
- 回放 14:36:31：扫 ujf5 → 窗口内 ujf5 离开、hncx 进入 → 关窗 → C 仍是 ujf5 且在播。之后人走进 ujf5 再走出来 → C 换成 hncx。
- C 是 `normal` 且未播：进入别的 `regionalTrigger` 区域不切换；扫别的码被忽略；扫 C 的码就激活并开窗。
- 队列只有 `normal` 时，帧末不会激活任何 Tour（碰撞永远不激活 `normal`）。
- 进入、离开已定位时，`alwaysDisplayed` 的显隐效果正确。
- 维持 I4、I5。

**`alwaysDisplayed` 生命周期（D13）**：用内存里搭的最小 Tour 预制体（带 `IteTourObject`、体积和内容根子物体）走 `IteTourAssembler`，内容为空时建树、拆树都同步完成。
- 装配完成后没有建树。
- 设为可见：建树，派发一次 `OnTourSceneLoaded`。
- 设为不可见：拆树。
- 再设为可见：重建，再派发一次 `OnTourSceneLoaded`。
- 非 `alwaysDisplayed` 的 Tour 不受影响。

**改动已有测试**
- `TourRegionPolicyTests`：删除，其中的用例迁移到新模块。
- `TourGuideTests`、`TourScanPolicyTests`、`ScanPromptPolicyTests`：按新规则更新（去掉 `pick`；`ScanState` 字段变化；区域门禁相关用例改为 C 门禁）。
- `CameraColliderMatchTests`：保留（`IsCamera` 不变）。
- `TourAssemblyTests`：删掉 `RetainsSceneWhenDeactivated` 的用例（D13）。

**回归**：在已打开的 Editor 里跑 `Uality.IteTour.Tests` 和 `MRBase.Ite.Host.Tests`，全部通过。

## 10. 真机验证（用户说「打包」才出包）

1. 日志里同一个体积的计数会到 2（Main Camera 和 XR Origin），全部离开才出队，以此确认 §2.1。
2. 扫 tag 0 后内容不被切走；走进 ujf5 的体积再走出来，才切换。
3. C 是 `normal` 时，走进 `regionalTrigger` 区域不切换；只提示 C；扫 C 才播放。
4. 冷启动和摘下再戴上后，`alwaysDisplayed` 不可见；扫码之后出现，里面的视频能播、能点，首屏效果在出现时播放（D13）。
5. 重复摘下、戴上、扫码几次，`alwaysDisplayed` 每次都正常出现，日志里每次定位后都有它的 `OnTourSceneLoaded`。
6. 每次扫码后日志里都有「锚定结算完成」；锚定引起的区域进出都出现在这一行之前（D9、D14）。
7. 人完全走出一个区域后，这个区域的计数回到 0。停在 1 说明相机侧某个碰撞体被停用再启用过（Unity 补发 Enter、不发 Exit）。

## 11. 不在范围内

- 触发体积尺寸减半（区域门禁 spec D4），维持不变。
- 区域门禁 spec D5（定位偏了只能靠重新佩戴恢复），仍待用户确认。
- 稳定器计时（状态机 spec §9）。
- XR rig 上 CharacterController 的去留：它属于 MRCore 的移动系统，本次只是把它当作一个相机侧碰撞体来计数。

## 12. 与既有文档的关系

- `2026-09-23-ite-scan-region-gate-design.md`：D1（定位后只认所在区域的码）由本文 D6 取代；D3（区域外不提示）由本文 D7 取代；D2、D4 不变；D5 维持待确认。
- `2026-09-23-ite-guide-state-machine-design.md`：D5（帧末按集合的净变化重选、随机挑选）由本文 D3、D2、D9 取代；D6 的快照去掉队列；I1 中「`alwaysDisplayed` 不受状态影响」由本文 D8 修订；三态与状态转换（§3）不变。
- design D29（`alwaysDisplayed` 装配时即建树）：由本文 D13 修订为「进入已定位时建树」。原文档随 `openspec/` 删除，决策记录在 `IteTourObject.CreateTourObject` 的注释里，D13 实施时一并改掉。
- `docs/handoff/2026-09-23-ite-region-trigger-issues.md`：本文对应其中的问题 1 和问题 2。
