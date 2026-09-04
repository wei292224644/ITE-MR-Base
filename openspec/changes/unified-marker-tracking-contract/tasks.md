## 0. 前置决策（动代码之前必须有答案）

- [ ] 0.1 定 `PicoMarkerProvider` 换源（TOB `SetMarkerInfoCallback` → AprilTag 检测结果）与换契约（事件 → `Poll()`）的先后：两步提交还是一次做完。结论回填 design 的 Open Questions
- [ ] 0.2 定统一派发频率取值，并明确它与 PICO 检测频率（`sampleHz`，当前 6）是同一个旋钮还是两个。结论回填 design D7 的 Open Question
- [ ] 0.3 按 0.2 的频率把 `stableFrameThreshold = 30` 换算成等效秒数，回填 design D7
- [ ] 0.4 确认"探针与生产靠场景分离规避 4U 相机独占"在 `MRSceneDirector` 运行时切场景下是否成立；不成立则本 change 的 Non-Goals 要重划
- [ ] 0.5 把 AprilTag 路线待补的真机度量列清（端到端延迟、检测率、CPU 占用、多标记并发），定各自判据；这些数字在本 change 接通生产链路后才第一次落在生产路径上

## 1. 新增契约与会话层（不动既有代码，可独立验证）

- [ ] 1.1 定义 `MarkerIdentity`（`RawPayload` / `NativeId` / `LogicalId` 三层，各自可空），三层的空与非空组合即诊断信息，不要压缩成单一字段
- [ ] 1.2 定义 `MarkerObservation`（`MarkerIdentity` + `Pose` + 观测时刻），与 `RawObservation`（平台侧未收敛身份的原始观测）
- [ ] 1.3 定义 `IMarkerObservationSource`：`Open()` / `Close()` / `Pause()` / `Resume()` / `Poll()`，**不含任何 event**
- [ ] 1.4 实现 `MarkerTrackingSession`：纯 C#、不继承 MonoBehaviour、不读 `Time.deltaTime`，唯一入口 `Tick(float deltaTime)`
- [ ] 1.5 Session 内实现派发节流（按 0.2 的频率）
- [ ] 1.6 Session 内实现身份收敛：调 `IMarkerIdParser`，registry miss 时保留实际 `RawPayload` / `NativeId` 并计数，不伪造 `LogicalId`、不静默丢弃
- [ ] 1.7 Session 内实现丢失时间滞回（默认 1.0 s，design D6）；平台移除信号作为"置为过期"的输入进入同一判定，不另开一条路径
- [ ] 1.8 Session 内实现 `Pause()` / `Resume()`；**暂停期间不累计缺席时长**，否则长暂停后恢复会先吐一轮虚假丢失

## 2. 会话层单测（本 change 的风险全在这里，必须在 EditMode 锁死）

- [ ] 2.1 `MockObservationSource`：可注入任意 `RawObservation` 序列与"本周期无观测"
- [ ] 2.2 节流：一个派发周期内推多次，只派发一次且携带最新位姿
- [ ] 2.3 滞回 A：连续缺席 0.5 s 后重现 → 不派发丢失
- [ ] 2.4 滞回 B：连续缺席 >1.0 s → 派发且仅派发一次丢失
- [ ] 2.5 滞回 C：平台移除信号后在滞回内重现 → 不派发丢失
- [ ] 2.6 滞回 D：丢失后重现 → 派发观测且缺席计时重置
- [ ] 2.7 暂停：暂停期间不派发任何事件；暂停时长 >1.0 s 后恢复且标记始终在视野内 → 不派发丢失
- [ ] 2.8 身份收敛：Quest 形态（有 `RawPayload` 无 `NativeId`）与 PICO 形态（有 `NativeId` 无 `RawPayload`）解析到同一 `LogicalId`
- [ ] 2.9 registry miss：不伪造 `LogicalId`，原始字段保留，miss 被计数
- [ ] 2.10 时间注入：同一组观测在不同 `deltaTime` 切分下派发次数与滞回结论一致

## 3. 稳定器换单位（design D7）

- [ ] 3.1 `MarkerStabilizer` 的 `stableFrameThreshold: int` → `stableDurationSeconds: float`，按 0.3 的换算值设默认
- [ ] 3.2 `MarkerStabilizerTests.cs` 三个用例改为按时长断言；补一个"派发频率翻倍、判稳真实时长不变"的用例，把 D7 的理由变成可执行事实

## 4. 平台观测源

- [ ] 4.1 `MockMarkerProvider` → `MockObservationSource`，删事件、实现 `Poll()`
- [ ] 4.2 `QuestObservationSource`：持有活动 `MRUKTrackable` 集合，每次 `Poll()` 读最新 `transform` / `IsTracked`（design D5，**行为改变**）
- [ ] 4.3 `QuestObservationSource` 的 `TrackableRemoved` 只从活动集合移除，不派发丢失——丢失归 Session
- [ ] 4.4 `PicoObservationSource`：`Poll()` 返回当帧 AprilTag 检测快照；删掉 `visibleIds` / `currentIds` 差集逻辑（该职责已上移到 Session）
- [ ] 4.5 `PicoObservationSource` 的 `Pause()` 停 AprilTag 检测但**保留 4U 相机会话**；`Close()` 才释放（design D8）
- [ ] 4.6 逐个确认：三个源都不含 `event`，都不持有丢失判定所需的历史状态

## 5. 迁移消费方

- [ ] 5.1 `MarkerAnchorService` 改订 `MarkerTrackingSession`
- [ ] 5.2 `IteHost/MarkerSourceAdapter` 改订 Session；同步更新 `:13` 那条注释——"可见期间每帧都发"从巧合变成契约保证，注释要说明它现在由谁保证
- [ ] 5.3 `AnchorRegistry.TryResolve` 删掉 `QuestPayload == rawId || PicoMarkerId.ToString() == rawId` 的 `||`，改为按 `LogicalId` 单条件
- [ ] 5.4 `MarkerTrackingBootstrapper` 组装 source + Session，并在 `Update` 里驱动 `Tick(Time.deltaTime)`
- [ ] 5.5 `DemoMarkerTrigger` 跟随新 mock API
- [ ] 5.6 `MarkerAnchorServiceTests` / `MockMarkerProviderTests` / `Ite/MarkerSourceAdapterTests` 跟随新契约
- [ ] 5.7 删除 `IMarkerTrackingProvider.cs`；全仓 grep 确认无残留引用

## 6. 真机验收

- [ ] 6.1 EditMode 全绿：`unity command run_tests --json`
- [ ] 6.2 PICO：扫 ID 0 / 250，确认 `AnchorRegistry` 单条件匹配仍命中、`AnchorEntity` 正常创建
- [ ] 6.3 PICO：遮挡 <1 s 不丢、>1 s 丢且只丢一次
- [ ] 6.4 PICO：`Pause()` 期间 CPU 下降，`Resume()` 后无权限申请、无相机会话重建
- [ ] 6.5 **Quest：验证 `Stabilized` 修复生效**——改动前它被 MRUK 源码证实从不触发（design D5 三条证据），本项是验证修复而非验证疑点；`Stabilized` 触发且 `AnchorEntity` 被创建，是 D5 有效的唯一判据
- [ ] 6.6 Quest：标记持续可见时位姿随 Transform 更新，而非停在首次发现的那一帧
- [ ] 6.7 Quest：扫 QR 0 / 250 回归，确认 D5 未破坏已验证路径
- [ ] 6.8 两端各记一份日志存档；任一判据未达成，把数字与原因写回 design，不手调参数掩盖

## 7. 收尾

- [ ] 7.1 回填 design 的 Open Questions（0.1–0.5 的结论、D7 的实际秒数）
- [ ] 7.2 把 0.5 列出的真机度量数字与结论写回 design；未达标项写清数字与原因，不手调参数掩盖
