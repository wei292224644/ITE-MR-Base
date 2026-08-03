## ADDED Requirements

### Requirement: 双手合十语义触发器

系统 SHALL 提供双手合十（合掌）识别能力：在双手均被追踪且活动策略判定满足条件并保持足够时长后，触发一次合十成立事件；从合十保持散开后触发一次散开事件。该能力 MUST NOT 内含任何业务副作用（不得直接调用圣物、定位、玩法 UI 等业务 API）。

#### Scenario: 合十保持后触发成立事件

- **WHEN** 当前活动策略判定合十条件连续满足不少于配置的 hold 时长
- **THEN** 系统触发恰好一次合十成立事件（`Performed`），且活动策略对应的合十保持状态为真

#### Scenario: 散开触发释放事件

- **WHEN** 合十成立事件已触发，之后活动策略按放宽后的释放阈值不再满足
- **THEN** 系统触发散开事件（`Released`），且合十保持状态为假

#### Scenario: 未保持足够时长不触发

- **WHEN** 活动策略判据短暂满足但未达到 hold 时长即散开
- **THEN** 系统不触发合十成立事件

#### Scenario: 触发器不含业务调用

- **WHEN** 检查合十判定相关实现与依赖
- **THEN** 其仅依赖手部子系统与手势/几何计算，不引用 SacredRelic、Marker 或其他玩法业务类型

### Requirement: 双路径并行求值

系统 SHALL 同时提供两条合十识别路径并在运行时可并行求值：路径 A（JointMath，纯关节几何与弯曲）与路径 B（HandPoseComposite，基于 `XRHandShape`/`XRHandPose` 的双手复合外加腕距条件）。对外边沿事件 MUST 仅由当前选中的活动路径驱动；两条路径的保持/命中状态 MUST 均可被诊断读取。

#### Scenario: 路径 A 可独立命中

- **WHEN** 双手满足路径 A 的几何与弯曲条件并完成 hold
- **THEN** 路径 A 的保持状态为真，无论路径 B 是否命中

#### Scenario: 路径 B 可独立命中

- **WHEN** 左右手姿态满足配置的 Hand Pose/Shape 条件且腕距在阈值内并完成 hold
- **THEN** 路径 B 的保持状态为真，无论路径 A 是否命中

#### Scenario: 活动路径切换影响事件源

- **WHEN** 将活动路径从 A 切换为 B（或反之）
- **THEN** 之后的 `Performed` / `Released` / 对外 `IsHeld` 仅反映新活动路径的状态

#### Scenario: 订阅方不感知路径分裂

- **WHEN** 业务或其它系统只订阅对外 `Performed` / `Released`
- **THEN** 其无需引用路径 A/B 的内部类型即可工作

### Requirement: 几何判据可离机验证（路径 A）

系统 SHALL 将路径 A 的核心几何与弯曲判据暴露为不依赖引擎帧循环的纯函数（或等价静态方法），以便 EditMode 测试注入假姿态与假 curl。

#### Scenario: 标准合掌匹配

- **WHEN** 注入两腕贴近、掌心相对、指尖同向、curl 低于上限的假数据
- **THEN** 路径 A 判据返回匹配

#### Scenario: 过远不匹配

- **WHEN** 两腕距离超过配置上限
- **THEN** 路径 A 判据返回不匹配

#### Scenario: 手背相对不匹配

- **WHEN** 掌心反向平行但朝向背对（手背贴手背）
- **THEN** 路径 A 判据返回不匹配

#### Scenario: 指尖反向不匹配

- **WHEN** 一只手指尖朝上、另一只朝下
- **THEN** 路径 A 判据返回不匹配

#### Scenario: 握拳对撞不匹配

- **WHEN** 两腕姿态似合掌但两侧中指 curl 高于上限
- **THEN** 路径 A 判据返回不匹配

### Requirement: 数据源中立

合十判定 MUST 仅从 `XRHandSubsystem`（经项目统一入口，如 `MRContext.Hands`）读取双手数据。MUST NOT 使用厂商专用手部 API，MUST NOT 依赖 `XRCommonHandGestures`。

#### Scenario: 经统一入口取手

- **WHEN** 运行时进行合十检测
- **THEN** 手部数据来自项目的 `XRHandSubsystem` 缓存/查找路径，而非 `OVRHand` / PICO hand prefab API

#### Scenario: 手部子系统未就绪

- **WHEN** `Hands` 为 null 或任一手未 tracked
- **THEN** 两条路径均不匹配，且不抛异常

### Requirement: 核心场景可订阅

核心场景 SHALL 挂载合十触发器组件实例，使其他系统在运行时能够获取该实例并订阅成立/散开事件。

#### Scenario: 场景中存在实例

- **WHEN** 加载并运行核心场景
- **THEN** 场景中存在可用的合十触发器组件实例

#### Scenario: 订阅方可收到事件

- **WHEN** 某组件在运行时订阅合十成立事件，且活动路径完成一次有效合十
- **THEN** 该订阅回调被调用一次

### Requirement: 诊断 HUD 显示 A/B 命中状态

系统 SHALL 在既有设备内诊断 HUD 上同时显示路径 A 与路径 B 的合十命中/保持状态，以及当前活动事件源，使头显内可直接对比两条路径。该显示 MUST 仅为诊断输出，MUST NOT 触发业务逻辑。

#### Scenario: HUD 可读双路径状态

- **WHEN** 诊断 HUD 处于运行且场景中存在合十触发器实例
- **THEN** HUD 文本中同时包含路径 A、路径 B 的命中状态，以及当前活动路径标识

#### Scenario: 无触发器实例时不崩溃

- **WHEN** 诊断 HUD 运行但场景中找不到合十触发器
- **THEN** HUD 仍正常刷新其他诊断行，不抛异常
