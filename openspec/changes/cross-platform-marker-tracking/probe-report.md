# Probe Report: cross-platform-marker-tracking

> Generated: 2026-08-03T17:37:30+08:00
> Summary: 64 questions, 54 consolidated decisions, 4 deferred device observations
> Status: Probe complete; remaining unknowns are explicit real-device gates, not planning questions.

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

- **Question**: 基础层是否应内建“单次扫描 / 重复扫描 / 辅助定位”等业务模式？
  - **AI recommendation**: 不新增业务模式枚举，也不由基础层决定扫描次数。基础层只提供启停、一次获取请求、生命周期和状态 Hook；单次触发或重复扫描辅助定位均由后续上层业务组合。（evidence: `openspec/changes/cross-platform-marker-tracking/design.md:23-30` 明确基础层不决定外部业务流程）
  - **User confirmation**: 确认；这些模式都是后续开发，本次只判断扫描架构是否可行。

- **Question**: 当前 change 应交付完整生产追踪服务，还是只交付架构可行性探针？
  - **AI recommendation**: 收缩为真机架构可行性验证。只实现 Quest QR 与 PICO QR→ArUco 的最小端到端探针，持久化记录平台/系统/SDK/权限、扫码结果、MarkerID、Pose、原生时间戳、回调间隔、线程、状态、错误码和并发行为，并将脱敏代表日志与结论纳入仓库。本 change 不实现完整生命周期、多目标、业务模式和生产 API。（evidence: 当前 `proposal.md:9-17` 和 `tasks.md:1-100` 承诺的是完整生产实现，与用户澄清的本次目标冲突）
  - **User confirmation**: 确认按可行性验证范围重写 proposal、design、spec 和 tasks，并保留详细日志供后续开发参考。

- **Question**: 详细真机日志如何在可调试性与数据安全之间取舍？
  - **AI recommendation**: 设备本地 JSONL 默认记录 MarkerID、完整 Pose、时间、线程、状态和错误，RawPayload 默认只记长度和 SHA-256；仅开发构建可显式开启 RawPayload 原文。提交仓库的代表日志须将 MarkerID 匿名化，并将世界 Pose 转为相对首帧坐标，保留时序和变化信息。（evidence: QR RawPayload 可能是 URL/JSON，世界 Pose 可反映现场物理布局）
  - **User confirmation**: 同意该本地详细日志与仓库脱敏样例边界。

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

- **Question**: 公开事件的时间戳应表示 Pose 采样时间还是 Hook 派发时间？
  - **AI recommendation**: 同时公开统一单调时钟下的 `SampleTimestamp` 和 `EventTimestamp`。`Lost` 携带最后有效 `SampleTimestamp` 以及实际终止的 `EventTimestamp`；PICO SDK 原生 `dTimestamp` 只在 Provider 内部用于丢弃乱序/陈旧样本，不直接暴露为跨平台时间。（evidence: `openspec/changes/cross-platform-marker-tracking/design.md:71` 原本只定义主线程形成时间；`Library/PackageCache/com.unity.xr.picoxr@e0740bf309b2/Enterprise/Scripts/Model/MarkerInfo.cs:27-28` 另有检测时间）
  - **User confirmation**: 确认双时间戳与内部乱序样本丢弃规则。

- **Question**: 为什么公开会话事件同时需要 MarkerID 和 SessionId，Quest 如何获得 MarkerID？
  - **AI recommendation**: MarkerID 标识跨丢失和重识别不变的逻辑 Marker；SessionId 标识其中一次具体追踪会话，Lost 后重建必须更换。所有 `Tracked / Tracking / Lost` 携带两者，外部以 `MarkerID + SessionId` 关联事件并防止旧异步结果污染新会话。Quest 从 MRUK `MarkerPayloadString` 交给同一初始化解析器，提取规范 MarkerID；解析失败则不建立会话。（evidence: `Assets/Scripts/Localization/Native/QuestMarkerProvider.cs:30-36`; `openspec/changes/cross-platform-marker-tracking/design.md:93-109` 已有内部会话代次但公开字段缺失）
  - **User confirmation**: 确认同时公开 MarkerID 和 SessionId。

- **Question**: MarkerID 如何比较和规范化？
  - **AI recommendation**: MarkerID 是不透明字符串，使用区分大小写的精确比较；去空格、大小写转换、JSON 字段提取或数字格式化都由解析器负责。（evidence: PICO 原始 ID 当前由 `iMarkerId.ToString()` 得到，`Assets/Scripts/Localization/Native/PicoMarkerProvider.cs:128`）
  - **User confirmation**: 同意。
  - **⚠️ 约束补充（审阅后回填）**：「不透明字符串」在 Quest 上成立，在 **PICO 上不成立**。`MarkerInfo.iMarkerId` 是 `int`（`Enterprise/Scripts/Model/MarkerInfo.cs:25`），而配对条件是 `iMarkerId.ToString()` 精确相等，因此 PICO 环境下 MarkerID 只能是某个 ArUco 整数 ID 的十进制表示——UUID、URL、带前导零的 `"007"` 一个都配不上，「区分大小写」在纯数字上也无实际作用。这是**印刷版式与解析器的联合约束**，不是服务能放宽的；不写明会导致解析器按「不透明」实现，真机首次配对 100% `PairingMismatch`。另：ArUco 字典容量（如 `DICT_4X4_250` 的 250）限制同场可区分的物理码数量，属平台事实，与「不设业务上限」不冲突。已回填至 design D3、spec「PICO 目标环境的 MarkerID 取值域」与 tasks 6.4b/10.5c。

- **Question**: 公开 MarkerID 与 PICO ArUcoId 是否应拆分为两个字段？
  - **AI recommendation**: 不拆分；两者是同一逻辑标识。将 MarkerID 明确为 ArUcoId 的规范十进制字符串（非负、无前导零）；QR RawPayload 仍可以是 URL、JSON 或其他格式，初始化解析器负责从中提取并规范化该数值。这保证其与 PICO SDK `iMarkerId.ToString()` 可精确配对。（evidence: `Assets/Scripts/Localization/Native/PicoMarkerProvider.cs:128`; `openspec/changes/cross-platform-marker-tracking/design.md:170`）
  - **User confirmation**: 确认 MarkerID 和 ArUcoId 是同一标识，接受规范十进制字符串约束。

- **Question**: QR 到 MarkerID 的规则如何扩展？
  - **AI recommendation**: 初始化时注入只读依赖，例如 `IMarkerIdParser.TryParse(rawPayload, out markerId, out reason)`。运行期间不提供替换解析器的 API；未来修改通过替换实现或在下一次初始化时注入不同实现完成。（evidence: QR 数据格式尚未确定，硬编码 UUID/JSON 规则会把业务格式固化进 Provider）
  - **User confirmation**: 解析器只在初始化时注入，目的是方便未来修改，不需要热切换。

- **Question**: QR 中无法解析 MarkerID 时如何处理？
  - **AI recommendation**: 不进入 `Tracked/Tracking/Lost` 生命周期，触发独立的解析失败诊断 Hook，携带原始内容和失败原因。（evidence: 身份尚未建立时无法产生可靠 Marker 生命周期）
  - **User confirmation**: 同意。

- **Question**: Hook 在什么线程触发？
  - **AI recommendation**: 所有公开 Hook 统一在 Unity 主线程触发。SDK 回调只更新内部状态，由主线程调度器派发。（evidence: 外部订阅者很可能直接操作 GameObject；Unity 对象 API 需要主线程）
  - **User confirmation**: 同意。
  - **⚠️ 范围更正（审阅后回填）**：结论不变，但**理由不是「SDK 会从别的线程回调」**。现有两端回调都已在主线程：PICO 的 `MarkerInfoCallback.CallBack` 与 `StringCallback.CallBack` 都先 `PXR_EnterpriseTools.QueueOnMainThread(...)`（`Enterprise/Scripts/Interfaces/MarkerInfoCallback.cs:38`、`Interfaces/StringCallback.cs:17`），Quest 的 `TrackableAdded`/`TrackableRemoved` 是主线程 UnityEvent。因此队列的价值在于派发时机解耦、代次拦截和顺序稳定，**不需要锁或并发原语，也不应为此写并发竞态测试**——没有生产者能制造那种竞态。已回填至 design D5、spec「Provider 回调不直接派发」与 tasks 2.2/3.7。

- **Question**: PICO 新目标获取状态应通过诊断还是独立公开 Hook 告知外部？
  - **AI recommendation**: 新增 `AcquisitionStateChanged` Hook，显式携带 `AwaitingQr / ScanningQr / AwaitingArUco`；`QrScanRequired` 由获取状态表达，`Diagnostic` 只用于失败和异常，避免外部解析诊断来驱动正常 UI 流程。（evidence: `openspec/changes/cross-platform-marker-tracking/design.md:57-63` 的公开事件缺少获取状态，而 `:156-173` 要求外部驱动扫码状态机）
  - **User confirmation**: 同意新增独立获取状态 Hook。

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
  - **⚠️ 缺口补充（审阅后回填）**：「取消」这一支**没有可靠的信号源**。`PXR_Enterprise.ScanQRCode(Action<string> callback)`（`Enterprise/Scripts/PXR_Enterprise.cs:3397`）返回 `void`，唯一出口就是那个回调；用户在系统扫码界面按返回时是否仍会回调，官方无任何保证。配合「同一时刻单请求」的约束，回调不来即等于获取通道永久锁死，后续每一次 `BeginQrScan` 都被拒。已补 `ScanningQr` 独立超时（默认 30 秒 → `QrScanTimedOut`）、`CancelQrScan()` 与扫描代次丢弃迟到回调，见 design D8、spec 三条新 scenario、tasks 6.2b/6.2c/10.5b。

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
  - **⚠️ 证据更正（审阅后回填）**：上面引用的 `PicoMarkerProvider.cs:77` 是 `ResolveTrackingOriginMode()`，它只是**告诉 SDK 按 floor 还是 device 补偿原点高度**，并**不做任何到 Unity 世界坐标的空间变换**；`:146` 的 `ToPose` 也只是 `double → float`。`MarkerInfoCallback.JsonToMarkerInfos`（`Enterprise/Scripts/Interfaces/MarkerInfoCallback.cs:88-97`）同样只做手性转换与高度补偿。因此 PICO 侧的 Pose 实为**追踪原点空间**，现状并未满足「返回世界坐标」这一确认结论。该转换缺口已在 design D13 与 tasks 7.6–7.9 中补上；原证据不足以支撑「已处理」的措辞，特此更正以免后续实现照此判断跳过该步骤。

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

- **Question**: `Unavailable/Error` 状态下的“外部手动重试”通过什么公开操作完成？
  - **AI recommendation**: 新增 `RetryInitialization()`，仅允许 `Unavailable/Error → Initializing`；其他状态调用不执行并返回明确结果。保留 `EnableTracking` / `DisableTracking` 本身的幂等语义。（evidence: `openspec/changes/cross-platform-marker-tracking/specs/cross-platform-marker-tracking/spec.md:130,154` 要求幂等启停与手动重试，但 `tasks.md:6` 原公开入口无重试方法）
  - **User confirmation**: 同意新增显式重试入口。

- **Question**: Provider 在已有活动目标时发生不可恢复错误怎么办？
  - **AI recommendation**: 先对全部活动目标触发 `Lost(ProviderError)`，再进入 `Error` 并停止派发；初始化阶段没有活动目标时只报告 `Error`。（evidence: 保证每个已开始的外部生命周期都有终止事件）
  - **User confirmation**: 同意。

- **Question**: PICO Provider 静默停止回调时，是否现在就定义超时并进入 Error？
  - **AI recommendation**: 使用可配置回调停滞超时防止无限重放陈旧 Pose，但官方 API 未承诺回调频率，具体阈值需真机数据。（evidence: PICO `SetMarkerInfoCallback` 官方参考只定义返回 Pose/ID，未定义 cadence；当前设计会按 30 Hz 重用最新 Pose）
  - **User confirmation**: 本 change 先不定超时阈值也不自动进入 Error；先做真机测试，保留回调时间、原生时间戳、间隔与状态日志，使真实行为可监控，再决定后续策略。

- **Question**: 同场出现重复 MarkerID 怎么办？
  - **AI recommendation**: 现场应避免重复；若仍同时出现，保留最先建立的目标，忽略后出现者并触发 `DuplicateMarkerId` 诊断，避免 Pose 在两个位置跳动。（evidence: 以 MarkerID 为字典键无法稳定区分相同身份的两个物理码）
  - **User confirmation**: 同意。
  - **⚠️ 更正（审阅后回填）**：「保留最先建立的目标」在 PICO 上**做不到**——这一点下一条已经指出，但落进 spec 时丢了，spec 一度只写了通用的「保留最早建立的观察」。规则按平台分治：Quest 有 `MRUKTrackable` 实例可保留首个；PICO 的 `MarkerInfo` 无观察实例 ID，只能整体丢弃该 ID 的全部条目。已回填至 design D9 与 spec 的两条平台 scenario。

- **Question**: PICO SDK 没有稳定物理观察 ID 时，重复 MarkerID 如何避免 Pose 误切换？
  - **AI recommendation**: 按平台细分。Quest 利用 MRUK Trackable 实例保留首个目标；PICO 同一快照出现多个相同 `iMarkerId` 时拒绝该 ID 的全部 Pose 并触发 `DuplicateMarkerId`，已有会话进入正常丢失宽限，配对中的目标继续等待。（evidence: `Library/PackageCache/com.unity.xr.picoxr@e0740bf309b2/Enterprise/Scripts/Model/MarkerInfo.cs:5-31` 仅提供 ID、Pose 与时间戳，没有观察实例 ID）
  - **User confirmation**: 同意采用该安全失败规则。

- **Question**: 运行中可以修改哪些配置？
  - **AI recommendation**: `trackingUpdateRateHz` 和 `lostGraceSeconds` 可在运行时调整并于后续调度生效；解析器是初始化只读依赖；Provider 与 `ArUcoToQrOffset` 应在关闭状态配置。（evidence: 身份和坐标语义不应在活动会话中突变）
  - **User confirmation**: 同意；特别澄清解析器只在初始化时注入。

- **Question**: 应用暂停或头显休眠时，活动 Marker 会话如何处理？
  - **AI recommendation**: 新增 `Suspended` 服务状态。暂停时停止公开 Hook 与超时计时、作废在途回调，但保留活动 ID、最后 Pose 和 PICO 配对；未完成扫码逻辑中断并回到 `AwaitingQr`。恢复后从零开始一次 `lostGraceSeconds` 重捕获宽限，成功则延续原会话，失败则 `Lost(OutOfView)`。（evidence: `openspec/changes/mr-core-scene-cross-platform/specs/mr-core-scene/spec.md:3-5` 规定核心场景常驻全应用生命周期，当前 Marker spec 未定义暂停边界）
  - **User confirmation**: 确认该 `Suspended` 与恢复宽限语义。

### Success criteria

- **Question**: 多目标能力如何验收？
  - **AI recommendation**: 基础层不设置固定数量上限，由平台 SDK/设备决定；真机基线为同场至少 3 个不同 MarkerID 可保持独立生命周期，超出能力时发送诊断。（evidence: PICO/Quest 的实际上限由运行环境决定，不能从示例配置推导 SDK 硬上限）
  - **User confirmation**: 同意。

- **Question**: 跨平台逻辑 Pose 如何验收？
  - **AI recommendation**: 同一块静止组合码应用 `ArUcoToQrOffset` 后，PICO 与 Quest 的逻辑 Pose 差异目标为位置不超过 5 cm、角度不超过 5°；只作为真机验收指标，不做运行时强制过滤。（evidence: 两个平台追踪不同物理图案，需要可测的校准结果）
  - **User confirmation**: 同意。
  - **⚠️ 判据更正（审阅后回填）**：5 cm / 5° 的阈值保留，但**比较对象错了**。两台头显各自建立独立的追踪原点，同一块码的世界坐标数值天然不同且**两端都正确**，直接相减得到的是两个原点之间的偏移，不是标定误差。改为比较相对量：固定放置两块组合码 A、B，两端各自算出 A→B 的相对变换后再比较。已回填至 spec「跨平台校准验收使用相对量」与 tasks 10.8/10.8b。

- **Question**: 哪些行为需要自动化或真机测试覆盖？
  - **AI recommendation**: 自动化测试覆盖事件顺序、宽限期、会话代次、解析失败、ID 不匹配、重复 ID、多目标隔离、全局开关和错误收敛；真机覆盖 Quest 连续 QR Pose、PICO QR→ArUco 配对、双 API 并发/降级、至少 3 目标、Lost 后手动重建会话和跨平台 Pose 偏差。（evidence: 当前 Provider 没有覆盖测试，CodeGraph blast-radius report）
  - **User confirmation**: 上述逐项行为均已确认，作为 change 的验收边界。

- **Question**: 范围收缩后，什么结果足以判定扫描架构可行？
  - **AI recommendation**: 以四个硬结果覆盖旧的生产级验收边界：Quest 与 PICO 各连续 10 次获取同一组合码，每次得到正确 MarkerID 和 6DOF Pose；PICO 完成 QR→同 ID ArUco 配对；同一固定 `ArUcoToQrOffset` 在多个位置/朝向下将两端逻辑 Pose 对齐至 5 cm / 5° 以内；每次测试生成字段完整、可提取的持久化日志。PICO 双 API 并发结果只记录为后续架构输入；只要顺序路径稳定即不判定整体不可行。（evidence: 范围已收缩为可行性探针；原 `spec.md:273-277` 已确认 5 cm / 5° 跨平台验收目标）
  - **User confirmation**: 确认该可行性通过标准。

- **Question**: 可行性测试的 QR+ArUco 物理夹具由谁提供？
  - **AI recommendation**: 由本 change 创建并纳入仓库一份版本化、可按 A4 100% 比例打印的测试夹具，包含相同规范 MarkerID 的 QR 与 PICO 支持的 Marker 图案，明确码尺寸、间距、朝向、打印比例和理论 `ArUcoToQrOffset`。（evidence: 仓库当前没有 QR/ArUco 图片、SVG 或 PDF 测试资产）
  - **User confirmation**: 同意由本 change 直接生成可用 A4 纸打印的 QR+ArUco 测试图片并纳入仓库。

- **Question**: PICO 官方 A4 Marker 已占用 160 mm × 160 mm 时，组合夹具如何在一张 A4 上保持官方打印尺寸？
  - **AI recommendation**: 改用 A4 竖版上下布局：PICO 官方 160 mm ArUco 图案保持页面中央和 100% 尺寸，约 48 mm 的 QR 放在上方空白区；两码使用同一个规范十进制 MarkerID，并按实际中心距和朝向计算固定 `ArUcoToQrOffset`。禁止用任意 OpenCV 字典或生成式图像仿制 PICO Marker。（evidence: PICO 官方打印指南要求 A4 图案为 160 mm × 160 mm、保留白边且不缩放；官方示例使用预生成的 `A4_<ID>` Marker，未公开可替代它的标准 OpenCV 字典名）
  - **User correction**: 生成预览后否决 48 mm QR；要求 QR 与 ArUco 使用相同边长。原 A4 单页上下布局不再成立，需重新选择承载方式。

- **Question**: 已取得 PICO 静态与动态官方 PDF 后，本 change 应只验证静态码还是两类码都验证？
  - **AI recommendation**: 生成两张 A4 组合夹具并分别留存真机日志：`QR 0 + A4_0_static` 与 `QR 250 + A4_250_dynamic`。两类图案均已逐位确认对应 OpenCV `DICT_4X4_1000` 的同 ID 码字；PICO 公开 `SetMarkerInfoCallback` 文档未说明静态/动态码的回调和移动行为差异，正适合在本次架构可行性探针中实测。（evidence: `/Users/wwj/Downloads/static marker/A4_0_static.pdf`、`/Users/wwj/Downloads/dynamic marker/A4_250_dynamic.pdf`；OpenCV 官方 `DICT_4X4_1000_BYTES` 中 ID 0 为 `B5 32`、ID 250 为 `7E 80`）
  - **User confirmation**: 确认两张都生成并验证。

- **Question**: QR 与 ArUco 等大后，如何继续使用 A4 打印并保持固定物理偏移？
  - **AI recommendation**: 每套夹具改为两张 A4 竖版：左页放 160 mm QR 外框（含标准四模块静区），右页放 160 mm ArUco；两页上下边对齐、左右边贴合安装，码中心水平距离固定为 210 mm。（evidence: 两个 160 mm 方形无法同时放入 210 mm × 297 mm 的单张 A4；双页拼接既保留标准 A4 打印，又恢复用户确认的“QR 左、ArUco 右”关系）
  - **User confirmation**: 确认改成双 A4。

- **Question**: 双 A4 拼接以外是否需要更稳定的单页打印版本？
  - **AI recommendation**: 新增 A3 横版单页作为首选夹具，同时保留双 A4 作为备用。A3 的 420 mm × 297 mm 恰好等于两张 A4 竖版横向拼接，可原样保持两个 160 mm 外框、QR 在左、ArUco 在右和 210 mm 中心距，并消除纸张拼接误差。（evidence: 当前双 A4 组合预览本身已是 420 mm × 297 mm；固定 Pose 偏移测试对夹具平整度和拼接误差敏感）
  - **User confirmation**: 确认 A3 最合适，要求再生成一套 A3；双 A4 继续作为备用。

- **Question**: 真机日志是否需要记录夹具和打印安装条件？
  - **AI recommendation**: 每次真机测试将夹具格式（A3/双 A4）、打印后 ArUco 实测边长、平整度/安装说明作为会话级环境字段写入日志，并关联夹具文件哈希。（evidence: 缩放、翘曲和双页拼接误差会直接进入 Pose 偏差；不记录这些条件就无法区分平台追踪误差与物理夹具误差）
  - **User confirmation**: 确认记录这些字段。

- **Question**: 夹具完成后是否还有需要继续通过问答决定的问题？
  - **AI recommendation**: 结束 probe；将 PICO 双 API 并发、Pose 坐标映射、运行时权限/TOB 授权和回调节奏保留为真机硬门槛，通过持久化日志获取事实，不继续猜测。（evidence: 四项均依赖目标设备、系统版本和现场运行行为，代码或公开资料无法替代实测）
  - **User confirmation**: 确认结束 probe，并进入现有 OpenSpec artifacts 的一致性更新。

## Deferred real-device observations

These are assumptions AI made without confirmation; they will be carried explicitly into artifacts:

- [ ] `[ASSUMED]` PICO 4 Ultra Enterprise 真机上 `ScanQRCode` 与 `SetMarkerInfoCallback` 是否能并行尚无官方保证；实现必须通过真机闸门选择“并行”或已确认的暂停 Tracking 降级路径。— affects: design, tasks, device acceptance tests
- [ ] `[ASSUMED]` 首选 A3 与备用双 A4 夹具均已确定 QR 与 ArUco 的物理中心距为 210 mm、QR 在左且无垂直偏移，但该位移映射到 PICO Marker 局部坐标后的轴、符号和旋转仍需由真机 Pose 日志确认。— affects: configuration schema, pose acceptance
- [ ] `[ASSUMED]` 目标 Quest/PICO 系统版本、运行时权限和 PICO TOB 企业授权在部署环境中可用；代码只能检测并报告 `Unavailable/Error`，实际开通仍需真机与企业后台确认。— affects: deployment notes, integration tests, rollout
- [ ] `[ASSUMED]` PICO Marker 回调的正常频率、最大间隔与静默停滞表现尚未经真机量化；本 change 只保留可监控日志，不猜测超时阈值或自动错误转移。— affects: diagnostics, device acceptance tests, future provider-stall policy

## Suggested next step

- [x] Start `$openspec-update-change cross-platform-marker-tracking` to align the existing proposal, design, specs, and tasks with this probe before implementation.
