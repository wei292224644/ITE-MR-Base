## Why

ITE 导览目前只在编辑器里跑通：`IteTourSpace.unity` 自带桌面相机、假扫码与屏幕空间 HUD，明确不出包。眼镜端缺的不是装配层——那一层真机可原样复用——而是它周围的四圈：设备内容场景与构建路径、跨场景的相机解析与触发体积的物理前提、真实观测源接入、以及 HMD 内的任何可见反馈。

其中三条是阻塞级：

- **PICO 上扫码激活零可用。** 观测 payload 是 AprilTag 数字 id（`PicoFiducialObservationSource.cs:402`），而宿主桥接只有一个 `******{tourId}******` 正则，每帧都判"不匹配外壳格式，已忽略"。
- **PICO 上防抖从不触发。** `MarkerStabilizer` 是源工程 `AnchorObject` 的逐参数移植（0.05m / 1° / smoothTime 0.01 / 连续 30 帧），而 `t = deltaTime / 0.01` 恒被钳到 1——平滑等于没做，比较的是相邻两帧的原始抖动（PICO 单应解出的位姿抖 2–5°），阈值 1°，计数器永远清零。
- **头显里全程零反馈。** 包广播 `OnLoadProgress` / `OnScanPromptChanged`，宿主只 `Debug.Log`；`IteEditorHud` 是屏幕空间 Overlay，VR 里根本不渲染。真机首跑要下载空间包与 5 个 tour 包，与"卡死"不可区分。

同时 `MRCore.unity` 里残留着一坨 ITE 脚手架（`-- ITE --` / `Tour Anchor` / `Marker Frame Offset` / `Tour Root`），既违反"ITE 运行时不驻留核心场景"，其层级又恰好是 `IteBootstrap.Validate()` 要报错的那条——留着会让人以为真机装配已经就位。

## What Changes

- 新增设备内容场景，Additive 挂在 `MRCore` 上，进 `BuildScript` 场景清单；装配层（AnchorRoot / TourRoot / IteHostBootstrap）抽成 prefab，编辑器场景与设备场景共用同一份，杜绝漂移。
- **BREAKING**（宿主装配约定）：`IteHostBootstrap` 的相机由"序列化引用"改为"序列化覆盖 → 运行时解析 `MRContext.Camera` → 缺失则报错"，因为内容场景引用不到 MRCore 里的 XR Camera。
- ITE rig 在运行时给解析到的相机挂载 Collider + kinematic Rigidbody 并在销毁时撤除——Unity 的触发回调要求两方至少一个带 Rigidbody，真机 XR Camera 上没有。
- 新增设备标记输入层：按平台构造观测源、驱动 `MarkerTrackingSession.Tick`、注入装配点。平台观测源的构造收敛到唯一一个工厂，`MarkerHookTestRig` 一并改用，工程内不再有第二处平台分支。
- **BREAKING**（桥接职责）：payload → tourId 的解析从宿主桥接**移入包内**。宿主只做三件事：稳定、施加标记到内容锚点的固定偏移、把原始 payload + 平台标签 + 位姿原样推给包。理由是二维码内容归内容方所有且形状会变（大概率变成一个地址），解析规则必须与内容同层演进，不能焊在宿主的一条正则上。
- 包内新增标记身份解析：Quest 走 QR 文本解析（当前外壳格式 `******{tourId}******`，形状可换）；PICO 走 `IteSpaceScene.Tour` 上与 `tourID` 平级的 AprilTag ID 字段反查。字段缺失向后兼容，不影响装配。
- 防抖接入 `MarkerStabilizer`，Quest 与 PICO **各一套参数**（阈值、smoothTime、稳定窗口）写进配置资产，附一次真机调参任务把实测值回填；稳定窗口同时由帧计数改为**时间制**——实测两端派发速率差 12.5 倍（PICO 5.6 Hz / Quest 70 Hz），同一个次数阈值在 PICO 上等于举着码不动 5.4 秒。
- 同一帧多张码同时判稳时，桥接只提交**最先判稳**的那个。现状是未定义行为：两个跟踪表都是字典、迭代顺序不保证，而后到者会把先到者刚激活的 tour 停用销毁。
- `PicoFiducialObservationSource` 内按 margin 下限过滤低置信检测。实测出现过场上不存在的假标记（margin 3.8，真检测分布 76.5–99.6），位姿解在相机前 22 cm 且照常派发——在 ITE 上等于激活错误 tour 并把内容锚到人脸前。
- 设备输入层承担应用生命周期（`OnApplicationPause` → 会话 `Pause()/Resume()`）与系统重定位（Quest `trackingOriginUpdated`、PICO `RecenterSuccess`）→ 统一要求重新扫码；Quest 侧另加 `SetAllowRecentering(false)` 加固。
- PICO 的佩戴状态改走原生通道 `PXR_Plugin.System.UserPresenceChangedAction`，不再赌通用输入设备是否上报。
- 接上 `PlatformOffsetConfig`：标记局部坐标系到内容锚点的固定偏移在提交前施加（当前该资产零消费方）。
- 新增 HMD 内世界空间面板：加载进度、`ScanPrompt` 状态、失败可见（下载失败 / `OnTourSceneLoaded` 超时）。归宿主，包继续零 UI 涉入。
- `networkAvailable` 从序列化 bool 改为运行时可达性判定，使已落地的离线自愈路径在现场断网时真的走得进去。
- **BREAKING**：从 `MRCore.unity` 删除全部 ITE 脚手架对象。MRCore 只保留 MR 基座行为（XR 装配、场景切换、手势、诊断），不含任何 ITE 对象。

## Capabilities

### New Capabilities

- `ite-tour-space-device`: 眼镜端的 ITE 导览装配——设备内容场景与构建路径、相机解析与触发体积物理前提、按平台构造的标记输入层与分平台防抖参数、HMD 内的加载与扫码反馈、网络可达性接线。
- `ite-marker-identity`: 标记 payload 到 tourId 的解析归包所有——两端各自的 payload 形状、解析失败的可见忽略、以及 AprilTag ID 在空间场景描述中的绑定位置。

### Modified Capabilities

- `ite-tour-space-host`: 桥接不再解析 payload，改为"稳定 + 偏移 + 原样透传"；相机由注入改为"覆盖 → 解析 → 报错"三段并承担触发体积的物理前提；"ITE 运行时不驻留核心场景"收紧为"MRCore 只含 MR 基座行为，不含任何 ITE 对象"。
- `unified-marker-tracking-contract`: 平台观测源的构造收敛到单一工厂，观测源装配路径上的 `#if MRBASE_*` 只允许出现一处。

## Impact

- 场景：`Assets/Scenes/MRCore.unity`（删 ITE 脚手架）、新增设备内容场景、`Assets/Scenes/IteTourSpace.unity`（改用共享 prefab）。
- 宿主代码：`Assets/Scripts/IteHost/*`（装配点、桥接、设备输入 rig、HMD 面板）、`Assets/Scripts/Localization/Native/*`（观测源工厂、`MarkerHookTestRig` 改用）、`Assets/Scripts/Editor/BuildScript.cs`（场景清单）。
- 包代码：`Packages/com.uality.ite-tour` —— 数据模型加 AprilTag ID 字段、新增标记身份解析、`SubmitMarkerScan` 的入口形状变化。这是本 change 唯一动包的部分。
- 程序集：`MRBase.Ite.Host.asmdef` 新增对 `MRBase.Core` 的引用（为 `MRContext`）。依赖方向仍单向：包对宿主零知识。
- 前置实测：现场标记的实际印制 payload、PICO 的 `userPresence` 上报与否、两端防抖参数，都只能由真机包读出来。

## Open Assumptions

已确认（由人拍板，见 `probe-report.md` 与 design 编号决策）：

- PICO 标记身份走 `IteSpaceScene.Tour` 上与 `tourID` 平级的 AprilTag ID 字段；解析归包（D5 / D7）。
- 防抖两端各一套参数 + 一次真机调参任务；稳定窗口改时间制（D9 / D22）。
- 相机碰撞体由 ITE rig 运行时挂载/撤除（D3）。
- HMD 内 UI 归宿主，不进包（D12）。
- 同帧多码：先判稳的赢（D20）。
- PICO 假标记在观测源内按 margin 过滤，收进本 change（D21）。
- 应用生命周期与系统重定位由设备 rig 承担，重定位统一触发强制重扫（D17 / D18）。
- PICO 佩戴状态走 PXR 原生通道（D19）。
- 扫描常开的代价先量后定，本 change 不落降频策略（D23）。
- `sceneName` 是部署期配置；"系统级配置菜单（重下载 + 切场景）"是后续独立 change（D24）。
- 区域自动切换的累计漂移按现有 tour space 逻辑，不补偿、不加验收门槛（D25）。

仍为假设：

- [ASSUMED] Quest 的 QR 印制格式当前仍是 `******{tourId}******`；本 change 含一次前置真机测量来核实，解析器形状按"会变成一个地址"预留。
- [ASSUMED] PICO 在 Android 生命周期恢复后是否需要重开 4U 相机会话未验证（归档实测只覆盖应用内 `Pause()/Resume()`）。
- [ASSUMED] `OpenXRSettings.AllowRecentering` 的原生默认值未知，工程内无序列化该设置。
- [ASSUMED] margin 下限 20 取自单次实测的双峰间隔，阈值做成可调字段。
- [ASSUMED] `PlatformOffsetConfig` 接上而非删除；偏移在提交给包之前施加。
- [ASSUMED] 本 change 只做适配与可见性，不做性能/内存优化；glTFast 纹理内存与 TMP 中文字体只做一次真机实测并记录结论。
- [ASSUMED] 设备场景与编辑器场景共用同一份装配 prefab，两者的差异只在输入层与 UI 层。
