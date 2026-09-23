# ITE 导览状态机（ite-guide-state-machine）—— 设计

> 状态：已评审，已实现（待真机验证）
> 日期：2026-09-23
> 范围：ITE 包 `TourDirector` 的状态表达方式与三个策略函数的入参；`IteRuntime` 对外 API 的少量调整；宿主 `IteHmdPanel`、`IteDeviceMarkerRig` 的重定位提示。碰撞体事件重复、稳定器计时、扫码后被区域切走三个问题不在本次范围（见 §9）。

## 1. 目标

把「导览现在处于什么状态」从几个互相牵制的 bool 收成**一个显式的状态**，每个状态的含义、进入方式、允许的行为都写死在一处。直接动因是 PICO 实测发现的 bug：摘下头显期间，区域仍能唤醒 Tour，戴上后看到的是一个已在播的 Tour，没有扫码提示。

用户的要求（2026-09-23）：
- 「等待扫码定位」是一个状态，和第一次扫描完全一样。运行中可以通过多种途径**直接进入**这个状态，而不一定是被某个事件激活。
- 不要用多个 bool 控制状态：状态一多就很难控制。

## 2. 现状（读码 + 真机日志）

### 2.1 状态散在 5 个字段里

`Packages/com.uality.ite-tour/Runtime/Core/TourDirector.cs`：

| 字段 | 性质 | 问题 |
|---|---|---|
| `_paused` | 模式（头显摘下） | 与下一行共同表达一个状态，可组合出 4 种，部分组合没有意义 |
| `_forcedScanPending` | 模式（必须扫码） | 只在戴上、冷启动、`RequireScan()` 时置位；**摘下时不置位** |
| `_activeTour` | 数据（在播 Tour） | — |
| `_reselectPending` + `_reselectCandidates` | 延迟动作（帧末区域重选） | 同一帧内先离开 A、再进入 A，帧末仍会按离开时算出的候选切走 A |
| `_promptDirty` | 脏标记（提示待重算） | 每个入口都要记得置位，漏一处提示就不更新 |

三个策略函数（`TourScanPolicy` / `ScanPromptPolicy` / `TourRegionPolicy`）通过 `ScanState` 读这两个 bool，**各自只看其中一部分**：`TourScanPolicy` 看 `Paused` 和 `ForcedScanPending`，`TourRegionPolicy` 只看 `ForcedScanPending`。

### 2.2 bug：摘下期间区域能唤醒 Tour

`SetHeadsetMounted(false)` 只停掉在播的 Tour 并置 `_paused = true`，`_forcedScanPending` 保持原值；如果之前已经扫过码，它就是 false。摘下期间应用照常运行、头显仍在追踪（PICO 实测：摘下状态下仍在持续产生区域进出事件和标记识别）。`TourRegionPolicy.Decide` 不看 `Paused`，于是：在播为空、区域「进入」事件到来时判定需要重选，激活区域里的 `regionalTrigger`（DazuRockCarvings 场景的 11 个 Tour 全是这种类型）。

戴上时虽然 `_forcedScanPending = true`，但摘下期间被激活的 Tour 仍在播。`ScanPromptPolicy` 的规则是「有 Tour 在播就不提示」，于是用户戴上后看到的是一个已在播的 Tour，没有扫码提示，用的还是旧锚定。

### 2.3 运行中要求重扫时不停掉在播的 Tour

`IteRuntime.RequireScan()`（追踪原点重置时由 `IteDeviceMarkerRig.HandleRecentered` 调用）只置 `_forcedScanPending`，注释明确写着「不停用当前 Tour」。于是出现了「必须扫码但有 Tour 在播」的组合：内容按已作废的锚定继续显示，提示是 Hidden。重定位黄条靠 `IteHmdPanel.NotifyRecentered()` 另行打开，只有在收到一次 Visible 提示时才清掉（见 `docs/superpowers/specs/2026-09-23-ite-scan-region-gate-design.md` §5）。

## 3. 状态模型

### 3.1 三个状态

```csharp
public enum GuideState
{
    Suspended,     // 头显摘下
    AwaitingScan,  // 等待扫码定位（与冷启动相同）
    Anchored,      // 已定位，正常导览
}
```

| 状态 | 扫码 | 区域唤醒 Tour | 在播 Tour | 扫码提示 |
|---|---|---|---|---|
| `Suspended` | 忽略 | 否 | 无 | 隐藏 |
| `AwaitingScan` | 任一已装配 Tour 的码都激活并锚定，不看区域、不看展示类型；随即进入 `Anchored` | 否 | 无 | 「扫任意码」 |
| `Anchored` | 区域门禁（区域门禁 spec D1）+ 按展示类型处理（与现状相同） | 是（`regionalTrigger`，见 D5） | 有或无 | 与现状相同（在播隐藏；区域外隐藏；区域内有 `regionalTrigger` 隐藏；只有 `normal` 列出其名字） |

**不变式**
- **I1**：只有 `Anchored` 状态下才可能有 Tour 在播（由 `TourDirector` 管的那一个）。`alwaysDisplayed` 的 Tour 在装配时就建树常驻，不属于「在播」，不受状态影响，与现状一致。
- **I2**：只有 `Anchored` 状态下区域才会唤醒 Tour。
- **I3**：「所在区域」集合在所有状态下都照常随进出事件更新，状态只决定要不要据此行动。

### 3.2 转换

```
             摘下（任何状态）
   ┌─────────────────────────────────┐
   ▼                                 │
Suspended ──戴上──▶ AwaitingScan ──扫码成功──▶ Anchored
                     ▲    ▲                      │
                     │    └──RequireScan(原因)────┘
                     └──RequireScan（在 AwaitingScan 时：原因更新，状态不变）
```

| 事件 | 从 | 到 | 附带动作 |
|---|---|---|---|
| 构造 | — | `AwaitingScan`（`ColdStart`） | — |
| 摘下 | 任何 | `Suspended`（`HeadsetRemoved`） | 停掉在播 Tour |
| 戴上 | `Suspended` | `AwaitingScan`（`HeadsetMounted`） | — |
| 戴上 | 其他 | 不变 | — |
| `RequireScan(原因)` | `Anchored` / `AwaitingScan` | `AwaitingScan`（该原因） | 停掉在播 Tour |
| `RequireScan(原因)` | `Suspended` | 不变 | — |
| 扫码生效 | `AwaitingScan` | `Anchored`（`Scanned`） | 激活并锚定 |
| 扫码生效 | `Anchored` | 不变 | 激活 / 重锚（与现状相同） |

原因：

```csharp
public enum GuideStateReason { ColdStart, HeadsetRemoved, HeadsetMounted, Recentered, HostRequested, Scanned }
```

## 4. 决策

### D1 一个状态枚举取代 `_paused` / `_forcedScanPending`

**选了什么**：`GuideState` 三值枚举，状态转换只能经 §3.2 列出的入口；三个策略函数的 `ScanState` 用 `GuideState State` 取代 `bool Paused` + `bool ForcedScanPending`，各自按状态分支。

**替代方案**：
- 保留两个 bool，把转换集中到一个方法里：组合爆炸还在，新加一个模式就翻倍。否决。
- 状态模式（每个状态一个类）：3 个状态用不着，分散了本来很短的逻辑。否决。

### D2 运行中进入 `AwaitingScan` 时停掉在播的 Tour（偏离现状）

**选了什么**：`RequireScan` 与冷启动、戴上进入的是同一个状态，停掉在播 Tour，提示「扫任意码」。

**为什么**：锚定作废之后继续显示内容，内容就是错位的。「等待扫码但有 Tour 在播」这种组合也正是 2.3 里黄条问题的根源。（用户已确认，2026-09-23。）

**替代方案**：
- 保留在播的 Tour，等扫码后再重新锚定：即现状，否决。
- 由调用方决定停不停：同一个状态会有两种表现，否决。

### D3 `Suspended` 下 `RequireScan` 不切换状态

**为什么**：摘下时锚定已视为作废，戴上一定会进入 `AwaitingScan`，所以这里不需要再切。在 `Suspended` 下切到 `AwaitingScan` 反而会让摘下期间开始认扫码（头显放在桌上对着码时会误锚定）。冷启动加载途中摘下的情况也由此自然覆盖。

### D4 `ActivateTour(tourId)` 只在 `Anchored` 下可用

**选了什么**：接口保留（用户要求），签名不变。`Anchored` 下按现有锚定激活；其他状态下返回 false 并打日志。

**为什么**：否则 `AwaitingScan` 下会冒出在播的 Tour，违反 I1。目前没有宿主调用方，唯一的测试调用的是不存在的 id，结果不变。

### D5 区域重选改为「帧末按集合的净变化判一次」，去掉 `_reselectPending`

**选了什么**：
- 区域进出事件只更新「所在区域」集合（I3），不再在事件上做重选判断。
- 帧末：若状态是 `Anchored`，且所在区域集合（按集合比较）与上一帧末不同，则判一次：在播 Tour 仍在集合内，就保持；否则停掉它，并在集合中的 `regionalTrigger` 里随机激活一个（没有就不激活）。
- 每帧末都记下当前集合，作为下一帧比较的基准，所有状态都记。
- 纯决策留在 `TourRegionPolicy`：一个函数把进出事件应用到集合上，一个函数按「状态 + 在播 + 集合」给出重选结论。

**为什么**：判断依据是数据（前后两帧的集合），不是「有人记得置位」的标志。只在 `Anchored` 下判断，I2 由结构保证。

**与现状的差异**（有意为之）：
- 同一帧里离开 A 又进入 A：现状会按「离开」时算出的候选切走 A；新规则下集合净变化为零，不切。
- 没有 Tour 在播时，离开某个区域后仍在一个 `regionalTrigger` 区域里：现状只在「进入」事件上重选，所以不激活；新规则会激活它。这与「在 `regionalTrigger` 区域里就自动播放」的语义一致。

**不变的**：刚锚定完的那一帧，集合还没有随体积移动而更新（要等下一个物理步产生进出事件），因此不会立即重选。锚定后被区域切走的问题（handoff 问题 1）不在本次范围。

**替代方案**：每帧无条件按「在播必须在集合内」判一次。否决：刚锚定完的那一帧，集合还是锚定前体积位置下的旧值，会立刻把刚扫的 Tour 切走。

### D6 扫码提示改为「帧末按状态快照的变化重算」，去掉 `_promptDirty`

**选了什么**：帧末把（状态、在播 tourId、所在区域集合）和上一帧末比较，有变化就重算提示，提示与上次不同才广播（沿用 `SamePrompt`）。

**为什么**：与 D5 相同，靠数据比较而不是靠每个入口记得置位。不每帧重算，是为了避免每帧都分配一次 Tour 描述列表（头显上的 GC 压力）。

### D7 状态核心做成纯逻辑，`TourDirector` 只做「照做」

**选了什么**：新增纯 C# 的状态核心（不碰 GameObject），持有状态、原因、在播 tourId、所在区域集合、上一帧快照。每个输入（摘下、戴上、`RequireScan`、扫码、区域进出、帧末、`ActivateTour`）返回要执行的动作：停用、激活（可带锚定位姿）、重锚。`TourDirector` 只负责把动作落到 `IteTourObject` 上。随机挑选通过构造参数注入，测试时可以换成确定性的挑法。

**为什么**：转换规则是这次改动的核心，必须能在 EditMode 里逐条测试。`IteTourObject` 是依赖内容加载的 MonoBehaviour，无法在测试里构造。这也延续了本包「决策在纯函数里，`TourDirector` 只剩照做」的既有结构（见 `TourDirector` 类注释）。

**替代方案**：状态留在 `TourDirector` 里，用真实的 `IteTourObject` 做测试。否决：测试要走内容加载链路，EditMode 跑不起来。

### D8 状态变化对外广播，重定位黄条改为跟随状态

**选了什么**：
- `IteRuntime` 新增事件 `OnGuideStateChanged(GuideState, GuideStateReason)` 和只读属性 `GuideState`。
- `IteRuntime.RequireScan()` 增加可选参数 `GuideStateReason reason = HostRequested`；`IteDeviceMarkerRig.HandleRecentered` 传 `Recentered`。
- `IteHmdPanel` 订阅状态变化：`状态 == AwaitingScan && 原因 == Recentered` 时显示黄条，状态一变就清掉。
- 删掉 `IteHmdPanel.NotifyRecentered()`，以及 `IteDeviceMarkerRig` 里查找面板的那段代码。

**为什么**：黄条本来就表达一个状态（「因为重定位，正在等待扫码」），跟着状态走就不会残留，区域门禁 spec §5 遗留的黄条问题一并解决。宿主和包之间也不再需要额外的「通知面板」通道。

### D9 冷启动不再在加载完成时额外调用 `RequireScan`；加载完成前扫码一律忽略

**选了什么**：状态核心一构造就处于 `AwaitingScan`（`ColdStart`），`IteRuntime.LoadAsync` 末尾的 `_director.RequireScan()` 删掉。同时 `IteRuntime` 新增私有门禁 `_initialized`，只在 `LoadAsync` 末尾、`OnInitialized` 广播前置真（加载失败则不置真）；`SubmitMarkerScan` 在门禁关闭时直接 `Debug.Log` 一行并返回，不进入解析。

**为什么**：加载是逐个 Tour 登记进 `_liveTours` 的（`IteTourAssembler.cs`），标记桥在 runtime 创建时就已经接上——**加载途中扫到已装配的 Tour 的码是能解析的**，此前认为「加载完成前没有已装配的 Tour，扫码本来就解析不出来」的前提不成立：会被激活并锚定、进入 `Anchored`；加载结束 `SetAllVolumesActive(true)` 的 Enter 批次随后还可能把它换成区域里的 `regionalTrigger`；加载失败后已装配的部分也能被扫到。基线（旧实现）加载期间扫码一律被忽略，这道门禁把它找回来。门禁放在 `IteRuntime.SubmitMarkerScan` 这个运行时输入边界上，不作为第四个 `GuideState`——「加载中」是 runtime 的生命周期阶段，不是导览状态：状态核心一构造就已经是 `AwaitingScan`，加载途中摘下头显时状态是 `Suspended`，戴上后照常进入 `AwaitingScan`，都不需要特殊处理。

实现时补充：去掉这次调用之后，第一条「扫任意码」提示在 runtime 构造后的第一帧就会广播（D6 的首帧必算），而真机上 `IteHmdPanel` 要到下一帧 `Update` 才挂钩，会错过它；此后整个 AwaitingScan 期间提示不变，不会再广播。所以 `IteRuntime` 暴露只读的当前提示 `ScanPrompt`（与 `GuideState` 同为「属性 + 变更事件」），宿主挂钩时先读一次同步。

## 5. 对外接口变化

| 接口 | 变化 |
|---|---|
| `IteRuntime.SetHeadsetMounted(bool)` | 签名不变，语义按 §3.2 |
| `IteRuntime.RequireScan()` | 增加可选参数 `reason`，默认 `HostRequested`；进入时停掉在播 Tour（D2） |
| `IteRuntime.ActivateTour(string)` | 签名不变，非 `Anchored` 状态下返回 false（D4） |
| `IteRuntime.GuideState` / `OnGuideStateChanged` | 新增（D8） |
| `IteRuntime.GuideStateReason` | 新增：进入当前状态的原因，只读属性（D8） |
| `IteRuntime.ScanPrompt` | 新增：当前扫码提示，只读属性，与 `OnScanPromptChanged` 广播的最近一次值同步（D9） |
| `IteRuntime.SubmitMarkerScan(...)` | 初始化完成前（含加载失败）忽略扫码，不进入解析（D9） |
| `ScanState`（策略函数入参） | `Paused` + `ForcedScanPending` → `State`（D1） |
| `IteHmdPanel.NotifyRecentered()` | 删除（D8） |
| `TourDirector.ForcedScanPending` | 删除（D1，状态已并入 `State`） |
| `TourDirector.FlushRegionTransitions` | 更名为 `EndOfFrame` |
| `TourDirector.EvaluateScanPrompt` | 改为 `private`（外部不再需要主动触发重算） |
| `RegionDecision.PendingTourIds` | 删除（帧末结算改用调用方传入的 `previousTourIds`，见 `TourRegionPolicy.Decide`） |
| `TourRegionPolicy.Decide` | 签名改为 `Decide(ScanState, IReadOnlyList<TourDescriptor>, IReadOnlyList<string> previousTourIds)`；新增 `TourRegionPolicy.Apply(current, tourId, transition)` 承担进出事件对集合的原地应用（D5，两者拆开见类注释） |

## 6. 行为变化清单（对照现状）

1. 摘下期间区域不再唤醒 Tour：这就是要修的 bug。
2. 追踪原点重置 / 宿主要求重扫时，停掉在播 Tour，提示「扫任意码」（D2）。
3. 重定位黄条在扫码进入 `Anchored` 时消失，不再残留（D8）。
4. 同一帧离开又进入同一区域，不再切走在播的 Tour；没有 Tour 在播时离开某个区域、仍在 `regionalTrigger` 区域里，会激活它（D5）。
5. 摘下期间扫码被忽略：与现状相同，只是改由状态表达。

## 7. 测试

纯逻辑测试放在 `Packages/com.uality.ite-tour/Tests/Editor/`（EditMode）：
- **状态核心**：§3.2 的每一条转换一个测试，外加：
  - 摘下期间进入区域不激活任何 Tour；
  - `Anchored` 下 `RequireScan` 停掉在播 Tour 并进入 `AwaitingScan`；
  - `Suspended` 下 `RequireScan` 保持 `Suspended`；
  - 非 `Anchored` 下 `ActivateTour` 被拒；
  - 同一帧离开又进入同一区域不切换；
  - 刚锚定完、集合未变时不重选；
  - 帧末快照未变时不重算提示。
- **三个策略函数**：已有测试从设置两个 bool 改为设置状态，期望值不变（D5 改动的区域重选部分除外，按 D5 更新）。
- **宿主**：`IteHmdPanel` 黄条随状态显示和清除（能在 `MRBase.Ite.Host.Tests` 里测就测，测不了就列入真机验证）。
- **回归**：在打开的 Editor 里跑 `Uality.IteTour.Tests` 与 `MRBase.Ite.Host.Tests`，全部通过。

## 8. 真机验证（要用户说「打包」才出包）

1. 扫码定位后摘下头显，拿着它走过几个区域再戴上：没有 Tour 在播，显示「扫任意码」，日志里没有区域唤醒 Tour 的记录；扫码后正常激活并锚定。
2. 头显放在桌上对着码摘下：不会被扫码锚定。
3. 触发一次追踪原点重置：在播 Tour 被停掉，出现黄条和「扫任意码」；扫码后黄条消失。

## 9. 不在范围内

以下问题见 `docs/handoff/2026-09-23-ite-region-trigger-issues.md`：
- 碰撞体事件重复：区域集合在第一次「离开」时就删掉了该区域。
- 扫码锚定后被区域重选立即切走。
- 稳定器计时：PICO 上「稳定 0.5 秒」实际要举着不动约 6 秒（推测，未验证）。

## 10. 与既有文档和代码的关系

- `docs/superpowers/specs/2026-09-23-ite-scan-region-gate-design.md` 的 D1–D3（区域门禁、原点重置同样不看区域、区域外不提示）语义不变，只是「必须扫码状态」改称 `AwaitingScan`。它的 §5 黄条问题由本文 D8 解决。
- 工作区里未提交的诊断日志（`TourDirector.cs`、`IteHmdPanel.cs`、`IteHostBootstrap.cs`）并入本次改动：`TourDirector` 被重写时，日志按新结构保留，另外加一条状态变化日志。
