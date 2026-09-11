## Context

编辑器验收已跑通（`ite-tour-space-editor-harness`）：`IteTourSpace.unity` 自带桌面相机 + 假扫码 + 屏幕空间 HUD，5 个 thirdDemo tour 的下载、装配、扫码激活、区域切换、UI 渲染全部可观察。装配层（AnchorRoot / TourRoot / `IteHostBootstrap`）按 `ite-tour-space-host` 的约定写成"真机可原样复用"。

眼镜端要补的是它周围四圈，各自的当前状态：

| 圈 | 当前 | 真机上的表现 |
|---|---|---|
| 场景与构建 | `IteTourSpace.unity` 明确不出包；`BuildScript` 场景清单只有 MRCore / MarkerHookTest / GsplatBench | 没有可装的包 |
| 相机 | `IteHostBootstrap.xrCamera` 是序列化 `Transform`；编辑器场景手挂 Collider + kinematic RB | 内容场景引用不到 MRCore 的 XR Camera → `IteRuntime.Create` 返回 null；即便接上，无 Rigidbody 则区域触发零事件且不报错 |
| 标记输入 | 只有 `MockObservationSource`；`IteMarkerBridge` 单条正则 | PICO payload 是 AprilTag 数字 id，每帧判"不匹配外壳格式"；防抖参数在 PICO 上从不触发 |
| HMD 内反馈 | 宿主只 `Debug.Log`；`IteEditorHud` 是 Overlay | 首跑下载全程零反馈 |

两处历史遗留同时清掉：`MRCore.unity` 里的 ITE 脚手架（`-- ITE --` / `Tour Anchor` / `Marker Frame Offset` / `Tour Root`，且 `Tour Root` 的父级是 `Marker Frame Offset`，正是 `IteBootstrap.Validate()` 要报错的层级）；`PlatformOffsetConfig` 零消费方。

约束：包对宿主零知识（依赖单向）；平台分支只允许出现在收敛点；`MRCore` 只承担 MR 基座行为。

## Goals / Non-Goals

**Goals:**

- Quest 与 PICO 两端，从 MRCore 启动、加性加载内容场景、完成下载装配、扫真码激活并正确锚定、区域触发切换，全过程在头显里可见。
- 标记身份解析与内容同层演进：二维码内容形状变化（大概率变成一个地址）不需要改宿主。
- 平台分支在观测源装配路径上只有一处。
- 装配层在编辑器场景与设备场景之间不可能漂移。

**Non-Goals:**

- 性能与内存优化。glTFast 运行时纹理、TMP 中文字体图集、5 个 tour 的常驻开销只做一次真机实测并记录结论，不在本 change 内调优。
- `VideoPlane` / `PrimitiveModelRender` / `ApproximateTrigger` / `PlayAudioAction` 四个组件类型仍不在验收范围（沿用编辑器验收的边界）。
- 摘下头显强制重扫的产品语义不改，只做实证记录。
- ITE 后端与内容生产流程不改；包只按新增字段反查，字段缺失照常工作。

## Decisions

### D1 设备场景与编辑器场景共用同一份装配 prefab

装配层做成 prefab（AnchorRoot → TourRoot + `IteHostBootstrap`），两个场景各放一个实例；差异只在输入层（假扫码 vs 设备观测源）与 UI 层（Overlay HUD vs 世界空间面板）。

**替代方案**：设备场景照着编辑器场景摆一遍。**否决理由**：两份摆放会各自演化，而锚定层级的错误不报错、只让内容出现在错误位置——恰恰是最难在真机上归因的一类。共用 prefab 让 `Validate()` 的两条前提只需要保证一次。

### D2 相机由"序列化覆盖 → 运行时解析 → 报错"三段解析

`IteHostBootstrap` 保留序列化字段作为**覆盖**（编辑器场景用桌面相机）；为空时解析 `MRContext.Instance.Camera`；两者都拿不到则报错并停用，不进入 `IteRuntime.Create`。`MRBase.Ite.Host.asmdef` 为此新增对 `MRBase.Core` 的引用——宿主适配层本就是唯一同时认识两边的程序集。

**替代方案**：把 XR Camera 做成跨场景引用 / 让内容场景自带一台相机。**否决理由**：Unity 不支持跨场景序列化引用；内容场景自带相机会与 XR Origin 的相机打架，且 `MRContext` 的注释已经写明 `Camera.main` 在多相机场景里不可靠。

### D3 触发体积所需的碰撞体由 ITE rig 运行时挂载并在销毁时撤除

在解析到相机后，由输入层给它 `AddComponent<SphereCollider>`（isTrigger）+ kinematic `Rigidbody`，`OnDestroy` 时移除自己加的那两个（只移除自己加的，已存在的不动）。

**替代方案 A**：常挂在 MRCore 的 XR Camera 上。**否决理由**：与"MRCore 只含 MR 基座行为"直接冲突，且所有内容场景都为 ITE 买单。
**替代方案 B**：绕开物理，每帧做点-盒判定。**否决理由**：包已有 `TourVolumeTrigger` 走物理回调，绕开等于在宿主重写一套区域判定，是重写不是适配。

装配时同时校验：相机所在 layer 与触发体积 layer 在碰撞矩阵里互相碰撞。这条错了不报错，只是永不触发——必须出声。

### D4 平台观测源的构造收敛到唯一工厂

新增 `MarkerSourceFactory.Create(GameObject host, out string detail)`，承担全部 `#if MRBASE_QUEST / MRBASE_PICO` 分支，含 Quest 侧的 `QuestMrukRuntimeInstaller.EnsureInitialized`（加性加载的场景里没有任何自动装配钩子赶得上，MRUK 必须显式装配，真机已复现）。`MarkerHookTestRig` 改用同一工厂。

**替代方案**：ITE 设备 rig 照抄 `MarkerHookTestRig.Awake` 的分支。**否决理由**："照搬更快"不是理由。两处平台分支意味着 MRUK 装配、SDK 缺失报错、平台未配置报错三条路径要各维护一份，而它们的差异只在真机上显形。

### D5 payload → tourId 的解析移入包内，宿主只做"稳定 + 偏移 + 透传"

包的入口从 `SubmitMarkerScan(markerId, pose)` 改为接受**原始 payload + 标记种类 + 位姿**；解析在包内完成。宿主 `IteMarkerBridge` 退化为：喂 `MarkerStabilizer` → 施加 `PlatformOffsetConfig` 的固定偏移 → 原样推给包。

**理由**：二维码内容归内容方所有，且形状会变（大概率变成一个地址）。解析规则与内容同层演进，才能在内容改版时不动宿主。这也与 `unified-marker-tracking-contract` 已有的"载荷只带平台标签与原始 payload，不做身份收敛"一致——会话层不解析，现在解析点从宿主再下沉一层到包。

**替代方案**：保留宿主正则并加一个平台分流。**否决理由**：把一个属于内容的决策焊在宿主的一条正则上；内容方每改一次码，宿主就要出一次包。

**代价**：包的公开面变化，编辑器场景的假扫码与相关 EditMode 测试同步改。本 change 内一起改完。

### D6 包内的标记种类用包自有枚举，不认识 `MarkerPlatform`

`MarkerPlatform` 是 `MRBase.Localization` 的类型，包引用它即破坏"包对宿主零知识"。包内定义自己的枚举（QrText / AprilTagId），宿主在桥接处做一次映射。

**替代方案**：传字符串 kind。**否决理由**：字符串拼错不报错，只是永远解析不出——正是本 change 要消灭的那类静默失败。

### D7 AprilTag ID 落在 `IteSpaceScene.Tour` 上，与 `tourID` 平级

per-tour 一个字段专门描述该 Tour 的 AprilTag ID，随 tour 增删自动同步。字段缺失时该 tour 只是不参与 AprilTag 反查，**不影响装配**——沿用 `isEnabled` 的缺省语义（JSON 省略仍装配）。

**替代方案 A**：场景级 `tagId → tourId` 映射表。**否决理由**：与 `tours` 数组可能不同步，多一层一致性校验。
**替代方案 B**：单独一份 json。**否决理由**：多一条下载/缓存/版本链路要养，而收益只是"换码不重发内容包"。

### D8 Quest 侧解析器按"形状会变"设计

当前实现按外壳格式 `******{tourId}******` 解析，但解析入口按可替换写：输入一段文本，输出 tourId 或"无法解析"。将来变成地址时只换这一处实现，不动桥接、不动会话、不动装配。

真机印制格式尚未核实——本 change 含一次前置测量（用现有 `MarkerHookTest` 包读一次现场码的 rawPayload），测量结果决定解析器首版实现。

### D9 防抖两端各一套参数，且修掉 smoothTime 的失效

`MarkerStabilizer` 的默认值（0.05m / 1° / smoothTime 0.01 / 30 帧）是源工程 `AnchorObject` 的逐参数移植。它在 PICO 上不成立的机理是：`t = Mathf.Clamp01(deltaTime / smoothTime)`，`deltaTime ≈ 0.014`、`smoothTime = 0.01` → `t` 恒为 1，`SmoothedPose` 每帧跳到目标，于是"平滑位姿 vs 目标位姿"的差就等于相邻两帧的原始抖动（2–5°），永远大于 1° 阈值，计数器永远清零，`Stabilized` 一次都不触发——外部看到的就是"扫不到"。

因此：阈值、smoothTime、稳定窗口三项**按平台各存一套**（配置资产），并排一次真机调参任务把实测值回填。Quest 侧 MRUK 的 trackable 由空间锚背书，预期参数远松于 PICO；两端仍走同一条代码路径，只是参数不同——避免"故障只能在各自真机上复现"。

稳定窗口的**量纲**同时要改，见 D22。

**替代方案**：只给 PICO 接稳定器，Quest 直通。**否决理由**：两端走出不同时序，而这正是本项目故障只能在真机复现的老问题。

硬件不是纸面上的理想值：这三项必须留成可调旋钮，不能写死。

### D10 稳定器留在宿主，不进包

它吃的是平台位姿噪声，属于输入层；包吃的是"一次确定的扫码"。

**副作用（正向）**：`MarkerStabilizer.HasFiredStableEvent` 天然是"每次采集只发一次"，丢失后由 `MarkerTrackingSession.MarkerLost` 触发 `Reset`。这顺带修掉了"`regionalTrigger` 类型 tour 在激活后的第 2 帧就用一个抖出来的位姿吃掉唯一一次二次锚定许可"——二次锚定重新变回"人有意重扫"的动作，与原设计一致。这条要在验收里被明确观察到。

### D11 `PlatformOffsetConfig` 在宿主提交前施加

标记局部坐标系到内容锚点的固定偏移，两端各一套，在稳定之后、推给包之前施加。该资产当前零消费方——接上，不删。缺省 identity 时行为与今天等价。

### D12 HMD 内 UI 归宿主，新建世界空间面板

订阅 `OnLoadProgress` / `OnSpaceSceneLoaded` / `OnScanPromptChanged` / `OnTourActivated` / `OnTourSceneLoaded`，渲染：加载进度与场景名、扫码提示（`ScanPrompt` 状态与 TourIds）、失败可见（下载失败、`OnTourSceneLoaded` 超时）。

**替代方案 A**：复用 MRCore 的 Status Canvas（已是 world-space）。**否决理由**：把 ITE 的内容状态灌进 MR 基座的 HUD，与"MRCore 不含 ITE 行为"冲突。
**替代方案 B**：做进包。**否决理由**：包当前对 UI 零涉入，加进去等于把宿主 UX 焊进可移植包。

### D13 网络可达性改为运行时判定

`networkAvailable` 由序列化 bool 改为 `Application.internetReachability != NotReachable`，并保留注入入口供测试与强制离线调试。序列化 bool 等于"打包时决定现场有没有网"，会让已落地的离线自愈路径（`scene-package-version-check`）在现场断网时根本走不进去。

### D14 `MRCore.unity` 删除全部 ITE 对象

删 `-- ITE --` / `Tour Anchor` / `Marker Frame Offset` / `Tour Root`。MRCore 只保留 XR 装配、场景切换、手势、诊断。`ite-tour-space-host` 中"ITE 运行时不驻留核心场景"的要求同步收紧为"不含任何 ITE 对象"——空物体脚手架同样在禁止之列，因为它比缺失更有害：它让人以为装配已经就位，而其层级恰好是 `Validate()` 要报错的那条。

### D15 启动时序门在 `MRBootstrap` 之后

设备 rig 在 XR 就绪（相机可解析、观测源可开）之后再触发 `IteRuntime.StartAsync`，而不是 `Start` 里无条件跑。`IteHostBootstrap` 增加"由外部触发启动"的入口，编辑器场景保持自启动。

**替代方案**：`Start` 里重试直到相机出现。**否决理由**：重试掩盖装配错误，"一直在等"与"配错了"不可区分。

### D16 真机验收以 thirdDemo 为内容，序列与编辑器验收一一对应

编辑器验收的 8 条序列（联网首跑、离线复跑、未扫码时区域不生效、首次扫码激活、区域自动切换、内容元素 UI 渲染、二次锚定不重建）在真机上逐条重跑，差异只在输入是真码。跑不通的项必须能指出是哪一圈的问题，而不是"真机上不行"。

### D17 应用生命周期由设备 rig 承担

设备输入 rig 接 `OnApplicationPause`：暂停时 `MarkerTrackingSession.Pause()`，恢复时 `Resume()`。契约中"暂停期间不派发、且**不累计缺席时长**"正是为这个场景留的——不接的话，灭屏期间缺席时间照常累计，戴回去立刻收到一串虚假 `MarkerLost`。

工程内 `OnApplicationPause` / `OnApplicationFocus` 目前一处都没有。

PICO 侧恢复后是否需要重开 4U 相机会话**未验证**：归档 change 的任务 7.4 实测过"`Resume()` 后无权限申请、无相机会话重建"，但那测的是应用内 Pause 按钮，不是 Android 生命周期。列为待实测，不预设结论。

### D18 recenter 统一触发强制重扫

锚定把**世界位姿**写进 `AnchorRoot` 的 local。系统 recenter 改的是追踪空间原点：同一物理头位报出不同位姿，Unity 相机在世界里跳，而钉在世界坐标的内容不动 → 内容与实物错开，无任何日志。

`XROrigin` 虽然订阅了 `trackingOriginUpdated`，但处理器只有 `MoveOffsetHeight()`（`XROrigin.cs:363-367`）——只调地板高度，不补偿内容。

语义统一为"世界原点变了 → 上一次锚定不再可信 → 要求重新扫码"，与"戴上头显"一致。两端各接自己的通道：

- Quest：`XRInputSubsystem.trackingOriginUpdated`；并显式 `OpenXRSettings.SetAllowRecentering(false)` 加固（`OpenXRSpaceSettings.cs:24`——该 API 只控制是否跟随，不触发 recenter 事件）。工程内未序列化该设置，跑的是原生默认值，默认为何未知，需真机确认。
- PICO：`PXR_Plugin.System.RecenterSuccess`（`PXR_Loader.cs:536-539`，由 `XR_TYPE_EVENT_KEY_EVENT` 触发，即长按 Home）。PXR 设置资产内无对应开关，关不掉，只能订阅。

**替代方案**：把 `AnchorRoot` 挂到 XR Origin 底下让它跟着走。**否决理由**：破坏 `Validate()` 的父级 identity 前提，锚定等式直接不成立。

### D19 PICO 的佩戴状态改走 PXR 原生通道

`HeadsetPresenceAdapter` 默认读 `InputDevices` 的 `userPresence`。PICO 有专门通道：`XR_TYPE_EVENT_DATA_USER_PRESENCE_CHANGED_EXT` → `PXR_Plugin.System.UserPresenceChangedAction`（`PXR_Loader.cs:527-533`），另有 `Pxr_GetPSensorState(ref bool isUserPresent)`。

因此 PICO 上注入这条读取实现，而不是赌 `InputDevices` 是否上报。`HeadsetPresenceAdapter` 的构造函数已经收 `Func<bool?> readPresence`，是为此留的注入点，不需要改它的形状。

这条同时消掉了原 D7 风险中"PICO 可能不上报"的待决——它不再是"只能靠实测赌"的事。

### D20 同一帧多张码：先判稳的赢

现场是沿 Z 轴每 2 米一个 tour，站在中间大概率两张码同框；两端都支持多标记，M5 实测"并发无相互干扰"——即两张都会各自判稳、各自提交。

而链路上没有任何一处收敛到"一个"：`MarkerTrackingSession.tracked` 与 `MarkerStabilizer.tracked` 都是 `Dictionary`，迭代顺序不保证；`TourDirector.SubmitMarkerScan` 逐次处理，`normal` 类型下后到者会把先到者刚激活的 tour 停用销毁。**现状是未定义行为，不是"顺序"**——同一现场两次开机可能不一样。

规定：**先判稳的赢**。同帧内后到的观测忽略；已激活期间其他码的稳定事件不抢，要抢得等当前码丢失。实现是桥接里一个"本帧已提交"标志位。

**替代方案**：取距相机最近的一个。**否决理由**：该判据很可能随产品逻辑变化，现在锁死会变成一条要反复推翻的规则；"先扫到的说了算"是字面语义，确定性，且比"后到覆盖"少一次销毁重建。

### D21 PICO 的低置信检测在观测源内过滤

M6 实测（2026-09-04，PICO 4 Ultra，445 次检测）：出现一次假标记 `tag 64 hamming=2 margin=3.8`，位姿解在相机正前方 22 cm，**照常作为 `MarkerObservation` 派发给业务层**，1.010 s 后正常 Lost。margin 分布是干净双峰：真检测 76.5–99.6（n=444, p50=91.1）vs 假检测 3.8。

放到 ITE 上后果升级：假 ID 一旦撞上某个 tour 的 AprilTag ID，就是激活错误的 tour 并把内容重锚到人脸前 22 cm；叠加 D20 的"先判稳者赢"，还会锁住真码约一秒。

过滤放在 `PicoFiducialObservationSource` 内，按 `margin` 下限（可调字段，起点 20）。

**替代方案**：给 `MarkerObservation` 加 margin / hamming 字段，让业务层判。**否决理由**：契约层保持"只吐最原始信息、不做质量判断"（归档 D3）不变；且 margin 是 AprilTag 的概念，Quest 的 QR 没有对应物，塞进公共载荷等于为一端污染契约。检测器知道 margin 是什么，业务层不知道——过滤就该发生在检测器里。

### D22 稳定判定改为时间制

M1 实测派发速率：**PICO 5.6 Hz、Quest 70.0 Hz，相差 12.5 倍**（PICO 是 4U 推送式相机的出帧率，Quest 是 72fps 下每帧一次 `Poll()`）。

而 `MarkerStabilizer.stableFrameThreshold` 数的是 `Feed()` 调用次数：Quest 上 30 次 ≈ 0.43 s，PICO 上 30 次 ≈ **5.4 s**。举着码不动五秒半，现场表现仍然是"扫不到"。

同一条链路上判据的量纲也不一致：`MarkerTrackingSession` 的丢失滞回从一开始就是时间制（1.0 s，`deltaTime` 由外部注入，正是为了可在 EditMode 测），稳定判定却是帧计数制。

改为"连续稳定 X 秒"，`Feed` 已经收 `deltaTime`，改动很小。两端仍各一套参数（D9），但调的是"人举着码停稳需要多久"这种有物理含义的量。起点 0.4–0.5 s。

### D23 PICO 上扫描常开的代价先量后定

M3 实测单帧检测（1280×960，n=63）：p50 **71.7 ms**、p95 79.6、max 124.4；且**命中 45–52 ms、未命中 68–79 ms**——AprilTag 无早退路径，"待机（无标记）是这条管线的最坏功耗工况，不是最好"。

该数字来自只渲染几个方块的探针场景，不能外推到带 glb 模型 + 富文本面板 + 透传的导览工况。而扫码在语义上必须常开——区域切换之外，用户随时可以扫另一张码换 tour。

本 change 不落降频/暂停策略，只做两件事：真机验收加一条"扫描开/关"对比的帧率与温升测量；`sampleHz`（2–15，默认 6）与 `quadDecimate` 明确记为部署期可调项。

**否决理由（针对"现在就定策略"）**：没有任何数字支撑一条策略，先定等于凭感觉挑参数。

### D24 `sceneName` 是部署期配置

`Assets/Settings/ITE/IteRuntimeConfig.asset` 一份，`sceneName` 由部署时填写，编辑器验收场景与设备场景共用。验收阶段填 `thirdDemo`，换现场内容时改这一个字段重出包。

"在头显里选空间场景"是独立的产品功能（要拉 `projectDirectory.json`、要一套选择界面、要处理切换时的卸载重建）。已确认的后续方向是**一个系统级配置菜单，支持重新下载与切换场景**——另开 change，不进这里。包侧的边界已经写明：`IteRuntimeConfig` 刻意不含 `ItePropertiesUrl`，"挑哪个场景属宿主 UX"。

### D25 区域自动切换的累计漂移不做补偿

扫码本身就是漂移的自动校正：每次扫码把整套布局重新钉到那张码上。但 `TourRegionPolicy` 决策出的 Activate 不扫码，用的是上一次锚定的坐标系，走完全程（thirdDemo 约 10 m）会带上累计漂移。

**按现有 tour space 的逻辑，不改语义、不做补偿、不加验收门槛**——该逻辑是既定的。做 anchor 持久化或重定位是另一个量级的工程，真要做另开。

## Risks / Trade-offs

- **现场码的印制格式与假设不符** → 前置真机测量任务排在实现之前；解析器按可替换写，改动收敛到一处实现。
- **PICO 调参找不到可用参数（抖动本身超出可稳定范围）** → 调参任务同时记录 `tagSizeMeters` / 分辨率 / `quadDecimate` 的实测影响；若仍不可用，退路是提高标记幅面或缩短工作距离，属现场部署参数而非代码问题。
- **包公开面变化波及编辑器验收场景与 EditMode 测试** → 同一 change 内一起改；编辑器假扫码改为推"原始 payload + 种类"，正好让编辑器路径与真机路径走同一条解析链，而不是各走各的。
- **相机碰撞体半径 / layer 碰撞矩阵配错** → 静默失效，装配时显式校验并报错。
- **摘下头显强制重扫** → 游客扶一下眼镜就要跑回码前重扫。本 change 只做实证记录（含 PICO 是否上报 `userPresence`），语义调整另开。
- **MRCore 删对象后，任何仍指向它们的引用会变 fake null** → 删之前全库搜引用；`IteTourSpace.unity` 与新设备场景都用 prefab 自带的层级，不引用 MRCore 的对象。
- **真机首跑下载耗时长**（5 个 tour 包）→ 本 change 只保证可见，不做断点续传或预置内容；若现场网络不可接受，另开。

## Migration Plan

1. **前置测量**（不改代码）：用现有 `MarkerHookTest` 包在两端各读一次现场码，记录 rawPayload 原文、位姿抖动幅度、PICO 的 `userPresence` 是否上报。
2. **包侧**：数据模型加 AprilTag ID 字段 → 标记身份解析 → 入口形状变更 + EditMode 测试。
3. **宿主侧**：观测源工厂（含 `MarkerHookTestRig` 改用）→ 桥接改造（稳定 + 偏移 + 透传）→ 装配点的相机三段解析、碰撞体挂载、启动时序门、网络可达性。
4. **场景与构建**：装配 prefab → 编辑器场景改用 → 新设备场景 → `BuildScript` 场景清单 → 删 MRCore 的 ITE 脚手架。
5. **UI**：世界空间面板。
6. **真机验收与调参**：两端各跑一遍 D16 的序列；防抖参数实测回填。

回滚：以上每一步都可独立回退；第 4 步删 MRCore 对象前先确认全库无引用。

## Open Questions

- 两端防抖参数的实测值（第 6 步给出）。Quest 原始抖动已测（2026-09-11，任务 1.1）：静置 404 次观测，位置极差 ≤ 3 mm，远低于位置阈值 0.05 m —— Quest 侧阈值有收紧空间，待组 9 在 ITE 场景里调。PICO 侧待 1.2。
- **线上 `thirdDemo.json` 没有 `aprilTagID` 字段**（2026-09-11 拉取核实：tour 对象只有 `isEnabled / displayType / triggerVolume / transform / tourID`）。内容方补上之前，PICO 的 AprilTag 反查对每个 tour 都是 null，扫码激活走不通。待绑的对应表见 `docs/test-fixtures/marker-sheets/README.md`。
- PICO 在 Android 生命周期恢复后是否需要重开 4U 相机会话（D17）。归档 7.4 只验了应用内 `Pause()/Resume()`。
- `OpenXRSettings.AllowRecentering` 的原生默认值（D18）。工程内无序列化该设置。
- `margin >= 20` 的下限取自单次实测的双峰间隔（D21），长期需更多样本；阈值做成可调字段。
- 二维码变成地址之后的解析规则由谁定（内容方），本 change 只保证换实现的成本是一处。

已消解：PICO 是否上报 `userPresence` —— 有原生通道，见 D19。

已消解：现场码的印制 payload —— `******{tourID}******`，由内容方确认（2026-09-11），与 `QrPayloadFormat` 现有正则一致，解析器首版不改。thirdDemo 的 tourID 是不透明字符串（如 `wm0l5qcn_ibd`），不是序号。
