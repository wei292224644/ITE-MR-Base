## Context

现有契约 `IMarkerTrackingProvider`(`Assets/Scripts/Localization/IMarkerTrackingProvider.cs`)只有两个事件:

```csharp
event Action<string, Pose> MarkerResolved;
event Action<string> MarkerLost;
```

两端各自实现,各自决定派发节奏与丢失判定。清点下来有代码或真机证据支撑的分叉:

| # | 分叉 | 证据 | 后果 |
|---|---|---|---|
| 1 | 派发节奏 | `QuestMarkerProvider.cs:35` 只在 `TrackableAdded` 读一次 Transform;PICO 持续推快照 | `MarkerStabilizer` 的 `Feed` 在 Quest 上只被调一次 |
| 2 | 丢失时机 | `PicoMarkerProvider.cs:134-140` 快照差集,当帧缺席即丢;Quest 依赖 `TrackableRemoved` | 短暂遮挡在 PICO 上就是"离开" |
| 3 | 无轻量暂停 | `PicoMarkerProvider.cs:69` `StopTracking()` 调 `UnBindEnterpriseService()` | 暂停扫描 = 解绑整个企业服务 |

分叉 1 的目标形状已经明确:Provider 保存活动 `MRUKTrackable`,主线程读取其最新 Transform / `IsTracked`,再按各自原生频率派发;不能沿用当前仅在 Added 时复制一次 Pose 的实现。理由见 D4,那里有 MRUK 包源码的三条证据。

**`AnchorRegistry.cs:18` 的 `QuestPayload == rawId || PicoMarkerId.ToString() == rawId` 双条件兜底,是身份语义在两端不一致的直接后果**——但本 change 明确不解决这件事(见下方"probe 校正"),因为承载它的整条生产链(`MarkerAnchorService`)本身要下线。

### probe 校正

本 change 在 artifacts 已完整之后经历了一次 `/opsx:probe`,用途是校正范围。以下三条原设计决策被推翻:

| 原决策 | 内容 | 否决理由 |
|---|---|---|
| 原 D3 | 事件载荷用三层 `MarkerIdentity`(`RawPayload`/`NativeId`/`LogicalId`),业务层只认 `LogicalId` | 「现在 Marker 的行为不牵扯到解析和抹平 Quest 与 Pico 的差异,hook 返回最原始的信息即可」——解析与映射归业务层,不进契约 |
| 原 D4 | `IMarkerIdParser` 从探针提升到生产,作为身份解析的落地实现 | 随原 D3 一起否决;parser 留在探针,不进生产路径 |
| 原 D7 | `MarkerStabilizer` 的 `stableFrameThreshold: int` 改为 `stableDurationSeconds: float` | 本次不动 `MarkerStabilizer`;新测试场景的盒子决定复用它而非重写触发逻辑,阈值单位维持现状 |

连带被否决的还有原设计对现有消费方的处理方式:`MarkerAnchorService` 那条"标记 → 内容"链路从"机械迁移"改为**下线**(删除,不迁移;可删除相关旧测试),ITE 导览包的消费从"机械迁移保持可编译"改为**注释掉**(不写中间层适配)。理由见 Risks / Trade-offs 与 proposal.md 的 Impact。

**两端回调都已在主线程**,本设计因此**不需要锁或并发原语**:

- PICO 的 `MarkerInfoCallback.CallBack` 与 `StringCallback.CallBack` 都先走 `PXR_EnterpriseTools.QueueOnMainThread(...)`(`Enterprise/Scripts/Interfaces/MarkerInfoCallback.cs:38`、`Interfaces/StringCallback.cs:17`)。
- Quest 的 `TrackableAdded` / `TrackableRemoved` 是主线程 `UnityEvent`。

没有生产者能制造跨线程竞态,所以也**不应为此写并发竞态测试**。

约束:

- 硬件会话是独占资源。`MarkerTrackingBootstrapper.cs:10-14` 已确立"标记识别是一份硬件会话,不该按消费方数量开多份"——但该文件本身随下线一起删除,新的会话生命周期由 `MarkerHookTestRig` 承担。
- PICO 的 marker 回调是**单槽注册**且 TOB 无反注册 API。`PXR_EnterprisePlugin.cs:1373` 是 `tobHelper.Call<int>("setMarkerInfoCallback", new MarkerInfoCallback(...))`——`set` 语义,一个回调引用,第二次注册静默覆盖第一次。这是**换数据源到 AprilTag 检测流**(而非继续用 TOB 回调)的动机之一:AprilTag 走的是 4U 相机流,不占用这个单槽回调。

## Goals / Non-Goals

**Goals:**

- 业务层在两端订阅同一套事件、得到同一种语义(除 `Platform` 字段本身外),不需要知道自己跑在哪个平台的派发细节。
- 节奏、丢失滞回、暂停这三件事各只有**一份**实现,且都在平台无关的代码里。
- 这三件事的逻辑可在 EditMode 下测试,不依赖真机。
- 给出一条不释放硬件会话的暂停路径。
- 有一个专门场景能可观察地验证 hook 行为——不需要复杂业务逻辑陪衬。

**Non-Goals:**

- **不做标记身份的解析或收敛。** 载荷只带 `Platform` + `RawPayload`,业务层自行 `int.Parse` 或做映射规则。原设计的三层 `MarkerIdentity`、`LogicalId`、生产级 `IMarkerIdParser` 均已被 probe 否决。
- 不决定业务层拿到事件后做什么;已确定"标记→内容"生产链(`MarkerAnchorService`)**下线**而非迁移,ITE 消费**注释掉**不迁移。
- 不实现 PICO 的 AprilTag 检测本身。检测器(`jp.keijiro.apriltag`,`tagStandard41h12`)与 4U 相机取流已在 `PicoQrCameraProbe.cs` 中落地;本 change 只把它的取流/检测循环抽成独立类型接入 `Poll()`,不动检测算法、不动取流栈、不动位姿解算。
- 不建立共享相机/企业服务门面。**已确认**:`MarkerHookTest` 场景与 `PicoQrCameraProbe` 探针场景视为互斥使用——两者共用同一份 4U 相机独占资源,不处理 `MRSceneDirector` 运行时切换时的共存问题,该限制留作已知约束而非本 change 的解决目标。
- 不改 `MarkerStabilizer` 的阈值单位——原 D7 已否决,继续用帧数。
- 不做跨平台派发限流——Quest 与 PICO 各自按原生频率派发,不统一到同一个 Hz。
- 不改夹具、不改 AprilTag family、不扩 ID 空间。

## Decisions

### D1. 观测源改为纯查询,事件只由会话层发

平台实现从 `IMarkerTrackingProvider`(自带事件)改为 `IMarkerObservationSource`:

```
Open() / Close() / Pause() / Resume() / Poll() -> IReadOnlyList<RawObservation>
```

事件唯一的发出点是 `MarkerTrackingSession`。

**替代方案**:保留现有接口形状,只把事件载荷换成结构体,节奏靠给 Provider 加 `Tick(dt)`。**否决理由**:那样滞回仍要在每个 Provider 里各实现一遍,分叉 2 会重新长出来。"平台差异只在 `Poll()` 里,语义只在 Session 里"是一条能防止复发的结构边界。

### D2. 会话层是纯 C#,时间由 `Tick(float deltaTime)` 注入

`MarkerTrackingSession` 不继承 `MonoBehaviour`、不读 `Time.deltaTime`,由已是 MonoBehaviour 的 `MarkerHookTestRig`(原设计中是 `MarkerTrackingBootstrapper`,该文件已随下线删除)驱动。

**理由**:滞回、暂停判定是本 change 全部风险所在,必须能在 EditMode 锁死。既有 `MarkerStabilizer.Feed(id, pose, deltaTime)` 已是同一风格,`MarkerStabilizerTests.cs` 正是靠注入 `deltaTime` 才能断言"第 3 次 Feed 触发"。

**替代方案**:Session 做成 MonoBehaviour 用 `Update` 驱动。**否决理由**:滞回时长这类判定就只能靠 PlayMode 测试或真机验证,反馈周期从秒级变成分钟级。

### D3. 载荷只带平台标签与原始 payload,不做身份收敛

```csharp
public enum MarkerPlatform { Quest, Pico }

public readonly struct MarkerObservation
{
    public readonly MarkerPlatform Platform;
    public readonly string RawPayload;   // Quest: QR 原文;PICO: iMarkerId.ToString()
    public readonly Pose Pose;           // 世界系
}
```

`RawPayload` 一律非空。`MarkerLost(MarkerPlatform, string rawPayload)` 同构。消费方要 int 自己 `int.Parse`。

**这是本次 probe 校正后的结论,替换了原设计的 D3(三层 `MarkerIdentity`)与 D4(`IMarkerIdParser` 提升到生产)。** 原设计的理由是"`AnchorRegistry.cs:18` 的 `||` 双条件把身份决策藏进了两个处理器的相互作用里,分层能让诊断'为什么没命中'区分'没扫到'/'解析失败'/'注册表没有'三种情况"。这个理由本身没有被推翻,但它依附的问题(身份收敛)被明确划出契约的范围——用户判断:「hook 返回最原始的信息即可」,身份解析是业务层的事,不该进平台无关的契约层。

**替代方案**:保留三层身份模型,只是不提供默认 parser 实现。**否决理由**:三层结构(`RawPayload`/`NativeId`/`LogicalId`)本身就是为身份收敛设计的形状,即使不提供 parser,字段的存在也在暗示契约"应该"关心身份——不如直接删掉,让业务层从"平台 + 原始 payload"这两个最基本的事实自己搭建它需要的任何身份模型。

### D4. Quest 侧改为持续读 Transform

`QuestObservationSource`(原 `QuestMarkerProvider`)从"`TrackableAdded` 时复制一次 Pose"改为持有活动 `MRUKTrackable` 集合,每次 `Poll()` 读最新 `transform` 与 `IsTracked`。

**这是行为改变,不是重构**——Quest 是当前已取得真机证据的一端,改完必须重测。

**这不是疑点,是已证实的断链。** 三条证据全部取自 MRUK 包源码 `Library/PackageCache/com.meta.xr.mrutilitykit@2a23a4eea58d/Core/Scripts/MRUK.Trackers.cs`:

1. `:162` / `:174` — MRUK 只暴露 `TrackableAdded` 与 `TrackableRemoved` 两个 `UnityEvent<MRUKTrackable>`,**没有 `TrackableUpdated` 事件**。
2. `:327-344` — `HandleTrackableAdded` 对已存在的 key 显式去重(`if (_trackables.ContainsKey(trackableKey)) { LogWarning(...); return; }`),所以 `TrackableAdded.Invoke`(`:343`)每个 trackable 一生只触发一次。
3. `:346-350` — `HandleTrackableUpdated` 只调 `UpdateTrackableProperties` 直接改组件字段,**不派发任何事件**。

推论链:`QuestMarkerProvider.cs:30-37` 的 `MarkerResolved` 每标记只发一次 → `MarkerStabilizer.Feed` 只被调一次 → `StableFrameCount` 停在 1 → 永远达不到 `stableFrameThreshold = 30` → `Stabilized` 从不触发。**Quest 的「标记 → 内容」生产链路从未在真机上跑通过**;探针路径不经过 `MarkerStabilizer`,不受影响。

**已确认**:`MRUKTrackable.IsTracked` 为 `false` 时按"缺席"处理,进入 D5 的滞回判定,而非当作已移除立即丢——与 Lost 的统一语义保持一致,避免遮挡瞬间被误判为"离开"。该字段的真机实际行为仍需在真机验收阶段核实(design D4 的语义是决策,真机行为是验证)。

**替代方案**:不改 Quest,在契约里显式区分"事件型源"与"轮询型源",让上层适配。**否决理由**:那等于把分叉从隐式变成显式,业务层仍要分平台写两套。

### D5. 丢失判定统一走时间滞回,默认 1.0 秒

Session 持有每个身份(按 `Platform` + `RawPayload` 区分)的 `lastSeenTime`,超过滞回时长才派发丢失。

T = 1.0 s 的来源:与工作包络中"首次识别 < 1 s"取同一尺度,进出对称。用时间而非帧数,因为采样率是可调旋钮(`PicoQrCameraProbe.cs:30` 的 `sampleHz`,当前 6,`[Range(2,15)]`),用帧数会跟着旋钮漂。

Quest 的 `TrackableRemoved` **不直接派发丢失**,而是作为"把 `lastSeenTime` 置为过期"的输入进入同一条判定。

**替代方案**:Quest 的移除信号是权威的,应立即派发。**否决理由**:两条判定路径就是两种语义,业务层又要分平台理解"离开"意味着什么。统一走滞回的代价只是 Quest 侧的丢失事件晚 1 秒,而收益是"离开"在两端严格同义。

### D6. 暂停与关闭分离

```
Open() / Close()      硬件会话生命周期
Pause() / Resume()    派发与检测开销
```

PICO 上 `Pause()` 停 AprilTag 检测但保留 4U 相机会话;`Close()` 才释放。Quest 上 `Pause()` 停轮询派发。真机上通过 `MarkerHookTest` 场景 HUD 的按钮触发,**不**走 `MarkerProbeXrControls.cs`——它 `:33-38` 依赖 Diagnostics 场景里叫 `RayTargetButton` 的模板对象,找不到就禁用自己,是"碰巧能跑"的耦合,不该被生产场景依赖。

**理由**:现在唯一的"停"是 `StopTracking()`,它在 PICO 上会 `UnBindEnterpriseService()` 解绑整个企业服务(`PicoMarkerProvider.cs:69`),重开需要 Init + Bind + 权限握手。把"暂时不扫"和"释放硬件"这两件成本差几个数量级的事压在同一个方法上,是典型的语义压缩。

**暂停期间不累计缺席时长**——否则暂停超过滞回时长后恢复,所有标记会先收到一轮虚假的丢失事件。

**已确认**:PICO 上 `Pause()` 停在检测分派层——相机流(`StartGetImageDatafor4U`、`OnImageAvailable`)与 4U 会话保持不变,只是不再把取到的帧交给 `DispatchDetection`。选这一层而不是同时停止 4U 帧消费,是因为改动面最小、且不必验证 4U 回调能否安全地被反复启停;代价是暂停期间相机取流的开销不会降到零,只省下检测算力。

### D7. 派发节奏不做跨平台限流

会话层按 `Tick` 持续派发可见期间标记的最新位姿,**不**把 Quest 的 72–90 Hz 压到 PICO 的 6 Hz,也不引入 `maxDispatchHz` 之类的旋钮。

**理由**:业务层要的语义是"可见期间持续拿到最新位姿",不是"两端每秒恰好 N 次"。为对齐两端而把好的一端(Quest)降级没有收益;`PicoQrCameraProbe.cs:30` 的 `sampleHz` 是为检测开销选的调参项,不该被拿来当成两端共用的派发频率。

**替代方案**:统一压到较慢一端的频率,保证两端行为"看起来一样"。**否决理由**:那是用降级换取表面一致,而业务层实际需要的是"新鲜度",不是"频率相等"。

### D8. 测试场景的盒子复用 `MarkerStabilizer`

`MarkerHookTestRig` 把 `MarkerObserved` 喂进 `MarkerStabilizer.Feed(payload, pose, deltaTime)`,`Stabilized` 事件触发时才显示/更新盒子;`MarkerLost` 时 `Reset` 对应状态并隐藏盒子。

**这与直觉相反但是用户明确选择的路径**:因为 D7 取消了跨平台限流、D4 让 Quest 改为持续派发,`MarkerStabilizer.Feed` 在两端现在都会被高频调用,不再有"Quest 上只调一次"的断链问题——`MarkerStabilizer` 现有的帧数阈值(30 帧)在两端都能正常累积。

**替代方案**:不走 `MarkerStabilizer`,直接跟每次 `MarkerObserved` 更新盒子位置。**否决理由(未采纳,记录供参考)**:直接更新能更早看到"hook 有没有发事件",但会把"hook 发得对不对"与"位姿是否稳定"两件事混在一起观察;用户选择复用 `MarkerStabilizer`,因为验收面本身就是"扫到内容时放一个 box",而不是"逐帧位姿抖动"。

### D9. PICO 观测源换数据源:从 TOB 快照到 AprilTag 检测流

把 `PicoQrCameraProbe.cs` 的相机会话与检测循环——`OnServiceBound`(`:148`)、`StartCameraStream`(`:186`)、`OnImageAvailable`(`:243`)、`Update` 里的采样节流(`:259`)、`DispatchDetection`(`:314`)、`DrainResults`(`:365`)——抽成独立的 `PicoFiducialObservationSource`,`Poll()` 返回当帧的 AprilTag 检测快照;`PicoQrCameraProbe` 改为消费它,只保留自己的遥测 `OnGUI` 与 `markerBox` 自检逻辑。删除 `PicoMarkerProvider.cs`。

**理由**:`PicoMarkerProvider.cs:79` 走的 TOB `SetMarkerInfoCallback` 已被真机证伪——`SFS_TRACKING_ENABLE_DYNAMIC_MARKER` 读回 0,开关不开就不产生任何回调(提交 `5286d01` 标题即"用 AprilTag 相机流替代被证伪的原生 ArUco")。AprilTag 检测流是当前唯一有真机证据的 PICO 标记识别路径。

**已确认抽取边界**:`latestFramePose` 与 `cameraBufferHandle`(`GCHandle`)连同相机会话的全部状态**完全搬入** `PicoFiducialObservationSource`;`PicoQrCameraProbe` 不再持有任何相机/检测状态,只保留自己的遥测 `OnGUI` 与 `markerBox` 自检显示逻辑,通过 `Poll()` 结果驱动。选择完全搬移而非薄封装,是为了不让两个类型同时持有同一份底层状态——那样任何一方的生命周期改动都要跨类型核对,正是 D1 想避免的重复。

**代价**:接受"PICO 探针需要重测"——相机管线的持有方从探针本身变成新抽取的类型。

### D10. Quest MRUK 运行时装配从探针专属提升为生产

把 `Native/Probe/QuestMarkerProbeRuntimeBootstrap.cs` 移到 `Native/QuestMrukRuntimeInstaller.cs`,去掉 `internal`;触发条件从"场景里有 `MarkerProbeEntry`"(`:20`)放宽为"`MarkerProbeEntry` 或 `MarkerHookTestRig`"。删除 `QuestMarkerProvider.cs`。

**理由**:`MarkerHookTest` 场景需要与探针场景相同的 MRUK 运行时对象(`OVRCameraRig`/`OVRManager`/`MRUK`),但共享场景本身故意不放任何 Meta 预制体(`:6-8` 的注释),所以需要复用这套"按需动态装配"的逻辑,而不是复制一份。装配顺序有讲究——`:37` 先 `SetActive(false)` 再配置组件、`:45` 才激活,复制容易漂,故移动文件而非复制内容。

**代价**:接受"Quest 探针需要重跑确认没退化"。

## Risks / Trade-offs

- **Quest 已验证路径被改动(D4)** → 该端必须完整重测,不能只跑 EditMode。测试项至少覆盖:持续位姿更新、遮挡后恢复、`Stabilized` 是否真的触发。`Stabilized` 在改动前**已被源码证实从不触发**(D4 的三条 MRUK 证据),所以这一项是**验证修复**而非验证疑点。
- **PICO 数据源同时要换(D9)** → 抽取相机管线与检测循环是本 change 最大的单块变更,`PicoQrCameraProbe.cs` 的历史行为(自检方块、遥测 OnGUI)必须在抽取后保持等价。
- **下线范围比原设计更大** → 除契约替换外,还要删除整条"标记 → 内容"生产链(9 个源文件 + 1 个场景 + 6 个测试文件)并注释掉 ITE 消费点。`IteHostBootstrap.cs` 对 `MarkerSourceAdapter` 的引用一旦注释,若还有其他文件间接依赖它的类型,会连带报错——按 probe 的明确指示"先注释掉关于 ITE 的行为,不要写中间层来解决错误",逐个注释而不是补兼容层。
- **AprilTag 路线的真机度量未做完** → 端到端延迟、检测率、CPU 占用、多标记并发这几项还没有实测数字。本 change 接通生产链路后,这些数字才第一次落在生产路径上。若任一项不达标,判据是把数字与原因写回本文档并停下,不是手调参数掩盖。
- **4U 相机会话不共享** → `MarkerHookTest` 场景与 `PicoQrCameraProbe` 探针场景已确认按互斥使用处理(见 Non-Goals),即不支持 `MRSceneDirector` 运行时从一个切到另一个;若日后需要同时使用,需要单独立项做共享门面。
- **HUD 的 XR 射线交互未在 PICO 上验证** → 已确认按现有 `MarkerProbeXrControls` 同款 TMP + UGUI 模式实现,假定 PICO 侧的 XR 射线能点击 UGUI `Button`;若真机验收时点不动,需要单独排查交互层,不在本 change 范围内预先解决。
- **一次性大改契约** → 新增类型 + 会话层单测先落地、再迁移平台源、再下线旧链、再建新场景,中途某些步骤之间会短暂不可编译(尤其是删除旧链与注释 ITE 那两步)。缓解:严格按 Migration Plan 的顺序推进,每步结束后跑一次编译确认。

## Migration Plan

1. 新增 `MarkerObservation` / `IMarkerObservationSource` / `MarkerTrackingSession`,并补齐 `MarkerTrackingSessionTests`(持续派发、滞回、暂停)。此步不动任何既有代码,可独立验证。
2. `MockMarkerProvider` 改为 `MockObservationSource`,跑通 Session 全部单测。
3. 迁移 `QuestObservationSource`(含 D4 行为改变)。
4. 抽取并迁移 `PicoFiducialObservationSource`(含 D9 换数据源),`PicoQrCameraProbe` 改为消费它。
5. 提升 Quest MRUK 装配:`QuestMarkerProbeRuntimeBootstrap` → `QuestMrukRuntimeInstaller`(D10)。
6. 下线旧"标记 → 内容"生产链:删除 `MarkerAnchorService`/`AnchorRegistry`/`AnchorEntity`/`AnchorEntityData`/`IAnchorDataSource`/`LocalJsonAnchorDataSource`/`IContentLoader`/`ImageContentLoader`/`DemoMarkerTrigger`/`MarkerTrackingBootstrapper` 及对应测试;删除 `LocalizationDemo.unity`。
7. 注释掉 ITE 消费点:`MarkerSourceAdapter.cs` 的引用、`IteHostBootstrap.cs:42,69`。
8. 删除 `IMarkerTrackingProvider.cs`、`QuestMarkerProvider.cs`(旧)、`PicoMarkerProvider.cs`(旧)、`MockMarkerProvider.cs`(旧);全仓 grep 确认无残留引用。
9. 新建 `MarkerHookTest.unity` 场景、`MarkerHookTestRig`、HUD;`BuildScript.cs` 加两个构建入口。
10. 两端真机回归。

**回滚**:步骤 1–2 不改变既有行为,可独立保留。步骤 3 起(含下线)为一个整体,回滚即整体还原。

## Open Questions

`probe-report.md` 遗留的 7 项 `[ASSUMED]` 开放假设已在 `/opsx:propose` 重生成阶段与用户逐条确认,结论已写入上述对应决策(D4/D6/D9/Non-Goals/Risks)与 `tasks.md` 的"已确认的前置结论"。当前无遗留的未决问题;`design D4` 中 `MRUKTrackable.IsTracked` 的真机实际行为、D6 的 `Pause()` 性能收益、真机度量数字(design Risks)仍需在 tasks 第 7 章真机验收阶段用实测数据核实——这些是**验证**而非**未决策**。
