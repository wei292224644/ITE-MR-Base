## Context

当前 `IMarkerTrackingProvider` 只暴露 `MarkerResolved(string, Pose)` 和 `MarkerLost(string)`。Quest 实现只在 MRUK Trackable Added 时复制一次 Pose；PICO 实现直接订阅企业 Marker 全量快照，用前后 ID 差集推断 Lost，并在停止追踪时解绑整个企业服务。`MarkerAnchorService` 随后对 Pose 做稳定化并创建业务对象，且同一 ID 激活后会忽略后续 Pose。

新能力需要把“平台追踪事实”从“业务如何使用 Marker”中拆开，并解决两端输入不对称：

- Quest：MRUK QR Trackable 同时提供 QR 原文和 QR 的 6DOF Pose。
- PICO：QR 扫描提供原文但不提供 Pose；Marker 回调提供 ArUco ID 和 Pose，因此必须先建立 QR 身份，再与相同 ID 的 ArUco 配对。
- 组合码采用固定排版，QR 在左、ArUco 在右；跨端统一以 QR 中心与朝向作为逻辑 Marker 坐标。
- PICO 企业服务可能被多个 TOB 能力共享，Marker Tracking 不能拥有全局 Unbind。

## Goals / Non-Goals

**Goals:**

- 提供与业务无关的统一 `Tracked → Tracking* → Lost` 生命周期和能力/诊断 Hook。
- 在 Unity 主线程按可配置频率持续派发最新有效世界 Pose。
- 支持 Quest QR 直接追踪和 PICO QR→ArUco 配对，并让两端输出相同语义的数据。
- 支持多个不同 MarkerID 同时活动，单个目标的获取、丢失与重建不影响其他目标。
- 对权限、平台能力、解析、配对、重复 ID、运行时错误和在途回调给出确定行为。
- 让现有 `MarkerAnchorService` 等业务代码作为普通订阅者迁移，而不是把业务规则放回 Provider。

**Non-Goals:**

- 不验证 QR 内容是不是 UUID、URL、JSON 或其他业务格式。
- 不负责 Marker 对应内容的查询、实例化、稳定化、销毁或场馆业务流程。
- 不实现自定义相机帧访问、OpenCV、软件 QR/ArUco 识别、自定义 ArUco 字典或视觉降级。
- 不承诺超出平台 SDK/设备能力的 Marker 数量，也不硬编码具体设备型号白名单。
- 不在运行时更换 MarkerID 解析器；解析器仅在服务初始化时注入。
- 不由 Marker Tracking 服务初始化、绑定或解绑 PICO 全局企业服务。

## Decisions

### D1. 服务层与 Provider 层分离

对业务公开 `IMarkerTrackingService`，平台 SDK 隐藏在内部 Provider 后面。

公开服务负责：

- 全局启停、能力状态与诊断；
- QR 原文解析、MarkerID 会话身份；
- 生命周期、宽限期、更新节流、多目标表；
- PICO 获取状态机与逻辑 Pose 校正；
- 主线程事件派发。

平台 Provider 只负责：

- 报告平台是否可用；
- 产生平台原始追踪观察；
- Quest 保存/轮询 MRUK Trackable；
- PICO 消费已绑定企业服务、触发 QR 扫描并接收 Marker 全量快照。

替代方案是把全部状态机分别写进两个 Provider。该方案会复制生命周期、线程、诊断和多目标逻辑，并容易让两端 Hook 语义漂移，因此不采用。

### D2. 统一事件与数据模型

公开事件：

- `Tracked(MarkerTrackingEvent data)`
- `Tracking(MarkerTrackingEvent data)`
- `Lost(MarkerLostEvent data)`
- `Diagnostic(MarkerTrackingDiagnostic data)`
- `StateChanged(MarkerTrackingStateChanged data)`

共同字段至少包含：

- `MarkerId`：不透明、区分大小写的字符串；
- `RawPayload`：QR 解码原文；
- `Pose`：以 QR 为逻辑原点的 Unity World Space Pose；
- `Platform`：Quest 或 PICO；
- `Timestamp`：该公开事件在主线程形成时的单调时间。

`MarkerLostEvent` 另外携带最后一次有效 Pose 和 `LostReason`。诊断使用稳定的枚举代码和可读详情，不要求业务解析日志文本。

替代方案是继续用多个位置参数。随着状态、原因和平台字段增加，位置参数易破坏调用方且无法平滑扩展，因此采用不可变事件数据对象。

### D3. MarkerID 解析是初始化只读依赖

服务初始化时注入 `IMarkerIdParser`，其概念契约为：

```text
TryParse(rawPayload) -> success + markerId | failureReason
```

基础层不修剪、不改大小写、不解释 JSON，也不验证 UUID。解析失败只触发 `MarkerIdParseFailed` 诊断，不创建目标会话。

运行期间不提供更换解析器的接口。若应用需要另一种规则，应关闭并销毁当前服务，以新解析器重新初始化。

替代方案是内置 UUID/JSON 解析或暴露运行时热切换。前者固化未知业务格式，后者会让活动会话的身份语义中途改变，因此均不采用。

### D4. 生命周期核心由单一主线程状态机拥有

每个活动目标保存：

- MarkerID、RawPayload、平台；
- 最新有效逻辑 Pose 和采样时间；
- 会话代次；
- `IsCurrentlyObserved`；
- 未观察起始时间；
- 下一次允许派发 `Tracking` 的时间。

生命周期规则：

1. 首个合法身份与有效 Pose 创建会话，同一帧只触发一次 `Tracked`。
2. 从下一次有效更新开始，只要 Provider 仍确认目标有效，就按 `trackingUpdateRateHz` 持续触发 `Tracking`；Marker 静止也照常触发。
3. Provider 不再观察目标时立即停止 `Tracking`，开始 `lostGraceSeconds`。
4. 宽限期内恢复则延续同一会话，不重复 `Tracked`。
5. 超时后触发一次 `Lost(OutOfView)` 并删除会话。
6. `Lost` 后携带旧代次的任何在途更新都被丢弃。

默认 `trackingUpdateRateHz = 30`，默认 `lostGraceSeconds = 0.5`。两者可在运行时更新并从下一轮调度生效。调度使用 `Update` 和单调时间累加，不绑定渲染帧数或 `FixedUpdate`。

### D5. 所有公开 Hook 在 Unity 主线程派发

SDK 回调只把不可变观察写入线程安全队列，不直接调用业务 Hook。服务在 `Update` 中：

1. 排空队列；
2. 校验服务/会话代次；
3. 更新状态机；
4. 按稳定顺序派发公开事件。

事件不在锁内触发，单个订阅者异常必须被隔离并记录，不能阻止其他订阅者或破坏内部状态。

### D6. 全局状态与启停

服务初始为 `Disabled`。外部显式启用后状态按以下集合变化：

- `Disabled`
- `Initializing`
- `Ready`
- `Unavailable`
- `Error`

重复启用/关闭是幂等操作。关闭时：

- 对所有活动目标各触发一次 `Lost(TrackingDisabled)`；
- 清空活动表、Quest Trackable 引用和 PICO 获取/配对状态；
- 增加服务代次，使已发出的异步 QR/SDK 回调失效；
- 返回 `Disabled`；
- 不解绑 PICO 企业服务。

初始化阶段能力缺失只报告 `Unavailable` 或 `Error`，因为尚未建立目标，不伪造 Lost。运行期间 Provider 发生不可恢复错误时，先终止所有活动目标为 `Lost(ProviderError)`，再进入 `Error`，等待外部手动重试。

### D7. Quest 直接追踪 QR

Quest Provider 监听 MRUK QR Trackable Added/Removed，并保存活动 `MRUKTrackable` 引用。每轮主线程更新读取 Trackable 的当前追踪状态和 Transform：

- Added 后先解析 `MarkerPayloadString`；
- 解析成功且 Pose 有效时创建会话；
- Trackable 持续有效时更新最新 Pose；
- Removed/不再有效时进入统一丢失宽限期；
- 同一码以后再次 Added 时创建新的会话并重新触发 `Tracked`。

Quest Pose 已以 QR 为物理参考点，因此逻辑校正为单位变换。

### D8. PICO 使用一个获取通道和多个活动目标

PICO 同时维护：

- 一个新目标获取状态：`AwaitingQr`、`ScanningQr`、`AwaitingArUco`；
- 一个按 MarkerID 索引的活动目标表。

流程：

1. 服务启用并就绪后进入 `AwaitingQr`，触发 `QrScanRequired` 诊断/通知，但不自动打开扫码。
2. 外部调用 `BeginQrScan()`；同一时刻第二个调用被拒绝并给出诊断。
3. QR 失败、取消或为空时触发 `QrScanFailed`，回到 `AwaitingQr`，不自动重试。
4. QR 解析失败按 D3 处理并回到 `AwaitingQr`。
5. 解析成功后保存 MarkerID 和 RawPayload，进入 `AwaitingArUco`。
6. 只接受 `iMarkerId.ToString()` 与待配对 MarkerID 精确相等的有效 Pose；不相等触发 `PairingMismatch` 并继续等待。
7. 配对成功时应用 D10 的 Pose 校正、创建活动会话、触发 `Tracked`，并让获取通道重新回到 `AwaitingQr`，从而可以继续添加其他目标。
8. `AwaitingArUco` 默认 15 秒超时；超时触发 `PairingTimedOut`，清除待配对 QR 并回到 `AwaitingQr`。
9. 活动 PICO 目标 Lost 后仅删除该会话，不自动扫码；外部以后重新调用 `BeginQrScan()`，完成新的 QR→ArUco 配对后创建新会话。

PICO Marker 回调仍按全量快照处理；每个活动 ID 是否出现在最新有效快照中决定其观察状态。单个目标的丢失不改变其他目标。

### D9. 多目标与重复 ID 采用确定性失败关闭

基础层不设置固定目标数量上限。活动表以区分大小写的 MarkerID 为键。

若同一会话中观察到两个相同 MarkerID：

- 保留最早建立的物理观察；
- 忽略后出现的观察；
- 触发 `DuplicateMarkerId` 诊断；
- 不允许已建立目标的 Pose 在两个位置之间跳动。

Provider 无法可靠报告容量上限时，不猜测数值；SDK 拒绝、丢帧或明确容量错误时通过 `CapacityExceeded`/Provider 诊断暴露。

### D10. 统一以 QR 为逻辑 Pose 原点

公开 Pose 始终是 Unity World Space。

- Quest：`logicalQrWorldPose = qrWorldPose`
- PICO：`logicalQrWorldPose = Compose(arucoWorldPose, arucoToQrOffset)`

`arucoToQrOffset` 是在 ArUco 局部坐标中定义的完整 Pose：

```text
position = aruco.position + aruco.rotation * offset.position
rotation = aruco.rotation * offset.rotation
```

首版只有一套全局 Offset，所有组合码必须使用相同尺寸、间距与朝向。Offset 只能在追踪关闭时配置，避免活动 Pose 语义突变。

不复用 `MarkerAnchorService` 当前的 `PlatformOffsetConfig` 来表达该偏移：现有 Offset 是 Marker 到业务 Target 的变换，而 `ArUcoToQrOffset` 是平台追踪结果到统一逻辑 Marker 的基础校准，两者层级不同。

### D11. PICO 企业服务由外部拥有

PICO Provider 接收一个已初始化/已绑定的企业服务门面或能力句柄。Marker Tracking 的 Enable/Disable 只注册、忽略或逻辑停用自身回调，不调用全局 `UnBindEnterpriseService()`。

如果 PICO SDK 没有 Marker 回调反注册 API，Provider 使用服务代次和 `enabled` 标志吞掉停用后的回调；共享企业服务在应用级 Bootstrap 退出时统一解绑。

替代方案是沿用当前 Provider 自己 Init/Bind/Unbind。该方案会让关闭 Marker Tracking 意外关闭其他 TOB 能力，因此不采用。

### D12. PICO QR/Marker 并发由真机闸门决定

官方接口没有承诺 `ScanQRCode` 与 `SetMarkerInfoCallback` 可并行。实现保留两种经过同一服务状态机的策略：

- 并行能力真机验证通过：QR 获取期间继续派发活动 ArUco 的 Tracking。
- 未通过或未知：进入安全降级，QR 扫描期间暂停活动 PICO 目标的 Tracking Hook，但不开始 Lost 计时；扫描结束后恢复 Marker 观察，恢复后仍不可见才从零开始正常宽限期。

默认策略在真机闸门完成前保持安全降级，不把未验证的并发当成事实。

## Risks / Trade-offs

- **[PICO 两个企业 API 不能并行]** → 以真机测试为硬闸门；默认采用暂停 Tracking 且不产生假 Lost 的安全策略。
- **[组合码实际排版与配置不一致]** → Offset 使用完整 Pose；提供校准测试夹具，并以跨端 5 cm / 5° 为验收指标。
- **[权限、系统版本或 TOB 授权不可用]** → 运行时能力探测并报告 `Unavailable/Error`，禁止进入假追踪状态；部署清单记录版本与授权。
- **[SDK 回调线程与停用竞态]** → 主线程队列加服务/会话代次校验；关闭后所有旧回调失效。
- **[30 Hz 事件对订阅者造成开销]** → 频率可运行时下调；事件数据保持小型不可变对象，Provider 复用快照集合并避免每帧 LINQ/临时分配。
- **[重复 MarkerID 无法区分物理对象]** → 保留首个目标、诊断并忽略冲突；现场仍必须保证同场 ID 唯一。
- **[PICO Lost 后需要重新建立 QR 身份]** → 不自动弹出扫码；通过 `QrScanRequired`/状态让外部选择时机，重新配对后建立新会话。
- **[现有业务依赖一次性 MarkerResolved]** → 提供迁移适配期，把现有 `MarkerAnchorService` 改为订阅新事件；追踪服务本身不吸收其稳定化和内容逻辑。

## Migration Plan

1. 新增统一数据类型、解析器、服务状态机和纯 C# / EditMode 测试，不连接平台 SDK。
2. 将 Quest Provider 迁移为原始观察源，验证持续 Transform、Removed、宽限恢复和新会话。
3. 把 PICO 企业服务所有权迁至平台 Bootstrap/共享门面，再接入 QR 获取和 Marker 快照。
4. 实现 PICO 配对、多目标、Offset 和并发安全降级；用伪 Provider 完成自动化测试。
5. 将 `MarkerAnchorService` 作为下游订阅者迁移，保持现有业务行为不变。
6. 完成 Quest 与 PICO 真机闸门；确认 PICO 并发策略、录入真实 Offset、记录系统版本/权限/授权。
7. 分平台回归后移除旧 `MarkerResolved` 契约。

回滚时可在迁移期保留旧 Provider 适配器并由 Bootstrap 切回；一旦下游全部迁移且旧接口删除，回滚需要恢复旧接口和两个 Provider，不影响业务数据文件。

## Open Questions

- PICO 4 Ultra Enterprise 真机上 `ScanQRCode` 与 `SetMarkerInfoCallback` 能否同时稳定运行？
- 最终印刷组合码对应的 `ArUcoToQrOffset` 精确位置与旋转值是多少？
- 目标设备的系统版本、Quest 权限配置、PICO TOB 企业授权与运行模式是否均满足 SDK 前置条件？

