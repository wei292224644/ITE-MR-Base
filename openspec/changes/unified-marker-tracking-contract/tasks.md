## 0. 已确认的前置结论(实现前对齐,不再是待决问题)

以下结论已在 `/opsx:propose` 阶段与用户逐条确认,记录于此供实现时对照,不是需要动代码前先讨论的开放问题:

- **0.1** `Pause()` 在 PICO 上停在检测分派层:相机流(`StartGetImageDatafor4U`/`OnImageAvailable`)与 4U 会话保持不变,只是取到的帧不再交给 `DispatchDetection`。对应实现见任务 3.6,设计见 design D6。
- **0.2** Quest 的 `MRUKTrackable.IsTracked` 为 `false` 时计入"缺席"进入统一滞回判定,不视为立即移除。对应实现见任务 3.2,设计见 design D4,验证见任务 7.6。
- **0.3** `MarkerHookTest` 场景与 `PicoQrCameraProbe` 探针场景按**互斥使用**处理,不支持 `MRSceneDirector` 运行时切换共存;该限制记录为已知约束(design Non-Goals / Risks),不在本 change 内解决。
- **0.4** AprilTag 路线的端到端延迟、检测率、CPU 占用、多标记并发这四项真机度量,接受在本 change 接通生产链路后才第一次实测;判据与数字在任务 7.9 / 8.2 阶段确定并写回 design,不预先设定门槛。

## 1. 新增契约与会话层(不动既有代码,可独立验证)

- [x] 1.1 定义 `MarkerObservation`(`Platform: MarkerPlatform` + `RawPayload: string` + `Pose`,`RawPayload` 一律非空,不做身份分层)
- [x] 1.2 定义 `IMarkerObservationSource`:`Open()` / `Close()` / `Pause()` / `Resume()` / `Poll()`,**不含任何 event**
- [x] 1.3 实现 `MarkerTrackingSession`:纯 C#、不继承 MonoBehaviour、不读 `Time.deltaTime`,唯一入口 `Tick(float deltaTime)`
- [x] 1.4 Session 按 `Tick` 直接派发,**不做跨平台限流或频率转换**——可见期间每个 `Tick` 都携带该标记的最新位姿
- [x] 1.5 Session 内实现丢失时间滞回(默认 1.0 s,design D5);平台移除信号作为"置为过期"的输入进入同一判定,不另开一条路径
- [x] 1.6 Session 内实现 `Pause()` / `Resume()`;**暂停期间不累计缺席时长**,否则长暂停后恢复会先吐一轮虚假丢失

## 2. 会话层单测(本 change 的风险全在这里,必须在 EditMode 锁死)

- [x] 2.1 `MockObservationSource`:可注入任意观测序列与"本周期无观测"
- [x] 2.2 持续派发:多次 `Tick` 均携带最新位姿,验证不限流语义——不因为"已经发过一次"而跳过后续 `Tick`
- [x] 2.3 滞回 A:连续缺席 0.5 s 后重现 → 不派发丢失
- [x] 2.4 滞回 B:连续缺席 >1.0 s → 派发且仅派发一次丢失
- [x] 2.5 滞回 C:平台移除信号后在滞回内重现 → 不派发丢失
- [x] 2.6 滞回 D:丢失后重现 → 派发观测且缺席计时重置
- [x] 2.7 暂停:暂停期间不派发任何事件;暂停时长 >1.0 s 后恢复且标记始终在视野内 → 不派发丢失
- [x] 2.8 时间注入:同一组观测在不同 `deltaTime` 切分下滞回结论一致

## 3. 平台观测源

- [x] 3.1 `MockMarkerProvider` → `MockObservationSource`,删事件,实现 `Poll()`
- [x] 3.2 `QuestObservationSource`:持有活动 `MRUKTrackable` 集合,每次 `Poll()` 读最新 `transform` / `IsTracked`(design D4,**行为改变**);`IsTracked=false` 一律计入缺席,MUST NOT 直接触发移除(0.2 的结论)
- [x] 3.3 `QuestObservationSource` 的 `TrackableRemoved` 只从活动集合移除,不派发丢失——丢失归 Session
- [x] 3.4 抽取 `PicoFiducialObservationSource`:把 `PicoQrCameraProbe` 的相机会话(`OnServiceBound`/`StartCameraStream`/`OnImageAvailable`/采样节流/`DispatchDetection`/`DrainResults`)与检测循环**连同 `latestFramePose`、`cameraBufferHandle` 全部状态**搬进新类型,`Poll()` 返回当帧 AprilTag 检测快照(design D9,0.1 抽取边界结论:完全搬移,不留薄封装)
- [x] 3.5 `PicoQrCameraProbe` 改为消费 `PicoFiducialObservationSource`,不再持有任何相机/检测状态,只保留自己的遥测 `OnGUI` 与 `markerBox` 自检逻辑
- [x] 3.6 `PicoFiducialObservationSource` 的 `Pause()` 只跳过 `DispatchDetection`,**相机流与 4U 会话保持不变**(0.1 的结论);`Close()` 才释放相机与企业服务
- [x] 3.7 删除 `PicoMarkerProvider.cs`(TOB `SetMarkerInfoCallback` 路线,已被真机证伪)
- [x] 3.8 逐个确认:两个观测源都不含 `event`,都不持有丢失判定所需的历史状态

## 4. Quest MRUK 运行时装配提升(design D10)

- [x] 4.1 把 `Native/Probe/QuestMarkerProbeRuntimeBootstrap.cs` 移到 `Native/QuestMrukRuntimeInstaller.cs`,去掉 `internal`
- [x] 4.2 触发条件从"找到 `MarkerProbeEntry`"放宽为"`MarkerProbeEntry` 或 `MarkerHookTestRig`"
- [x] 4.3 删除 `QuestMarkerProvider.cs`

## 5. 下线旧"标记 → 内容"生产链(删除,非迁移)

- [x] 5.1 删除源码:`MarkerAnchorService.cs`、`AnchorRegistry.cs`、`AnchorEntity.cs`、`AnchorEntityData.cs`、`IAnchorDataSource.cs`、`LocalJsonAnchorDataSource.cs`、`IContentLoader.cs`、`ImageContentLoader.cs`、`DemoMarkerTrigger.cs`、`MarkerTrackingBootstrapper.cs`
- [x] 5.2 删除场景:`Assets/Scenes/LocalizationDemo.unity`
- [x] 5.3 删除测试:`AnchorEntityTests.cs`、`AnchorRegistryTests.cs`、`LocalJsonAnchorDataSourceTests.cs`、`MarkerAnchorServiceTests.cs`、`MockMarkerProviderTests.cs`、`Ite/MarkerSourceAdapterTests.cs`
- [x] 5.4 注释掉 `Scripts/IteHost/MarkerSourceAdapter.cs` 的引用点与 `IteHostBootstrap.cs:42,69` 对它的调用;**不写中间层适配**,若引出编译错误就继续沿链路逐个注释,不补兼容层
- [x] 5.5 全仓 grep 确认 `IMarkerTrackingProvider` 无残留引用后删除 `IMarkerTrackingProvider.cs`

## 6. 新场景与 HUD

- [x] 6.1 新建 `Assets/Scenes/MarkerHookTest.unity`(与 `MRCore` 组成 `ProbeScenes` 模式的场景列表)
- [x] 6.2 实现 `MarkerHookTestRig`:`Awake` 按 `#if MRBASE_QUEST / MRBASE_PICO` 建对应观测源,装配 `MarkerTrackingSession`,`Update` 里驱动 `Tick(Time.deltaTime)`,订阅 `MarkerObserved`/`MarkerLost`
- [x] 6.3 `MarkerObserved` 喂进 `MarkerStabilizer.Feed`(design D8);`Stabilized` 时在稳定位姿处显示盒子 + 世界空间 TMP 标签(平台 / `RawPayload`,billboard 朝相机)
- [x] 6.4 `MarkerLost` 时移除/隐藏对应盒子,并 `Reset` 对应的 `MarkerStabilizer` 状态
- [x] 6.5 每次 Observed/Lost 事件打一条 `[MarkerHook]` 前缀 Console 日志
- [x] 6.6 常驻 HUD:列出活跃标记、`Observed`/`Lost` 累计次数
- [x] 6.7 HUD 增加 Pause/Resume 按钮,驱动 `MarkerTrackingSession` 的 `Pause()`/`Resume()`;沿用 `MarkerProbeXrControls` 同款 TMP + UGUI 模式(假定 PICO 侧 XR 射线可点击 UGUI `Button`,若真机验收(任务 7.4)发现点不动,单独排查交互层,不在本任务内解决)
- [x] 6.8 `BuildScript.cs` 新增两个菜单项:`MRBase/Build/Marker Hook Test/Quest Development` 与 `/PICO Development`(PICO 带 `excludePluginRoot: k_MetaPackageRoot`,照 `BuildScript.cs:114-125` 现有模式),`applicationIdSuffix` 用 `.markerhook`

## 7. 真机验收

- [x] 7.1 EditMode 全绿:`unity command run_tests --json`
- [ ] 7.2 PICO:扫 ID 0 / 250,盒子出现在标记上并显示 `RawPayload`
- [ ] 7.3 PICO:遮挡 <1 s 不丢、>1 s 丢且只丢一次
- [ ] 7.4 PICO:`Pause()` 期间 CPU 下降,`Resume()` 后无权限申请、无相机会话重建
- [ ] 7.5 **Quest:验证 `Stabilized` 修复生效**——改动前它被 MRUK 源码证实从不触发(design D4 三条证据),本项是验证修复而非验证疑点
- [ ] 7.6 Quest:标记持续可见时位姿随 Transform 更新,而非停在首次发现的那一帧
- [ ] 7.7 Quest:扫 QR 0 / 250 回归,确认改动未破坏已验证路径
- [ ] 7.8 PICO 探针(`PicoQrCameraProbe`)与 Quest 探针(`MarkerProbe` 场景)各重跑一次,确认抽取(D9)与装配移动(D10)没有造成退化
- [ ] 7.9 两端各记一份日志存档;任一判据未达成,把数字与原因写回 design,不手调参数掩盖

## 8. 收尾

- [x] 8.1 回填 design 的 Open Questions 指向的 proposal.md `## Open Assumptions`(0.1–0.4 的结论)
- [ ] 8.2 把 0.4 列出的真机度量数字与结论写回 design;未达标项写清数字与原因,不手调参数掩盖
