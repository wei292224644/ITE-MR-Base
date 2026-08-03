## ADDED Requirements

### Requirement: 输入中立化通过多 binding 实现

跨平台的手部输入 SHALL 通过在同一个 `InputAction` 上同时绑定 Quest 与 PICO 两个平台的输入设备路径实现，使各平台在运行时各自命中其可用设备。输入层 MUST NOT 通过条件编译区分平台。

#### Scenario: Quest 上命中 Meta 设备

- **WHEN** 应用在 Quest 上运行并执行捏合
- **THEN** 对应 action 产生输入，其生效控件归属于 Meta 的手部 aim 设备

#### Scenario: PICO 上命中 PICO 设备

- **WHEN** 应用在 PICO 上运行并执行捏合
- **THEN** 对应 action 产生输入，其生效控件归属于 `PicoAimHand` 设备

#### Scenario: 输入配置无平台条件编译

- **WHEN** 检查输入相关配置与代码
- **THEN** 手部输入的 action 与 binding 为单一份配置，不按平台分叉

### Requirement: 禁止依赖厂商单侧填充的中立接口

系统 MUST NOT 依赖 `XRCommonHandGestures` 或 `XRHandDevice` 上的 aim / pinch 通道作为跨平台捏合来源 —— 该通道虽为中立 API，但 PICO 的 provider 未开启通用姿态数据供给（`canSurfaceCommonPoseData` 为默认值），在 PICO 上无数据。

#### Scenario: 不引用通用手势通道

- **WHEN** 检索源码与输入配置中对通用手势/aim 通道的使用
- **THEN** 命中数为 0；捏合信号仅来自各平台 aim 设备的显式 binding

### Requirement: 捏合选择

用户 SHALL 能通过捏合手势选中并抓取场景中的可交互对象，松开捏合后释放。该行为在两端 MUST 一致。

#### Scenario: 捏合抓取与释放

- **WHEN** 用户将手靠近测试对象并捏合
- **THEN** 对象被选中并随手移动；松开捏合后对象被释放

#### Scenario: 两端行为一致

- **WHEN** 在 Quest 与 PICO 上分别执行同一捏合抓取操作
- **THEN** 两端的选中、跟随、释放行为一致

### Requirement: 指尖触碰

用户 SHALL 能用指尖按下场景中的可触碰控件，并获得可见反馈。该行为在两端 MUST 一致。

#### Scenario: 指尖按下按钮

- **WHEN** 用户用指尖触碰测试按钮
- **THEN** 按钮触发并呈现可见反馈

### Requirement: 交互输入的可诊断性

系统 SHALL 提供在设备上可见的诊断呈现，至少包含当前已连接输入设备列表、手部交互相关 action 的当前值、以及各 action 当前生效的控件来源。

#### Scenario: 设备上读取输入诊断

- **WHEN** 在 Quest 或 PICO 上运行诊断呈现并执行捏合
- **THEN** 屏幕上可读出设备列表、action 数值变化、以及当前生效控件所属设备

#### Scenario: 诊断不依赖编辑器连接

- **WHEN** 头显与开发机断开连接后独立运行
- **THEN** 诊断信息仍在设备屏幕上可读

### Requirement: 交互输入配置不受包升级覆盖

手部输入的 action 配置 SHALL 保存为项目自有资产，MUST NOT 直接修改随包分发的示例输入资产（升级会覆盖）。

#### Scenario: 输入资产位于项目目录

- **WHEN** 检查手部输入 action 资产的路径
- **THEN** 其不位于随包导入的 Samples 目录内
