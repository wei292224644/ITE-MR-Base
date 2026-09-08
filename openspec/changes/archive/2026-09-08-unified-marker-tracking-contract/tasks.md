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
- [x] 4.4 `MarkerHookTestRig.Awake()` 的 Quest 分支显式调用 `QuestMrukRuntimeInstaller.EnsureInitialized(out detail)`,失败 `LogError`(design D10 修正:`AfterSceneLoad` 钩子对加性加载的场景不生效)
- [x] 4.5 `QuestObservationSource` 的 MRUK 订阅改为可重试且出声:`Open()` 试一次,之后每次 `Poll()` 重试;不可用时 `LogWarning` 一次,订阅成功打日志(design D10 修正)

## 5. 下线旧"标记 → 内容"生产链(删除,非迁移)

- [x] 5.1 删除源码:`MarkerAnchorService.cs`、`AnchorRegistry.cs`、`AnchorEntity.cs`、`AnchorEntityData.cs`、`IAnchorDataSource.cs`、`LocalJsonAnchorDataSource.cs`、`IContentLoader.cs`、`ImageContentLoader.cs`、`DemoMarkerTrigger.cs`、`MarkerTrackingBootstrapper.cs`
- [x] 5.2 删除场景:`Assets/Scenes/LocalizationDemo.unity`
- [x] 5.3 删除测试:`AnchorEntityTests.cs`、`AnchorRegistryTests.cs`、`LocalJsonAnchorDataSourceTests.cs`、`MarkerAnchorServiceTests.cs`、`MockMarkerProviderTests.cs`、`Ite/MarkerSourceAdapterTests.cs`
- [x] 5.4 注释掉 `Scripts/IteHost/MarkerSourceAdapter.cs` 的引用点与 `IteHostBootstrap.cs:42,69` 对它的调用;**不写中间层适配**,若引出编译错误就继续沿链路逐个注释,不补兼容层
- [x] 5.5 全仓 grep 确认 `IMarkerTrackingProvider` 无残留引用后删除 `IMarkerTrackingProvider.cs`

## 6. 新场景与 HUD

- [x] 6.1 新建 `Assets/Scenes/MarkerHookTest.unity`(与 `MRCore` 组成 `ProbeScenes` 模式的场景列表)
- [x] 6.2 实现 `MarkerHookTestRig`:`Awake` 按 `#if MRBASE_QUEST / MRBASE_PICO` 建对应观测源,装配 `MarkerTrackingSession`,`Update` 里驱动 `Tick(Time.deltaTime)`,订阅 `MarkerObserved`/`MarkerLost`
- [x] 6.3 `MarkerObserved` 直接摆/挪盒子 + 世界空间 TMP 标签(平台 / `RawPayload`,billboard 朝相机);**不经过 `MarkerStabilizer`**(design D8,已被真机证伪的反例)
- [x] 6.4 `MarkerLost` 时移除对应盒子(测试场景不持有 `MarkerStabilizer`,无状态需 `Reset`)
- [x] 6.5 每次 Observed/Lost 事件打一条 `[MarkerHook]` 前缀 Console 日志
- [x] 6.6 常驻 HUD:列出活跃标记、`Observed`/`Lost` 累计次数
- [x] 6.7 HUD 增加 Pause/Resume 按钮,驱动 `MarkerTrackingSession` 的 `Pause()`/`Resume()`;沿用 `MarkerProbeXrControls` 同款 TMP + UGUI 模式(假定 PICO 侧 XR 射线可点击 UGUI `Button`,若真机验收(任务 7.4)发现点不动,单独排查交互层,不在本任务内解决)
- [x] 6.8 `BuildScript.cs` 新增两个菜单项:`MRBase/Build/Marker Hook Test/Quest Development` 与 `/PICO Development`(PICO 带 `excludePluginRoot: k_MetaPackageRoot`,照 `BuildScript.cs:114-125` 现有模式),`applicationIdSuffix` 用 `.markerhook`

## 7. 真机验收

- [x] 7.1 EditMode 全绿:`unity command run_tests --json`
- [x] 7.2 PICO:扫 ID 0 / 250,盒子出现在标记上并显示 `RawPayload`。2026-09-04 18:06 实测通过(用户目视确认盒子与标签正常);日志侧 `[MarkerHook] Observed` 227 次,ID 0 与 250 各约半数,位姿解算零失败,零 app 异常
- [x] 7.3 PICO:遮挡 <1 s 不丢、>1 s 丢且只丢一次。实测每次 Lost 距上一次同 ID Observed **1.01–1.02 s**,与 design D5 的 1.0 s 滞回一致;连续可见期间无虚假 Lost
- [x] 7.4 PICO:`Pause()` 期间 CPU 下降,`Resume()` 后无权限申请、无相机会话重建。**用户已在已安装的 `...markerhook` 包上实测通过**(2026-09-08 口头确认);本会话未采集 CPU 数字,故 design Measurements 仍把「`Pause()` 的性能收益」列为无量化数据
- [x] 7.5 **Quest:验证持续派发修复生效**。2026-09-04 18:52 实测通过:一次会话内 `[MarkerHook] Observed` **3276 次**(payload 0 共 1263、payload 250 共 2017)。改动前 MRUK 每个 trackable 只发一次 `TrackableAdded`,理论上限是 2 次——3276 次证明 D4 的"持续读 Transform"确实生效。装配链三层齐全:`QuestMrukRuntimeInstaller` 已跑、`[QuestObservationSource] 已订阅` 1 次。原文:——标记持续可见期间 `[MarkerHook] Observed` 持续到达(计数随时间稳定增长),而非只在首次发现时来一次。改动前 MRUK 只发一次 `TrackableAdded`(design D4 三条源码证据),本项是验证修复而非验证疑点。**注意**:D8 改定后测试场景已不再使用 `MarkerStabilizer`,判据不再是 `Stabilized` 是否触发
- [x] 7.6 Quest:标记持续可见时位姿随 Transform 更新,而非停在首次发现的那一帧。用户目视确认盒子正常跟随
- [x] 7.7 Quest:扫 QR 0 / 250 回归。两个 payload 均持续派发(0:1263 次、250:2017 次),`Lost` 4 次
- [x] 7.8 老探针回归 —— **改为删除**(D13)。原计划是把 `PicoQrCameraProbe` 与 `MarkerProbe` 各重跑一次确认 D9/D10 无退化。实际结论:
  - **D9 已由 PICO 真机证实无退化**(2026-09-08):`[PicoFiducialProbe] 自检方块已挂到 Camera.main 前方 0.5 m` + `BindEnterpriseService=True` / `OpenCameraAsyncfor4U=True` / `detector ready 1280x960` / `tag 0 margin=89.1` / `tag 250 margin=89.9`。这一跑还抓到并修掉了一个真 bug:`PicoQrCameraProbe.unity` 重构后未重新保存,场景里缺 `PicoFiducialObservationSource` 组件(`[RequireComponent]` 不会追加到已序列化的场景)→ `Start()` NRE。
  - **D10 已由 Quest 真机证实无退化**(2026-09-08):`[QuestMrukRuntimeInstaller] Quest MRUK runtime ready: OVRCameraRig=...; OVRManager=...; MRUK=...; scenePermission=True`,QR preflight `mrukInstanceAvailable/qrCodeTrackingSupported/scenePermissionGranted` 全为 true。该轮同时**直接验收了 8.3**——钩子删除后装配仅靠显式调用仍然就绪。
  - 两个老探针场景与其专属代码随后按 D13 删除,验收收敛到 `MarkerHookTest` 一处;删除后重编译 `error CS` 为 0。
- [x] 7.9 两端各记一份日志存档;任一判据未达成,把数字与原因写回 design,不手调参数掩盖。**已存档**:`logs/pico-2026-09-04.log`(1423 行)与 `logs/quest-2026-09-04.log`(3283 行),按 `[MarkerHook]` / 平台源 / 异常三类过滤后的完整记录,design 的 Measurements 段全部数字可由其复算

## 8. 收尾

- [x] 8.1 回填 design 的 Open Questions 指向的 proposal.md `## Open Assumptions`(0.1–0.4 的结论)
- [x] 8.2 把 0.4 列出的真机度量数字与结论写回 design;未达标项写清数字与原因,不手调参数掩盖。**已写回** design `## Measurements`(M1 派发速率 / M2 滞回 / M3 检测耗时 / M4 检测率口径 / M5 多标记并发 / M6 误检),并如实列出三项未测:端到端延迟(未插桩)、CPU 占用(仅有 detectMs 代理)、PICO `Pause()` 收益(=7.4)。M4、M6 各暴露一个新问题,记入 design Open Questions 的 OQ1 / OQ2
- [x] 8.3 删除 `QuestMrukRuntimeInstaller.InstallForProbeScene`(`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 钩子)。本项目每个构建都先启动 `MRCore`、内容场景一律加性加载,该钩子在放宽触发条件后仍然一次都命中不了;留着只会伪装成"已负责 bring-up"的诱饵(它正是 4.2 误判的来源)。两个消费方(`MarkerHookTestRig`、`QuestMarkerProbeAdapter.cs:92`)本就显式调 `EnsureInitialized()`,删除后无行为变化。D10 已改写,`MarkerHookTestRig` 的相关注释同步更新
- [x] 8.4 `BuildScript` 包名改为单一常量 `k_ApplicationId = "com.uality.xiangtangshan"`(D11):删除 `applicationIdSuffix` 形参与 6 个调用点的实参、删除快照/还原逻辑,改为构建开始时无条件 `SetApplicationIdentifier`;`ProjectSettings.asset` 里已被污染的 `com.uality.xiaotangshan.gsplatbench.qrcamprobe.qrcamprobe.markerprobe` 一并归位。Editor 重编译无 `error CS`。**真机验证(2026-09-08)**:`aapt2 dump packagename` = `com.uality.xiangtangshan`,装机后 `pm list packages` 只有这一个我们的包,且构建结束后 `ProjectSettings.asset` 未再被写入后缀——累积链已断
- [x] 8.5 `productName` / `companyName` 从「小汤山」改为「响堂山」(用户确认原值是笔误)。经 Editor 的 `PlayerSettings` API 改写并 `SaveAssets()`,不手改 YAML,避免与 Editor 内存值冲突
- [x] 8.6 Android XR loader 的构建后还原改为固定默认值(D12):删除 `SnapshotAndroidLoaders()`,`RestoreAndroidLoaders(manager)` 无条件还原到 `k_DefaultAndroidLoader = k_OpenXRLoader`。工作区里被 11:23 那次中断构建留下的 `PXR_Loader` 残留已归位(经 Editor 的 `XRPackageMetadataStore` API 改写并 `SaveAssets()`,不只改 YAML,避免 Editor 内存值覆盖回去);`XRGeneralSettingsPerBuildTarget.asset` 已无 git diff。Editor 重编译无 `error CS`
- [x] 8.7 按 D13 删除老探针:`MarkerProbe.unity`、`PicoQrCameraProbe.unity`、`Localization/Probe/` 与 `Localization/Native/Probe/` 两棵树、`MarkerProbeCoreTests` / `MarkerProbePlatformFlowTests`、`MarkerProbeDeviceRunner.cs`,以及 `BuildScript` 的 5 个构建入口与 2 个场景常量(共 36 个文件)。删除前已确认生产代码对 Probe 的提及全是注释;删除后清理了 4 处过期注释与 16 个失去消费方的遥测访问器。重编译 `error CS` 为 0,`MRBase.Build.Editor.dll` 44KB → 26KB
