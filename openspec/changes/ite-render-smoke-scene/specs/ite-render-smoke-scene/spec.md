## ADDED Requirements

### Requirement: 渲染冒烟场景自包含且可在编辑器内独立运行

`Assets/Scenes/IteRenderTest.unity` SHALL 自带运行 ITE 内容管线所需的全部装配：一台相机、`AnchorRoot`、`TourRoot`、以及持有 `IteHostBootstrap` 的 `ITE Host`。该场景 MUST NOT 依赖 `MRCore.unity` 被先行加载，MUST NOT 依赖任何 XR 设备连接。

该场景 MUST NOT 出现在 `EditorBuildSettings` 或 `BuildScript` 的场景清单里——它是编辑器验收面，不是出包产物。

#### Scenario: 直接打开场景按 Play

- **WHEN** 在编辑器中打开 `IteRenderTest.unity` 并进入 Play 模式，未连接任何 XR 设备
- **THEN** `IteRuntime.Create` 返回非 null，加载链正常开始，控制台 MUST NOT 出现 `[ITE] 装配不完整` 或 `XR loader 未初始化` 导致的中断

#### Scenario: 不污染出包

- **WHEN** 执行 `MRBase/Build/Quest` 或 `MRBase/Build/Pico`
- **THEN** 产物中不包含 `IteRenderTest.unity`

### Requirement: ITE 运行时不驻留核心场景

`MRCore.unity` MUST NOT 包含 `IteHostBootstrap` 或任何 ITE 运行时装配点。ITE 导览是内容，其装配点 SHALL 由内容场景持有。

#### Scenario: 核心场景启动不触发 ITE 联网

- **WHEN** 加载 `MRCore.unity` 并进入 Play 模式
- **THEN** 控制台 MUST NOT 出现任何 `[ITE]` 或 `[IteTour]` 前缀的日志，MUST NOT 发起对 `ite-spatial-config.uality.cn` 的请求

### Requirement: 显式激活器把 tour 内容建出来

场景 SHALL 包含一个激活组件，在 `IteRuntime.OnInitialized` 之后对配置的 tourId 调用一次 `IteRuntime.ActivateTour(tourId)`。

该组件 MUST NOT 依赖标记扫码、区域触发或 `alwaysDisplayed` 显示类型——`thirdDemo` 的 tour 无一属于后者，不显式激活则实体树不会被构建，且不产生任何错误输出。

#### Scenario: 激活后实体树被构建

- **WHEN** 加载链完成且激活器调用 `ActivateTour("wm0l5qcn_ibd")`
- **THEN** `IteRuntime.OnTourSceneLoaded` 以该 tourId 触发，`TourRoot` 之下出现该 tour 的实例，其实体子树非空

#### Scenario: 找不到 tour 时可见地失败

- **WHEN** 激活器配置的 tourId 不在已装配的 tour 列表中
- **THEN** `ActivateTour` 返回 false，激活器 SHALL 输出一条错误日志指明该 tourId

### Requirement: 加载链各阶段可从日志区分

激活器 SHALL 以统一前缀输出加载链的分段状态，至少覆盖：进度（`OnLoadProgress`）、场景描述已解析（`OnSpaceSceneLoaded`）、全部 tour 装配完成（`OnInitialized`）、实体树构建完成（`OnTourSceneLoaded`）。

`IteTourObject.Enable()` 由 `TourDirector.Activate` 以 fire-and-forget 方式调用，其内部异常不会冒泡。因此激活器 SHALL 在调用 `ActivateTour` 后若在超时时限内未收到 `OnTourSceneLoaded`，输出一条错误日志。

#### Scenario: 缺资源时不被误判为渲染失败

- **WHEN** 实体树已构建（`OnTourSceneLoaded` 已触发）但模型不可见
- **THEN** 日志足以表明链路走到了构建完成这一步，问题范围收敛到资源加载或渲染本身，而非下载或装配

#### Scenario: 构建实体树抛异常时可见

- **WHEN** `CreateTourScene` 内部抛出异常，`OnTourSceneLoaded` 因此不触发
- **THEN** 激活器在超时后输出错误日志，MUST NOT 静默停留

### Requirement: 离线运行，内容取自本地缓存

场景的 `IteHostBootstrap.networkAvailable` SHALL 为 false，加载链 MUST NOT 在 Play 期间发起任何网络请求。内容缓存的落盘布局与准备步骤 SHALL 有文档，精确到文件级。

#### Scenario: 断网可运行

- **WHEN** 在无网络连接的机器上按 `cache-layout.md` 摆好缓存并进入 Play
- **THEN** 加载链正常完成并渲染出 tour 内容

#### Scenario: 缓存缺失时的失败可归因

- **WHEN** 缓存目录中缺少空间场景描述
- **THEN** 抛出的异常信息包含场景名，可与"渲染失败"区分开
