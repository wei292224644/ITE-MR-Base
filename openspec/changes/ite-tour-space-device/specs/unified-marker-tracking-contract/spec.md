## ADDED Requirements

### Requirement: 平台观测源的构造收敛到单一工厂

按平台选择并构造 `IMarkerObservationSource` 的逻辑 SHALL 只存在于一个工厂入口。观测源装配路径上的 `#if MRBASE_QUEST` / `#if MRBASE_PICO` MUST NOT 出现在第二处——包括探针场景的装配根与任何业务侧的输入层。

该工厂 SHALL 同时承担平台专属的前置装配（Quest 侧显式初始化 MRUK 运行时——加性加载的场景里没有任何自动装配钩子赶得上），并把"平台未配置"与"平台 SDK 未安装"报成两条可区分的错误：前者是构建配置问题，后者是包缺失。

#### Scenario: 探针与业务共用同一工厂

- **WHEN** 审阅标记探针场景的装配根与 ITE 设备输入层
- **THEN** 两者都经同一工厂取得观测源，各自代码中 MUST NOT 出现平台条件编译

#### Scenario: Quest 侧的 MRUK 由工厂显式装配

- **WHEN** 在 Quest 构建下经工厂取得观测源
- **THEN** MRUK 运行时已被显式初始化，订阅不会因 `MRUK.Instance` 为 null 而静默失败

#### Scenario: 两类失败可区分

- **WHEN** 构建意图为 PICO 但 `com.unity.xr.picoxr` 未安装
- **THEN** 工厂报出"SDK 未安装"，与"平台未配置"是两条不同的错误日志

### Requirement: 低置信的 AprilTag 检测在观测源内被滤掉

AprilTag 检测器会产出假标记：实测 445 次检测中出现一次场上根本不存在的 ID，其解码 margin 为 3.8，而真检测的 margin 分布在 76.5–99.6，两者是干净的双峰。该假检测当时照常派发给业务层并存活了一个滞回周期。

PICO 观测源 SHALL 按 margin 下限过滤检测结果，低于下限的 MUST NOT 进入 `Poll()` 的返回。下限 SHALL 是可调字段。

该过滤 SHALL 发生在观测源内，MUST NOT 通过给 `MarkerObservation` 增加质量字段把判断推给业务层——载荷只带平台标签与原始 payload 的约定不变，且 margin 是 AprilTag 专有概念，Quest 的 QR 没有对应物。

#### Scenario: 假标记不进入派发

- **WHEN** 检测器产出一个 margin 低于下限的结果
- **THEN** 该结果 MUST NOT 出现在 `Poll()` 的返回中，业务层 MUST NOT 收到对应的 `MarkerObserved`

#### Scenario: 真标记不被误伤

- **WHEN** 检测器产出 margin 在真检测分布内的结果
- **THEN** 该结果照常派发

#### Scenario: 载荷形状不变

- **WHEN** 审阅 `MarkerObservation` 的字段
- **THEN** 其中 MUST NOT 出现 margin、hamming 或任何检测质量字段
