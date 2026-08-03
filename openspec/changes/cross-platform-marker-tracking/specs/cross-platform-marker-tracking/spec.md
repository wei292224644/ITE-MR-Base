## ADDED Requirements

### Requirement: 业务无关的统一追踪契约

系统 SHALL 提供平台无关的 Marker Tracking 服务，通过 `Tracked`、`Tracking`、`Lost`、`Diagnostic` 和 `StateChanged` Hook 向外部报告追踪事实。服务 MUST NOT 查询业务注册表、创建业务对象、验证内容类型或决定外部业务流程。

#### Scenario: 外部订阅统一 Hook

- **WHEN** 外部模块需要消费 Marker 追踪结果
- **THEN** 它只依赖统一 Marker Tracking 服务及其事件数据
- **THEN** 它不需要引用 Quest MRUK 或 PICO 企业 SDK 类型

#### Scenario: 追踪服务不创建业务内容

- **WHEN** 一个 Marker 成功进入 Tracked 状态
- **THEN** 服务只派发追踪事件
- **THEN** 服务不创建 GameObject、不加载网页或 JSON 内容、不查询业务 Anchor 注册表

### Requirement: 统一事件数据

`Tracked` 与 `Tracking` 数据 MUST 包含 MarkerID、QR 原始内容、Unity World Space 的逻辑 Pose、平台类型和时间戳。`Lost` 数据 MUST 另外包含最后一次有效逻辑 Pose 和明确的 LostReason。

#### Scenario: Quest 事件数据完整

- **WHEN** Quest 识别并持续追踪一个 QR
- **THEN** Tracked 和 Tracking Hook 包含由 QR 原文解析出的 MarkerID、完整 RawPayload、QR 世界 Pose、Quest 平台标识和时间戳

#### Scenario: PICO 事件保留 QR 原文

- **WHEN** PICO 完成 QR 与 ArUco 配对并持续追踪
- **THEN** 后续每个 Tracked、Tracking 和 Lost Hook 均携带配对时保存的 QR RawPayload

#### Scenario: Lost 携带终止上下文

- **WHEN** 一个活动目标结束追踪
- **THEN** Lost Hook 包含该会话最后一次有效 Pose、MarkerID、RawPayload、时间戳和 LostReason

### Requirement: 可注入的 MarkerID 解析

系统 SHALL 在初始化时接收一个 MarkerID 解析器。解析器 MUST 从不透明 QR 原文中产生 MarkerID 或失败原因；基础服务 MUST NOT 假定原文是 UUID、URL、JSON 或任何固定格式，也 MUST NOT 对解析结果执行额外修剪、大小写转换或数字规范化。

#### Scenario: 自定义格式解析成功

- **WHEN** 初始化注入的解析器能够从 QR 原文提取 MarkerID
- **THEN** 服务使用解析器返回的字符串作为区分大小写的精确身份

#### Scenario: QR 内容不是 UUID

- **WHEN** QR 原文是网址、JSON 或其他非 UUID 内容且解析器能提取 MarkerID
- **THEN** 服务接受该结果并继续平台追踪流程

#### Scenario: MarkerID 解析失败

- **WHEN** 解析器无法从 QR 原文产生有效 MarkerID
- **THEN** 服务触发包含 RawPayload 和失败原因的 MarkerIdParseFailed 诊断
- **THEN** 服务不为该 QR 触发 Tracked、Tracking 或 Lost

#### Scenario: 解析器生命周期

- **WHEN** 服务已经完成初始化
- **THEN** 服务不提供运行时替换解析器的操作

### Requirement: 确定的目标生命周期

每个合法目标会话 SHALL 遵循一次 `Tracked`、零次或多次 `Tracking`、一次 `Lost` 的顺序。首次有效身份与 Pose MUST 在同一主线程更新中只触发 `Tracked`；`Tracking` MUST 从下一次有效更新开始。

#### Scenario: 首次识别

- **WHEN** 一个尚无活动会话的 MarkerID 获得合法身份和有效 Pose
- **THEN** 服务触发一次 Tracked
- **THEN** 服务在同一更新中不再为该会话触发 Tracking

#### Scenario: 静止目标持续追踪

- **WHEN** Provider 持续确认活动目标有效但 Pose 数值未变化
- **THEN** 服务仍按配置频率持续触发 Tracking

#### Scenario: Lost 后拒绝旧更新

- **WHEN** 一个会话已经触发 Lost
- **THEN** 携带该旧会话代次的在途观察不得再触发 Tracking

#### Scenario: 同一码重新建立会话

- **WHEN** 一个已经 Lost 的 MarkerID 后续重新满足平台获取流程
- **THEN** 服务创建新会话并再次触发 Tracked

### Requirement: 丢失宽限与恢复

Provider 暂时不再观察活动目标时，服务 SHALL 立即暂停 Tracking 并进入可配置的丢失宽限期，默认 0.5 秒。宽限期内恢复 MUST 延续同一会话；超过宽限期 MUST 触发一次 `Lost(OutOfView)`。

#### Scenario: 宽限期内恢复

- **WHEN** 目标暂时不可见后在 lostGraceSeconds 内恢复有效观察
- **THEN** 服务恢复 Tracking
- **THEN** 服务不触发 Lost，也不重复触发 Tracked

#### Scenario: 超过宽限期

- **WHEN** 目标持续不可见超过 lostGraceSeconds
- **THEN** 服务触发一次 Lost，原因为 OutOfView
- **THEN** 服务删除该活动会话

#### Scenario: 宽限期内不派发旧 Pose

- **WHEN** 目标处于丢失宽限期
- **THEN** 服务不触发 Tracking，也不把最后已知 Pose 当作新样本重复派发

### Requirement: 主线程与可配置派发频率

所有公开 Hook MUST 在 Unity 主线程触发。服务 SHALL 使用独立于渲染帧率和 `FixedUpdate` 的时间调度派发 Tracking，默认 trackingUpdateRateHz 为 30，并允许运行时修改更新频率。

#### Scenario: SDK 从非主线程回调

- **WHEN** 平台 SDK 在非 Unity 主线程提交观察
- **THEN** 服务先缓存观察，并在后续 Unity 主线程更新中派发公开 Hook

#### Scenario: 渲染帧率高于更新频率

- **WHEN** 渲染帧率高于 trackingUpdateRateHz
- **THEN** Tracking Hook 不超过配置的目标频率

#### Scenario: 运行时降低频率

- **WHEN** 外部在追踪期间降低 trackingUpdateRateHz
- **THEN** 新频率从后续调度周期生效，当前目标会话不重启

### Requirement: 全局手动控制与能力状态

Marker Tracking SHALL 默认关闭，并只在外部显式启用后初始化和接收追踪。服务 MUST 提供 `Disabled`、`Initializing`、`Ready`、`Unavailable` 和 `Error` 状态，且重复启用或关闭 MUST 是幂等操作。

#### Scenario: 默认不自动启动

- **WHEN** 应用创建 Marker Tracking 服务但尚未调用 EnableTracking
- **THEN** 服务保持 Disabled
- **THEN** 服务不申请追踪能力、不启动 QR 扫描、不派发 Marker 生命周期事件

#### Scenario: 能力不可用

- **WHEN** 设备不支持、权限缺失或平台依赖未就绪
- **THEN** 服务进入 Unavailable 或 Error 并报告原因
- **THEN** 服务不伪造任何 Lost 事件

#### Scenario: 关闭活动追踪

- **WHEN** 外部在存在活动目标时调用 DisableTracking
- **THEN** 服务为每个活动目标触发一次 Lost，原因为 TrackingDisabled
- **THEN** 服务清空目标与配对状态、使在途回调失效并进入 Disabled

#### Scenario: 运行期间 Provider 失败

- **WHEN** Provider 在存在活动目标时发生不可恢复错误
- **THEN** 服务先为全部活动目标触发 Lost，原因为 ProviderError
- **THEN** 服务进入 Error 并停止后续 Tracking 派发，直到外部手动重试

### Requirement: Quest QR 连续追踪

Quest Provider SHALL 使用 MRUK QR Trackable 的原文和当前 Transform 建立并持续更新目标。Trackable 移除或不再有效时 MUST 进入统一丢失宽限；后续重新识别同一码 MUST 建立新会话。

#### Scenario: Quest 首次识别 QR

- **WHEN** MRUK 添加一个类型为 QRCode、RawPayload 可解析且 Pose 有效的 Trackable
- **THEN** 服务以该 QR 的世界 Pose 创建会话并触发 Tracked

#### Scenario: Quest 持续更新

- **WHEN** 活动 MRUK Trackable 的 Transform 在追踪期间变化
- **THEN** 服务后续 Tracking Hook 使用最新 Transform，而不是首次 Added 时的 Pose 副本

#### Scenario: Quest 重新识别

- **WHEN** Quest QR 会话已经 Lost，MRUK 后续再次添加同一 QR
- **THEN** 服务无需外部启动扫码即可建立新会话并触发新的 Tracked

### Requirement: PICO QR 到 ArUco 配对

PICO SHALL 先通过外部触发的 QR 扫描获得 RawPayload 和 MarkerID，再等待 `iMarkerId` 与待配对 MarkerID 区分大小写精确相等的有效 ArUco Pose。只有两者匹配时系统 MUST 建立会话并触发 Tracked。

#### Scenario: 成功配对

- **WHEN** QR 解析得到 MarkerID，且后续有效 ArUco 的 iMarkerId 与其精确相等
- **THEN** 服务保存 QR RawPayload、校正 ArUco Pose、建立新会话并触发 Tracked

#### Scenario: ID 不匹配

- **WHEN** AwaitingArUco 期间观察到不同 MarkerID 的 ArUco
- **THEN** 服务触发 PairingMismatch 诊断并继续等待原 MarkerID
- **THEN** 服务不为不匹配观察触发 Tracked

#### Scenario: 配对等待超时

- **WHEN** QR 解析成功后 15 秒内没有出现匹配 ArUco
- **THEN** 服务触发 PairingTimedOut 诊断、清除待配对 QR 并回到 AwaitingQr
- **THEN** 服务不自动重新启动 QR 扫描

### Requirement: PICO QR 扫描由外部启动

PICO 服务进入 AwaitingQr 时 SHALL 通知外部需要扫码，但 MUST NOT 自动或强制打开扫码。外部 SHALL 通过 BeginQrScan 启动单个扫码请求。

#### Scenario: 等待外部启动

- **WHEN** PICO 服务已就绪并处于 AwaitingQr
- **THEN** 服务触发 QrScanRequired 通知
- **THEN** 在外部调用 BeginQrScan 前不打开扫码

#### Scenario: 重复启动扫码

- **WHEN** 一个 QR 扫描请求尚未结束且外部再次调用 BeginQrScan
- **THEN** 服务拒绝第二个请求并触发诊断

#### Scenario: 扫码失败或取消

- **WHEN** QR 扫描失败、取消或返回空内容
- **THEN** 服务触发 QrScanFailed 并回到 AwaitingQr
- **THEN** 服务不自动重试，也不为未建立的目标触发 Lost

#### Scenario: Lost 后不强制扫码

- **WHEN** 一个活动 PICO 目标触发 Lost
- **THEN** 服务删除该会话但不自动调用 QR 扫描
- **THEN** 外部以后完成新的 QR→ArUco 配对时，服务建立新会话并触发 Tracked

### Requirement: PICO 多目标独立追踪

PICO 实现 SHALL 将单一新目标获取通道与活动目标集合分离。一个目标完成配对后，获取通道 MUST 能继续添加其他 MarkerID；一个目标的丢失或重新获取 MUST NOT 重置其他活动目标。

#### Scenario: 连续添加多个目标

- **WHEN** 外部依次完成三个不同 MarkerID 的 QR→ArUco 配对
- **THEN** 三个目标均拥有独立活动会话并持续产生 Tracking

#### Scenario: 单个目标丢失

- **WHEN** 三个活动目标中的一个超过丢失宽限期
- **THEN** 只有该目标触发 Lost 并被删除
- **THEN** 其他两个目标继续其原会话

#### Scenario: 不设置业务固定上限

- **WHEN** 活动目标数量增加
- **THEN** 基础服务不以固定 70、10 或其他业务常量拒绝目标
- **THEN** 平台明确报告容量限制时服务通过诊断暴露

### Requirement: 重复 MarkerID 冲突处理

同一会话中若同时观察到两个相同 MarkerID，系统 MUST 保留最先建立的目标，忽略后出现的观察并触发 DuplicateMarkerId 诊断。系统 MUST NOT 在两个物理位置之间切换该 ID 的 Pose。

#### Scenario: 同 ID 出现在两个位置

- **WHEN** Provider 同时报告两个身份相同但 Pose 明显不同的观察
- **THEN** 服务继续使用最早已建立目标的观察
- **THEN** 服务忽略冲突观察并触发 DuplicateMarkerId

### Requirement: 以 QR 为原点的跨平台世界 Pose

公开 Pose SHALL 使用 Unity World Space，并以 QR 中心和朝向作为逻辑 Marker 原点。Quest SHALL 直接使用 QR World Pose；PICO SHALL 将 ArUco World Pose 与全局、可配置的完整 `ArUcoToQrOffset` Pose 组合后输出。

#### Scenario: Quest 逻辑 Pose

- **WHEN** Quest 报告 QR World Pose
- **THEN** 公开逻辑 Marker Pose 与 QR World Pose 相同

#### Scenario: PICO 并排码校正

- **WHEN** PICO 报告右侧 ArUco World Pose，系统配置包含从 ArUco 到左侧 QR 的局部位置和旋转偏移
- **THEN** 公开逻辑 Marker Pose 等于 ArUco World Pose 与该 Offset 的 Pose 组合

#### Scenario: 活动期间修改 Offset

- **WHEN** 追踪处于启用状态
- **THEN** 系统拒绝修改 ArUcoToQrOffset，避免活动会话的 Pose 语义突变

#### Scenario: 跨平台校准验收

- **WHEN** Quest 与 PICO 追踪同一块静止组合码并应用实际 Offset
- **THEN** 两端逻辑 Pose 的位置差异不超过 5 cm，角度差异不超过 5°
- **THEN** 该阈值只用于验收，不作为运行时 Pose 过滤条件

### Requirement: PICO 企业服务所有权隔离

PICO Marker Tracking MUST 消费由平台 Bootstrap 或共享服务初始化并绑定的企业能力。Marker Tracking 的 EnableTracking 或 DisableTracking MUST NOT 调用全局企业服务 Init、Bind 或 Unbind。

#### Scenario: 关闭 Marker Tracking

- **WHEN** 外部关闭 PICO Marker Tracking
- **THEN** Marker Tracking 停止处理自身回调并使在途回调失效
- **THEN** 共享 PICO 企业服务保持绑定，其他 TOB 能力不受影响

#### Scenario: 企业服务尚未就绪

- **WHEN** 外部企业服务未绑定或授权不可用
- **THEN** Marker Tracking 报告 Unavailable 或 Error
- **THEN** Marker Tracking 不自行绑定或解绑企业服务

### Requirement: PICO 扫码与追踪并发降级

系统 MUST 将 `ScanQRCode` 与 Marker 回调能否并行作为 PICO 真机能力闸门。在并发能力未知或验证失败时，系统 SHALL 使用安全降级：扫码期间暂停活动 PICO 目标的 Tracking Hook但不触发 Lost，扫码结束后恢复观察，恢复后仍不可见才开始正常丢失宽限。

#### Scenario: 并发能力验证通过

- **WHEN** 目标 PICO 环境已验证 QR 扫描和 Marker 回调可并行
- **THEN** 添加新 QR 期间现有活动目标继续产生 Tracking

#### Scenario: 使用安全降级

- **WHEN** 并发能力未知或验证失败且外部开始新的 QR 扫描
- **THEN** 服务暂停现有 PICO 目标的 Tracking Hook
- **THEN** 服务不因该暂停触发 Lost 或消耗 lostGraceSeconds

#### Scenario: 扫码后恢复失败

- **WHEN** 安全降级扫码结束后恢复 Marker 观察，但某活动目标仍不可见
- **THEN** 该目标从恢复时刻开始正常丢失宽限
- **THEN** 只有超过宽限期后才触发 Lost

### Requirement: 多目标与跨平台真机验收

实现 MUST 在 Quest 与 PICO 目标设备上完成真机能力验收。基础验收 SHALL 包含至少三个不同 MarkerID 的独立生命周期，以及静止、移动、短暂遮挡、超时丢失和重新建立会话。

#### Scenario: 三目标验收

- **WHEN** 同场放置并依次获取至少三个不同 MarkerID
- **THEN** 每个目标均具有独立 Tracked、Tracking 和 Lost 顺序
- **THEN** 操作其中一个目标不改变其他目标的会话

#### Scenario: 能力前置条件不满足

- **WHEN** 目标系统版本、权限或 PICO TOB 授权不满足平台 SDK 要求
- **THEN** 验收记录具体环境与失败原因
- **THEN** 服务报告 Unavailable 或 Error，而不是伪造成功追踪
