# ite-tour-space-host Specification

## Purpose
ITE 导览在 `MR_Base` 中的宿主装配：锚定层级校验、标记事件桥接、触发体积默认开启、加载/激活事件转发。装配层与观测源解耦，运行时不驻留核心场景。
## Requirements
### Requirement: 锚定所依赖的场景层级必须被显式约束并校验

扫码锚定的数学是这样闭合的：`ChangeTourObjectTransform` 把 `TourRoot` 的 local 变换设为"被扫中 tour 的 local 矩阵的逆"，再把 `AnchorRoot` 的 local 位姿设为扫到的位姿；两者相乘后被扫中的 tour 恰好落在 `AnchorRoot` 上，其余 tour 保持相对布局。

该恒等式成立有两个前提：

- `TourRoot` MUST 是 `AnchorRoot` 的**直接子物体**。中间夹任何带非单位变换的节点，等式即破。
- `AnchorRoot` 的父级 MUST 处于世界原点、无旋转、无缩放（`AnchorRoot` 为根级物体即满足）。位姿是以 `SetLocalPositionAndRotation` 写入的，而标记位姿是**世界**位姿，父级上的任何变换都会被二次施加。

两条前提违反时不会产生任何运行时错误，只会让内容出现在错误的位置。因此装配点 SHALL 在初始化时校验这两条并在不满足时输出错误日志。

#### Scenario: 层级不满足时可见地失败

- **WHEN** 装配时 `TourRoot` 不是 `AnchorRoot` 的直接子物体
- **THEN** 装配点 SHALL 输出一条指明该约束的错误日志

#### Scenario: 锚点父级带变换时可见地失败

- **WHEN** 装配时 `AnchorRoot` 的父级存在非单位的位移、旋转或缩放
- **THEN** 装配点 SHALL 输出一条指明该约束的错误日志

#### Scenario: 层级满足时扫中的 tour 落在标记位姿上

- **WHEN** 层级满足约束，标记位姿为 P，扫到的标记对应 tour T
- **THEN** T 的世界位姿等于 P，其余 tour 相对 T 的位置关系与空间场景描述中一致

### Requirement: 标记事件到 ITE 的桥接发生在业务层

`MarkerTrackingSession` 只派发带平台标签与原始 payload 的观测，不做任何身份解析（见 `unified-marker-tracking-contract`）。而 `TourScanPolicy` 是以 payload 与 tourId 做**字面比对**，中间没有任何解析。

宿主 SHALL 提供一个桥接组件，订阅 `MarkerTrackingSession.MarkerObserved`，把 `RawPayload` 转换为 tourId 后调用 `IteRuntime.SubmitMarkerScan`。该转换 MUST NOT 出现在 `MarkerTrackingSession` 或任何 `IMarkerObservationSource` 实现内。

payload 的外壳格式 SHALL 可配置，默认沿用源工程的 `******{tourId}******`。

#### Scenario: 合规 payload 被转为 tourId

- **WHEN** 会话派发 `RawPayload` 为 `"******wm0l5qcn_ibd******"` 的观测
- **THEN** 桥接以 `markerId = "wm0l5qcn_ibd"` 与该观测的位姿调用 `SubmitMarkerScan`

#### Scenario: 不合规 payload 被忽略且可见

- **WHEN** 会话派发 `RawPayload` 不匹配外壳格式的观测（例如 `"250"`）
- **THEN** 桥接 MUST NOT 调用 `SubmitMarkerScan`，且 SHALL 输出一条包含该原始 payload 的日志

#### Scenario: 剥壳后不在场景中的 tourId

- **WHEN** 剥壳得到的 tourId 不属于已装配的任何 tour
- **THEN** `SubmitMarkerScan` 正常调用并被 `TourScanPolicy` 判为忽略，桥接 SHALL 输出一条日志指明该 tourId 与当前可用的 tourId 列表

### Requirement: 触发体积在装配完成时默认开启，宿主可覆盖

`IteTourObject.CreateTourObject` 结束时触发体积处于停用状态，`ChangeDisplayType` 只负责为 `alwaysDisplayed` 关闭体积。若无人开启，区域触发链路完全不工作且不产生任何错误输出——这正是当前状态：开启入口存在但零调用方。

包 SHALL 在加载链完成时（`OnInitialized` 之后、进入强制扫码状态之前）默认开启全部非 `alwaysDisplayed` tour 的触发体积。宿主 MUST NOT 需要为了让区域触发工作而额外调用任何东西。

包 SHALL 同时暴露开关，使宿主能够延后开启或临时关闭（例如加载页仍覆盖视野时不希望区域触发抢走控制权）。

#### Scenario: 装配完成后体积默认可用

- **WHEN** `OnInitialized` 已触发，宿主未做任何额外调用
- **THEN** 全部非 `alwaysDisplayed` 的 tour 的触发体积处于启用状态

#### Scenario: 宿主可关闭

- **WHEN** 宿主调用开关将触发体积关闭
- **THEN** 相机进出体积 MUST NOT 产生任何区域事件；再次开启后恢复

#### Scenario: 相机进入体积产生事件

- **WHEN** 装配点配置的相机（其碰撞体带 Rigidbody）进入某个 tour 的触发体积
- **THEN** 该 tour 的区域进入事件被派发给 director

### Requirement: 加载与激活的全过程可从宿主侧观察

宿主 SHALL 转发 `IteRuntime` 的全部对外事件：`OnLoadProgress`、`OnSpaceSceneLoaded`、`OnSpaceSceneAssetsLoaded`、`OnInitialized`、`OnTourActivated`、`OnTourDeactivated`、`OnTourSceneLoaded`、`OnScanPromptChanged`。

`TourDirector.Activate` 以 fire-and-forget 方式调用 `IteTourObject.Enable()`，其内部异常不会冒泡。因此宿主 SHALL 在 `OnTourActivated` 之后若在超时时限内未收到对应 tourId 的 `OnTourSceneLoaded`，输出一条错误日志。

#### Scenario: 实体树构建失败时可见

- **WHEN** `CreateTourScene` 内部抛出异常，`OnTourSceneLoaded` 因此不触发
- **THEN** 宿主在超时后输出错误日志，MUST NOT 静默停留

#### Scenario: 缺资源时不被误判为渲染失败

- **WHEN** `OnTourSceneLoaded` 已触发但模型不可见
- **THEN** 事件序列足以表明链路走到了构建完成这一步，问题范围收敛到资源加载或渲染本身，而非下载或装配

### Requirement: ITE 运行时不驻留核心场景

`MRCore.unity` MUST NOT 包含 `IteHostBootstrap` 或任何 ITE 运行时装配点。ITE 导览是内容，其装配点 SHALL 由内容场景持有。

#### Scenario: 核心场景启动不触发 ITE 联网

- **WHEN** 加载 `MRCore.unity` 并进入 Play 模式
- **THEN** 控制台 MUST NOT 出现任何 `[ITE]` 或 `[IteTour]` 前缀的日志，MUST NOT 发起对 `ite-spatial-config.uality.cn` 的请求

### Requirement: 宿主装配层与输入源解耦

宿主装配层（装配点、标记桥接、触发体积开启时机、事件转发）MUST NOT 依赖任何具体的 `IMarkerObservationSource` 实现，也 MUST NOT 依赖相机由何种方式驱动。更换观测源或相机装配 SHALL 不需要修改装配层代码。

依赖方向 SHALL 是单向的：`MarkerTrackingSession` 的构造与其观测源的选择归**输入层**所有，装配层只提供注入入口。装配点 MUST NOT 自行构造会话或持有观测源的序列化引用——在 `MonoBehaviour` 上，可序列化字段只能是具体类型，那等于把某个观测源实现焊进装配层。

#### Scenario: 更换观测源

- **WHEN** 把 `MockObservationSource` 换成 `QuestObservationSource` 或 `PicoFiducialObservationSource`
- **THEN** 装配点、标记桥接与事件转发的代码 MUST NOT 需要修改

#### Scenario: 会话由外部注入

- **WHEN** 审阅装配点的字段与构造逻辑
- **THEN** 其中 MUST NOT 出现任何 `IMarkerObservationSource` 具体实现类型，会话只能经由注入入口传入

#### Scenario: 未注入会话时导览仍可加载

- **WHEN** 没有任何输入层向装配点注入会话
- **THEN** 加载链仍完整执行到 `OnInitialized`，触发体积仍被开启，只是没有扫码输入；装配点 SHALL 输出一条说明"未接入标记源，扫码激活不可用"的日志，MUST NOT 报错中断
