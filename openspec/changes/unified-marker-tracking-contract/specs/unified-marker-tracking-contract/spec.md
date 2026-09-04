## ADDED Requirements

### Requirement: 观测源只提供查询，不发事件

平台实现 SHALL 只暴露纯查询边界 `Poll()` 与生命周期 `Open()` / `Close()`，MUST NOT 自行触发任何面向业务层的事件。身份收敛、派发节奏、丢失判定 MUST NOT 出现在任何平台实现内。

平台差异只允许存在于 `Poll()` 的实现里；一切语义只允许存在于会话层。

#### Scenario: 平台实现不暴露事件

- **WHEN** 审阅任一 `IMarkerObservationSource` 实现
- **THEN** 该类型 MUST NOT 声明 `event`，且 MUST NOT 持有丢失判定所需的历史状态（如上一次可见 ID 集合）

#### Scenario: 业务层只从会话层订阅

- **WHEN** 业务层需要订阅标记事件
- **THEN** 唯一可订阅的来源是 `MarkerTrackingSession`，业务层 MUST NOT 直接引用任何平台实现类型

### Requirement: 标记身份分层且显式

事件载荷 SHALL 使用分层的 `MarkerIdentity`，至少区分三层：平台原始载荷（`RawPayload`，Quest 为 QR 原文、PICO 为空）、平台原生标识（`NativeId`，PICO 为 AprilTag 整数 ID、Quest 为空）、业务逻辑标识（`LogicalId`）。

业务层 SHALL 只按 `LogicalId` 匹配。注册表 MUST NOT 用多条件或运算兜底不同平台的身份语义。

#### Scenario: Quest 观测携带 QR 原文

- **WHEN** Quest 观测到一个 QR Trackable，其 `MarkerPayloadString` 为 `"250"`
- **THEN** 事件载荷的 `RawPayload` 为 `"250"`，`NativeId` 为空，`LogicalId` 由 `IMarkerIdParser` 解析得出

#### Scenario: PICO 观测携带原生整数 ID

- **WHEN** PICO 检测到 AprilTag ID 250
- **THEN** 事件载荷的 `NativeId` 为 250，`RawPayload` 为空，`LogicalId` 由 `IMarkerIdParser` 解析得出

#### Scenario: 两端同一张夹具解析到同一业务身份

- **WHEN** Quest 扫到 `RawPayload = "250"`，PICO 扫到 `NativeId = 250`
- **THEN** 两者的 `LogicalId` 相同，注册表用同一条单条件规则命中同一业务对象

#### Scenario: 未注册的标记不伪造身份

- **WHEN** 观测到的标记在注册表中无对应项
- **THEN** 会话层 SHALL 记录一次 registry miss 并保留实际的 `RawPayload` / `NativeId`，MUST NOT 编造 `LogicalId`，MUST NOT 静默丢弃该观测

### Requirement: 统一派发节奏

会话层 SHALL 以单一可配置频率派发观测事件，两端节奏一致且与各平台原生回调频率解耦。

会话层 SHALL 通过 `Tick(float deltaTime)` 由外部驱动时间，MUST NOT 内部读取 `Time.deltaTime` 或依赖 Unity 运行时，以保证节流与滞回逻辑可在 EditMode 下测试。

#### Scenario: 派发频率不随平台回调频率变化

- **WHEN** 平台在一个派发周期内推送了多次观测
- **THEN** 会话层对该标记只派发一次，携带该周期内最新的位姿

#### Scenario: Quest 侧持续派发而非仅在新增时派发

- **WHEN** 一个 Quest QR Trackable 已被观测到并持续可见，其 Transform 随后发生变化
- **THEN** 会话层在后续每个派发周期继续派发该标记的最新位姿，而不是只在首次发现时派发一次

#### Scenario: 时间由外部注入

- **WHEN** 测试以固定 `deltaTime` 调用 `Tick`
- **THEN** 派发次数与滞回判定完全由注入的时间决定，与真实墙钟时间无关

### Requirement: 丢失判定统一使用时间滞回

会话层 SHALL 在标记连续缺席超过滞回时长（默认 1.0 秒）后才派发丢失事件。缺席时长 SHALL 以注入的时间累计，而非帧数。

平台侧的移除信号（如 Quest 的 `TrackableRemoved`）SHALL 作为"立即置为过期"的输入进入同一条滞回判定，MUST NOT 绕过它直接派发丢失事件。

#### Scenario: 短暂遮挡不触发丢失

- **WHEN** 标记连续缺席 0.5 秒后重新出现
- **THEN** 不派发丢失事件，观测事件继续

#### Scenario: 持续缺席触发丢失且只触发一次

- **WHEN** 标记连续缺席超过 1.0 秒，且此后继续缺席
- **THEN** 派发且仅派发一次丢失事件

#### Scenario: 平台移除信号也走滞回

- **WHEN** Quest 报告 `TrackableRemoved`，但该标记在滞回时长内被重新观测到
- **THEN** 不派发丢失事件

#### Scenario: 丢失后重新出现按新一轮处理

- **WHEN** 标记已派发丢失事件，此后重新被观测到
- **THEN** 派发观测事件，且该标记的缺席计时重置

### Requirement: 暂停与关闭是两件事

契约 SHALL 区分暂停（`Pause` / `Resume`）与关闭（`Open` / `Close`）。暂停 SHALL 停止派发与平台侧的检测开销，但 MUST NOT 释放底层硬件会话；只有关闭才释放。

#### Scenario: 暂停期间不派发

- **WHEN** 会话处于暂停状态且标记在视野内
- **THEN** 不派发任何观测事件或丢失事件

#### Scenario: 恢复无需重新授权

- **WHEN** 会话暂停后恢复
- **THEN** 观测事件继续，且不触发任何权限申请或硬件会话重建

#### Scenario: 暂停不被误判为丢失

- **WHEN** 会话暂停超过滞回时长后恢复，标记始终在视野内
- **THEN** 恢复后不派发丢失事件——暂停期间不累计缺席时长

#### Scenario: 关闭释放硬件会话

- **WHEN** 调用 `Close()`
- **THEN** 平台侧释放其硬件会话（PICO 释放相机流，Quest 反注册 Trackable 监听）

### Requirement: 稳定判定以时长表达

稳定判定的阈值 SHALL 以秒表达，MUST NOT 以帧数或派发次数表达，以免语义随采样率旋钮漂移。

#### Scenario: 阈值语义不随派发频率变化

- **WHEN** 派发频率从 6 Hz 改为 12 Hz，稳定时长阈值不变
- **THEN** 标记从静止到判稳所需的真实时长不变
