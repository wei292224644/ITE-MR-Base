## Why

Quest 直接从 QR Trackable 获得身份和 Pose，而 PICO 需要先扫描 QR，再从独立的 ArUco Marker 回调取得 ID 与 Pose；目前缺少真机证据证明这两条原生路径能够支撑同一套后续扫描业务。现在应先用最小探针和可追溯日志验证架构可行性，避免在平台行为尚未确认前设计完整生产生命周期。

## What Changes

- 为 Quest 增加最小真机探针，记录 MRUK QR Trackable 的原始内容、解析后的 MarkerID、6DOF Pose、追踪状态和回调节奏。
- 为 PICO 增加最小真机探针：由外部触发 QR 扫描，解析 MarkerID，再观察相同整数 ID 的 ArUco Pose；顺序路径稳定即可证明本次架构可行。
- 分别验证 PICO 官方静态 ID 0 与动态 ID 250，记录两类 Marker 的真实回调、移动和静默行为，不从文件命名推断运行时语义。
- 使用平台原生 SDK；不接入相机帧、OpenCV 运行时识别、自定义视觉算法或软件降级。
- 持久化详细 JSONL 日志，覆盖会话/平台/系统/SDK/权限/授权、状态、错误码、线程、原生与本地时间、回调间隔、MarkerID、Pose、夹具格式、实测尺寸、平整度和夹具文件哈希。
- 默认只保存 QR RawPayload 的长度和 SHA-256；开发构建可显式记录原文。入库代表日志必须脱敏，并把世界 Pose 转成相对首帧坐标。
- 纳入版本化打印夹具：A3 横版单页为首选，双 A4 为备用；QR 外框与 ArUco 外框均为 160 mm，QR 在左、ArUco 在右，物理中心距为 210 mm。打印后 ArUco 边长与 QR→ArUco 中心距**均需实测**并记录实测值——中心距是将来的 `ArUcoToQrOffset`，在双 A4 上由手工拼页决定，量边长推不出来。
- 记录 PICO Marker 回调的注册参数（`trackingMode`、`cameraYOffset`、返回值），它们经 SDK 的原点高度补偿直接改变每个样本的 posY。
- Probe 独占 PICO Marker 回调槽：`setMarkerInfoCallback` 是单槽 set 语义且无反注册 API，Probe 与生产 Provider 不得同时注册，Probe 停止时不解绑企业服务。
- Quest 与 PICO 各连续执行 10 次获取；验证正确 MarkerID、6DOF Pose、PICO QR→同 ID ArUco 配对、日志字段完整，并用相对 Pose 评估 5 cm / 5° 对齐目标。
- 将 PICO QR 扫描与 Marker 回调能否并行、Marker 局部坐标映射、权限/TOB 授权和回调节奏作为真机观测结果；并发失败不否决稳定的顺序架构。
- 明确不在本 change 实现生产级生命周期、多目标管理、业务对象创建、一次性触发模式、重复扫描辅助定位模式或最终公共 API 迁移；这些都基于本次证据后续单独设计。

## Capabilities

### New Capabilities

- `cross-platform-marker-tracking`: Quest QR 与 PICO QR→ArUco 原生扫描架构的真机可行性探针、持久化诊断证据、打印夹具和通过标准。

### Modified Capabilities

- （无。`openspec/specs/` 当前没有已归档的 Marker Tracking 能力规格；正在进行的 `mr-core-scene-cross-platform` change 仅包含后续 marker 接入任务，没有定义本能力的需求契约。）

## Impact

- 实现范围：只允许为真机探针、结构化日志和必要的平台适配做最小改动；不重构现有生产接口或迁移 `MarkerAnchorService`。
- 平台依赖：Quest 使用现有 Meta MRUK；PICO 使用现有 `Unity.XR.PICO.TOBSupport` 与目标设备已提供的企业能力。
- 测试资产：新增可复现的夹具生成器、静态/动态 A3 PDF、双 A4 备用 PDF、300 DPI 预览和打印/校验说明。
- 测试产物：设备本地保留完整 JSONL；仓库只收录脱敏代表日志、环境记录和结论摘要。
- 后续影响：本 change 的结论将决定生产 Provider、公共事件模型、单次触发、重复扫描定位和并发策略是否值得另立 change 实现。
