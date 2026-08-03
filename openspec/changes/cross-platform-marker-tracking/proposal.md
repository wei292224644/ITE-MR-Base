## Why

现有 Marker 接口只能报告一次识别结果和一次丢失，无法向外部提供统一、持续、可诊断的跨平台 6DOF 追踪生命周期；Quest 直接追踪 QR，而 PICO 还需要把 QR 身份与 ArUco Pose 配对，两端语义目前不一致。

本变更建立一个与业务内容无关的基础能力：外部只订阅统一 Hook，即可获知 Marker 首次追踪、持续追踪、丢失、能力状态和诊断信息，并在 Quest 与 PICO 上获得以 QR 为逻辑原点的 Unity 世界坐标 Pose。

## What Changes

- 新增统一 Marker Tracking 服务契约，提供 `Tracked`、`Tracking`、`Lost`、`Diagnostic` 和 `StateChanged` Hook。
- 新增完整生命周期语义、主线程派发、可配置更新频率、丢失宽限期、全局启停和会话隔离。
- 将 QR 原文视为不透明数据；通过初始化时注入的解析器提取 MarkerID，不校验 UUID、URL 或 JSON 格式。
- Quest Provider 基于 MRUK QR Trackable 持续输出 Pose，丢失后由平台重新识别直接建立新会话。
- PICO Provider 实现外部触发的 QR→ArUco 配对状态机、超时与诊断、多目标独立追踪，以及 Lost 后的手动重新配对。
- 统一 Pose 为以 QR 中心和朝向为原点的 Unity World Space Pose；PICO 通过全局可配置的 `ArUcoToQrOffset` 校正并排码图的物理偏移。
- 将 PICO 企业服务 Init/Bind/Unbind 从 Marker Tracking 生命周期中解耦，追踪服务只消费已就绪的平台企业能力。
- 只接入 Quest 与 PICO 平台原生 SDK；不新增 OpenCV、自定义相机帧识别、自定义 ArUco 字典或软件视觉降级。
- **BREAKING**：现有只包含 `MarkerResolved(string, Pose)` / `MarkerLost(string)` 的 Provider 契约不足以表达新生命周期；现有下游需要迁移到统一事件数据与状态模型。

## Capabilities

### New Capabilities

- `cross-platform-marker-tracking`: 业务无关的 Quest QR 与 PICO QR→ArUco 多目标 6DOF 追踪契约、生命周期、诊断、配置和跨平台 Pose 统一规则。

### Modified Capabilities

- （无。`openspec/specs/` 当前没有已归档的 Marker Tracking 能力规格；正在进行的 `mr-core-scene-cross-platform` change 仅包含后续 marker 接入任务，没有定义本能力的需求契约。）

## Impact

- 公共 API：新增统一服务接口、事件数据、状态/诊断/丢失原因模型、MarkerID 解析器接口和 PICO 扫码入口。
- 现有代码：重构 `IMarkerTrackingProvider`、`QuestMarkerProvider`、`PicoMarkerProvider`、`MarkerTrackingBootstrapper`；将 `MarkerAnchorService` 迁移为统一 Hook 的下游消费者。
- 平台依赖：Quest 继续依赖 Meta MRUK；PICO 继续依赖 `Unity.XR.PICO.TOBSupport`，但企业服务所有权移交平台 Bootstrap 或共享服务。
- 配置：新增 `trackingUpdateRateHz`（默认 30 Hz）、`lostGraceSeconds`（默认 0.5 秒）、PICO 配对超时（默认 15 秒）和全局 `ArUcoToQrOffset`。
- 测试：新增平台无关 EditMode 生命周期测试、Provider 适配测试，以及 Quest/PICO 真机并发、配对、多目标、权限和 Pose 校准闸门。
- 下游：业务代码不再从平台 Provider 获取内容；只通过统一 Hook 接收 Marker 数据并自行决定加载、创建或销毁行为。

## Open Assumptions

- [ ] `[ASSUMED]` PICO 4 Ultra Enterprise 真机上 `ScanQRCode` 与 `SetMarkerInfoCallback` 是否能并行尚无官方保证；实现必须通过真机闸门选择“并行”或已确认的暂停 Tracking 降级路径。— affects: design, tasks, device acceptance tests
- [ ] `[ASSUMED]` `ArUcoToQrOffset` 的精确位置与旋转数值尚未给出，将由最终组合码的实际尺寸、间距和朝向通过系统配置提供。— affects: configuration schema, test fixture, pose acceptance
- [ ] `[ASSUMED]` 目标 Quest/PICO 系统版本、运行时权限和 PICO TOB 企业授权在部署环境中可用；代码只能检测并报告 `Unavailable/Error`，实际开通仍需真机与企业后台确认。— affects: deployment notes, integration tests, rollout
