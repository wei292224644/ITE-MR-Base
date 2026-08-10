## ADDED Requirements

### Requirement: 测量条件锁定与可见

装置 SHALL 在采样开始前显式锁定会改变 GPU 工作量的系统级变量，并在 HUD 上**持续显示其实时值**（而非启动时的设定值）。至少覆盖：ASW / SpaceWarp 状态、XR 刷新率、渲染视口缩放、FFR 等级、动态分辨率状态。任一项在采样期间发生变化时，装置 MUST 将该次采样标记为无效。

#### Scenario: 采样前锁定条件

- **WHEN** 启动一次 sweep
- **THEN** 装置关闭 ASW/SpaceWarp、固定刷新率、固定渲染视口缩放与 FFR 等级，并把这些值写入本次 sweep 的产出记录头部

#### Scenario: 条件在采样中漂移

- **WHEN** 采样期间检测到刷新率、渲染缩放或 FFR 等级与锁定值不一致
- **THEN** 装置将当前档标记为 `invalid` 并在 HUD 与日志中给出可见提示，不静默采纳该档数据

#### Scenario: 条件实时可读

- **WHEN** 装置运行中的任意时刻查看 HUD
- **THEN** 上述每一项的当前实际值均可读，且与本次 sweep 的锁定值并列显示

### Requirement: GPU 帧时间为主指标

装置 SHALL 以 GPU 帧时间（毫秒）为主指标，报告其中位数与 1% low，并同时显示当前刷新率对应的帧预算与占用比例。FPS MAY 显示，但 MUST NOT 作为档间比较的依据。

#### Scenario: 报告中位数而非均值

- **WHEN** 一档采样结束
- **THEN** 产出中包含该档 GPU 时间的中位数与 1% low，均值 MAY 缺省

#### Scenario: 帧预算可见

- **WHEN** HUD 显示 GPU 时间
- **THEN** 同时显示当前刷新率下的帧预算毫秒数与已用占比

#### Scenario: 帧时间不可用时显式失败

- **WHEN** `FrameTimingManager` 未返回有效数据（例如 Frame Timing Stats 未启用）
- **THEN** HUD 显示明确的不可用提示，装置 MUST NOT 以 FPS 反推 GPU 时间冒充主指标

### Requirement: 运行时旋钮可控且状态可见

装置 SHALL 允许在设备上不重新编译地切换被测旋钮，且 HUD MUST 显示每个旋钮的当前值。至少覆盖：立体渲染模式（Single Pass Instanced / Multi-pass）、MSAA、SH degree、Splat Downscale、排序刷新间隔、渲染视口缩放。

#### Scenario: 手柄切换旋钮

- **WHEN** 用户在设备上通过手柄操作旋钮控制器
- **THEN** 对应旋钮值即时改变、渲染即时反映该改变，且 HUD 上该项显示同步更新

#### Scenario: 旋钮状态与产出一致

- **WHEN** 一档采样写入产出记录
- **THEN** 该记录包含采样时全部旋钮的取值快照

#### Scenario: 重置

- **WHEN** 用户触发重置
- **THEN** 全部旋钮回到 baseline 档定义的取值

### Requirement: 累加式 sweep 与采样契约

装置 SHALL 提供自动 sweep：按预定义的**累加**序列逐档配置旋钮，每档先预热再采样，取该档 GPU 时间中位数。装置 SHALL 另提供单变量模式（每档仅从 baseline 改动一项）作为归因回归手段。

#### Scenario: 预热后采样

- **WHEN** 进入新的一档
- **THEN** 装置先运行一段预热时间且该段数据不计入统计，之后才开始采样

#### Scenario: 累加语义

- **WHEN** sweep 运行在默认（累加）模式
- **THEN** 第 N 档的旋钮配置等于第 N-1 档的配置再叠加本档新增的一项

#### Scenario: 单变量模式可用

- **WHEN** sweep 运行在单变量模式
- **THEN** 每档的旋钮配置等于 baseline 仅改动本档指定的一项

#### Scenario: sweep 可中断

- **WHEN** 用户在 sweep 进行中触发停止
- **THEN** 装置停止推进档位，已完成的档的数据仍被完整写出

### Requirement: 结构化产出

装置 SHALL 将每档结果写入 `Application.persistentDataPath` 下的 CSV，并将逐档记录与周期性状态快照写入同目录下的 `.log` 文件。产出 MUST 包含足以复现该次测量的上下文。周期性输出 MUST NOT 经由引擎 Console 日志（`Debug.Log`）—— 仅生命周期里程碑与错误可进 Console。

#### Scenario: 产出内容完整

- **WHEN** 一次 sweep 结束
- **THEN** CSV 中每行至少包含：档序号与名称、全部旋钮取值快照、splat 总数与 renderer 数量、GPU 中位数与 1% low、CPU 中位数、锁定条件的实时值、有效性标记

#### Scenario: 上下文可复现

- **WHEN** 查看产出文件头部
- **THEN** 可读到设备标识、被测资产标识与 splat 原始数量、剪枝阈值、sweep 模式、锁定条件、装置版本

#### Scenario: 日志落文件而非 Console

- **WHEN** 装置运行并周期性产生状态快照与逐档记录
- **THEN** 这些内容写入 `.log` 文件且每行带时间戳，Console 中只出现就绪、sweep 起止、文件路径等里程碑与错误

#### Scenario: 日志文件路径可发现

- **WHEN** 装置就绪
- **THEN** `.log` 与 CSV 的完整路径出现在 Console 里程碑中，便于在 `adb logcat` 里定位后取出文件

### Requirement: 装置不污染被测量对象

HUD 与日志的开销 MUST 被限制且可验证。HUD 文本刷新 SHALL 不高于 5Hz，日志输出 SHALL 不高于 0.5Hz，且装置 SHALL 支持在采样期间整体关闭 HUD。

#### Scenario: 刷新限频

- **WHEN** 装置运行
- **THEN** HUD 文本重建频率不超过 5Hz，日志输出频率不超过 0.5Hz

#### Scenario: 自证开销

- **WHEN** 执行装置开销自检
- **THEN** 装置分别在 HUD 开启与关闭下各采样一次并报告两者 GPU 中位数之差

### Requirement: 与主工程解耦、可整体移除

装置 SHALL 位于独立程序集与独立场景，并使用**专属**的 URP Asset 与 Renderer Data 承载 3DGS 的 Renderer Feature。主工程既有程序集 MUST NOT 引用装置，主工程既有渲染配置 MUST NOT 被装置修改。

#### Scenario: 主工程不依赖装置

- **WHEN** 检查主工程既有程序集的引用关系
- **THEN** 无任何一个引用装置程序集或 3DGS 第三方包

#### Scenario: 产品渲染配置不被改动

- **WHEN** 检查既有 Quest / PICO 构建配置所使用的 URP Renderer Data
- **THEN** 其上不存在 3DGS 的 Renderer Feature

#### Scenario: 可整体删除

- **WHEN** 移除装置的程序集目录、场景、专属 URP 资产与第三方包依赖
- **THEN** 主工程仍可编译并正常运行，无残留引用
