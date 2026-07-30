## ADDED Requirements

### Requirement: 手部数据统一来源

手部关节数据 SHALL 唯一来源于 Unity XR Hands 的 `XRHandSubsystem`。系统 MUST NOT 使用厂商专属手部 API（`OVRHand` / `OVRSkeleton` / `PXR_HandTracking` 等）作为手部呈现的数据来源。

#### Scenario: Quest 上的数据来源

- **WHEN** 应用在 Quest 上运行并查询当前 `XRHandSubsystem` 的 descriptor
- **THEN** 存在且仅存在一个运行中的手部子系统，由 OpenXR `XR_EXT_hand_tracking` 提供

#### Scenario: PICO 上的数据来源

- **WHEN** 应用在 PICO 上运行并查询当前 `XRHandSubsystem` 的 descriptor
- **THEN** 其 `id` 为 `"PICO Hands"`（由 PICO Integration SDK 的 provider 注册）

#### Scenario: 呈现代码不含厂商符号

- **WHEN** 检索手部呈现相关源码
- **THEN** 不含 `OVRHand` / `OVRSkeleton` / `PXR_HandTracking` 等厂商类型引用

### Requirement: 半透明手部呈现

系统 SHALL 为左右手各呈现一个半透明手部网格，由 `XRHandSubsystem` 的关节数据驱动。两端 MUST 使用同一份手部网格资产与同一份材质，使外观在两台设备上一致。

#### Scenario: 双手出现并跟随

- **WHEN** 用户在视野内举起双手
- **THEN** 左右手各出现一个半透明手部网格，跟随真实手部运动，且左右手不互换

#### Scenario: 两端外观一致

- **WHEN** 对比 Quest 与 PICO 上的手部呈现
- **THEN** 使用的网格资产与材质为同一份（相同 GUID），非按平台分别提供

#### Scenario: 真实手部可透视

- **WHEN** 在 passthrough 下观察手部呈现
- **THEN** 真实手部透过半透明网格可见，网格不完全遮挡真实手部

### Requirement: 手部网格资产授权合规

手部网格资产 MUST NOT 取自 Quest 或 PICO 厂商 SDK 内附带的手部模型文件（其许可限定于各自平台），SHALL 使用许可允许跨平台分发的资产。

#### Scenario: 资产来源合规

- **WHEN** 检查手部网格资产的来源路径
- **THEN** 其不来自 `com.meta.xr.sdk.core` 或 PICO SDK 的目录

### Requirement: 追踪丢失与恢复的显示行为

当某只手的追踪丢失时，系统 SHALL 停止呈现该手；追踪恢复后 MUST 重新呈现于当前实际位置。丢失期间 MUST NOT 将手部网格残留在最后已知位置。

#### Scenario: 手移出视野

- **WHEN** 用户将一只手移出追踪视野
- **THEN** 该手的呈现消失，另一只手不受影响

#### Scenario: 手移回视野

- **WHEN** 用户将该手移回追踪视野
- **THEN** 该手的呈现重新出现于当前实际位置，不出现卡死或位置残留

### Requirement: 运行时手部网格不作为依赖

系统 MUST NOT 依赖运行时提供的手部网格数据（`XRHandSubsystemProvider.TryGetMeshData`），因该能力在 PICO 侧未实现。

#### Scenario: 不调用运行时网格接口

- **WHEN** 检索源码中对运行时手部网格查询接口的调用
- **THEN** 命中数为 0

### Requirement: 性能基线

在核心场景仅包含手部呈现与少量测试对象的条件下，系统 SHALL 在两端稳定维持不低于 72 fps，并 MUST 记录实测数值作为后续内容增加时的对照基线。

#### Scenario: 两端帧率达标

- **WHEN** 在 Quest 与 PICO 上运行核心场景并举手观察
- **THEN** 两端帧率均不低于 72 fps，且实测数值被记录留档
