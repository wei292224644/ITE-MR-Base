# Probe Report: cross-platform-marker-tracking

> Generated: 2026-07-31T15:03:01+08:00
> Summary: 45 questions, 35 consolidated decisions, 3 open assumptions

## Confirmed decisions

### Scope & intent

- **Question**: 这项 change 应负责业务对象创建，还是只提供跨平台 Marker 追踪基础能力？
  - **AI recommendation**: 只提供基础能力，通过 Hook 报告目标首次追踪、持续追踪和丢失，不关心网页、JSON、锚点或其他业务内容。现有 `MarkerAnchorService` 已承担注册表解析和内容创建，继续把业务放在它的下游可以保持边界清晰。（evidence: `Assets/Scripts/Localization/MarkerAnchorService.cs:43`）
  - **User confirmation**: 只设计基础能力，具体业务不属于本 change。

- **Question**: QR 解码结果是否必须是 UUID？
  - **AI recommendation**: 将 QR 原文视为不透明字符串，不校验 UUID、URL 或 JSON；基础层只要求解析器最终能提取 MarkerID。（evidence: 用户说明 UUID 只是随手示例，真实内容格式尚未确定）
  - **User confirmation**: 只解析 QR 内容，不做 UUID 校验；内容未来可能是网址、JSON 或其他格式，但会包含 MarkerID。

- **Question**: 是否实现自定义相机视觉识别作为平台能力的降级方案？
  - **AI recommendation**: 首版只使用平台原生能力；Quest 使用 MRUK/Meta QR Trackable，PICO 使用企业 SDK 的 QR 扫描和 Marker Pose 回调，不实现相机帧访问、OpenCV、自定义 ArUco 字典识别或软件视觉降级。（evidence: 当前 Provider 已分别基于 MRUK 与 `PXR_Enterprise`，`Assets/Scripts/Localization/Native/QuestMarkerProvider.cs:30`、`Assets/Scripts/Localization/Native/PicoMarkerProvider.cs:69`）
  - **User confirmation**: 同意列为非目标。

- **Question**: 是否硬编码设备型号名单？
  - **AI recommendation**: 不硬编码型号；运行时查询 Provider 能力并报告状态。文档把 PICO 4 Ultra Enterprise 和 Quest 作为当前主要验收设备。（evidence: 平台 SDK、权限和设备版本可能独立变化）
  - **User confirmation**: 同意。

### Impact

- **Question**: 现有 Marker 接口能否直接承载新生命周期？
  - **AI recommendation**: 演进现有抽象或新增统一服务接口，因为当前 `IMarkerTrackingProvider` 只有 `MarkerResolved(string, Pose)`、`MarkerLost(string)`，无法表达持续 Tracking、原始 QR、状态、诊断和丢失原因。（evidence: `Assets/Scripts/Localization/IMarkerTrackingProvider.cs:4`）
  - **User confirmation**: 对外使用统一的基础追踪 Hook，不暴露业务逻辑。

- **Question**: 平台 Provider 与业务层应如何依赖？
  - **AI recommendation**: 对外暴露 `IMarkerTrackingService`，使用 C# 事件 `Tracked`、`Tracking`、`Lost`、`Diagnostic`、`StateChanged`；外部订阅事件，不继承 Quest/PICO Provider。（evidence: 当前 `MarkerAnchorService` 直接订阅 Provider，`Assets/Scripts/Localization/MarkerAnchorService.cs:24`）
  - **User confirmation**: 同意。

- **Question**: PICO 企业服务的 Init/Bind/Unbind 应由谁拥有？
  - **AI recommendation**: 企业服务生命周期由平台 Bootstrap 或共享服务拥有，Marker Tracking 只消费已就绪能力。全局关闭追踪不能解绑整个企业服务，因为它可能被其他 TOB 能力共享。（evidence: 当前 `PicoMarkerProvider.StopTracking` 会解绑整个企业服务，`Assets/Scripts/Localization/Native/PicoMarkerProvider.cs:55`）
  - **User confirmation**: 同意。

- **Question**: 现有业务服务如何受影响？
  - **AI recommendation**: `MarkerAnchorService` 可作为新 Hook 的一个下游消费者，但新能力不再以“稳定后只创建一次内容”为自身语义。当前服务对已激活 ID 会忽略后续 Pose，证明持续 Tracking 应位于它的上游。（evidence: `Assets/Scripts/Localization/MarkerAnchorService.cs:48`）
  - **User confirmation**: change 不关心外部业务如何消费 Hook。

### Design

#### Public contract

- **Question**: Hook 应携带哪些统一数据？
  - **AI recommendation**: 生命周期数据至少包含 `MarkerID`、`RawPayload`、Unity World Space 的逻辑 `Pose`、平台类型和时间戳；`Lost` 另含最后有效 Pose 和 `LostReason`。PICO 配对后保存 QR 原文，并在后续事件持续携带。（evidence: 当前 Quest 已能取得 `MarkerPayloadString`，但当前接口会把上下文压缩成 `string, Pose`，`Assets/Scripts/Localization/Native/QuestMarkerProvider.cs:30`）
  - **User confirmation**: 同意。

- **Question**: MarkerID 如何比较和规范化？
  - **AI recommendation**: MarkerID 是不透明字符串，使用区分大小写的精确比较；去空格、大小写转换、JSON 字段提取或数字格式化都由解析器负责。（evidence: PICO 原始 ID 当前由 `iMarkerId.ToString()` 得到，`Assets/Scripts/Localization/Native/PicoMarkerProvider.cs:128`）
  - **User confirmation**: 同意。

- **Question**: QR 到 MarkerID 的规则如何扩展？
  - **AI recommendation**: 初始化时注入只读依赖，例如 `IMarkerIdParser.TryParse(rawPayload, out markerId, out reason)`。运行期间不提供替换解析器的 API；未来修改通过替换实现或在下一次初始化时注入不同实现完成。（evidence: QR 数据格式尚未确定，硬编码 UUID/JSON 规则会把业务格式固化进 Provider）
  - **User confirmation**: 解析器只在初始化时注入，目的是方便未来修改，不需要热切换。

- **Question**: QR 中无法解析 MarkerID 时如何处理？
  - **AI recommendation**: 不进入 `Tracked/Tracking/Lost` 生命周期，触发独立的解析失败诊断 Hook，携带原始内容和失败原因。（evidence: 身份尚未建立时无法产生可靠 Marker 生命周期）
  - **User confirmation**: 同意。

- **Question**: Hook 在什么线程触发？
  - **AI recommendation**: 所有公开 Hook 统一在 Unity 主线程触发。SDK 回调只更新内部状态，由主线程调度器派发。（evidence: 外部订阅者很可能直接操作 GameObject；Unity 对象 API 需要主线程）
  - **User confirmation**: 同意。

#### Lifecycle and scheduling

- **Question**: 单个目标的生命周期如何定义？
  - **AI recommendation**: 首个有效身份与 Pose 建立新会话并触发一次 `Tracked`；后续有效期间持续触发 `Tracking`；暂时无样本时立即暂停 `Tracking` 并进入宽限期；宽限期内恢复则延续原会话；超时后触发一次 `Lost` 并终止该会话。（evidence: 当前 Quest 只有 Added/Removed，PICO 则通过全量快照差集推断丢失，`Assets/Scripts/Localization/Native/QuestMarkerProvider.cs:30`、`Assets/Scripts/Localization/Native/PicoMarkerProvider.cs:108`）
  - **User confirmation**: 同意。

- **Question**: 同一帧内 `Tracked` 和 `Tracking` 是否都触发？
  - **AI recommendation**: 首次成功的那一帧只触发 `Tracked`；从下一次有效 Pose 样本开始触发 `Tracking`；`Lost` 后不再接受旧会话更新。（evidence: 避免外部在同一帧重复初始化）
  - **User confirmation**: 同意。

- **Question**: 静止 Marker 是否仍持续触发 `Tracking`？
  - **AI recommendation**: 只要平台仍报告有效追踪，就按配置频率持续触发，即使 Pose 没变化；没有有效追踪样本时才暂停。（evidence: 用户要求“一直 Tracking(hook)”）
  - **User confirmation**: 是。

- **Question**: Tracking Hook 是否跟随渲染帧率？
  - **AI recommendation**: 使用独立的时间累加调度，不绑定 `FixedUpdate` 物理频率，也不要求每个渲染帧触发；提供 `trackingUpdateRateHz`，默认 30 Hz。（evidence: 用户明确允许更低频率甚至考虑 `FixedUpdate`）
  - **User confirmation**: 同意可配置低频，默认 30 Hz。

- **Question**: 丢失宽限期默认多长？
  - **AI recommendation**: 默认 0.5 秒，可全局配置；宽限期内不发送 `Tracking`，也不发送 `Lost`。（evidence: 一般追踪抖动需要短暂去抖，general assumption）
  - **User confirmation**: 同意。

- **Question**: 全局开关初始状态和幂等语义是什么？
  - **AI recommendation**: 默认关闭；外部显式调用 `EnableTracking()` 后才初始化/接收追踪。重复启用和关闭均幂等。（evidence: 防止启动即申请权限或意外启动企业能力）
  - **User confirmation**: 同意。

- **Question**: 全局关闭时活动目标如何结束？
  - **AI recommendation**: 对全部活动目标触发一次 `Lost(TrackingDisabled)`，清空会话与 PICO 配对状态，并通过会话代次令牌忽略在途回调；重新启用是全新会话。（evidence: 异步 QR 回调可能晚于关闭返回）
  - **User confirmation**: 同意。

- **Question**: `Lost` 应提供什么上下文？
  - **AI recommendation**: 提供最后有效世界 Pose、MarkerID、RawPayload、时间戳和枚举原因，例如 `OutOfView`、`TrackingDisabled`、`ProviderError`。（evidence: 仅传 ID 无法让下游正确记录终止状态）
  - **User confirmation**: 同意。

#### Quest workflow

- **Question**: Quest 丢失后如何重新建立会话？
  - **AI recommendation**: QR 丢失超过宽限期后触发 `Lost`；同一码再次被 MRUK 系统识别时直接建立新会话并触发新的 `Tracked`，不要求业务额外启动扫码。（evidence: MRUK Provider 以 Trackable Added/Removed 驱动，`Assets/Scripts/Localization/Native/QuestMarkerProvider.cs:11`）
  - **User confirmation**: 同意。

- **Question**: Quest 如何产生持续 Pose？
  - **AI recommendation**: Provider 保存活动 `MRUKTrackable`，主线程读取其最新 Transform/IsTracked，再按统一调度频率派发；不能沿用当前仅在 Added 时复制一次 Pose 的实现。（evidence: 当前 `HandleTrackableAdded` 只读取一次 Transform，`Assets/Scripts/Localization/Native/QuestMarkerProvider.cs:30`）
  - **User confirmation**: 统一基础能力必须持续发送 Tracking Hook。

#### PICO workflow

- **Question**: PICO 如何组合 QR 身份和 ArUco 6DOF Pose？
  - **AI recommendation**: 状态机为 `AwaitingQr → ScanningQr → AwaitingArUco → Active`。先解码 QR，由解析器提取 MarkerID；随后只接受 `iMarkerId` 精确相等的 ArUco Pose，成功后触发 `Tracked`。不匹配时发诊断并继续等待。（evidence: PICO `ScanQRCode` 只返回内容，`SetMarkerInfoCallback` 返回 ID 和 Pose；当前 Provider 已使用后者，`Assets/Scripts/Localization/Native/PicoMarkerProvider.cs:79`）
  - **User confirmation**: 同意。

- **Question**: PICO 的 QR 扫描由谁启动？
  - **AI recommendation**: 进入 `AwaitingQr` 时只触发 `QrScanRequired`；外部根据 UI/时机调用 `BeginQrScan()`。基础层不自动或强制打开扫码，同一时刻只允许一个扫码请求。（evidence: 本 change 是业务无关能力，扫码 UI 时机属于外部决策）
  - **User confirmation**: 同意。

- **Question**: PICO 目标 `Lost` 后是否强制重扫？
  - **AI recommendation**: `Lost` 仅清除该目标旧配对并回到可获取状态，不自动启动 QR；外部之后自行调用 `BeginQrScan`。当新的 QR→ArUco 配对完成时直接建立新会话并触发 `Tracked`。（evidence: 用户纠正“完整流程”表示重新配对的前置条件，不表示系统强制执行）
  - **User confirmation**: 表述准确。

- **Question**: QR 成功后等待 ArUco 多久？
  - **AI recommendation**: `AwaitingArUco` 默认超时 15 秒，可配置；超时发 `PairingTimedOut` 诊断，清除本次 QR 并回到 `AwaitingQr`，不自动重新扫码。（evidence: 防止陈旧 QR 身份与未来无关 ArUco 错配）
  - **User confirmation**: 同意。

- **Question**: QR 扫描取消、失败或返回空内容时怎么办？
  - **AI recommendation**: 触发 `QrScanFailed` 诊断并回到 `AwaitingQr`；不自动重试，也不为尚未建立的目标伪造 `Lost`。（evidence: 目标身份/会话尚未建立）
  - **User confirmation**: 同意。

- **Question**: 多个目标如何同时追踪？
  - **AI recommendation**: 单独维护一个“新目标获取通道”和一个 `activeTracks[MarkerID]` 字典。完成一个 QR→ArUco 配对后，该目标持续活动，同时获取通道可继续添加下一个；一个目标丢失不影响其他目标。（evidence: 单一全局状态机会阻塞多目标持续追踪）
  - **User confirmation**: 同意。

- **Question**: 如果 PICO 的 QR 扫描和 ArUco 回调不能并行，如何降级？
  - **AI recommendation**: 将并发能力列为真机硬闸门；若不能并行，添加新 QR 时暂时停止现有目标的 `Tracking` Hook但不触发 `Lost`，扫码结束后恢复 ArUco；恢复后仍识别不到的目标再按正常宽限期触发 `Lost`。（evidence: PICO 官方 API 分别描述两个接口，但没有提供并发保证）
  - **User confirmation**: 接受该降级方案。

#### Pose semantics and calibration

- **Question**: Hook 的 Pose 使用什么坐标系？
  - **AI recommendation**: 统一返回 Unity World Space Pose；各 Provider 负责平台坐标系、XR Origin 和线程转换。（evidence: 当前 PICO Provider 已处理 Tracking Origin，并接收已转为 Unity 手性的 Pose，`Assets/Scripts/Localization/Native/PicoMarkerProvider.cs:77`、`:146`）
  - **User confirmation**: 返回世界坐标。

- **Question**: Quest 的 QR 中心和 PICO 的 ArUco 中心如何统一？
  - **AI recommendation**: 以 QR 中心与朝向作为逻辑 Marker Pose。Quest 直接使用 QR Pose；PICO 对 ArUco 世界 Pose 应用可配置的局部 `ArUcoToQrOffset`。参数使用完整 `Pose`（位置和旋转），不只是一个距离。（evidence: QR 在左、ArUco 在右并排排版时，两者即使都在世界坐标中也具有不同物理参考点）
  - **User confirmation**: 以 QR 为准；两码按固定方式排列，现阶段 QR 在左、ArUco 在右；完整偏移可作为系统参数调整。

- **Question**: 偏移按每个 MarkerID 配置还是按版式全局配置？
  - **AI recommendation**: 首版只支持一套全局 `ArUcoToQrOffset`，要求所有组合码使用相同尺寸、间距和朝向。（evidence: 用户说明会采用特定统一排列方式）
  - **User confirmation**: 同意。

#### Failure and boundary behavior

- **Question**: 能力初始化失败如何通知外部？
  - **AI recommendation**: 提供 `Initializing`、`Ready`、`Unavailable`、`Error` 状态 Hook。设备不支持、权限缺失或企业接口未就绪时不进入 Marker 生命周期，只报告状态和原因，由外部处理后手动重试。（evidence: 当前 PICO Init/Bind 失败只写日志并返回，`Assets/Scripts/Localization/Native/PicoMarkerProvider.cs:45`、`:69`）
  - **User confirmation**: 同意。

- **Question**: Provider 在已有活动目标时发生不可恢复错误怎么办？
  - **AI recommendation**: 先对全部活动目标触发 `Lost(ProviderError)`，再进入 `Error` 并停止派发；初始化阶段没有活动目标时只报告 `Error`。（evidence: 保证每个已开始的外部生命周期都有终止事件）
  - **User confirmation**: 同意。

- **Question**: 同场出现重复 MarkerID 怎么办？
  - **AI recommendation**: 现场应避免重复；若仍同时出现，保留最先建立的目标，忽略后出现者并触发 `DuplicateMarkerId` 诊断，避免 Pose 在两个位置跳动。（evidence: 以 MarkerID 为字典键无法稳定区分相同身份的两个物理码）
  - **User confirmation**: 同意。

- **Question**: 运行中可以修改哪些配置？
  - **AI recommendation**: `trackingUpdateRateHz` 和 `lostGraceSeconds` 可在运行时调整并于后续调度生效；解析器是初始化只读依赖；Provider 与 `ArUcoToQrOffset` 应在关闭状态配置。（evidence: 身份和坐标语义不应在活动会话中突变）
  - **User confirmation**: 同意；特别澄清解析器只在初始化时注入。

### Success criteria

- **Question**: 多目标能力如何验收？
  - **AI recommendation**: 基础层不设置固定数量上限，由平台 SDK/设备决定；真机基线为同场至少 3 个不同 MarkerID 可保持独立生命周期，超出能力时发送诊断。（evidence: PICO/Quest 的实际上限由运行环境决定，不能从示例配置推导 SDK 硬上限）
  - **User confirmation**: 同意。

- **Question**: 跨平台逻辑 Pose 如何验收？
  - **AI recommendation**: 同一块静止组合码应用 `ArUcoToQrOffset` 后，PICO 与 Quest 的逻辑 Pose 差异目标为位置不超过 5 cm、角度不超过 5°；只作为真机验收指标，不做运行时强制过滤。（evidence: 两个平台追踪不同物理图案，需要可测的校准结果）
  - **User confirmation**: 同意。

- **Question**: 哪些行为需要自动化或真机测试覆盖？
  - **AI recommendation**: 自动化测试覆盖事件顺序、宽限期、会话代次、解析失败、ID 不匹配、重复 ID、多目标隔离、全局开关和错误收敛；真机覆盖 Quest 连续 QR Pose、PICO QR→ArUco 配对、双 API 并发/降级、至少 3 目标、Lost 后手动重建会话和跨平台 Pose 偏差。（evidence: 当前 Provider 没有覆盖测试，CodeGraph blast-radius report）
  - **User confirmation**: 上述逐项行为均已确认，作为 change 的验收边界。

## Open assumptions [NEEDS CLARIFICATION]

These are assumptions AI made without confirmation; they will be carried explicitly into artifacts:

- [ ] `[ASSUMED]` PICO 4 Ultra Enterprise 真机上 `ScanQRCode` 与 `SetMarkerInfoCallback` 是否能并行尚无官方保证；实现必须通过真机闸门选择“并行”或已确认的暂停 Tracking 降级路径。— affects: design, tasks, device acceptance tests
- [ ] `[ASSUMED]` `ArUcoToQrOffset` 的精确位置与旋转数值尚未给出，将由最终组合码的实际尺寸、间距和朝向通过系统配置提供。— affects: configuration schema, test fixture, pose acceptance
- [ ] `[ASSUMED]` 目标 Quest/PICO 系统版本、运行时权限和 PICO TOB 企业授权在部署环境中可用；代码只能检测并报告 `Unavailable/Error`，实际开通仍需真机与企业后台确认。— affects: deployment notes, integration tests, rollout

## Suggested next step

- [ ] Run `/opsx:propose cross-platform-marker-tracking` to generate artifacts (it will read this report)
