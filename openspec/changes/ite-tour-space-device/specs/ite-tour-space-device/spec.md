## ADDED Requirements

### Requirement: 设备内容场景经 MRCore 启动并出包

眼镜端的 ITE 导览 SHALL 由一个内容场景承载，经 `MRSceneDirector` 以 `LoadSceneMode.Additive` 加载在常驻的 `MRCore.unity` 之上，并出现在 `BuildScript` 的场景清单中。

该场景 MUST NOT 携带桌面相机、假扫码或屏幕空间 HUD——那是编辑器验收场景的装备。

装配层（`AnchorRoot`、其直接子物体 `TourRoot`、`IteHostBootstrap`）SHALL 由一份 prefab 提供，编辑器验收场景与设备场景共用同一份实例。两者的差异 SHALL 只存在于输入层与 UI 层。

#### Scenario: 真机启动进入导览

- **WHEN** 在头显上启动构建产物
- **THEN** `MRCore` 先完成 XR 装配，设备内容场景被加性加载，ITE 加载链开始执行

#### Scenario: 装配层不可能在两个场景之间漂移

- **WHEN** 修改装配 prefab 的层级或字段
- **THEN** 编辑器验收场景与设备场景同时生效，MUST NOT 需要分别修改

#### Scenario: 编辑器场景不进出包

- **WHEN** 执行真机构建
- **THEN** 产物中不包含 `IteTourSpace.unity`

### Requirement: 相机的碰撞体由 ITE 装配自行挂载并撤除

Unity 的触发回调要求参与的两个碰撞体中至少一个带 Rigidbody。真机的 XR 相机上没有。

设备装配 SHALL 在解析到相机后为其挂载 trigger 碰撞体与 kinematic `Rigidbody`，并在自身销毁时**只**撤除由它挂上的那些组件，已存在的组件 MUST NOT 被移除。

`MRCore.unity` 的 XR 相机 MUST NOT 常驻这两个组件——那会让所有内容场景为 ITE 买单。

装配时 SHALL 校验相机所在 layer 与触发体积所在 layer 在碰撞矩阵中互相碰撞，不满足时输出错误日志。该条件不满足时区域触发永不产生事件且不报任何错。

#### Scenario: 相机走入触发体积

- **WHEN** 佩戴者走入某个 tour 的触发体积
- **THEN** 该 tour 的区域进入事件被派发

#### Scenario: 导览卸载后相机还原

- **WHEN** 设备内容场景被卸载
- **THEN** 相机上由本装配挂载的碰撞体与 Rigidbody 被移除，其原有组件保持不变

#### Scenario: 碰撞矩阵不满足时可见地失败

- **WHEN** 相机所在 layer 与触发体积 layer 在碰撞矩阵中不互相碰撞
- **THEN** 装配 SHALL 输出一条指明该约束的错误日志

### Requirement: 设备标记输入层构造观测源并注入会话

设备输入层 SHALL 承担：按平台取得观测源、构造 `MarkerTrackingSession`、每帧以真实时间推进 `Tick`、把会话注入装配点。引用方向 SHALL 是输入层 → 装配点。

观测源 SHALL 经统一工厂取得（见 `unified-marker-tracking-contract`），输入层自身 MUST NOT 出现 `#if MRBASE_*`。

平台未配置、平台 SDK 缺失、观测源打开失败三种情况 SHALL 分别输出可区分的错误日志。

#### Scenario: Quest 上取得 MRUK 观测源

- **WHEN** 在 Quest 构建下启动设备内容场景
- **THEN** MRUK 运行时被显式装配，Quest 观测源被创建并订阅成功，日志中可见订阅成功

#### Scenario: PICO 上取得 AprilTag 观测源

- **WHEN** 在 PICO 构建下启动设备内容场景
- **THEN** PICO 相机会话被打开，AprilTag 观测源开始产出检测批次

#### Scenario: 未注入会话时导览仍可加载

- **WHEN** 输入层因平台未配置而停用
- **THEN** 加载链仍完整执行到 `OnInitialized`，触发体积仍被开启，装配点输出"未接入标记源"的日志，MUST NOT 报错中断

### Requirement: 位姿防抖参数按平台各存一套且可调

标记位姿在两端的噪声特性不同：Quest 的 trackable 由空间锚背书，PICO 的位姿由单应解出、相邻两帧可抖 2–5 度。

宿主 SHALL 在把扫码推给包之前经 `MarkerStabilizer` 稳定，其位置阈值、角度阈值、平滑时间常数与稳定窗口 SHALL 按平台各存一套，且 SHALL 是可在不改代码的前提下调整的配置项。

平滑时间常数 SHALL 大于典型帧间隔，否则平滑退化为逐帧跳变、稳定判定永不成立。

稳定窗口 SHALL 以**时间**表达，MUST NOT 以观测次数表达。两端的派发速率相差一个数量级（实测 PICO 5.6 Hz、Quest 70 Hz），同一个次数阈值在两端是完全不同的等待时长；且同一条链路上的丢失滞回本就是时间制，两个判据不应量纲不一致。

两端 SHALL 走同一条代码路径，差异只在参数。

#### Scenario: 稳定后才提交一次扫码

- **WHEN** 某个标记进入视野并持续可见
- **THEN** 位姿在满足稳定条件后向包提交一次扫码，持续可见期间 MUST NOT 反复提交

#### Scenario: 丢失后重新出现按新一轮处理

- **WHEN** 该标记被判定丢失后再次出现并重新稳定
- **THEN** 向包再提交一次扫码

#### Scenario: 二次锚定回到人为动作

- **WHEN** 某个 `regionalTrigger` 类型的 tour 已被激活，且其标记持续留在视野内
- **THEN** 其二次锚定许可 MUST NOT 被消耗；只有标记丢失后重新扫到才消耗

#### Scenario: 参数按平台生效

- **WHEN** 在 Quest 与 PICO 上分别运行
- **THEN** 各自采用本平台那套参数，MUST NOT 共用一组阈值

### Requirement: 应用生命周期变化时暂停与恢复标记会话

头显上"摘下"不只是佩戴状态变化：系统会挂起应用或灭屏。设备输入层 SHALL 在应用暂停时 `Pause()` 标记会话、恢复时 `Resume()`。

不这么做的后果是：暂停期间缺席时长照常累计，恢复后立刻派发一串虚假的 `MarkerLost`。

佩戴状态的读取 SHALL 使用平台提供的通道；PICO 上 SHALL 使用其原生佩戴状态事件，MUST NOT 依赖通用输入设备是否恰好上报。

#### Scenario: 灭屏恢复后不产生虚假丢失

- **WHEN** 应用被系统挂起数秒后恢复
- **THEN** MUST NOT 因暂停期间的缺席而派发 `MarkerLost`；标记仍在视野时照常继续观测

#### Scenario: PICO 上佩戴状态可读

- **WHEN** 在 PICO 上摘下并戴回头显
- **THEN** 佩戴状态变化被读到并推给包，MUST NOT 落到"平台不上报"的兜底路径

### Requirement: 追踪原点变化后要求重新扫码

锚定把世界位姿写入 `AnchorRoot`。系统重定位改变追踪空间原点后，相机在世界中的位姿整体偏移，而内容不动——内容与实物错开，且不产生任何日志。

设备装配 SHALL 订阅平台的重定位通知，收到后要求重新扫码（与"戴上头显"同一语义：上一次锚定不再可信），并在 HMD UI 中提示。

Quest 侧 SHALL 同时显式关闭"世界原点跟随系统重定位"，作为加固。

#### Scenario: 重定位后进入强制扫码

- **WHEN** 佩戴者触发系统重定位
- **THEN** 当前锚定被视为失效，导览进入强制扫码状态，HMD UI 提示重新扫码

#### Scenario: 重定位不静默错位

- **WHEN** 重定位发生
- **THEN** MUST NOT 出现"内容悄悄偏在墙里或身后而无任何提示"的状态

### Requirement: 标记到内容锚点的固定偏移在提交前施加

标记的局部坐标系原点通常不是内容应当出现的位置，且两端的标记坐标系约定不同。宿主 SHALL 在稳定之后、推给包之前施加按平台配置的固定偏移。

偏移缺省为 identity，此时行为与不施加偏移等价。

#### Scenario: 偏移生效

- **WHEN** 配置了非 identity 的平台偏移，扫到位姿 P
- **THEN** 推给包的位姿是 P 施加该偏移之后的结果

#### Scenario: 缺省偏移不改变行为

- **WHEN** 平台偏移为 identity
- **THEN** 推给包的位姿与稳定后的标记位姿一致

### Requirement: 加载与扫码状态在头显中可见

宿主 SHALL 提供世界空间 UI，渲染：加载进度与空间场景名、`ScanPrompt` 的状态与其 TourIds、以及失败的可见化（下载失败、`OnTourSceneLoaded` 超时）。

该 UI MUST NOT 使用屏幕空间 Overlay——它在头显中不渲染。

该 UI SHALL 属于宿主，MUST NOT 进入 `com.uality.ite-tour` 包，也 MUST NOT 挂在 `MRCore.unity` 上。

#### Scenario: 首跑下载过程可见

- **WHEN** 无本地缓存、真机首次启动
- **THEN** 头显中可见加载进度推进与当前空间场景名

#### Scenario: 未扫码时提示可见

- **WHEN** 加载完成但尚未发生任何扫码
- **THEN** 头显中可见扫码提示

#### Scenario: 失败可见

- **WHEN** 某个 tour 在超时时限内未收到 `OnTourSceneLoaded`
- **THEN** 头显中出现可见的失败提示，MUST NOT 只停留在日志里

### Requirement: 网络可达性在运行时判定

`IteRuntime` 的联网判定 SHALL 由运行时的网络可达性提供，MUST NOT 由构建期固定的序列化开关决定。SHALL 保留注入入口，供测试与强制离线调试使用。

#### Scenario: 现场断网走离线路径

- **WHEN** 真机启动时无网络、本地缓存已就位
- **THEN** 加载链走离线路径完整完成，MUST NOT 发起网络请求

#### Scenario: 有网时走联网路径

- **WHEN** 真机启动时网络可达
- **THEN** 版本查询与下载照常进行

### Requirement: ITE 加载链在 XR 就绪之后启动

设备装配 SHALL 在 XR 就绪（相机可解析、观测源可打开）之后再触发 `IteRuntime.StartAsync`。MUST NOT 以"重试直到相机出现"的方式掩盖装配错误——那让"还在等"与"配错了"不可区分。

编辑器验收场景保持自启动，不受本条约束。

#### Scenario: XR 就绪后开始加载

- **WHEN** 真机启动，XR 装配完成
- **THEN** ITE 加载链随后开始，日志顺序可见

#### Scenario: XR never 就绪时可见地失败

- **WHEN** XR 装配未能完成
- **THEN** 装配点 SHALL 输出错误日志并停用，MUST NOT 静默停留在等待中

### Requirement: 真机验收序列与编辑器验收一一对应

以 `thirdDemo` 为内容，编辑器验收的序列 SHALL 在两端真机上逐条重跑，差异只在输入是真实标记：联网首跑、离线复跑、未扫码时区域触发不生效、首次扫码激活并渲染、区域自动切换、内容元素 UI 渲染、二次锚定不重建内容。

任何一项跑不通时，事件序列 SHALL 足以指出问题落在哪一层，而不是"真机上不行"。

#### Scenario: 首次扫真码激活并锚定

- **WHEN** 在真机上扫到某个 tour 的实际标记
- **THEN** 该 tour 被激活、内容被构建、其世界位姿落在标记位姿（含配置偏移）上

#### Scenario: 区域自动切换

- **WHEN** 已有 tour 激活，佩戴者走入另一个 tour 的触发体积
- **THEN** 在帧末重选中，前一个 tour 被停用销毁、新 tour 被激活并构建

#### Scenario: 扫描开销被量到

- **WHEN** 在 PICO 上运行导览
- **THEN** SHALL 记录扫描开启与关闭两种状态下的帧率与机身温升，作为后续是否引入降频策略的依据；检测采样率与降采样参数 SHALL 是部署期可调项

#### Scenario: 失败可归因

- **WHEN** 某项验收未通过
- **THEN** 日志中的事件序列足以区分：下载失败、装配失败、解析失败、稳定未触发、区域触发未产生事件
