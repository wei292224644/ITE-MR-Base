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

### D8. 测试场景的盒子直接跟每次 `MarkerObserved` 更新,不走 `MarkerStabilizer`

`MarkerHookTestRig` 收到 `MarkerObserved` 就直接摆/挪盒子,`MarkerLost` 时销毁。测试场景**不引用** `MarkerStabilizer`。

**理由**:本场景的验收面是"hook 发没发、发得对不对"。插一层稳定器会把"hook 没发"与"稳定器没判稳"压成同一个外部现象——两者都表现为"什么都没有"。这正是 D4 里 Quest 断链的形状,不能在专门用来验 hook 的场景里再复现一次。

**替代方案**:喂进 `MarkerStabilizer`,`Stabilized` 时才显示,理由是盒子更稳。**否决,且已被真机证伪**:2026-09-04 的 PICO 真机日志显示,检测层完全正常(`detect 174/180 = 97%`,ID 0 与 250 各 175 次,位姿解算零失败,`[MarkerHook] Observed` 230 次),但盒子一次都没出现。原因是单应解出的位姿相邻两次旋转抖动 **2-5 度**(如 tag 0 的 `rot=(355.5, 38.6, 178.0)` → `(350.4, 38.9, 178.2)`),而 `MarkerStabilizer.cs:24` 的 `rotationThreshold = 1f`;`smoothTime = 0.01` 使 `t = Clamp01(deltaTime / 0.01)` 在 72 fps 下恒为 1,`SmoothedPose` 每次直接跳到 target,于是比较的就是相邻两次原始位姿之差。`MarkerStabilizer.cs:46` 的 `moved` 几乎每次为真,`StableFrameCount` 反复清零,永远够不到 30,`Stabilized` 一次都没触发。

**记录一处文档事故**:本节此前的版本写着"这与直觉相反但是用户明确选择的路径"。该归因不成立——probe 阶段用户对"盒子走不走稳定器"的回答是"跟着走"(即不走稳定器),`/opsx:propose` 重生成 artifacts 时把该决策翻了面,并把翻面后的结论归给了用户。实现忠实执行了 `tasks.md 6.3`,所以 bug 的来源是文档而非编码。

### D9. PICO 观测源换数据源:从 TOB 快照到 AprilTag 检测流

把 `PicoQrCameraProbe.cs` 的相机会话与检测循环——`OnServiceBound`(`:148`)、`StartCameraStream`(`:186`)、`OnImageAvailable`(`:243`)、`Update` 里的采样节流(`:259`)、`DispatchDetection`(`:314`)、`DrainResults`(`:365`)——抽成独立的 `PicoFiducialObservationSource`,`Poll()` 返回当帧的 AprilTag 检测快照;`PicoQrCameraProbe` 改为消费它,只保留自己的遥测 `OnGUI` 与 `markerBox` 自检逻辑。删除 `PicoMarkerProvider.cs`。

**理由**:`PicoMarkerProvider.cs:79` 走的 TOB `SetMarkerInfoCallback` 已被真机证伪——`SFS_TRACKING_ENABLE_DYNAMIC_MARKER` 读回 0,开关不开就不产生任何回调(提交 `5286d01` 标题即"用 AprilTag 相机流替代被证伪的原生 ArUco")。AprilTag 检测流是当前唯一有真机证据的 PICO 标记识别路径。

**已确认抽取边界**:`latestFramePose` 与 `cameraBufferHandle`(`GCHandle`)连同相机会话的全部状态**完全搬入** `PicoFiducialObservationSource`;`PicoQrCameraProbe` 不再持有任何相机/检测状态,只保留自己的遥测 `OnGUI` 与 `markerBox` 自检显示逻辑,通过 `Poll()` 结果驱动。选择完全搬移而非薄封装,是为了不让两个类型同时持有同一份底层状态——那样任何一方的生命周期改动都要跨类型核对,正是 D1 想避免的重复。

**代价**:接受"PICO 探针需要重测"——相机管线的持有方从探针本身变成新抽取的类型。

### D10. Quest MRUK 运行时装配从探针专属提升为生产

把 `Native/Probe/QuestMarkerProbeRuntimeBootstrap.cs` 移到 `Native/QuestMrukRuntimeInstaller.cs`,去掉 `internal`。删除 `QuestMarkerProvider.cs`。装配的**唯一入口是消费方显式调用** `EnsureInitialized()`——原计划的做法(把 `[RuntimeInitializeOnLoadMethod]` 的触发条件从"场景里有 `MarkerProbeEntry`"放宽为"`MarkerProbeEntry` 或 `MarkerHookTestRig`")已被真机证伪并撤销,见下。

**理由**:`MarkerHookTest` 场景需要与探针场景相同的 MRUK 运行时对象(`OVRCameraRig`/`OVRManager`/`MRUK`),但共享场景本身故意不放任何 Meta 预制体(`:6-8` 的注释),所以需要复用这套"按需动态装配"的逻辑,而不是复制一份。装配顺序有讲究——`:37` 先 `SetActive(false)` 再配置组件、`:45` 才激活,复制容易漂,故移动文件而非复制内容。

**代价**:接受"Quest 探针需要重跑确认没退化"。


**真机证伪与修正(2026-09-04)**:只放宽触发条件**不够**。`InstallForProbeScene` 是 `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`,只在**首个场景**加载后跑一次;而探针类构建的 `MRSceneDirector.firstScene` 为空,那一刻场景里只有 `MRCore`,`FindFirstObjectByType<MarkerHookTestRig>()` 返回 null,安装器直接跳过。等用户从菜单加性加载 `MarkerHookTest` 时,安装器已经过去了,不会再跑。

后果被 `QuestObservationSource.Open()` 的静默 `return`(`MRUK.Instance == null` 时直接放弃且不重试、不报错)放大成"完全没有任何反应"——分不清是没扫到、没订阅,还是场景没进。Quest 真机日志佐证:`[QuestMrukRuntimeInstaller]` 0 行(成功时会打 `Quest MRUK runtime ready`)、`[MarkerHook]` 0 行、`TrackableAdded` 0 行。

因此改三处:

1. `MarkerHookTestRig.Awake()` 的 Quest 分支**显式调用** `QuestMrukRuntimeInstaller.EnsureInitialized(out detail)`,失败即 `LogError`。
2. `QuestObservationSource` 的订阅改为**可重试且出声**:`Open()` 试一次,之后每次 `Poll()` 再试(MRUK 可能晚一两帧才就绪);MRUK 不可用时打一次 `LogWarning`,订阅成功时打一条日志。静默失败是这次真机排查最大的成本来源。
3. **删掉 `InstallForProbeScene` 这个钩子本身**(2026-09-08)。放宽触发条件后它仍然一次都不会命中:本项目**每个**构建(含探针)都先启动 `MRCore`,内容场景一律加性加载(见 CLAUDE.md 的 "One persistent core scene"),所以 `AfterSceneLoad` 永远只看得见 `MRCore`。留着它没有功能收益,却制造了一个**诱饵**——它看起来负责了 bring-up,正是它让本次排查一开始误判成"放宽触发条件即可"(tasks 4.2)。这与 CLAUDE.md "不要再增加静默失败路径"的约束同向。

装配契约因此收敛为一句:**谁需要 MRUK,谁在自己的 `Start()`/`Awake()` 里调 `EnsureInitialized()`**。两个现有消费方(`MarkerHookTestRig`、`QuestMarkerProbeAdapter.cs:92`)本来就都这么做,删除后无行为变化。

### D11. Android 包名固定为单一常量,取消按构建入口加后缀

`BuildScript` 原先给每个测试入口追加一个后缀(`.markerprobe` / `.markerhook` / `.qrcamprobe` / `.gsplatbench`),做法是「快照当前包名 → 追加 → `finally` 还原」,目的是让各测试包在设备上并存对照。**改为:所有入口共用一个常量 `k_ApplicationId = "com.uality.xiangtangshan"`,无条件写入,不快照、不还原。**

**证伪它的真机证据(2026-09-08)**:PICO 上 `pm list packages` 查到三个包——

```
com.uality.xiaotangshan.gsplatbench.qrcamprobe
com.uality.xiaotangshan.gsplatbench.qrcamprobe.markerhook
com.uality.xiaotangshan.gsplatbench.qrcamprobe.qrcamprobe.qrcamprobe
```

同一轮构建结束后 `ProjectSettings.asset` 里留下的是 `com.uality.xiaotangshan.gsplatbench.qrcamprobe.qrcamprobe.markerprobe`。

**失效机理**:还原只改内存里的 `PlayerSettings`,而一次构建要跑十几分钟;其间 Unity 只要把 `ProjectSettings.asset` 落一次盘,带后缀的值就成了**下一次构建的快照基线**,后缀于是逐次累积。原代码的注释其实已经写明了「还原只改内存值、落盘滞后」这个事实,但把它当成了读取时的注意事项,没意识到它同时是一个写入竞态。这正是 CLAUDE.md 说的「行为依赖隐式因素:调用顺序、标志位被谁先清」——快照式还原的正确性依赖「构建期间没有任何人落盘」这个无人保证的前提。

**否决的替代方案**:(a) 在 `finally` 里显式 `AssetDatabase.SaveAssets()` 强制落盘——把竞态窗口缩小而不是消除,构建被 kill 时仍然漏;(b) 从一个硬编码的基线常量派生后缀(`k_ApplicationId + suffix`)而不是从当前值派生——能修掉累积,但保留了「构建会临时改写全局 `PlayerSettings`」这个形状,`finally` 一旦漏掉就仍然污染工程文件。

**选 (c) 单一常量、无条件写入**:没有快照就没有可被污染的基线,没有还原就没有时序竞态。**代价是各测试包互相覆盖、不能在设备上并存**——这是用户明确指定的取舍(「包名固定成 `com.uality.xiangtangshan`,不要乱取名字,只用这一个」)。需要并存对照时,改回法是给 `k_ApplicationId` 传参而不是恢复快照机制。

**遗留**:设备上已经堆积的三个旧包名不会被新构建覆盖(它们是不同的应用),需要手动 `adb uninstall` 清理。

### D12. Android XR loader 构建后还原到固定默认值,不还原到「构建开始时的值」

`BuildScript` 原先在构建前 `SnapshotAndroidLoaders()`,构建后按快照 `RestoreAndroidLoaders(manager, state)`。**改为:删除快照,`RestoreAndroidLoaders(manager)` 无条件还原到 `k_DefaultAndroidLoader = k_OpenXRLoader`**——即仓库里 `XRGeneralSettingsPerBuildTarget.asset` 提交的那个值。

**证据(2026-09-08)**:工作区里该资产的 Android loader 变成了 `PXR_Loader`——

```
-  guid: 32a1694c620e9e84f92318b0e8150a49   ← Assets/XR/Loaders/OpenXRLoader.asset (HEAD)
+  guid: 1cd2f79e439b74a228114f5174e32004   ← Assets/XR/Loaders/PXR_Loader.asset
```

**失效机理**:还原逻辑本身没写错,错的是它**依赖「构建一定会跑到 `finally`」**。当天 11:23 有一次 PICO batchmode 构建被中止,那时 `ApplyAndroidLoader(k_PicoLoader)` 已经执行并 `AssetDatabase.SaveAssets()` 落了盘,而 `finally` 因进程被杀从未运行。于是 `PXR_Loader` 留在磁盘上;此后**每一次**构建启动时都把它快照成「用户的设置」,再忠实地还原回去——错误状态从此自我维持,而且每次构建都在重新确认它。

这与 D11 的包名累积是**同一个形状**:对全局工程状态做「快照 → 改 → 还原」,而正确性依赖一个无人保证的前提(D11 是「构建期间没人落盘」,D12 是「构建一定跑完 finally」)。CLAUDE.md 的「行为依赖隐式因素」正指这类。

**否决的替代方案**:(a) 把 `SaveAssets()` 推迟到 `finally` 之后——中断时仍然可能已被 Unity 自行落盘,只是窗口更小;(b) 在构建入口处校验快照值是否属于「合法静息态」并纠偏——等于要先声明默认值,那不如直接用默认值,还少一层判断。

**代价**:开发者若为了在 Editor 里跑 PICO Play Mode 而手动切成 `PXR_Loader`,任何一次构建都会把它改回 OpenXR。这是**确定且可见**的行为,优于当前「静默漂移且自我维持」。需要长期停在 PICO loader 时,改 `k_DefaultAndroidLoader` 而不是恢复快照机制。

**注**:同一个 `finally` 里的 `DisableAndroidPluginsUnder` 不受此问题影响——它用的是 `PluginImporter.SetIncludeInBuildDelegate`,会话级、不落盘,构建被中断后残留随 Editor 进程一起消失。

### D13. 删除两个老探针场景,验收收敛到 `MarkerHookTest` 一处

删除 `MarkerProbe.unity`、`PicoQrCameraProbe.unity` 及其专属代码(`Localization/Probe/`、`Localization/Native/Probe/` 两棵树共 11 个源文件)、两个 EditMode 测试、Editor 侧的 `MarkerProbeDeviceRunner.cs`,以及 `BuildScript` 里的 5 个构建入口。**跨平台验收此后只有 `MarkerHookTest.unity` 一个场景。**

**推翻了什么**:proposal 的「保留不动」原本写着「整个 `Localization/Probe/` 与 `Localization/Native/Probe/`(除两处提升/改造)」。那条的前提是「老探针还要继续当回归基准用」——用户在 7.8 收尾时判定不需要:两端各测一个不同场景本来就不是对照,而三套 marker 场景(`MarkerHookTest` / `MarkerProbe` / `PicoQrCameraProbe`)对同一件事有三份验收装置,本身就是要还的债。

**为什么原来是两个不同场景**:7.8 的配法不是「两端对照」,而是「哪个决策改了哪条路径」——D9(相机管线抽取)只暴露在 `PicoQrCameraProbe`(Quest 没有 AprilTag 相机路径),D10(MRUK 装配提升)只暴露在 Quest 的 `MarkerProbe`(`PicoQrCameraProbe` 不碰 MRUK)。这个配法在任务里只写了场景名没写理由,容易误读成对照实验——这是 7.8 描述本身的缺陷。

**删除前确认的边界**:`MarkerHookTestRig` / `MarkerHookTestHud` / `QuestObservationSource` / `PicoFiducialObservationSource` / `QuestMrukRuntimeInstaller` 对 Probe 的提及**全部是注释**,真实代码依赖是单向的(Probe → 生产代码)。删除后重编译 `error CS` 为 0。随之失去消费方的 16 个遥测只读访问器(`IsCameraOpen`/`LastDetectMs`/…)一并删除——它们唯一的消费方是 `PicoQrCameraProbe` 的 `OnGUI`。

**代价**:
- 老探针积累的 JSONL 遥测 schema(`MarkerProbeJsonlWriter` 的 `markerId`/`picoArUcoId`/`qrId`/`logicalMarkerId` 分层)随之消失。它本来就是被 D3 否决的身份收敛路线的产物,留着是死重。
- `MarkerIdParser` / `IMarkerIdParser` 一并删除。proposal 已明确「不复用 `IMarkerIdParser`」,此处只是把它真正清掉。
- 连带删掉 `MarkerProbeDeviceRunner.cs`,其中包含 `PICO Official CameraRendering` 的装机脚本。该文件的 `PackageName` 常量是 `com.DefaultCompany.MixedRealityTemplate`——早已与实际包名不符,工具处于失效状态,不构成实际损失。对应的构建入口 `MRBase/Build/PICO Official CameraRendering Sample` 保留。

## Risks / Trade-offs

- **Quest 已验证路径被改动(D4)** → 该端必须完整重测,不能只跑 EditMode。测试项至少覆盖:持续位姿更新、遮挡后恢复、`Stabilized` 是否真的触发。`Stabilized` 在改动前**已被源码证实从不触发**(D4 的三条 MRUK 证据),所以这一项是**验证修复**而非验证疑点。
- **PICO 数据源同时要换(D9)** → 抽取相机管线与检测循环是本 change 最大的单块变更,`PicoQrCameraProbe.cs` 的历史行为(自检方块、遥测 OnGUI)必须在抽取后保持等价。
- **下线范围比原设计更大** → 除契约替换外,还要删除整条"标记 → 内容"生产链(9 个源文件 + 1 个场景 + 6 个测试文件)并注释掉 ITE 消费点。`IteHostBootstrap.cs` 对 `MarkerSourceAdapter` 的引用一旦注释,若还有其他文件间接依赖它的类型,会连带报错——按 probe 的明确指示"先注释掉关于 ITE 的行为,不要写中间层来解决错误",逐个注释而不是补兼容层。
- **AprilTag 路线的真机度量**(已于 2026-09-04 实测,见 Measurements) → 检测率、多标记并发、滞回三项已拿到数字并达标;**端到端延迟与 CPU 占用仍无数字**,`detectMs` 只是后者的代理。按约定如实记录未达标项,未手调参数掩盖。
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

## Measurements(2026-09-04 真机实测)

**条件**:PICO 4 Ultra(`PA9410MGL5140677G`)与 Quest 3(`2G0YC1ZF7Z0SC3`),`MRBase/Build/Marker Hook Test/{PICO,Quest} Development`,标记为 `tagStandard41h12` 的 ID 0 与 250(Quest 侧为承载 `"0"`/`"250"` 的等价 QR)。原始日志存档于本目录 `logs/pico-2026-09-04.log` 与 `logs/quest-2026-09-04.log`,下列全部数字可由其复算。

### M1. 派发速率——D7「不限流」的实测后果

| 平台 | payload | 可见总时长 | 事件数 | 速率 |
|---|---|---|---|---|
| PICO | 0 | 39.8 s | 224 | **5.6 Hz** |
| PICO | 250 | 39.3 s | 220 | **5.6 Hz** |
| Quest | 0 | 18.0 s | 1261 | **70.0 Hz** |
| Quest | 250 | 29.0 s | 2015 | **69.4 Hz** |

两端相差 **12.5 倍**。PICO 的 5.6 Hz 就是 4U 推送式相机的出帧率,Quest 的 70 Hz 就是 72 fps 下每帧一次的 `Poll()`。语义一致(可见期间持续派发最新位姿),速率不一致——这正是 D7 明确接受的取舍,实测确认它不是隐患而是设计。

Quest 侧的 3276 次同时是 **D4 的判决书**:改动前 MRUK 对已存在的 trackable 显式去重、位姿更新不派发事件(`MRUK.Trackers.cs:327-344`),两个标记的理论上限是 **2 次**。

### M2. 丢失滞回——D5 判据 1.0 s

从每个 payload 的最后一次 `Observed` 到对应 `Lost` 的间隔:

| 平台 | n | min | max | mean |
|---|---|---|---|---|
| PICO | 15 | 1.001 s | 1.023 s | 1.011 s |
| Quest | 4 | 1.000 s | 1.002 s | 1.001 s |

**达标。** 两端偏差全部落在 `[1.000, 1.023]`,上界 23 ms 来自 `Tick` 的帧粒度(72 fps = 13.9 ms),不是滞回逻辑本身的误差。两端走同一份判定,数字也确实一致——D5 的核心主张成立。

### M3. 检测耗时——CPU 占用的代理指标

`detectMs`(AprilTag 单帧检测,1280×960,n=63):min 45.2 / p50 **71.7** / p95 79.6 / max 124.4 / mean 69.6 ms。

有一条结构性发现:**命中时 45–52 ms,未命中时 68–79 ms**。AprilTag 在图中没有 tag 时要跑完整幅搜索、没有早退路径,所以「扫不到」比「扫得到」更贵约 50%;峰值 124.4 ms 出现在连续未命中段。这意味着待机(无标记)是这条管线的**最坏**功耗工况,不是最好——D6 的 `Pause()` 因此比预期更有价值。

这**不是**端到端延迟:端到端还要叠加 4U 相机的取帧与推送延迟,本轮未插桩测量。见"未达标项"。

### M4. 检测率——原口径不成立

日志里的 `detect N/M = P%`,分母 M 累计的是**每一次检测循环**,包含标记根本不在视野的帧。因此 P% 随时间单调衰减,不是检测率:

- 第一轮:70% → 100% → 100% → 17% → 50% → 0 → …一路降到 **6%**
- 第二轮:53% → 77% → 100% → 53% → 100%

按 30 帧窗口取增量重算:**标记稳定在视野中时,窗口命中率为 100%**(第一轮第 2/3 窗口、第二轮第 3/5 窗口);标记在画面边缘或移动中时 50–77%。累计的 6% 只说明标记大部分时间不在视野,不说明检测能力。

**结论**:`detectAttempts` 的分母口径必须改成"标记应当可见的帧"(或干脆改成滑动窗口命中率),否则这条遥测不可比。本 change **不修**——它只是 `PicoFiducialObservationSource` 的一行 `OnGUI` 遥测,不进契约、不影响任何派发行为;记为已知缺陷。

### M5. 多标记并发

两端两个标记的可见时长与事件数近乎相等(PICO 39.8/39.3 s、224/220 次;Quest 18.0/29.0 s、1261/2015 次),各自的滞回独立计时(M2 的 15 次与 4 次分别归属两个 payload)。**并发无相互干扰,达标。**

### M6. 误检——D3 留下的真实缺口

PICO 的 445 次检测中,`hamming > 0` 出现两次:

- `tag 0 hamming=1 margin=76.5` — 真标记,一位纠错,位姿与相邻帧一致,无害。
- `tag 64 hamming=2 margin=3.8 pos=(0.010, -0.119, -0.223)` — **假标记**。场上根本没有 ID 64,位姿解在相机前 22 cm。它照样作为 `MarkerObservation` 派发给了业务层,并在 1.010 s 后正常 `Lost`。

`margin` 的分布是干净的双峰:真检测 76.5–99.6(n=444,p50 = 91.1),假检测 3.8(n=1)。**一条 `margin >= 20` 的下限即可滤掉,代价为零。**

本 change 没有加这条过滤,是 D3 的直接后果——契约层只吐最原始信息、不做质量判断。但这把误检过滤的责任明确推给了业务层,而业务层**目前拿不到 `margin` / `hamming`**:`MarkerObservation` 只有 `Platform` / `RawPayload` / `Pose`。这是 D3 的一个真实缺口,记入 Open Questions,本 change 不修。

### 未达标 / 未测项

按 tasks 0.4 的约定,以下项本轮没有拿到数字,如实记录而不用其他数字顶替:

| 项 | 状态 | 原因 |
|---|---|---|
| 端到端延迟 | **无数字** | 需在 4U 取帧时刻打时间戳并一路带到派发点,本轮未插桩 |
| CPU 占用 | **无直接数字** | 仅有 M3 的 `detectMs` 作代理;未采样进程级 CPU |
| PICO `Pause()` 的性能收益 | **未测**(tasks 7.4) | HUD 的 PAUSE 按钮已在包内,可随时补测 |

## Open Questions

`probe-report.md` 遗留的 7 项 `[ASSUMED]` 开放假设已在 `/opsx:propose` 重生成阶段与用户逐条确认,结论已写入上述对应决策(D4/D6/D9/Non-Goals/Risks)与 `tasks.md` 的"已确认的前置结论"。`design D4` 中 `MRUKTrackable.IsTracked` 的真机行为与真机度量数字已在 2026-09-04 的验收中核实(见 Measurements);D6 的 `Pause()` 性能收益仍未测(tasks 7.4)。

真机验收**新暴露**出两个未决问题,均不在本 change 范围内解决:

**OQ1. 误检的过滤责任无处安放。** D3 决定契约层只吐最原始信息,`MarkerObservation` 因此只有 `Platform` / `RawPayload` / `Pose`。实测(M6)证明假检测确实会发生并原样派发给业务层,而分辨它所需的 `margin` / `hamming` **恰恰被 D3 挡在了契约之外**——业务层拿不到判据,却被交付了判断责任。三条出路:(a) 在 `PicoFiducialObservationSource` 内部加 `margin` 下限,契约不变——最省,但把一个质量策略藏进平台实现,正是 D1 想消灭的形状;(b) `MarkerObservation` 增一个平台无关的 `Confidence`(Quest 侧填 1.0),把判据交给业务层——结构最正确,但引入了 D3 明确拒绝的"抹平差异";(c) 维持现状,业务层按位姿合理性自行兜底。**推荐 (b)**,但它动的是 D3 本身,应当另立 change 而不是在本 change 里悄悄改契约。

**OQ3. PICO 包里 MRUK 每帧抛 `DllNotFoundException`。** 2026-09-08 的 PICO 探针真机跑中,25 秒内 1520 次(≈ 每帧一次):

```
DllNotFoundException: Unable to load DLL 'OVRPlugin'
  at OVRPlugin.get_initialized ()                      OVRPlugin.cs:3644
  at MRUK.get_IsOpenXRAvailable ()                     MRUK.Shared.cs:51
  at MRUK.UpdateGlobalContext ()                       MRUK.Shared.cs:170
  at MRUKGlobalContext.Update ()                       MRUKGlobalContext.cs:45
```

`excludePluginRoot: k_MetaPackageRoot` 只排掉 Meta 的**原生** `.so`,MRUK 的**托管**程序集仍然进包;MRUK 自己的 `MRUKGlobalContext` 是包内自动实例化的常驻单例,于是在 PICO 上每帧调一次 `OVRPlugin` 并每帧抛一次。

**这不是本 change 引入的**:`excludePluginRoot` 早于本 change,`QuestMrukRuntimeInstaller` 被 `#if MRBASE_HAS_MRUK && MRBASE_QUEST` 挡在 PICO 之外。但**也无法证明它一直存在**——9 月 4 日那轮 PICO 日志的 logcat 过滤只留了 `I/Unity`,0 行 `E/Unity`,错误级别的行当时就被丢掉了。修它要么在 PICO 构建里连托管程序集一起剥离,要么接受它;两者都超出本 change 范围。

**OQ2. `detectAttempts` 的分母口径。** 见 M4——现口径把"标记不在视野"计入失败,使 `detect N/M = P%` 随时间衰减到无意义。修它需要先定义"标记应当可见"这件事该由谁判断,不是改一行分母的事。
