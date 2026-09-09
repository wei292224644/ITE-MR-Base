# ite-tour-space-editor-harness Specification

## Purpose
编辑器验收场景 `IteTourSpace.unity`：自包含、不进出包清单、不依赖 XR。驱动层可整块停用；假扫码走完整标记契约；thirdDemo 的扫码 + 区域触发流程可在桌面走通。
## Requirements
### Requirement: 验收场景自包含且不依赖 XR 设备

`Assets/Scenes/IteTourSpace.unity` SHALL 自带运行 ITE 导览所需的全部装配：一台相机、`AnchorRoot`、其直接子物体 `TourRoot`、装配点、标记桥接、以及编辑器驱动层。

该场景 MUST NOT 依赖 `MRCore.unity` 被先行加载，MUST NOT 依赖任何 XR 设备连接或 XR loader 初始化。

该场景 MUST NOT 出现在 `EditorBuildSettings` 或 `BuildScript` 的场景清单里——它携带桌面相机与假扫码驱动，不是出包产物。

#### Scenario: 直接打开场景按 Play

- **WHEN** 在编辑器中打开 `IteTourSpace.unity` 并进入 Play 模式，未连接任何 XR 设备
- **THEN** 加载链正常开始，控制台 MUST NOT 出现装配不完整或 XR 未初始化导致的中断

#### Scenario: 不污染出包

- **WHEN** 执行真机构建
- **THEN** 产物中不包含 `IteTourSpace.unity`

### Requirement: 编辑器驱动层可整块停用且与宿主装配层分离

场景 SHALL 分为两层：宿主装配层（真机可原样复用）与编辑器驱动层（桌面相机走位、假扫码、调试 HUD）。

停用编辑器驱动层 MUST NOT 影响宿主装配层的任何行为——加载链仍应完整执行，只是没有输入源与可视化。

#### Scenario: 停用驱动层后加载链照常

- **WHEN** 编辑器驱动层的全部组件被停用后进入 Play
- **THEN** 内容仍完整装配、`OnInitialized` 仍触发，只是无扫码输入、无 HUD

### Requirement: 相机装配满足触发体积的物理前提

Unity 的触发回调要求参与的两个碰撞体中至少一个带 Rigidbody。相机侧 SHALL 携带碰撞体与 kinematic Rigidbody，否则区域触发不产生任何事件且不报错。

相机 Transform SHALL 是装配点配置给包的那一台——包以父子链比对识别谁进出体积，不依赖 tag。

#### Scenario: 相机走入触发体积

- **WHEN** 相机在场景中移动并进入某个 tour 的触发体积范围
- **THEN** 该 tour 的区域进入事件被派发

### Requirement: 假扫码走完整的标记契约链路

假扫码 SHALL 通过 `MockObservationSource` 注入观测，经 `MarkerTrackingSession` 派发，再经宿主标记桥接转给 ITE。MUST NOT 直接调用 `IteRuntime.SubmitMarkerScan` 绕过桥接。

`MockObservationSource` 与 `MarkerTrackingSession` 均由**驱动层**构造并持有，再由驱动层主动注入宿主装配点。引用方向 SHALL 是驱动层 → 装配点；装配点 MUST NOT 反向引用驱动层或 mock 源。

驱动层 SHALL 每帧以真实时间推进 `MarkerTrackingSession.Tick`，使丢失滞回逻辑在运行时真实生效。

#### Scenario: 会话由驱动层注入

- **WHEN** 驱动层在场景启动时构造会话并交给装配点
- **THEN** 装配点建立桥接并开始接收观测；装配点自身的字段中 MUST NOT 出现 `MockObservationSource`

#### Scenario: 按键触发一次扫码

- **WHEN** 操作者触发对某个 tourId 的假扫码
- **THEN** 该次观测经会话派发、经桥接剥壳后到达 `SubmitMarkerScan`，其位姿被用于锚定

#### Scenario: 停止投喂后触发丢失

- **WHEN** 停止注入观测超过滞回时长
- **THEN** `MarkerTrackingSession` 派发一次且仅一次 `MarkerLost`

### Requirement: 调试 HUD 摊开加载与激活的内部状态

HUD SHALL 显示：加载进度、空间场景名、已装配的 tourId 列表、当前激活的 tourId、`ScanPrompt` 的状态与其 TourIds、相机当前所在的触发体积集合、以及最近一次标记观测与丢失。

#### Scenario: 未扫码时的提示状态可见

- **WHEN** 加载完成但尚未发生任何扫码
- **THEN** HUD 显示 `ScanPrompt` 为 Visible 且 TourIds 为空

#### Scenario: 激活后提示收起

- **WHEN** 某个 tour 被激活
- **THEN** HUD 显示当前激活的 tourId，且 `ScanPrompt` 为 Hidden

### Requirement: 完整导览流程在编辑器内可被走通

以 `thirdDemo` 为内容，下列序列 SHALL 全部可观察。thirdDemo 的 5 个 tour 沿 Z 轴每 2 米一个、互不重叠，构成可行走的布局。

#### Scenario: 联网首跑

- **WHEN** 无本地缓存、联网进入 Play
- **THEN** 5 个 tour 全部下载解压完成，进度到达 1，`OnInitialized` 触发

#### Scenario: 离线复跑结果一致

- **WHEN** 缓存已就位、断网进入 Play
- **THEN** 加载链完整完成，结果与联网首跑一致

#### Scenario: 未扫码时区域触发不生效

- **WHEN** 加载完成、尚未扫码，相机走入任一触发体积
- **THEN** MUST NOT 有任何 tour 被激活（强制扫码语义）

#### Scenario: 首次扫码激活并渲染

- **WHEN** 假扫 `wm0l5qcn_ibd`
- **THEN** 该 tour 的实体树被构建，其 glb 模型可见，`ScanPrompt` 转为 Hidden

#### Scenario: 区域自动切换

- **WHEN** 已有 tour 激活，相机前行进入 `earyserh_i5x` 的触发体积
- **THEN** 在帧末重选中，前一个 tour 被停用销毁、`earyserh_i5x` 被激活并构建

#### Scenario: 内容元素 UI 渲染

- **WHEN** 假扫 `4kvhqwvp_12f`（`normal` 类型，14 个实体）
- **THEN** 其 10 个富文本面板全部渲染出来，圆角框材质与播放/暂停按钮贴图正确显示

#### Scenario: 二次锚定不重建内容

- **WHEN** 对当前激活的 `regionalTrigger` 类型 tour 再次扫到同一个标记，且其二次锚定许可尚未消耗
- **THEN** 该 tour 被重新锚定到新位姿，其内容 MUST NOT 被销毁重建，二次锚定许可被消耗一次

### Requirement: 未被 thirdDemo 覆盖的组件类型明确不在验收范围

`ComponentRegistry` 注册了 11 个组件类型，thirdDemo 的 5 个 tour 覆盖其中 7 个。`VideoPlane`、`PrimitiveModelRender`、`ApproximateTrigger`、`PlayAudioAction` 不被本 change 验证，且 MUST NOT 通过构造合成内容数据来验证。

#### Scenario: 边界被记录

- **WHEN** 评估本 change 的验收结论
- **THEN** 上述 4 个组件类型的运行时行为视为未验证，需在使用到它们的内容出现时另行验收
