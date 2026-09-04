## Context

现有契约 `IMarkerTrackingProvider`（`Assets/Scripts/Localization/IMarkerTrackingProvider.cs`）只有两个事件：

```csharp
event Action<string, Pose> MarkerResolved;
event Action<string> MarkerLost;
```

两端各自实现，各自决定身份语义、派发节奏与丢失判定。清点下来有五处分叉，每一处都有代码或真机证据：

| # | 分叉 | 证据 | 后果 |
|---|---|---|---|
| 1 | rawId 语义 | `QuestMarkerProvider.cs:36` 发 QR 原文；`PicoMarkerProvider.cs:128` 发 `iMarkerId.ToString()` | `AnchorRegistry.cs:18` 用 `QuestPayload == rawId \|\| PicoMarkerId.ToString() == rawId` 兜底 |
| 2 | 派发节奏 | `QuestMarkerProvider.cs:35` 只在 `TrackableAdded` 读一次 Transform；PICO 持续推快照 | `MarkerStabilizer` 的 `Feed` 在 Quest 上只被调一次 |
| 3 | 丢失时机 | `PicoMarkerProvider.cs:134-140` 快照差集，当帧缺席即丢；Quest 依赖 `TrackableRemoved` | 短暂遮挡在 PICO 上就是"离开" |
| 4 | 稳定阈值单位 | `MarkerStabilizer.cs:18,60` `stableFrameThreshold = 30`（帧） | PICO 6 Hz 下 = 5 秒；Quest 上永远到不了 30 |
| 5 | 无轻量暂停 | `PicoMarkerProvider.cs:69` `StopTracking()` 调 `UnBindEnterpriseService()` | 暂停扫描 = 解绑整个企业服务 |

分叉 2 的目标形状已经明确：Provider 保存活动 `MRUKTrackable`，主线程读取其最新 Transform / `IsTracked`，再按统一调度频率派发；不能沿用当前仅在 Added 时复制一次 Pose 的实现。理由见 D5，那里有 MRUK 包源码的三条证据。

**两端回调都已在主线程**，本设计因此**不需要锁或并发原语**：

- PICO 的 `MarkerInfoCallback.CallBack` 与 `StringCallback.CallBack` 都先走 `PXR_EnterpriseTools.QueueOnMainThread(...)`（`Enterprise/Scripts/Interfaces/MarkerInfoCallback.cs:38`、`Interfaces/StringCallback.cs:17`）。
- Quest 的 `TrackableAdded` / `TrackableRemoved` 是主线程 `UnityEvent`。

没有生产者能制造跨线程竞态，所以也**不应为此写并发竞态测试**。派发队列的价值在派发时机解耦与顺序稳定，不在线程安全。

约束：

- 消费方有两个且语义不同——`MarkerAnchorService`（持续锚定）与 `IteHost/MarkerSourceAdapter`（扫描触发）。后者的注释 `:13` 明说它假设"`MarkerResolved` 在标记可见期间每帧都发"，这假设在 Quest 上不成立。
- 硬件会话是独占资源。`MarkerTrackingBootstrapper.cs:10-14` 已确立"标记识别是一份硬件会话，不该按消费方数量开多份"。
- PICO 的 marker 回调是**单槽注册**且 TOB 无反注册 API。`PXR_EnterprisePlugin.cs:1373` 是 `tobHelper.Call<int>("setMarkerInfoCallback", new MarkerInfoCallback(...))`——`set` 语义，一个回调引用，第二次注册静默覆盖第一次，没有返回值或日志能说明前一方已失效。叠加 `PicoMarkerProvider.cs:69` 的 `StopTracking()` 会 `UnBindEnterpriseService()` 解绑**整个**企业服务：两方同时活着时谁后注册谁赢，任一方停止都会连带解绑对方。

## Goals / Non-Goals

**Goals:**

- 业务层在两端订阅同一套事件、得到同一种语义，不需要知道自己跑在哪个平台。
- 身份、派发节奏、丢失滞回、暂停这四件事各只有**一份**实现，且都在平台无关的代码里。
- 这四件事的逻辑可在 EditMode 下测试，不依赖真机。
- 给出一条不释放硬件会话的暂停路径。

**Non-Goals:**

- 不决定业务层拿到事件后做什么；`MarkerAnchorService` 的锚定策略与 ITE 的触发策略都留在各自上层。
- 不实现 PICO 的 AprilTag 检测本身。检测器（`jp.keijiro.apriltag`，`tagStandard41h12`）与 4U 相机取流已在 `Assets/Scripts/Localization/Native/Probe/PicoQrCameraProbe.cs` 中落地；本 change 只把它的输出接成 `Poll()` 的数据源，不动检测算法、不动取流栈、不动位姿解算。
- 不建立共享相机/企业服务门面（探针与生产的共存问题）。见 Open Questions。
- 不设计注册表的数据格式与来源，沿用既有 `IAnchorDataSource` / `AnchorEntityData`。
- 不改夹具、不改 AprilTag family、不扩 ID 空间。

## Decisions

### D1. 观测源改为纯查询，事件只由会话层发

平台实现从 `IMarkerTrackingProvider`（自带事件）改为 `IMarkerObservationSource`：

```
Open() / Close() / Poll() -> IReadOnlyList<RawObservation>
```

事件唯一的发出点是 `MarkerTrackingSession`。

**替代方案**：保留现有接口形状，只把事件载荷换成结构体，节奏靠给 Provider 加 `Tick(dt)`。**否决理由**：那样滞回与节流仍要在每个 Provider 里各实现一遍，分叉 2/3/4 会重新长出来——现在这五处分叉正是"每端各写一遍"的产物。"平台差异只在 `Poll()` 里，语义只在 Session 里"是一条能防止复发的结构边界，而不只是一次性修复。

### D2. 会话层是纯 C#，时间由 `Tick(float deltaTime)` 注入

`MarkerTrackingSession` 不继承 `MonoBehaviour`、不读 `Time.deltaTime`，由已是 MonoBehaviour 的 `MarkerTrackingBootstrapper` 驱动。

**理由**：滞回、节流、稳定判定是本 change 全部风险所在，必须能在 EditMode 锁死。既有 `MarkerStabilizer.Feed(id, pose, deltaTime)` 已是同一风格，`MarkerStabilizerTests.cs` 正是靠注入 `deltaTime` 才能断言"第 3 次 Feed 触发"。

**替代方案**：Session 做成 MonoBehaviour 用 `Update` 驱动。**否决理由**：滞回时长这类判定就只能靠 PlayMode 测试或真机验证，反馈周期从秒级变成分钟级。

### D3. 身份分层显式化，删除注册表的 `||` 兜底

```
MarkerIdentity { RawPayload?, NativeId?, LogicalId }
```

`AnchorRegistry.TryResolve` 改为只按 `LogicalId` 单条件匹配。

分层不是新发明——探针的日志 schema 早已是这个形状：`MarkerProbeJsonlWriter.cs:43-47` 的 `markerId / picoArUcoId / qrId / logicalMarkerId / businessObjectId`。**探针的身份模型比生产的成熟得多**（生产的 `AnchorEntityData` 只有 4 个字段），本决策是把它提上来。

**替代方案**：保留裸 string，约定两端都发 `LogicalId`。**否决理由**：那会丢掉平台原始事实。诊断"为什么这张标没命中"时必须能区分"没扫到"、"扫到但解析失败"、"解析出来但注册表没有"——`RawPayload` / `NativeId` / `LogicalId` 三层各自为空与否正好把这三种情况分开。探针已经证明这个区分有用（`MarkerIdParseFailure` 枚举有 6 个取值）。

### D4. 复用 `IMarkerIdParser`，从探针提升到生产

`MarkerIdParser.cs:41-44` 的类注释：

> future URL, JSON, or lookup rules belong behind the same interface in a later production change

本 change 即那个 change。生产侧新增一个基于注册表查表的实现；`StandardFixtureMarkerIdParser`（只接受 `"0"` / `"250"`）保留给探针。

**替代方案**：在 Session 里直接写解析逻辑。**否决理由**：接口已经存在且带完整的失败分类与脱敏（`RawPayloadSummary`），为单一实现再造一个是重复；且 QR payload 未来若变 URL，只需换实现。

### D5. Quest 侧改为持续读 Transform

`QuestMarkerProvider` 从"`TrackableAdded` 时复制一次 Pose"改为持有活动 `MRUKTrackable` 集合，每次 `Poll()` 读最新 `transform` 与 `IsTracked`。

**这是行为改变，不是重构**——Quest 是当前已取得真机证据的一端，改完必须重测。

**这不是疑点，是已证实的断链。** 三条证据全部取自 MRUK 包源码 `Library/PackageCache/com.meta.xr.mrutilitykit@2a23a4eea58d/Core/Scripts/MRUK.Trackers.cs`：

1. `:162` / `:174` — MRUK 只暴露 `TrackableAdded` 与 `TrackableRemoved` 两个 `UnityEvent<MRUKTrackable>`，**没有 `TrackableUpdated` 事件**。
2. `:327-344` — `HandleTrackableAdded` 对已存在的 key 显式去重（`if (_trackables.ContainsKey(trackableKey)) { LogWarning(...); return; }`），所以 `TrackableAdded.Invoke`（`:343`）每个 trackable 一生只触发一次。
3. `:346-350` — `HandleTrackableUpdated` 只调 `UpdateTrackableProperties` 直接改组件字段，**不派发任何事件**。

推论链：`QuestMarkerProvider.cs:30-37` 的 `MarkerResolved` 每标记只发一次 → `MarkerStabilizer.Feed` 只被调一次 → `StableFrameCount` 停在 1 → 永远达不到 `stableFrameThreshold = 30` → `Stabilized` 从不触发 → `AnchorEntity` 从不创建。**Quest 的「标记 → 内容」生产链路从未在真机上跑通过**；探针路径不经过 `MarkerStabilizer` / `AnchorRegistry` / `MarkerAnchorService`，不受影响。注意这不只是"位姿会是过期快照"——实际后果是整条链死掉，而非位姿不准。

**替代方案**：不改 Quest，在契约里显式区分"事件型源"与"轮询型源"，让上层适配。**否决理由**：那等于把分叉从隐式变成显式，业务层仍要分平台写两套。而且分叉 4 不会被消除：`stableDurationSeconds` 在一次性事件源上依然无意义。

### D6. 丢失判定统一走时间滞回，默认 1.0 秒

Session 持有每个身份的 `lastSeenTime`，超过滞回时长才派发丢失。

T = 1.0 s 的来源：与工作包络中"首次识别 < 1 s"取同一尺度，进出对称。用时间而非帧数，因为采样率是可调旋钮（`PicoQrCameraProbe.cs:30` 的 `sampleHz`，当前 6，`[Range(2,15)]`），用帧数会跟着旋钮漂。

Quest 的 `TrackableRemoved` **不直接派发丢失**，而是作为"把 `lastSeenTime` 置为过期"的输入进入同一条判定。

**替代方案**：Quest 的移除信号是权威的，应立即派发。**否决理由**：两条判定路径就是两种语义，业务层又要分平台理解"离开"意味着什么。统一走滞回的代价只是 Quest 侧的丢失事件晚 1 秒，而收益是"离开"在两端严格同义。若将来实测证明这 1 秒不可接受，再按平台配置滞回时长——那时它是一个显式参数，不是一个隐式差异。

### D7. 稳定阈值从帧数改为秒

`MarkerStabilizer` 的 `stableFrameThreshold: int` → `stableDurationSeconds: float`。

**理由**：帧数在两端不同义（分叉 4），且会随采样率旋钮漂移（`PicoQrCameraProbe.cs:30` 的 `sampleHz`，`[Range(2,15)]`，当前 6）——一个本该稳定的判定被绑在了一个性能调参上。

**迁移**：现值 30 帧。在统一派发频率下换算成等效秒数，具体数值在实现时按选定的派发频率确定并记录。

### D8. 暂停与关闭分离

```
Open() / Close()      硬件会话生命周期
Pause() / Resume()    派发与检测开销
```

PICO 上 `Pause()` 停 AprilTag 检测但保留 4U 相机会话；`Close()` 才释放。Quest 上 `Pause()` 停轮询派发。

**理由**：现在唯一的"停"是 `StopTracking()`，它在 PICO 上会 `UnBindEnterpriseService()` 解绑整个企业服务（`PicoMarkerProvider.cs:69`），重开需要 Init + Bind + 权限握手。把"暂时不扫"和"释放硬件"这两件成本差几个数量级的事压在同一个方法上，是典型的语义压缩。

**暂停期间不累计缺席时长**——否则暂停超过滞回时长后恢复，所有标记会先收到一轮虚假的丢失事件。

## Risks / Trade-offs

- **Quest 已验证路径被改动（D5）** → 该端必须完整重测，不能只跑 EditMode。测试项至少覆盖：持续位姿更新、遮挡后恢复、`Stabilized` 是否真的触发。注意 `Stabilized` 在改动前**已被源码证实从不触发**（D5 的三条 MRUK 证据），所以这一项是**验证修复**而非验证疑点：改动前的 Quest 侧没有可回归的基线，只有"扫到 QR 能发一次 `MarkerResolved`"这一段是已验证的。
- **PICO 数据源同时要换** → `PicoMarkerProvider.cs:79` 今天仍从企业 TOB 的 `SetMarkerInfoCallback` 取快照，而 AprilTag 检测只存在于探针 `PicoQrCameraProbe.cs` 里。本 change 既要换契约形状（事件 → `Poll()`），又要换数据来源（TOB → AprilTag），是同一个文件上的两件事。缓解：拆成两步提交，先换来源保持旧契约行为等价，再换契约；两步各自可回滚。
- **AprilTag 路线的真机度量未做完** → 端到端延迟、检测率、CPU 占用、多标记并发这几项还没有实测数字（已有的只是"能检出 ID 0 / 250"这一层）。本 change 接通生产链路后，这些数字才第一次落在生产路径上。若任一项不达标，判据是把数字与原因写回本文档并停下，不是手调参数掩盖。
- **`anchor_registry.json` 不存在** → `MarkerTrackingBootstrapper.cs:6` 默认引用它，仓库内无此文件也无 `StreamingAssets/`。契约接通后每个标记都会走 registry miss。属于本 change 之外的缺口，但会掩盖验收结果。
- **一次性大改契约** → 9 个生产文件 + 4 个测试文件同时改，中途不可用。缓解：先落 Session 与新类型并补齐其单测，再逐个迁移消费方，最后删旧接口。
- **`MarkerSourceAdapter` 的既有假设失效** → 其 `:13` 注释与 `MarkerSourceAdapterTests.cs:50` 都假设"可见期间每帧都发"。统一节奏后该假设变成契约保证而非巧合，但注释与测试都要同步更新，否则下一个人会以为它仍是巧合。

## Migration Plan

1. 新增 `MarkerIdentity` / `MarkerObservation` / `IMarkerObservationSource` / `MarkerTrackingSession`，并补齐 `MarkerTrackingSessionTests`（滞回、节流、暂停、身份收敛）。此步不动任何既有代码，可独立验证。
2. `MockMarkerProvider` 改为 `MockObservationSource`，跑通 Session 全部单测。
3. `MarkerStabilizer` 按 D7 换单位，同步 `MarkerStabilizerTests`。
4. 迁移 `QuestObservationSource`（含 D5 行为改变）与 `PicoObservationSource`。
5. 迁移消费方：`MarkerAnchorService`、`MarkerSourceAdapter`、`AnchorRegistry`（删 `||`）、`MarkerTrackingBootstrapper`（驱动 `Tick`）、`DemoMarkerTrigger`。
6. 删除 `IMarkerTrackingProvider`。
7. 两端真机回归。

**回滚**：步骤 1–3 不改变既有行为，可独立保留。步骤 4 起为一个整体，回滚即整体还原。

## Open Questions

- **PICO 换源与换契约的先后**：先把 `PicoMarkerProvider` 的快照来源从 TOB 换成 AprilTag、保持现有事件契约不变，再整体换契约；还是一次做完？前者每步可独立验证、可独立回滚，代价是 PICO 侧要写一段活不过一个提交的过渡代码。
- **是否顺带抽共享相机/企业服务门面**：探针与生产 Provider 都要开 4U 相机，而真机已证 4U 相机会话**不共享**——第二方拿不到 `camOpenned`，`StartGetImageDatafor4U` 持续 `result=-1`，且无任何错误提示。当前靠"探针与生产分处不同场景"规避，但 `MRSceneDirector` 能在运行时切场景。本 change 的 Non-Goals 暂时排除了门面，需确认这个规避在实际场景切换下是否成立。
- **统一派发频率取值**：PICO 探针当前 `sampleHz = 6`（`PicoQrCameraProbe.cs:30`）。这个值是为检测开销选的，未必适合作为跨端派发频率。需要一个决策：派发频率与检测频率是同一个旋钮，还是两个。
- **D7 的等效秒数**：30 帧在选定派发频率下换算成多少秒，需要在实现时确定并回填本文档。
