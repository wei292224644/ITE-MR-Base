## MODIFIED Requirements

### Requirement: 标记事件到 ITE 的桥接发生在业务层

`MarkerTrackingSession` 只派发带平台标签与原始 payload 的观测，不做任何身份解析（见 `unified-marker-tracking-contract`）。

宿主 SHALL 提供一个桥接组件，订阅 `MarkerTrackingSession.MarkerObserved` 与 `MarkerLost`，并只承担三件事：

1. **稳定**：把观测喂给 `MarkerStabilizer`，按平台参数判稳；丢失时重置该标记的稳定状态。
2. **偏移**：对稳定后的位姿施加按平台配置的"标记局部坐标系到内容锚点"的固定偏移。
3. **透传**：把**原始 payload、标记种类与位姿**原样推给包。

payload 到 tourId 的解析 MUST NOT 发生在桥接、`MarkerTrackingSession` 或任何 `IMarkerObservationSource` 实现内——它归包所有（见 `ite-marker-identity`）。桥接中 MUST NOT 出现任何外壳格式、正则或标记编号表。

理由：payload 的形状由内容方定义且会变（预期会变成一个地址）。解析焊在宿主，等于内容每改一次码，宿主就要出一次包。

#### Scenario: 稳定后的观测被原样透传

- **WHEN** 某个标记的观测经稳定判定成立
- **THEN** 桥接以该观测的原始 payload、标记种类与施加偏移后的位姿调用包的扫码入口

#### Scenario: 桥接不解释 payload

- **WHEN** 会话派发原始 payload 为 `"250"` 的观测
- **THEN** 桥接 MUST NOT 判断其是否合规，照常稳定后透传；是否可解析由包判定并记日志

同一帧内多个标记同时判稳时，桥接 SHALL 只提交**最先判稳**的那一个，其余忽略。已有 tour 激活期间，其他标记的稳定事件 MUST NOT 抢占；要抢占须等当前标记丢失后重新扫。

理由：链路上没有任何一处收敛到"一个"——会话与稳定器的跟踪表都是字典，迭代顺序不保证，而 `TourDirector` 逐次处理、后到者会把先到者刚激活的 tour 停用销毁。不规定就是未定义行为，同一现场两次开机可能不一样。

#### Scenario: 同帧多个标记只认第一个

- **WHEN** 两张标记在同一帧内同时判稳
- **THEN** 只有最先判稳的那个被提交，另一个被忽略且 MUST NOT 触发任何激活或重锚

#### Scenario: 持续可见期间只提交一次

- **WHEN** 同一标记在视野内持续可见
- **THEN** 桥接只在稳定成立的那一次调用包的扫码入口，MUST NOT 每帧调用

#### Scenario: 丢失后重新出现按新一轮处理

- **WHEN** 该标记被判定丢失后再次出现并重新稳定
- **THEN** 桥接再调用一次包的扫码入口

### Requirement: ITE 运行时不驻留核心场景

`MRCore.unity` 只承担 MR 基座行为：XR 装配、场景切换、手势输入、诊断。它 MUST NOT 包含 `IteHostBootstrap`、任何 ITE 运行时装配点，也 MUST NOT 包含任何以 ITE 命名或为 ITE 预留的对象——包括空物体形式的锚定脚手架。

空脚手架比缺失更有害：它不产生任何运行时错误，却让人以为真机装配已经就位；而其层级一旦不满足锚定约束，内容只会出现在错误位置。

ITE 导览是内容，其装配点 SHALL 由内容场景持有。

#### Scenario: 核心场景启动不触发 ITE 联网

- **WHEN** 加载 `MRCore.unity` 并进入 Play 模式
- **THEN** 控制台 MUST NOT 出现任何 `[ITE]` 或 `[IteTour]` 前缀的日志，MUST NOT 发起对 `ite-spatial-config.uality.cn` 的请求

#### Scenario: 核心场景不含 ITE 对象

- **WHEN** 审阅 `MRCore.unity` 的对象层级
- **THEN** 其中 MUST NOT 存在锚定根、Tour 根、标记偏移节点等任何为 ITE 预留的对象

## ADDED Requirements

### Requirement: 相机按"序列化覆盖 → 运行时解析 → 报错"三段取得

装配点的相机 MUST NOT 只能由序列化引用提供：内容场景以加性方式加载在 `MRCore` 之上，而 Unity 不支持跨场景序列化引用，XR 相机因此无法在设备场景里被指定。

装配点 SHALL 按顺序取得相机：序列化字段非空时用它（编辑器验收场景用桌面相机）；为空时向宿主的 XR 上下文解析；两者都拿不到时输出错误日志并停用，MUST NOT 带着空相机进入 `IteRuntime.Create`。

#### Scenario: 编辑器场景用序列化覆盖

- **WHEN** 序列化字段指定了桌面相机
- **THEN** 使用该相机，不做任何解析

#### Scenario: 设备场景解析 XR 相机

- **WHEN** 序列化字段为空且宿主 XR 上下文已就绪
- **THEN** 解析到 XR Origin 下的相机并用于装配

#### Scenario: 两者都拿不到时可见地失败

- **WHEN** 序列化字段为空且 XR 上下文无相机
- **THEN** 装配点 SHALL 输出错误日志并停用，MUST NOT 进入加载链
