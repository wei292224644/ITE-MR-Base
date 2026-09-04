## ADDED Requirements

### Requirement: 观测源只提供查询,不发事件

平台实现 SHALL 只暴露纯查询边界 `Poll()` 与生命周期 `Open()` / `Close()` / `Pause()` / `Resume()`,MUST NOT 自行触发任何面向业务层的事件。派发节奏、丢失判定 MUST NOT 出现在任何平台实现内。

平台差异只允许存在于 `Poll()` 的实现里;一切语义只允许存在于会话层。

#### Scenario: 平台实现不暴露事件

- **WHEN** 审阅任一 `IMarkerObservationSource` 实现
- **THEN** 该类型 MUST NOT 声明 `event`,且 MUST NOT 持有丢失判定所需的历史状态(如上一次可见 ID 集合)

#### Scenario: 业务层只从会话层订阅

- **WHEN** 业务层需要订阅标记事件
- **THEN** 唯一可订阅的来源是 `MarkerTrackingSession`,业务层 MUST NOT 直接引用任何平台实现类型

### Requirement: 载荷只带平台标签与原始 payload,不做身份收敛

事件载荷 SHALL 只携带 `MarkerPlatform`(Quest / Pico)与 `RawPayload`(string,非空)。会话层与观测源 MUST NOT 引入除平台标签外的身份分层字段(如 `LogicalId`、`NativeId`),MUST NOT 依赖任何身份解析或映射接口。业务层若需要将 `RawPayload` 转换或匹配到业务对象,SHALL 在会话层之外自行完成,契约本身不提供、不假设任何解析规则。

#### Scenario: Quest 观测携带 QR 原文

- **WHEN** Quest 观测到一个 QR Trackable,其 `MarkerPayloadString` 为 `"250"`
- **THEN** 事件载荷的 `RawPayload` 为 `"250"`,`Platform` 为 `Quest`

#### Scenario: PICO 观测携带整数 ID 的字符串形式

- **WHEN** PICO 检测到 AprilTag ID 250
- **THEN** 事件载荷的 `RawPayload` 为 `"250"`(即 `iMarkerId.ToString()`),`Platform` 为 `Pico`

#### Scenario: 会话层不解析、不映射、不产生 miss

- **WHEN** 观测到的 `RawPayload` 在任何外部注册表或映射规则中都没有对应项
- **THEN** 会话层仍然正常派发该观测,不产生任何"registry miss"计数或错误——它不知道也不关心是否存在映射

### Requirement: 统一派发节奏,不做跨平台限流

会话层 SHALL 在标记可见期间的每个 `Tick` 派发其最新位姿观测,MUST NOT 限制或统一两端的原生派发频率。

会话层 SHALL 通过 `Tick(float deltaTime)` 由外部驱动时间,MUST NOT 内部读取 `Time.deltaTime` 或依赖 Unity 运行时,以保证滞回逻辑可在 EditMode 下测试。

#### Scenario: Quest 侧持续派发而非仅在新增时派发

- **WHEN** 一个 Quest QR Trackable 已被观测到并持续可见,其 Transform 随后发生变化
- **THEN** 会话层在后续每个 `Tick` 继续派发该标记的最新位姿,而不是只在首次发现时派发一次

#### Scenario: 两端各自频率原样传递,不被压到同一个 Hz

- **WHEN** Quest 的原生渲染频率与 PICO 的 `sampleHz` 采样频率不同
- **THEN** 会话层不对两端做统一节流或频率转换,各自按自身被 `Tick` 调用的频率派发

#### Scenario: 时间由外部注入

- **WHEN** 测试以固定 `deltaTime` 调用 `Tick`
- **THEN** 滞回判定完全由注入的时间决定,与真实墙钟时间无关

### Requirement: 丢失判定统一使用时间滞回

会话层 SHALL 在标记连续缺席超过滞回时长(默认 1.0 秒)后才派发丢失事件。缺席时长 SHALL 以注入的时间累计,而非帧数。

平台侧的移除信号(如 Quest 的 `TrackableRemoved`)SHALL 作为"立即置为过期"的输入进入同一条滞回判定,MUST NOT 绕过它直接派发丢失事件。

#### Scenario: 短暂遮挡不触发丢失

- **WHEN** 标记连续缺席 0.5 秒后重新出现
- **THEN** 不派发丢失事件,观测事件继续

#### Scenario: 持续缺席触发丢失且只触发一次

- **WHEN** 标记连续缺席超过 1.0 秒,且此后继续缺席
- **THEN** 派发且仅派发一次丢失事件

#### Scenario: 平台移除信号也走滞回

- **WHEN** Quest 报告 `TrackableRemoved`,但该标记在滞回时长内被重新观测到
- **THEN** 不派发丢失事件

#### Scenario: 丢失后重新出现按新一轮处理

- **WHEN** 标记已派发丢失事件,此后重新被观测到
- **THEN** 派发观测事件,且该标记的缺席计时重置

#### Scenario: Quest 的 IsTracked 变为 false 时计入缺席而非立即丢失

- **WHEN** Quest 侧某个 `MRUKTrackable` 的 `IsTracked` 变为 `false`
- **THEN** 该标记被计入"缺席",进入与其他缺席场景相同的滞回判定,MUST NOT 因为 `IsTracked=false` 就立即派发丢失事件

### Requirement: 暂停与关闭是两件事

契约 SHALL 区分暂停(`Pause` / `Resume`)与关闭(`Open` / `Close`)。暂停 SHALL 停止派发与平台侧的检测开销,但 MUST NOT 释放底层硬件会话;只有关闭才释放。

#### Scenario: 暂停期间不派发

- **WHEN** 会话处于暂停状态且标记在视野内
- **THEN** 不派发任何观测事件或丢失事件

#### Scenario: 恢复无需重新授权

- **WHEN** 会话暂停后恢复
- **THEN** 观测事件继续,且不触发任何权限申请或硬件会话重建

#### Scenario: 暂停不被误判为丢失

- **WHEN** 会话暂停超过滞回时长后恢复,标记始终在视野内
- **THEN** 恢复后不派发丢失事件——暂停期间不累计缺席时长

#### Scenario: 关闭释放硬件会话

- **WHEN** 调用 `Close()`
- **THEN** 平台侧释放其硬件会话(PICO 释放相机流,Quest 反注册 Trackable 监听)

### Requirement: 测试场景可观察地验证 hook 行为

新增的验收场景(`MarkerHookTest.unity`)SHALL 让"扫到标记"这一行为在不依赖复杂业务逻辑的情况下可被直接观察:扫描位置出现一个盒子并显示扫描出的内容,同时提供累计计数与暂停/恢复控制。

#### Scenario: 扫到标记显示内容

- **WHEN** `MarkerHookTestRig` 通过 `MarkerStabilizer` 判定某标记已稳定(`Stabilized` 触发)
- **THEN** 在该标记的稳定位姿处显示一个盒子,并在其上方显示 `Platform` 与 `RawPayload` 的文本标签

#### Scenario: 丢失后盒子消失

- **WHEN** 该标记随后触发 `MarkerLost`
- **THEN** 对应的盒子从场景中移除或隐藏,且该标记的 `MarkerStabilizer` 状态被重置

#### Scenario: HUD 显示累计计数

- **WHEN** 场景运行期间持续产生 Observed / Lost 事件
- **THEN** HUD 显示当前活跃标记列表,以及 Observed / Lost 的累计次数

#### Scenario: Pause 停止派发但不清空已有显示

- **WHEN** 用户点击 HUD 上的 Pause 按钮
- **THEN** 不再产生新的观测或丢失事件,直到点击 Resume;已显示的盒子保持原状,不因暂停而被误判丢失
