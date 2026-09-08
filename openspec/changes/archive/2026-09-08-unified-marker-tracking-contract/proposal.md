## Why

`IMarkerTrackingProvider` 现有的两个事件——`MarkerResolved(string, Pose)` 与 `MarkerLost(string)`——把平台差异漏给了调用方,而且漏的不只是身份,是节奏本身。

Quest 只在 `TrackableAdded` 时复制一次 Pose(`QuestMarkerProvider.cs:35`),PICO 则持续推快照。下游 `MarkerStabilizer` 用 `stableFrameThreshold = 30` **帧**判稳(`MarkerStabilizer.cs:18,60`)——这在 PICO 6 Hz 采样下是 5 秒,在 Quest 上则**永远达不到**,`Stabilized` 不会触发。这不是推测:MRUK 包源码(`Library/PackageCache/com.meta.xr.mrutilitykit@2a23a4eea58d/Core/Scripts/MRUK.Trackers.cs:162,174,327-344,346-350`)证实 MRUK 只暴露 `TrackableAdded`/`TrackableRemoved` 两个事件、没有 `TrackableUpdated`,且对已存在的 trackable 显式去重、位姿更新不派发任何事件。结论:**Quest 的「标记 → 内容」生产链路从未在真机跑通过**——不是位姿不准,是整条链死掉。

丢失判定同样分叉:PICO 靠快照差集,当帧缺席即判丢(`PicoMarkerProvider.cs:134-140`);Quest 靠 `TrackableRemoved`。短暂遮挡在 PICO 上就是"离开"。暂停也分叉:唯一的"停"是 `StopTracking()`,它在 PICO 上会 `UnBindEnterpriseService()` 解绑整个企业服务(`PicoMarkerProvider.cs:69`)——暂停扫描的代价是解绑硬件。

**范围经 `/opsx:probe` 校正**:原方案还打算在契约层把 Quest 的 QR 原文与 PICO 的整数 ID 收敛成统一业务身份(三层 `MarkerIdentity` + `LogicalId`),理由是 `AnchorRegistry.cs:18` 的 `||` 双条件兜底把身份决策藏进了两个处理器的相互作用里。这条**被否决**——「现在 Marker 的行为不牵扯到解析和抹平 Quest 与 Pico 的差异,hook 返回最原始的信息即可」,解析与映射归业务层。本 change 因此**只统一节奏、丢失滞回、暂停**这三件事,事件载荷只带平台标签与原始 payload,不做任何身份收敛。

同样被划出范围的是现有的"标记 → 内容"生产链(`MarkerAnchorService` → `AnchorRegistry` → `AnchorEntity`):它从未有过真机验收面——`anchor_registry.json` 在仓库里不存在(`MarkerTrackingBootstrapper.cs:6` 默认引用它,无 `StreamingAssets/`),唯一驱动它的 `LocalizationDemo.unity` 是 mock + 手点按钮凑 30 帧假位姿(`DemoMarkerTrigger.cs:24-31`)——而它做的正是被划出范围的身份解析,故随本 change **下线**而非迁移。ITE 导览包的消费同理下线(注释不删,降低耦合)。

## What Changes

- **BREAKING** `IMarkerTrackingProvider` 整体替换为新契约:平台实现只提供 `Open()` / `Close()` / `Pause()` / `Resume()` / `Poll()`,不再自己发事件;事件唯一的发出点是新增的 `MarkerTrackingSession`(纯 C#,`Tick(float deltaTime)` 驱动)。
- **BREAKING** 事件载荷从裸 `string` 换成 `MarkerObservation { MarkerPlatform Platform; string RawPayload; Pose Pose }`。`RawPayload` 一律非空——Quest 发 QR 原文,PICO 把 `iMarkerId` `ToString()`。**不做身份分层,不引入 `LogicalId`/`NativeId`,不复用 `IMarkerIdParser`**——已由本次 probe 校正确认业务层自行解析。
- 派发节奏:Session 按 `Tick` 持续派发可见期间的最新位姿,**不做跨平台限流**(Quest 与 PICO 各自的原生频率原样传递,不压到同一个 Hz)。
- `MarkerLost` 统一走 1.0 s 时间滞回,平台移除信号(如 Quest 的 `TrackableRemoved`)作为"置为过期"的输入进入同一判定,不直接派发丢失。
- 新增 `Pause()` / `Resume()`,与 `Open()` / `Close()` 分离;PICO 上 `Pause()` 停检测但保留 4U 相机会话,`Close()` 才释放。
- `MarkerStabilizer` **本次不动**——阈值仍是帧数(`stableFrameThreshold`),不换算成秒。原设计的 D7(改秒)已被否决。
- **下线**(非迁移)现有"标记 → 内容"生产链:`MarkerAnchorService`、`AnchorRegistry`、`AnchorEntity`、`AnchorEntityData`、`IAnchorDataSource`、`LocalJsonAnchorDataSource`、`IContentLoader`、`ImageContentLoader`、`DemoMarkerTrigger`、`MarkerTrackingBootstrapper`,连同 `LocalizationDemo.unity` 场景与对应测试一并删除。
- ITE 导览包(`IteHost/MarkerSourceAdapter.cs`)**注释掉**引用,不写迁移或适配层;`IteHostBootstrap.cs:42,69` 里对它的调用同样注释。
- PICO 观测源换数据来源:把 `PicoQrCameraProbe` 的相机会话与 AprilTag 检测循环抽成 `PicoFiducialObservationSource`,探针改为消费它、只留自己的遥测与 `markerBox`;删除仍在用 TOB `SetMarkerInfoCallback` 的 `PicoMarkerProvider.cs`(该路线已被真机证伪)。
- Quest 侧的 MRUK 运行时装配从探针专属(`Native/Probe/QuestMarkerProbeRuntimeBootstrap.cs`)提升为生产可用(`Native/QuestMrukRuntimeInstaller.cs`),触发条件放宽为"场景里有 `MarkerProbeEntry` 或 `MarkerHookTestRig`";删除 `QuestMarkerProvider.cs`。
- 新建 `Assets/Scenes/MarkerHookTest.unity`——两端共用的验收场景:`MarkerHookTestRig` 按 `#if MRBASE_QUEST / MRBASE_PICO` 建对应观测源并订阅 `MarkerObserved`/`MarkerLost`;扫到内容后走 `MarkerStabilizer.Feed`,`Stabilized` 时在扫描位置放一个盒子 + 世界空间 TMP 标签(平台 / payload,billboard 朝相机);每次事件打一条 `[MarkerHook]` 前缀日志;常驻 HUD 显示活跃标记与 `Observed`/`Lost` 累计次数,并提供 Pause/Resume 按钮。
- `BuildScript.cs` 新增两个构建入口:`MRBase/Build/Marker Hook Test/Quest Development` 与 `/PICO Development`,`applicationIdSuffix` 用 `.markerhook`。

## Capabilities

### New Capabilities

- `unified-marker-tracking-contract`: 跨平台标记追踪的公共契约——统一观测源的纯查询边界、统一派发节奏(不限流)、统一丢失滞回、统一暂停/恢复语义;载荷只带平台与原始 payload,不做身份收敛(该收窄由本次 probe 校正)。新增一个专测该 hook 行为的场景。

### Modified Capabilities

(无。`openspec/specs/` 当前为空,尚无已归档主 spec 需要 delta。)

## Impact

**公共契约**
- `Assets/Scripts/Localization/IMarkerTrackingProvider.cs` — 替换
- 新增 `MarkerTrackingSession.cs`、`MarkerObservation.cs`、`IMarkerObservationSource.cs`

**平台实现**
- 新增 `QuestObservationSource.cs`(原 `QuestMarkerProvider.cs` 删除)
- 新增 `PicoFiducialObservationSource.cs`(原 `PicoMarkerProvider.cs` 删除,数据源由 TOB 换成 AprilTag)
- `MockMarkerProvider.cs` → `MockObservationSource.cs`
- `Native/Probe/QuestMarkerProbeRuntimeBootstrap.cs` → `Native/QuestMrukRuntimeInstaller.cs`(提升为生产,去掉 `internal`)
- `Native/Probe/PicoQrCameraProbe.cs` 改为消费 `PicoFiducialObservationSource`,只留遥测与 `markerBox`

**下线(删除,非迁移)**
- 源码:`MarkerAnchorService.cs`、`AnchorRegistry.cs`、`AnchorEntity.cs`、`AnchorEntityData.cs`、`IAnchorDataSource.cs`、`LocalJsonAnchorDataSource.cs`、`IContentLoader.cs`、`ImageContentLoader.cs`、`DemoMarkerTrigger.cs`、`MarkerTrackingBootstrapper.cs`
- 场景:`Assets/Scenes/LocalizationDemo.unity`
- 测试:`AnchorEntityTests.cs`、`AnchorRegistryTests.cs`、`LocalJsonAnchorDataSourceTests.cs`、`MarkerAnchorServiceTests.cs`、`MockMarkerProviderTests.cs`、`Ite/MarkerSourceAdapterTests.cs`
- 注释不删:`Scripts/IteHost/MarkerSourceAdapter.cs` 整个文件的引用点、`IteHostBootstrap.cs:42,69`

**保留不动**
- `MarkerStabilizer.cs` + `MarkerStabilizerTests.cs`(帧数阈值不变;下线后暂时零消费方,仅被新场景的 `MarkerHookTestRig` 消费)
- `AprilTagDetectorCore.cs` / `PlanarPoseSolver.cs` / `PicoEnterpriseCameraPose.cs` / `PoseMath.cs` / `PlatformOffsetConfig.cs` 及各自测试
- ~~整个 `Localization/Probe/` 与 `Localization/Native/Probe/`~~ —— **该条已被 D13 推翻**:两棵树连同 `MarkerProbe.unity` / `PicoQrCameraProbe.unity` 一并删除,跨平台验收收敛到 `MarkerHookTest` 一个场景。`QuestMrukRuntimeInstaller` 与 `PicoFiducialObservationSource` 已先行提升为生产代码,不受影响。

**新增**
- `Assets/Scenes/MarkerHookTest.unity` + `MarkerHookTestRig.cs` + HUD
- `BuildScript.cs`:两个新构建入口(Marker Hook Test / Quest Development、/ PICO Development)

**测试**
- 增:`MarkerTrackingSessionTests.cs`(持续派发、滞回、暂停)
- 改:`MarkerStabilizerTests.cs` 若因消费方接口变化需要跟随调整,阈值单位本身不变

## Open Assumptions

以下 7 项 `[ASSUMED]` 开放假设均已在 `/opsx:propose` 重生成阶段与用户逐条确认(全部采纳推荐项),结论已写回对应 artifact,当前无遗留的未决假设:

| 假设 | 确认结论 | 写回位置 |
|---|---|---|
| Success criteria 是否按已确认决策推导 | 接受推导结果,不逐条重新确认 | tasks.md 第 7 章 |
| `MarkerHookTest` 与 `PicoQrCameraProbe` 场景能否共存 | 按互斥使用处理,不解决运行时切换共存 | design.md Non-Goals / Risks |
| PICO `Pause()` 停在哪一层 | 停在检测分派层,跳过 `DispatchDetection`,相机流不变 | design.md D6, tasks.md 3.6 |
| PICO 相机管线抽取边界 | `latestFramePose`/`cameraBufferHandle` 完全搬入新类型,探针不再持有相机状态 | design.md D9, tasks.md 3.4/3.5 |
| Quest `IsTracked=false` 的处理 | 计入缺席,进入统一滞回判定,不立即丢失 | design.md D4, spec.md, tasks.md 3.2 |
| HUD 在 PICO 上的 XR 射线交互 | 假定可行,复用现有 TMP+UGUI 模式,真机验收时再核实 | design.md Risks, tasks.md 6.7 |
| AprilTag 真机度量何时补齐 | 接通生产链路后才第一次实测,数字与判据写回 design | tasks.md 0.4 / 7.9 / 8.2 |

`design.md` 中 `MRUKTrackable.IsTracked` 的真机实际行为、`Pause()` 的性能收益、AprilTag 的真机度量数字仍需在 tasks 第 7 章真机验收阶段用实测数据核实——这些是**验证已确认的决策**,不是**未决策的假设**。
