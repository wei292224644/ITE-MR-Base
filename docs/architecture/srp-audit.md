# SRP 变更轴审计清单

> 建立：2026-09-20 · 判据：`openspec/constitution.md` 条款 I / II
> 状态：**第一遍浅扫已完成（114/114）**，第二遍考古进行中（⬜ 未扫 / 🔍 已浅扫 / ✅ 已考古坐实）

这是一份**活文档**。后续每个重构 change 完成后回来更新对应行，不要让它随 change 归档。
判据、五条豁免与 waiver 机制见 `openspec/constitution.md`；
本清单的产出规则见 `openspec/changes/repo-srp-baseline/design.md` D6 / D7。

## 覆盖范围

审计范围 **114 个 `.cs`**。轴数一栏的含义：`坐实/候选`——坐实轴附 commit hash，
无法坐实的标 `[推测轴]` 且不计入排序。

### 已知未覆盖区（本清单**不是**全仓）

| 未覆盖区 | 行数 | 性质 | 是否需要补审 |
|---|---|---|---|
| `Assets/Scripts/GsplatBench/` | 1827 | **待删**。CLAUDE.md 记明 removable as a unit；`BuildScript` 入口已于 2026-09-18 移除 | 永不补审 |
| `Assets/Scripts/SacredRelic/` | 1615 | **暂缓**。当前不活跃：独占 `SacredRelicDemo.unity`、无其他场景引用、最后实质改动 2026-08-03 | **接回导览或任何活场景时必须补审** |
| `Packages/wu.yize.gsplat/` | — | **跨仓**。git submodule（独立仓 `gsplat-unity`），改它要跨仓 PR | 在该仓自行处理 |
| `Assets/Tests/` | — | **判据不适用**。一个 fixture 服务多个用例本就正当 | 不补审，但重复 fixture 记作对应产品类的证据 |

## 清单

### Localization（20 文件）

`Assets/Scripts/Localization`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `AprilTagDetectorCore.cs` | 251 | 2 候选 | native AprilTag 绑定变（`Detect:129`/`Dispose:240`）/ 位姿解算方式变（`TrySolvePose:197`，static，不共享检测句柄） | 🔍 |
| `FiducialConfidencePolicy.cs` | 14 | 1 | 置信度阈值策略变 | 🔍 |
| `IMarkerObservationSource.cs` | 25 | 1 | 观测源契约变 | 🔍 |
| `MarkerObservation.cs` | 28 | 1 | 观测数据契约变 | 🔍 |
| `MarkerStabilizer.cs` | 109 | 1 | 防抖与稳定判定变 | 🔍 |
| `MarkerStabilizerProfile.cs` | 57 | 1 | 防抖参数配置变 | 🔍 |
| `MarkerTrackingSession.cs` | 102 | 1 | 会话语义变（轮询转发与丢失滞回共享 `tracked`，按合并规则记一条） | 🔍 |
| `MockObservationSource.cs` | 60 | 1 | `IMarkerObservationSource` 契约变（测试替身） | 🔍 |
| `AxisGizmo.cs` | 129 | 1 | 轴向可视化约定变 | 🔍 |
| `MarkerHookTestHud.cs` | 152 | 1 | 探针 HUD 展示内容变 | 🔍 |
| `MarkerHookTestRig.cs` | 213 | 2 候选 | 探针可视化（box/label）变 / 会话生命周期与暂停恢复变（`Awake:45`/`Update:69`/`Pause:87`） | 🔍 |
| `MarkerSourceFactory.cs` | 71 | 1 | 平台源选择与失败分类变 | 🔍 |
| `PicoFiducialObservationSource.cs` | 472 | 3 候选 | PICO 企业相机 API 变（`Open:121`/`Close:148`）/ AprilTag 检测参数与流程变 / 帧抓取时序变（`Update:300`） | 🔍 |
| `PicoHeadsetPresence.cs` | 60 | 1 | PICO 佩戴通道 API 变 | 🔍 |
| `QuestMrukRuntimeInstaller.cs` | 187 | 1 | MRUK 初始化方式变 | 🔍 |
| `QuestObservationSource.cs` | 137 | 1 | MRUK QRCode API 变 | 🔍 |
| `PicoEnterpriseCameraPose.cs` | 32 | 1 | PICO 相机位姿约定变 | 🔍 |
| `PlanarPoseSolver.cs` | 377 | 2 候选 | 平面位姿解算算法变（`TrySolve:20`）/ Unity 相机空间转换约定变（`ToUnityCameraSpace:123`，design D30） | 🔍 |
| `PlatformOffsetConfig.cs` | 11 | 1 | 贴纸与锚点物理偏移配置变 | 🔍 |
| `PoseMath.cs` | 11 | 1 | 位姿复合数学变 | 🔍 |

### IteHost（11 文件）

`Assets/Scripts/IteHost`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `EditorFakeScan.cs` | 32 | 1 | 桌面假扫码的位姿与载荷格式变（纯 static helper） | 🔍 |
| `EditorFlyMotion.cs` | 44 | 1 | 桌面飞行运动学变（纯 static helper） | 🔍 |
| `HeadsetPresenceAdapter.cs` | 75 | 1 | 佩戴状态读取源变 | 🔍 |
| `IteDeviceMarkerRig.cs` | 277 | 3 候选 | 观测源构建与失败呈现变 / 运行时启动时序变（注入后触发 `StartRuntimeAsync`）/ 会话生命周期变（`OnDestroy:258`） | 🔍 |
| `IteEditorFakeScan.cs` | 180 | 2 候选 | 假扫码触发方式变（`Trigger:142`/`Update:87`）/ 会话投喂与丢失模拟变（`Tick:160`） | 🔍 |
| `IteEditorFly.cs` | 112 | 1 | 桌面飞行输入映射变 | 🔍 |
| `IteEditorHud.cs` | 158 | 2 候选 | HUD 展示内容变（`Update:50`）/ runtime 事件挂接变（`Start:32`/`OnDestroy:155` 的 Hook/Unhook） | 🔍 |
| `IteEditorHudText.cs` | 49 | 1 | HUD 文本格式变（纯 static） | 🔍 |
| `IteHmdPanel.cs` | 298 | 3 候选 | 面板展示与布局变 / runtime 与 host.Failed 事件挂接变 / 重定位跟随变（`NotifyRecentered:79`） | 🔍 |
| `IteHostBootstrap.cs` | 350 | 6 候选 | 场景装配契约变 / 包事件签名变 / PICO 佩戴通道变 / 网络判定策略变 / 会话注入时序变 / 失败呈现变 —— handoff #7 的本体 | 🔍 |
| `IteMarkerBridge.cs` | 144 | 1 | 标记桥接语义变（会话事件→防抖→提交，状态链共享，按合并规则记一条） | 🔍 |

### Editor（3 文件）

`Assets/Scripts/Editor`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `BuildScript.cs` | 640 | 1 坐实 + 1 推测 | 出包配置变（入口与平台配置合并，证据 `1959911`/`f8f935a`/`9d7b902`）/ adb 装机行为变 `[推测轴]`（仅 `3079a7a` 附带） | ✅ |
| `IteSceneSetup.cs` | 248 | 3 候选 | rig 预制体结构变（`CreateRigPrefab:28`）/ 设备场景组装变（`CreateDeviceScene:74`）/ 编辑器场景迁移路径变（`MigrateEditorScene:123`） | 🔍 |
| `ManifestGuard.cs` | 82 | 1 | Android manifest 后处理规则变 | 🔍 |

### Core（9 文件）

`Assets/Scripts/Core`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `EffectShowcase.cs` | 71 | 1 | 展示切换方式变 | 🔍 |
| `MRBootstrap.cs` | 89 | 1 | 就绪门槛检查项变（XR loader / XR Origin / 手部子系统） | 🔍 |
| `MRContext.cs` | 53 | 1 | XR 查找面变（`Camera`/`Origin`） | 🔍 |
| `MRSceneDirector.cs` | 174 | 1 | 加性场景切换语义变（`Load:120`/`UnloadCurrent:164` 与内容场景清单共享 `CurrentScene`，合并记一条） | 🔍 |
| `MRSceneMenu.cs` | 110 | 1 | 场景菜单交互变 | 🔍 |
| `PalmsTogetherGesture.cs` | 208 | 2 候选 | 双判定源并行对照变（`Source` 枚举 + `trackerA`/`trackerB`）/ 手势事件语义变（`Performed:40`/`Released:43`） | 🔍 |
| `PalmsTogetherHoldTracker.cs` | 49 | 1 | 保持时长判定变 | 🔍 |
| `PalmsTogetherJointMath.cs` | 83 | 1 | 关节几何判定变（纯 static + Tuning） | 🔍 |
| `XrCameraAnchor.cs` | 56 | 1 | 相机锚定跟随变 | 🔍 |

### IceSpriteFx（3 文件）

`Assets/Scripts/IceSpriteFx`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `IceSpriteFxDisableXrSimulator.cs` | 31 | 1 | XR 模拟器拆除方式变 | 🔍 |
| `IceSpriteFxTestInput.cs` | 61 | 1 | 测试输入映射变 | 🔍 |
| `IceSpritePresence.cs` | 304 | 2 候选 | 出现/消失动画变（`Appear:88`/`Vanish:93`）/ 传送行为变（`TeleportTo:101`） | 🔍 |

### Transitions（2 文件）

`Assets/Scripts/Transitions`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `GroundUpRevealController.cs` | 212 | 1 | 地面揭示时序变（`Play`/`SetProgress`/`ResetToHidden`/`CompleteImmediately` 共享进度状态，合并记一条） | 🔍 |
| `IceSpriteTeleport.cs` | 98 | 1 | 传送相位时序变（纯 static） | 🔍 |

### Diagnostics（1 文件）

`Assets/Scripts/Diagnostics`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `DiagnosticsHud.cs` | 246 | 2 候选 | 采集指标项变 / HUD 渲染与布局变（`Update:49`） | 🔍 |

### Platform（1 文件）

`Assets/Scripts/Platform`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `PlatformRuntime.cs` | 198 | 2 坐实 + 1 推测 → **豁免 5** | passthrough 开法变（`2f8ba63`）/ 重定位事件源变（`d790dcd`）/ MRUK 包行为变 `[推测轴]`。命中豁免 5：加一个平台合改 1 文件、拆改 3 | ✅ |

### Common（1 文件）

`Assets/Scripts/Common`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `StaticInstance.cs` | 35 | 1 | 单例重复实例语义变（`BindInstanceForTesting` 与 `Awake` 共享 `_instance`，合并记一条） | ✅ |

### ite-tour/Core（22 文件）

`Packages/com.uality.ite-tour/Runtime/Core`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `Entity.cs` | 38 | 1 | 实体激活钩子语义变 | 🔍 |
| `IteBootstrap.cs` | 86 | 1 | 装配契约与校验项变（`MissingRequired`/`Validate` 共享字段集） | 🔍 |
| `IteContentPipeline.cs` | 325 | 3 候选 | 服务端接口与 JSON 契约变（`FetchSpaceSceneAsync:73`/`FetchTourAsync:100`）/ 资源加载变（`LoadSceneSpritesAsync:129`）/ 缓存版本判定变（`ShouldDownloadTourPackage:176`） | 🔍 |
| `IteRuntime.cs` | 323 | 3 候选 | 加载链编排变（`StartAsync:130`）/ 对外事件面变（8 个 `event`）/ 标记扫码提交入口变（`SubmitMarkerScan`，D32） | 🔍 |
| `IteRuntimeDriver.cs` | 18 | 1 | MonoBehaviour 驱动宿主变 | 🔍 |
| `IteTourAssembler.cs` | 123 | 1 | tour 实例装配与生命周期变（`CreateAsync`/`Find`/`DestroyAll` 共享 `_liveTours`） | 🔍 |
| `IteTourObject.cs` | 524 | 4 候选 | 内容树构建变（`CreateTourObject:86`）/ 场景绑定与锚定变换变（`BindScene:66`/`ChangeTourObjectTransform:134`）/ 触发体积进出通知变（`NotifyVolumeTransition:74`）/ 场景就绪世代变（`IsSceneReady:58`）—— handoff #2 已记它初始化顺序有问题 | 🔍 |
| `LoadProgress.cs` | 24 | 1 | 加载进度算法变（纯 static） | 🔍 |
| `MarkerFrame.cs` | 25 | 1 | 标记→内容锚点固定旋转变（design D32） | 🔍 |
| `MarkerIdentity.cs` | 161 | 1 | 标记身份解析语义变（`Resolve:82` 建在 `TryParseTourId:48` 之上，同一条链，合并记一条） | 🔍 |
| `MarkerKind.cs` | 20 | 1 | 标记种类枚举变 | 🔍 |
| `ScanPromptPolicy.cs` | 100 | 1 | 扫码提示策略变 | 🔍 |
| `SceneRoles.cs` | 27 | 1 | 场景角色识别变 | 🔍 |
| `TourAnchoring.cs` | 56 | 1 | 锚定几何变（design D33 刚提成纯函数） | 🔍 |
| `TourAssembly.cs` | 41 | 1 | 装配与展示类型策略变 | 🔍 |
| `TourAssetPaths.cs` | 43 | 1 | 资源路径约定变 | 🔍 |
| `TourDirector.cs` | 333 | 4 候选 | 导览激活与切换变（`ActivateById:99`/`Observe:56`）/ 扫码策略变（`RequireScan:71`/`SubmitMarkerScan:113`）/ 佩戴状态驱动变（`SetHeadsetMounted:80`）/ 区域进出待选变（`PendingTourIds:68`） | 🔍 |
| `TourIdLists.cs` | 26 | 1 | id 列表工具变（纯 static） | 🔍 |
| `TourRegionPolicy.cs` | 120 | 1 | 区域进出判定策略变 | 🔍 |
| `TourScanPolicy.cs` | 161 | 1 | 扫码决策策略变 | 🔍 |
| `TourSceneLifecycle.cs` | 87 | 1 | 场景构建世代机变 | 🔍 |
| `TourVolumeTrigger.cs` | 25 | 1 | 触发体积转发变 | 🔍 |

### ite-tour/Components（18 文件）

`Packages/com.uality.ite-tour/Runtime/Components`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `ActionComponents.cs` | 400 | 3 类各 1 | 多类文件：PlayAudio / Spin / PlayAnimation 三个 action 组件各 1 轴。判据按**类**判，故不违反；但三者互不相干却同住一个 400 行文件，记一笔 | 🔍 |
| `ActionSettings.cs` | 212 | 1 | 动作参数契约变 | 🔍 |
| `AnimationAudioMap.cs` | 51 | 1 | 动画音频映射构建变 | 🔍 |
| `AutoRotate.cs` | 28 | 1 | 自转行为变 | 🔍 |
| `BaseActionComponents.cs` | 30 | 1 | 动作组件基类契约变 | 🔍 |
| `BaseComponent.cs` | 40 | 1 | 组件基类服务定位变（`Awake:23` 用 `GetComponentInParent`——handoff #2 否决改注入的原因） | 🔍 |
| `BaseElementComponent.cs` | 13 | 1 | 元素组件基类契约变 | 🔍 |
| `BaseTriggerComponent.cs` | 54 | 1 | 触发派发变（`Dispatch:25`） | 🔍 |
| `ComponentLoadOrder.cs` | 72 | 1 | 组件加载顺序规则变 | 🔍 |
| `ComponentRegistry.cs` | 47 | 1 | 组件类型解析变 | 🔍 |
| `EMWModelRenderElement.cs` | 106 | 2 候选 | 模型加载与动画控制器变（`Constructor:31`）/ 点击事件注入变（`InjectTapEvent:104`） | 🔍 |
| `ElementComponents.cs` | 134 | 2 类各 1 | 多类文件：EMWModelRender / RichText 两个元素组件各 1 轴 | 🔍 |
| `IteTourElementPrefabs.cs` | 18 | 1 | 元素预制体清单变 | 🔍 |
| `PrimitiveShapes.cs` | 77 | 1 | 基本体形状映射变 | 🔍 |
| `RichTextElement.cs` | 161 | 2 候选 | 富文本渲染与布局变（`Constructor:41`）/ 音频播放控制变（`ToggleAudio:121`/`PauseAudio:133`/`PlayAudio:144`） | 🔍 |
| `RichTextLayout.cs` | 23 | 1 | 富文本布局算法变 | 🔍 |
| `TriggerComponents.cs` | 75 | 3 类各 1 | 多类文件：Load / Tap / Approximate 三个触发组件各 1 轴 | 🔍 |
| `VideoPlaneElement.cs` | 214 | 2 候选 | 视频播放控制变（`PlayVideo:151`/`PauseVideo:163`）/ 播放器 UI 控制变（`SetControllerActive:194`） | 🔍 |

### ite-tour/Internal（14 文件）

`Packages/com.uality.ite-tour/Runtime/Internal`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `AnimationAudioController.cs` | 57 | 1 | 动画音频驱动变 | 🔍 |
| `BoxColliderWireframeDrawer.cs` | 86 | 1 | 线框绘制变 | 🔍 |
| `ContentAssetLoader.cs` | 166 | 2 候选 | 资源类型加载变（`LoadSpriteAsync:62`/`LoadAudioClipAsync:76`/`LoadGlbAsync:100`）/ HTTP etag 探测变（`FetchEtagAsync:143`） | 🔍 |
| `EventEmitter.cs` | 48 | 1 | 事件总线契约变 | 🔍 |
| `HierarchyBoundsCalculator.cs` | 89 | 2 候选 | 包围盒计算变（`CalculateLocalBounds:10`）/ Gizmo 绘制变（`DrawLocalBoundsGizmo:77`） | 🔍 |
| `LegacyAnimationController.cs` | 104 | 1 | 遗留动画控制契约变 | 🔍 |
| `Matrix4x4Extensions.cs` | 47 | 1 | 矩阵扩展数学变 | 🔍 |
| `RoundedBoxUIProperties.cs` | 88 | 1 | 圆角网格修饰变 | 🔍 |
| `SpacePackageEtagCache.cs` | 23 | 1 | 空间包 etag 缓存键与存储变 | 🔍 |
| `TourVersionCache.cs` | 26 | 1 | tour 版本缓存键与存储变 | 🔍 |
| `ZipContentDownloader.cs` | 204 | 2 候选 | 下载流程变（`DownloadAndExtractAsync:34`）/ 解压实现变（`ExtractAsync:81`/`ExtractSync:92`） | 🔍 |
| `ZipEntryPath.cs` | 63 | 1 | zip 条目路径安全解析变 | 🔍 |
| `ZipTopLevel.cs` | 26 | 1 | zip 顶层枚举变 | 🔍 |
| `ZipTopLevelResolver.cs` | 82 | 1 | zip 顶层目录判定变 | 🔍 |

### ite-tour/Data（5 文件）

`Packages/com.uality.ite-tour/Runtime/Data`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `ActionData.cs` | 117 | 1 | 动作数据契约变（DTO） | 🔍 |
| `ElementData.cs` | 146 | 1 | 元素数据契约变（DTO） | 🔍 |
| `TriggerData.cs` | 37 | 1 | 触发数据契约变（DTO） | 🔍 |
| `IteSpaceScene.cs` | 112 | 1 | 空间场景数据契约变（DTO） | 🔍 |
| `IteTourData.cs` | 114 | 1 | 导览数据契约变（DTO） | 🔍 |

### ite-tour/Convert（3 文件）

`Packages/com.uality.ite-tour/Runtime/Convert`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `AssetConverter.cs` | 54 | 1 | Asset 多态反序列化契约变 | 🔍 |
| `ComponentActionConverter.cs` | 58 | 1 | ComponentAction 多态反序列化契约变 | 🔍 |
| `ComponentConverter.cs` | 64 | 1 | Component 多态反序列化契约变 | 🔍 |

### ite-tour/Config（1 文件）

`Packages/com.uality.ite-tour/Runtime/Config`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `IteRuntimeConfig.cs` | 53 | 1 | 运行时配置与 URL 模板变 | 🔍 |

## 疑似但未坐实

（第二遍产出：全部轴均为 `[推测轴]` 的类列在这里，不得悄悄丢弃）

## 重构优先级

（第二遍产出：按「坐实轴数 × 消费者数」排序）

## 测试台子重复（产品类证据）

| 产品类 | 重复的 fixture | 说明 |
|---|---|---|
| `IteHostBootstrap` | `IteHostBootstrapTests.cs:84-126` 的 `HostFixture` / `IteHostCameraResolutionTests.cs:43` 的 `BuildHost` | 同一台子写两遍；`HostFixture` 需 `SerializedObject` 反射写五个私有字段才能立起来 → 构造路径不可直接调用 |
