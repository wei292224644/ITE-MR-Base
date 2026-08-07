## Context

项目当前状态：Unity `6000.4.4f1`、URP `17.4.0`、Android XR loader 仅 `OpenXRLoader`（`Assets/XR/Loaders/`），Android 已启用 `MetaXRFeature` / `MetaQuestFeature` / `HandTracking` / `MetaHandTrackingAim` 等 OpenXR feature。已装 Meta XR Core SDK + MRUK `205.0.0`（经查为最新）、`com.unity.xr.hands 1.7.3`、`XRI 3.4.1`、`com.unity.xr.compositionlayers 2.4.0`。PICO SDK 尚未安装。

已有的跨端范式：`IMarkerTrackingProvider` + `QuestMarkerProvider` / `PicoMarkerProvider` + `MarkerTrackingBootstrapper`（`#if MRBASE_QUEST` / `#elif MRBASE_PICO` 工厂）。Build Profile `Quest.asset` / `PICO.asset` 已带 `MRBASE_QUEST` / `MRBASE_PICO` define，且 `m_OverrideGlobalSceneList: 0`（共享场景列表）。既有 `MRBase.Localization.Native.asmdef` 已用 `versionDefines` 生成 `MRBASE_HAS_MRUK`。`Assets/Scripts/Common/StaticInstance.cs` 提供带测试注入口的单例基类。

本设计的所有跨端结论均来自源码级核验，非文档推断。关键核验结果集中记录在「已核验的硬约束」一节。

### 硬约束（本设计的事实基础）

| # | 事实 | 依据 |
|---|---|---|
| 1 | PICO Integration SDK 实现了 Unity `XRHandSubsystem`，descriptor `id = "PICO Hands"` | `PICO/Runtime/Scripts/Hand/PXR_HandSubsystem.cs`（`#if !PICO_OPENXR_SDK` + `#if XR_HANDS`） |
| 2 | 该实现内部直接调用 PICO 原生 API，**XR Hands 是薄包装，零精度损失** | 同上 `:225` `PXR_HandTracking.GetJointLocations(HandType.HandLeft, ...)` |
| 3 | provider 只需实现两个 abstract 成员，签名与 XR Hands 1.7.3 逐字一致 | `XRHandSubsystemProvider.cs:33` `GetHandLayout`、`:119` `TryUpdateHands` |
| 4 | **通用手势通道在 PICO 上是空的** —— PICO 未 override `canSurfaceCommonPoseData`（默认 `false`），亦未 override `TryGetPinchValue` / `TryGetAimPose` | `XRHandSubsystemProvider.cs:131` |
| 5 | PICO 提供对等的 InputSystem 设备 `PicoAimHand`，含 `indexPressed` / `aimFlags` / `pinchStrengthIndex` | `PXR_HandSubsystem.cs:410/416/428` + PICO 官方 XRI 接线文档的绑定表 |
| 6 | **运行时手部网格是 Meta 独占** | `XRHandSubsystemProvider.cs:300` `detectedHandMeshLayout => OpenXRMetaQuest`；PICO 未实现 `TryGetMeshData` |
| 7 | XRI 的手部交互链路建在 `XRHandSubsystem` 之上 | `XR Origin Hands (XR Rig).prefab`：`XRHandTrackingEvents` → `NearFarInteractor` / `Poke Interactor` |
| 8 | Editor 模拟器是第三个 `XRHandSubsystem` provider，与两家平级 | `XRI/.../Simulator/XRDeviceSimulatorHandsSubsystem.cs` |
| 9 | **AR Foundation 只有四项跨端交集**：Session / Camera / Anchor / Raycast。Plane 在 PICO 侧只有自有 API，Occlusion 与 BoundingBox 无实现，ImageTracking 两家均无 | 本地 `com.unity.xr.meta-openxr/Runtime/Subsystems/*` 对比 PICO `Runtime/Subsystem/*` + `Runtime/Scripts/SensePack/PXR_PlaneDetectionManager.cs` |
| 10 | XR Composition Layers 是 provider-based（依赖里无 openxr），PICO 已实现该 provider，layer 类型两端几乎逐个对齐 | `com.unity.xr.compositionlayers/package.json`；`PICO/Runtime/CompositionLayers/PXR_{Quad,Cylinder,Cube,Equirect,Default}Layer.cs` + `PXR_CustomLayerHandler.cs`（含 `#if UNITY_VIDEO`） |
| 11 | PICO Integration SDK 3.4 是**双后端**的，两个 PICO SDK 不互斥 | `PXR_HandSubsystem.cs` 为 `#if !PICO_OPENXR_SDK`；`Runtime/Scripts/OpenXRFeatures/Features/PassthroughFeature.cs` 为 `#if PICO_OPENXR_SDK` |
| 12 | PICO 3.4 声明的版本兼容点 | `PICO/Runtime/Unity.XR.PICO.asmdef` `versionDefines`：`xr.hands >= 1.1.0`、`xr.openxr >= 1.16.0`、`compositionlayers >= 1.0.0`、`arfoundation >= 6.0.0` |
| 13 | **两家 SDK 携带同名 native 库，只要都装着就在 Gradle 阶段冲突** —— `lib/arm64-v8a/libopenxr_loader.so` 同时来自 PICO 的 `LoaderForUnitySDK_1_1_0.aar` 与 Meta 的 `OVRPlugin.aar`，`MergeNativeLibsTask` 直接失败。native plugin 是否进包由 `PluginImporter` 决定，**与启用了哪个 XR loader 无关**，所以双 APK 挡不住 | Quest 出包实测（本变更实施中发现） |
| 14 | **Git 来源的包不可变，无法改其 plugin 平台兼容性** —— `SetCompatibleWithPlatform(Android, false)` 在内存中生效，`SaveAndReimport()` 后被静默回滚（实测 `before=True → 设为 false → 重读 True`，`PackageInfo.source = Git`）。可用的是会话级、不落盘的 `SetIncludeInBuildDelegate`（PICO SDK 自己也用它门控 `PxrPlatform.aar`） | 同上 |
| 15 | **PICO SDK 的构建校验对所有 Android 构建生效，不分平台** —— `PXR_BuildHooks.OnPreprocessBuild` 要求 `androidApplicationEntry = Activity`，而 Unity 6 默认 `GameActivity`，导致装了 PICO SDK 后连 Quest 包都打不出来。`Activity` 对 Meta 同样有效，故两端共用一个值即可 | `PXR_BuildProcessor.cs:219`，Quest 出包实测 |
| 16 | **`OVRProjectConfig.handTrackingSupport` 必须是 `ControllersAndHands`（默认 `ControllersOnly`）**，否则 APK 不声明 `oculus.software.handtracking`，Quest 在无手柄时直接拦截启动，logcat 报 `common_system_dialog_app_launch_blocked_controller_required`。机制：XR Hands 在 order 10 已正确写入该项，但 Meta 的 `OVRGradleGeneration`（order 99999）按这个配置把它从 `unityLibrary/src/main/AndroidManifest.xml` **删掉**。不是上游 bug，是配置遗漏 | 构建期探针实测。`ControllersOnly` 时：order 11 `True` 2775B → order 99998 `True` 2775B → order 100001 `False` 3444B。改 `ControllersAndHands` 后同链路终态 `True` 3692B，APK aapt2 dump 确认 |
| 16b | **`PXR_Settings` 必须注册进 `EditorBuildSettings` 的 config object 表（键 `Unity.XR.PXR.Settings`）**，否则 PICO 那 20 多项 meta-data 一项都不进包，而构建仍报 Succeeded。机制：`PXR_XmlTools.GetSettings()` 读这张表，表里没有则返回 null，`PXR_BuildProcessor.cs:486` 解引用它抛 `NullReferenceException`，Unity 当普通日志吞掉，`doc.Save()` 从不执行。正常流程由 Project Settings > XR Plug-in Management 的 PICO 页面注册；本项目用 `XRPackageMetadataStore` 代码装 loader，绕过了该页面，故由 `BuildScript.EnsurePicoSettingsRegistered()` 自行补上 | Editor.log 中该异常的完整栈；修复前后 manifest 2548B → 5936B，APK aapt2 dump 确认 |
| 16c | **「构建成功」不是 manifest 正确的证据。**16 与 16b 是同一类故障：厂商钩子静默早退或静默删除，构建照报 Succeeded，只有装进真机才暴露。故由 `ManifestGuard`（order 100000，排在全部厂商钩子之后）按激活的 loader 断言必需声明齐全，缺项即 `BuildFailedException`。它按 loader 分派而非 `MRBASE_QUEST` / `MRBASE_PICO`——那两个是 Build Profile 的 scripting define，只进 player 程序集，编辑器程序集读不到 | 两端各踩一次；`Assets/Scripts/Editor/ManifestGuard.cs` |
| 17 | **`Meta.ARCameraFeature` 只是许可，不是开关** —— OpenXR 设置里 `UnityEngine.XR.OpenXR.Features.Meta.*` 全部启用，passthrough 仍不亮；真正驱动它的是场景里的 `ARCameraManager`（配 `AR Session` 与 alpha=0 的 SolidColor 背景）。两端的 passthrough 都必须在场景层显式装配，不能只靠 Project Settings | `meta-openxr@2.5.1/Documentation~/features/camera.md`「Scene setup」；Quest 真机实测：补上后 logcat 报 `PassthroughApiManager: PT is: ON numLayers: 1` |
| 18 | **Quest 的 `XRHandSubsystem` 同样不供给通用姿态数据** —— 真机实测 `supportsAimPose / supportsAimActivateValue / supportsPinchValue / supportsGraspValue` 全为 `False`，与 PICO 一致（硬约束 #4）。捏合走的是 InputSystem 侧的 `MetaAimHand` 设备。这把「中立点在 InputAction 多绑定」从「PICO 的特例」升级为**两端共同的事实** | Quest 真机 HUD：`supports aimPose=False aimActivate=False pinchValue=False grasp=False`，同时 `Select Value activeControl: /MetaAimHand1/pinchStrengthIndex` |

## Goals / Non-Goals

**目标设备**：Quest 3 / 3S + PICO 4 Ultra，始终采用最新型号（用户确认）。两端同为骁龙 XR2 Gen 2，性能同级 —— 性能基线与内容规格上限按 Gen 2 统一制定，不需为 Gen 1 设备（Quest 2 / PICO 4 非 Ultra / Neo3）下调，也不需做设备分级。

**Goals**

- 建立一份核心场景，Quest / PICO / Editor 三种装配共用，不产生场景副本
- 建立平台差异的四层隔离机制，使业务层完全不感知平台
- 使「一份 rig 两端通吃」这一关键假设在最早的里程碑被真机验证或推翻
- 使日常开发不必打包（Editor 模拟器 + PC 串流）
- 建立防止「改一端弄坏另一端」的检查手段

**Non-Goals**

- 不做自研手势（翻手 / 全握 / 双手合十）——留 M2，需真机反复调参
- 不做追踪丢失的降级路径（控制器通道、丢失提示）——用户明确延后
- 不做 AR Foundation 的平面 / 遮挡 / 包围盒（PICO 侧无实现，属平台层工作）
- 不做 composition layer 呈现路径（M1 全部 in-scene 渲染）
- 不迁移既有三个 demo 场景
- 不追求两端手部追踪质量一致（这是设备能力差异，无法抽象）

## Decisions

### D1：手部数据源统一为 Unity XR Hands，不用厂商手部 API

**决定**：手部关节数据唯一来源 `XRHandSubsystem`。

**理由**（按分量）：

1. 硬约束 #2 —— XR Hands 在 PICO 上就是原生 API 的薄包装，**「通用换兼容、牺牲质量」这个权衡不存在**
2. 硬约束 #7 —— XRI 整条手部交互链路建在 `XRHandSubsystem` 上。走厂商 API 等于放弃 XRI，poke / 抓取 / UI 射线 / 悬停全部要自己实现两遍，手势判定只是其中一小块
3. 硬约束 #8 —— 走 XR Hands 白送 Editor 模拟器；走厂商 API 则 Quest 要另装 Meta XR Simulator、PICO 只能串流
4. 两条数据源会污染所有下游（渲染、手势、interactor、将来的遮挡 / 物理 / 网络同步各 ×2），除非在两条源上再套抽象 —— 而那层抽象就是 `XRHandSubsystem` 本身

**考虑过的替代**：
- **各家 SDK 各自实现（`OVRHand` / PICO hand prefab）**：否决，理由同上。厂商 SDK 的手部渲染确实调得更好（PICO 甚至提供 `HandEditorTransparentOutlinedHandPrepassZ.mat` 这种透明描边+深度预写材质），但代价是整块渲染进平台层且两端外观不同
- **Meta Building Blocks**：否决。产出物是 `OVRCameraRig` / `OVRHand` / `OVRPassthroughLayer`，底层走 `OVRPlugin` 与 Meta 私有扩展，PICO 上不可用

**例外条款**：厂商独占的手部能力（Meta `OVRHandTrackingWideMotionModeSample`、`MetaXRSimultaneousHandsAndControllersShim`；PICO `ControllerWithHand`）若将来需要，以 L2 平台层**增强**形式接入，不替换基础数据源。

### D2：输入中立点在 InputAction 的多 binding，不在 Unity 的中立 API

**决定**：同一个 `InputAction` 同时挂两条 binding：

```
XRI LeftHand Interaction / Select
  ├── <MetaAimHand>{LeftHand}/indexPressed
  └── <PicoAimHand>{LeftHand}/indexPressed
Select Value / UI Press Value → .../pinchStrengthIndex
XRI LeftHand / Aim Position|Rotation|Flags → .../devicePosition|deviceRotation|aimFlags
```

**理由**：硬约束 #4 —— `XRCommonHandGestures` / `XRHandDevice` 的 aim 通道看似是官方的跨端捏合抽象，但 PICO 未 override `canSurfaceCommonPoseData`，该通道在 PICO 上无数据。这类失败是**静默的**：手在动、interactor 在、select 永不触发，且 Editor 里完全看不出来。硬约束 #5 提供了对等设备，Input System 的多 binding 机制使设备不存在时自然空转。

**考虑过的替代**：
- **依赖 `XRCommonHandGestures`**：否决，见上
- **`#if` 分平台各配一份 action 资产**：否决。输入配置本可零分叉，引入 `#if` 会使输入资产成为需要双份维护的东西
- **自建手势语义层从关节数学算捏合**：M1 不采用（多 binding 已足够，且厂商 pinch 经过训练、抗遮挡更好）。M2 做自研手势时该层才必需（翻手 / 全握 / 双手合十）

**风险**：此决定依赖未经真机验证的假设，故被列为 M1 的头号验收项（见 D8）。

### D3：一个常驻核心场景 + N 个 additive 业务场景，而非两个平台场景

**决定**：`MRCore.unity` 常驻，业务 additive 挂载，依赖方向业务 → `MRContext` 单向。

**理由**：

1. **引用方向**。若把 XR Origin 放在平台场景、业务放共享场景，则业务中任何需要相机 / 手 / interactor 引用的对象在 Editor 里都拉不到引用，只能运行时查找。反过来（核心常驻、业务 additive）业务只依赖 `MRContext` 这一有类型、可测试的入口，业务场景内零跨场景引用
2. **Editor 是第三个平台**。硬约束 #8 + 既有 `MockMarkerProvider`/`MarkerAnchorServiceTests` 已是这个思路。若平台差异做成场景边界，Editor 无设备开发需要第三个场景，业务变更要同步三份
3. **性能与场景数量无关**。foveation / SpaceWarp / 多视图 / MSAA 全是 loader 与 feature 级配置，按构建目标配，分场景换不来任何性能
4. 平台差异实际只有一个组件（`PXR_Manager`），不值得一个场景

**考虑过的替代**：
- **两个平台场景 + 共享业务场景（用户初始设想）**：切分方向正确（平台层 / 业务层分离），但边界放错。被否决的直接原因是跨场景反向引用，以及它建立在「各家 SDK 有最优实现」这一在手部追踪上不成立的前提上（见 D1）
- **两个完全独立的场景**：否决，核心场景是最不该 fork 的东西，每个后续功能都要加两遍

**例外条款**：唯一值得分场景的情形是两端**产品流程**根本不同（如 PICO 走 LBE 多人、Quest 走单人房间尺度）。那是产品分叉，不是设备适配。

### D4：平台差异的四种手法，按差异所在层选择

```
差异在哪一层?
├─ 输入信号（捏合 / 握持 / aim pose）      → 手法 A：InputAction 多 binding（零代码）
├─ 需挂/不挂某平台组件（PXR_Manager）      → 手法 B：prefab 变体或运行时 AddComponent
├─ 同一能力两家 API 不同（marker/passthrough/平面） → 手法 C：provider 接口 + #if 工厂
└─ 不在运行时，在包/配置里（loader/feature/manifest/图形/音频） → 手法 D：构建期切换
```

手法 C 延续既有 `IMarkerTrackingProvider` / `MarkerTrackingBootstrapper` 范式，不新造模式。

### D5：两条正交的 define 轴

| 轴 | 来源 | 含义 | 既有先例 |
|---|---|---|---|
| `MRBASE_QUEST` / `MRBASE_PICO` | Build Profile 的 Scripting Defines | 为哪个平台构建 | `Quest.asset` / `PICO.asset` 已配 |
| `MRBASE_HAS_*` | asmdef `versionDefines` | 包是否安装 | `MRBase.Localization.Native.asmdef` 的 `MRBASE_HAS_MRUK` |

两者必须同时判断（`#if MRBASE_PICO && MRBASE_HAS_PICO_SDK`），因为「为 Pico 构建」与「Pico 包在项目里」是独立的事 —— 队友 clone 下来未装包时代码仍须编译通过。

程序集布局：

```
Assets/Scripts/
├── Core/        MRBase.Core            MRContext / MRBootstrap / 能力接口
├── Interaction/ MRBase.Interaction     手势判定（M2）
├── Platform/
│   ├── Quest/   MRBase.Platform.Quest  defineConstraints: [MRBASE_HAS_MRUK]
│   ├── Pico/    MRBase.Platform.Pico   defineConstraints: [MRBASE_HAS_PICO_SDK]
│   └── Sim/     MRBase.Platform.Sim    无外部依赖
└── Editor/      MRBase.Build.Editor    构建前处理与校验
```

用 `defineConstraints` 让「包没装 → 程序集整体跳过编译」，优于在文件里撒 `#if`。这同时修掉既有 `MRBase.Localization.Native.asmdef` 的隐患：它硬引用 `meta.xr.mrutilitykit` + `Oculus.VR` 且 `defineConstraints: []`，加入 PICO 后会同时硬引用两家 SDK。

**架构约束（可做 CI grep 检查）**：`#if MRBASE_*` 仅允许出现在 `MRBootstrap` 与 `MRBase.Platform.*` 内部；业务程序集出现 `OVR` / `PXR_` 符号即违规。

### D6：PICO 端采用 Integration SDK 3.4 模式 1（PXR_Loader）

硬约束 #11 表明 PICO 两个 SDK 是双后端配合关系，存在三种组合：

| | 组合 | 优 | 劣 |
|---|---|---|---|
| 1 | Integration SDK only（PXR_Loader） | 官方 XRI/XR Hands 教程即此路，文档最齐；企业 ArUco 确定可用；3.4.0 = 2026-03-25 活跃维护 | 与 Quest 的 OpenXR 路径不同，须构建期切 loader |
| 2 | Integration SDK + PICO OpenXR SDK（`PICO_OPENXR_SDK` 模式） | 两端同为 `OpenXRLoader`，可能免去切 loader，且共享同一条 OpenXR 初始化路径 | PICO OpenXR SDK 1.4.x 最后提交 2025-06-03，停滞 13 个月；`OpenXRFeatures/Features/PICO/` 下**未见 marker 相关文件**，企业 ArUco 能力是否可用未知 |
| 3 | OpenXR SDK only | 配置最统一 | 失去企业能力，否决 |

**决定：组合 1。** 依据用户给定的原则「按各自更新最频繁的来」—— Integration SDK 3.4.0（2026-03）活跃，PICO OpenXR SDK（2025-06）停滞。组合 2 的潜在收益（免去手法 D）不足以抵偿「核心业务能力可能不可用」+「依赖停滞组件」两项风险。

**后果**：手法 D 的构建期 loader 切换为**必需**工作；两端 XR 初始化路径不同，排查经验不共享。

### D7：手模型自备，用 XR Hands sample 自带 FBX

**决定**：M1 使用 `Assets/Samples/XR Hands/1.7.3/HandVisualizer/` 的手网格 + 半透明材质。

**理由**：硬约束 #6 —— 运行时手部网格是 Meta 独占，通过中立通道拿不到；要用厂商手模型就得走各家 API，手部渲染整块落进平台层（与 D1 冲突）。而厂商 SDK 内的 FBX 有授权限制（`PXR_HandSubsystem.cs` 文件头：`proprietary to PICO Technology Co., Ltd.`），跨平台分发有合规风险。sample 自带资产随 Unity 包分发、两端授权干净，且 `Left/Right Hand Tracking.prefab` 已接好 `XRHandSkeletonDriver`。

**考虑过的替代**：从厂商 SDK 拷 FBX（授权风险，否决）；只画关节球（不是「手」，达不到需求）；自制或采购（产品化阶段再做，M1 不投）。

**已知技术难点**：半透明手网格自相交（手指叠手指 + `ZWrite Off`）会产生排序错乱。PICO 自己的 `HandEditorTransparentOutlinedHandPrepassZ.mat` 命名提示解法为**深度预写 + 描边**，M1 沿此方向。

### D8：M1 范围以「验证关键假设」为准，而非「功能最小化」

**决定**：M1 = passthrough + 半透明手 + 捏合选择 + 指尖触碰，不含自研手势。

**理由**：捏合与触碰在 XRI 现成 rig 里已接好，成本近零，但它们验证的是 D2 那个**未经真机验证、且失败方式静默**的假设。该假设若不成立，「一份 rig 两端通吃」需重做，因此必须在最早的里程碑暴露。自研手势需真机反复调参，且前提是手已稳定显示，故放 M2。

M1 的验收项 7（双 binding 在两端各自命中）是本变更的真实价值所在。

### D9：呈现路径 M1 全部 in-scene，composition layer 留作后续可选升级

硬约束 #10 表明 composition layer 在两端是**通用的**（provider-based 抽象，PICO 已实现，layer 类型逐个对齐，且 `PXR_CustomLayerHandler` 含 `#if UNITY_VIDEO` 支持视频）。但已知问题较多（PICO 侧记录有 Android Surface 垂直镜像、零 alpha 图渲染异常、多视图与 cubemap 不兼容）。

**决定**：M1 全部走 in-scene 渲染（零风险、Editor 可预览），M2/M3 再对真正需要清晰度的内容（视频、大段文字 UI）单独升级，升级路径走 `IPresentationSurface` 接口。A→B 的切换成本不对称（B→A 容易，A→B 要重做资产与层级），故此决定需在 M1 就明确记录。

### D10：诊断场景作为 M0 的首个交付物

**决定**：先建 `Diagnostics.unity` + 设备内 HUD，再做核心场景。

**理由**：M0 的多条验证项本质是「想知道运行时到底是什么状态」，而 Editor 的 Input Debugger 只能看 Editor 的设备 —— M0 最关键的一项（PICO 上是否存在 `PicoAimHand`）必须在设备上读。HUD 至少呈现：`XRHandSubsystem` descriptor `id`、双手 `isTracked` 与有效关节数、`InputSystem.devices` 列表、手部 action 的当前值与 `activeControl`、自算 fps、passthrough 状态。一个场景可一次覆盖 M0 的多数验证项，且在后续调参中长期有用。

### D11：三层调试链路

```
① XR Interaction Simulator（XRI 自带，项目已有 XRDeviceSimulatorSettings.asset）
   键鼠模拟头+双手+预设手势。硬约束 #8 使其零成本可用
   验不了：passthrough、追踪质量差异、双 binding 命中（模拟器造的是第三种设备）
② PC 串流（Meta Quest Link / PICO Developer Center）
   真手部追踪、不打包。日常调参主力
③ 打包真机
   passthrough 观感、真实性能、追踪质量差异只能这样验
```

### D12：依赖升级一次到位后锁定

依据硬约束 #12 应用用户给定原则（升最新，冲突选配套）：

| 包 | 当前 | 动作 | 依据 |
|---|---|---|---|
| `com.unity.xr.hands` | 1.7.3 | → 1.8.1 | 最新稳定；PICO 声明 `>= 1.1.0` 无上限 |
| `com.unity.xr.interaction.toolkit` | 3.4.1 | → 3.5.1 | 最新稳定；**不取 3.6.0-pre.1** |
| `com.unity.xr.meta-openxr` | 2.5.0 | → 2.5.1 | 补丁版 |
| `com.unity.xr.compositionlayers` | 2.4.0 | → 2.5.0 | 最新稳定 |
| `com.unity.xr.openxr` | 1.16.1 | **停住** | 1.17.1 可用，但 PICO 仅声明 `UNITY_OPENXR_1_16_0` |
| URP / AR Foundation | 17.4.0 / 6.4.2 | 不动 | 由 Unity 6000.4.4 绑定 |
| Meta XR Core / MRUK | 205.0.0 | 不动 | 经查即最新 |
| `com.unity.xr.picoxr` | — | 装 3.4.0 | 经查即最新；无 release 附件，用 git URL `#release_3.4.0` |

升级后 `com.unity.xr.hands` 由 1.7.3 变 1.8.1，硬约束 #3 的签名核验须重做。

### D13：防回归三道防线

1. **锁版本 + 两端编译回归**（最便宜，最该有）：版本精确固定；升级作为独立变更并双端验证，不夹在功能提交中。两端编译回归由 D14 的打包脚本命令行入口直接提供（纯编译级、不需设备，能抓 asmdef 引用断裂 / define 写错 / 包缺失，这恰是双端项目最常见的坏法），无需另写检查
2. **手势判定层写 EditMode 测试**（M2）：`MRBase.Interaction` 是纯关节数学，可喂假数据；照抄既有 `MarkerStabilizerTests` / `MockMarkerProvider` 套路。这是唯一可自动化覆盖的跨端逻辑
3. **真机回归清单**（最贵）：每个里程碑在两台设备各跑一遍，人工。跨端项目无更便宜的替代

### D14：统一打包脚本作为构建期差异的唯一入口

**问题**：Unity 6.0 的 XR loader 配置存于 `Assets/XR/XRGeneralSettingsPerBuildTarget.asset`，按 BuildTargetGroup（Android）存一份。Quest 与 PICO 同属 Android，共用这一份。Build Profile 覆盖不了它 —— 经核实其可覆盖范围仅 Player / Graphics / Quality Settings，且 `PICO.asset` 的字段中确实无任何 XR 项（`m_BuildTarget` / `m_Scenes` / `m_ScriptingDefines` / `m_PlayerSettingsYaml` / `m_PlatformBuildProfile`）。

loader 在**任何场景加载之前**被读取并执行（`Android Settings.m_InitManagerOnStart: 1`），因此场景结构无法影响它 —— 无论采用一个还是两个核心场景，这笔成本相同。loader 错配的失败发生在用户代码运行之前，表现为黑屏且无日志可查。

**决定**：不使用「手工切换 + 构建后校验」，改为**由打包脚本完整拥有构建流程**：

```
BuildQuest() / BuildPico()
  1. 激活对应 Build Profile（带入其 defines 与 scene 列表）
  2. 设置 Android loader：Quest → OpenXRLoader，Pico → PXR_Loader
       （XRPackageMetadataStore.AssignLoader / RemoveLoader）
  3. 设置 OpenXR feature 开关（Quest 需 Meta 系 feature，Pico 不需）
  4. 校验：defines 与 loader 一致、Spatializer 未指向厂商插件；不一致则中止
  5. 排除另一端的 Android native plugin（硬约束 #13：不排除则 Gradle 合并冲突）
       用 SetIncludeInBuildDelegate，不能用 SetCompatibleWithPlatform（硬约束 #14）
  6. BuildPipeline.BuildPlayer(...)
  7. finally 还原 loader、plugin delegate 与激活的 Profile
  8. 可选：出包后 adb install 到已连接设备

另有一项一次性的全局配置（不属脚本职责，改一次即可）：
`androidApplicationEntry` 必须为 `Activity`（硬约束 #15）。

入口：
  菜单项  MRBase/Build/Quest · MRBase/Build/Pico            日常打包
  命令行  Unity -batchmode -quit -executeMethod ...BuildPico  CI 与编译回归
```

**理由**：人不再触碰 Project Settings、不再点 Build 按钮，「忘切 loader」这一失败模式从根上消失，而非事后补救。该脚本同时白送两件原本要单独做的事 —— D13 第一道防线的「两端编译回归」与 CI 出包基础。

**考虑过的替代**：
- **`IPreprocessBuildWithReport` 自动改写 XR 设置**：否决。它挂在正常 Build 按钮上，以副作用方式修改项目设置资产，每次打包在 `git status` 留噪音，且构建后不还原会导致项目状态随上次打包漂移
- **`IPreprocessBuildWithReport` 只校验不改写**：否决。只读不写虽然干净，但仍保留「人手工切」这一步，只是把黑屏包换成构建失败。打包脚本能同时消掉手工步骤与失败可能
- **手工切换 + README 说明**：否决。隐式步骤在多人参与或间隔数周后必然出事，且黑屏难以归因到「忘切 loader」

**已知坑（必须写入实施说明）**：切换 Build Profile 会改动 scripting defines（`MRBASE_QUEST` ↔ `MRBASE_PICO`），改 define 触发脚本重编译，**重编译会打断正在执行的编辑器脚本**。因此禁止编写在一次执行内连续构建两端的 `BuildBoth()`；两端须由外层 shell 分两次调用 Unity。

**排期**：提前至 M0（原计划 M2）。M0 阶段需反复两端出包（依赖升级验证、PICO SDK 闸门、双 binding 验证各需出包），脚本先行可显著减少重复劳动；且即使 PICO SDK 闸门失败，脚本对 Quest 端仍然有效，不构成浪费。

### D15：探针包与主包共用同一条启动路径，删除 MRCoreLoader

**问题**：探针场景（`MarkerProbe` / `PicoQrCameraProbe`）自身不带 XR 装配。原先以**探针场景为启动场景**出包，靠场景里挂的 `MRCoreLoader` 在 `Awake` 里 `LoadScene("MRCore", Additive)` 把核心装配反向拉进来。同一份工程因此有两条启动路径 —— 主包里 MRCore 先起，探针包里 MRCore 后到。各 demo 场景也带着同一个组件，用途是「在编辑器里单独按 Play 看某个 demo」。

**决定**：`ProbeScenes` 首项改为 `MRCore`，探针场景由 `MRSceneDirector` 以 Additive 加载。`MRCoreLoader` 从全部 8 个场景移除，脚本删除。

**理由**：

1. **两条路径的时序不同，而差异只在真机上显形**。主包里 `MRContext` 在内容场景脚本的 `Awake` 之前就绪；探针包里 `MRCoreLoader` 用同步 `LoadScene`，MRCore 的 `Awake` 排在本帧末尾之后 —— 于是「探针内容不得在自己的 `Awake`/`Start` 里读 `MRContext`」成为一条真实约束，却只写在 `MRCoreLoader` 的注释里。这正是「行为依赖隐式因素：调用顺序」
2. **`StaticInstance` 的重复实例防护本就是为这条路径准备的**。见其 `Awake` 注释：漏判时 `_instance` 指向 Unity 伪 null 对象，`Instance != null` 为 false 而 `Instance` 又不是真 null。删掉第二条路径后该失败模式不再有触发源。防护保留 —— 漏判的代价不对称，留着比省下几行便宜
3. **D3 已确立「一个常驻核心场景 + N 个 additive 业务场景」**。探针包是同一套结构的实例，不该有自己的启动约定

**代价**：探针包里 `MRSceneDirector.firstScene` 为空，起来后需在菜单点一次进探针场景（探针包只有一个内容场景，菜单只有一项）。用一次点击换掉一整条平行启动路径。

**考虑过的替代**：
- **保留 `MRCoreLoader` 只服务探针**：改动最小，但两条启动路径与上述时序约束都留着，等于把一个已知问题降级成注释
- **探针场景各自内嵌 XR 装配**：违反 D3，也违反 `MRSceneDirector` 里「XR Origin 层级极深，不做成 prefab」的判断；两份装配需手动同步
- **探针包也设 `firstScene`**：需要 `BuildScript` 在构建期改写 `MRCore.unity` 的序列化值再还原，属 D14 明确否决的「以副作用方式修改项目设置资产」

## Risks / Trade-offs

- **[PICO SDK 3.4 与 Unity 6000.4.4 不兼容]** → 头号闸门，M0 第一项验证。`package.json` 只写 `"unity": "2021.3"`（下限），PICO 官网仍有陈旧的「Unity 6 支持中」表述，但 3.4 代码里已有 Unity 6 + URP + GLES + Multipass 的 MSAA 检查。不通过则整个 PICO 路径需改方案（退路：评估组合 2，或降 Unity 版本 —— 后者代价极高）
- **[双 binding 假设不成立]** → 排为 M1 头号验收项（D8）。失败模式静默（手在动、select 不触发、Editor 看不出），故须在设备上通过 HUD 读 `activeControl` 直接确认。不成立则需引入自建手势语义层从关节数学算捏合，工作量与调参成本上升
- **[XR Hands 1.8.1 破坏 PICO provider]** → M0 重做签名核验；失败则退回 1.7.3 并记录为版本上限
- **[打包脚本自身出错或与 Unity API 变更脱节]** → 由 D14 引入的新风险，替代原先「手工切 loader 忘记 → 黑屏包」那条（该失败模式已由 D14 从根上消除）。缓解：脚本内含步骤 4 的一致性校验，任何不一致直接中止而非硬打；脚本很小（设置 → 构建 → 还原），且每次 M0 出包都在使用它，问题会被立即发现而非潜伏
- **[打包脚本在一次执行内切换两端导致重编译打断]** → 禁止 `BuildBoth()`；两端由外层 shell 分两次调用 Unity（D14 已知坑）
- **[半透明手自相交排序错乱]** → 深度预写 + 描边（D7）；M1 在两端各看一次实际观感
- **[PICO 远场射线点不到 UI（官方标注 5.13.0 已知问题）]** → M0 实测是否命中目标设备。命中则 UI 设计不得把关键操作只放在远场射线上，近场 poke 作为主路径
- **[纯手势无降级路径，追踪丢失时用户卡死]** → 用户明确延后。rig 结构上保留控制器挂点但 M1 不实现；风险显式记录，不静默承担
- **[两端追踪质量/延迟差异]** → 无法抽象。交互设计上留容错（加大命中体积、给视觉反馈、不做需要精细稳定的手势）
- **[厂商 Spatializer 插件误启用]** → 全局单选且只在真机上错。spec 中约束为 Unity 内置，并列入构建前校验候选项
- **[两个 SDK 更新频繁导致 API 漂移]** → 锁版本 + 升级作为独立双端验证变更（D13 第 1 道）
- **[放弃厂商手部渲染的品质]** → 已知取舍。厂商 SDK 的手部材质经过调校（PICO 甚至提供成品透明描边材质），自备手模型需自行达到同等观感。换来的是两端外观一致 + 单套实现 + 模拟器可用

## Migration Plan

```
M0  可行性闸门（不通过则修订本设计的对应假设，勿写产品代码）
    分支：feature/qrcode-marker-localization(30 提交) → master → feature/mr-core-scene
    依赖升级至 D12 表格；装 PICO SDK 3.4.0；重做签名核验
    打包脚本（D14）—— 后续所有出包都用它，先行以省重复劳动
    建 Diagnostics.unity + HUD（D10）
    Editor 模拟器 → Quest 真机（对照组）→ PICO 真机（真正的未知）
M1  核心场景：MRCore.unity + passthrough + 半透明手 + 捏合 + 触碰
    两端各出一个 APK，逐条对照验收清单；记录 fps 基线
M2  装配层成型：asmdef 拆分与 defineConstraints、MRContext/MRBootstrap 下沉、
    自研手势层（MRBase.Interaction）+ EditMode 测试
M3  业务接入：Localization / SacredRelic 改为 additive 挂载到 MRCore
```

**回滚策略**：M0 各步均可独立回滚 —— 依赖升级回退 `Packages/manifest.json`；PICO SDK 移除包即可；分支合并前先打 tag。M1 之前不修改任何既有业务代码，故 M0/M1 失败不影响已完成的两个功能。

## Open Questions

- **外部阻塞（不属本变更范围）：PICO 出包被 `PicoMarkerProvider.cs` 阻塞。** 该文件属上一变更
  `qrcode-marker-localization`，位于 `#if MRBASE_PICO` 内，此前 `MRBASE_PICO` 从未生效故从未编译。
  装入 PICO SDK 3.4.0 后经反射核实与真实 API 有结构性不符：
  正确类型为 `Unity.XR.PICO.TOBSupport.PXR_Enterprise`（程序集 `PICO.TobSupport`，非 `Unity.XR.PXR`）；
  真实签名为 `static int SetMarkerInfoCallback(TrackingOriginModeFlags trackingMode, float cameraYOffset, Action<List<MarkerInfo>> markerInfos)`，
  回调一次给出**当前可见 marker 的列表**而非单个 marker，且不提供「某 marker 已丢失」的信号；
  `MarkerInfo` 字段为 `iMarkerId / posX,posY,posZ / rotationX,Y,Z,W / validFlag / markerType / dTimestamp / reserve`。
  现有代码假设「单 marker 回调 + isTracked 标志」，需重写而非改名。
  待定项：`trackingMode` 与 `cameraYOffset` 取值、`StopTracking()` 的正确做法、
  `MarkerLost` 是否用前后帧差集实现、`validFlag` 语义（须真机实测）。
  **影响本变更的任务 6.2（PICO 出包）；建议另开变更修复，本变更不承担。**

  **已解决（2026-07-31）**。用户确认目标机为企业版 / 具备 TOB 授权后按真实 API 重写，
  编译已通过（`MRBase.Localization.Native` 程序集，实现 `IMarkerTrackingProvider` 成立）。
  上述待定项的落法：
  - `trackingMode` 从活动 `XRInputSubsystem.GetTrackingOriginMode()` 读取，取不到才回退 `Floor`。
    写死会让 SDK 按错误的原点高度补偿 `posY`，每个 marker 差一个人高，真机上极难定位。
  - `cameraYOffset` 传 `0`：SDK 仅在 `Device` 模式下使用它，`Floor` 模式内部会置零。
  - `StopTracking()` 用 `PXR_Enterprise.UnBindEnterpriseService()`（TOB 无反注册 marker 回调的 API），
    并以 `tracking` 标志吞掉解绑期间在途的回调。
  - `MarkerLost` 确为前后帧差集。快照为 `null` 时不能提前返回，否则上一帧可见的 marker 永远收不到 lost。
  - `validFlag == 0` 视为识别无效，跳过（仍须真机复核语义）。
  - 坐标系无需自行转换：`MarkerInfoCallback.JsonToMarkerInfos` 已把右手系转为 Unity 左手系
    （`posZ` 取负、`rotationX/Y` 取负）并补偿原点高度，且经 `PXR_EnterpriseTools.QueueOnMainThread`
    派发回主线程，故回调内可直接访问 Unity API。
  - 新增两条编译期约束：`Unity.XR.PICO.TOBSupport` 内有与 `UnityEngine.Pose` 同名的 `Pose`，
    须用 `using Pose = UnityEngine.Pose;` 钉死；asmdef 须引用 `PICO.TobSupport` 程序集。

  **仍未验证**：企业服务能否在目标机上真正绑定。`PXR_EnterprisePlugin` 的实现体裹在
  `#if (UNITY_ANDROID && !UNITY_EDITOR)` 内，Editor 中恒返回 `-1`，只能真机验（任务 6.x）。

- Pico 模式 1 下 passthrough 的启用方式未查清。`PassthroughFeature.cs` 为 `#if PICO_OPENXR_SDK`，模式 1 下不编译；模式 1 应走 PXR 的 seethrough（`Utils/PXR_VstModelPosCheck.cs` 那套），具体入口待 M0 确认
- 两端 passthrough 下相机 clear / alpha 的具体配置待 M0 实测确定
- `IPassthroughController` 是否 M1 就抽成接口。倾向 M1 直接在 `MRBootstrap` 里 `#if`，M2 再抽（避免过早抽象）
- MR 下半透明手的产品化形态（追踪可视化 / 仅轮廓 / 特效增强 / 纯遮挡代理）—— M1 取追踪可视化，最终形态待真机观感决定
- 手模型 FBX 的产品化来源（自制 / 采购）
- 分发渠道与企业授权（PICO ArUco 需 LBE 模式 + 企业授权；商店分发与现场侧载的差异未确认）—— 本变更不涉及，M3 依赖
