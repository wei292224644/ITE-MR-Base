# SRP 变更轴审计清单

> 建立：2026-09-20 · 判据：`openspec/constitution.md` 条款 I / II
> 状态：**第一遍浅扫进行中**（Localization / IteHost 已完成）（⬜ 未扫 / 🔍 已浅扫 / ✅ 已考古坐实）

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
| `BuildScript.cs` | 640 | — | — | ⬜ |
| `IteSceneSetup.cs` | 248 | — | — | ⬜ |
| `ManifestGuard.cs` | 82 | — | — | ⬜ |

### Core（9 文件）

`Assets/Scripts/Core`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `EffectShowcase.cs` | 71 | — | — | ⬜ |
| `MRBootstrap.cs` | 89 | — | — | ⬜ |
| `MRContext.cs` | 53 | — | — | ⬜ |
| `MRSceneDirector.cs` | 174 | — | — | ⬜ |
| `MRSceneMenu.cs` | 110 | — | — | ⬜ |
| `PalmsTogetherGesture.cs` | 208 | — | — | ⬜ |
| `PalmsTogetherHoldTracker.cs` | 49 | — | — | ⬜ |
| `PalmsTogetherJointMath.cs` | 83 | — | — | ⬜ |
| `XrCameraAnchor.cs` | 56 | — | — | ⬜ |

### IceSpriteFx（3 文件）

`Assets/Scripts/IceSpriteFx`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `IceSpriteFxDisableXrSimulator.cs` | 31 | — | — | ⬜ |
| `IceSpriteFxTestInput.cs` | 61 | — | — | ⬜ |
| `IceSpritePresence.cs` | 304 | — | — | ⬜ |

### Transitions（2 文件）

`Assets/Scripts/Transitions`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `GroundUpRevealController.cs` | 212 | — | — | ⬜ |
| `IceSpriteTeleport.cs` | 98 | — | — | ⬜ |

### Diagnostics（1 文件）

`Assets/Scripts/Diagnostics`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `DiagnosticsHud.cs` | 246 | — | — | ⬜ |

### Platform（1 文件）

`Assets/Scripts/Platform`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `PlatformRuntime.cs` | 198 | — | — | ⬜ |

### Common（1 文件）

`Assets/Scripts/Common`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `StaticInstance.cs` | 35 | — | — | ⬜ |

### ite-tour/Core（22 文件）

`Packages/com.uality.ite-tour/Runtime/Core`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `Entity.cs` | 38 | — | — | ⬜ |
| `IteBootstrap.cs` | 86 | — | — | ⬜ |
| `IteContentPipeline.cs` | 325 | — | — | ⬜ |
| `IteRuntime.cs` | 323 | — | — | ⬜ |
| `IteRuntimeDriver.cs` | 18 | — | — | ⬜ |
| `IteTourAssembler.cs` | 123 | — | — | ⬜ |
| `IteTourObject.cs` | 524 | — | — | ⬜ |
| `LoadProgress.cs` | 24 | — | — | ⬜ |
| `MarkerFrame.cs` | 25 | — | — | ⬜ |
| `MarkerIdentity.cs` | 161 | — | — | ⬜ |
| `MarkerKind.cs` | 20 | — | — | ⬜ |
| `ScanPromptPolicy.cs` | 100 | — | — | ⬜ |
| `SceneRoles.cs` | 27 | — | — | ⬜ |
| `TourAnchoring.cs` | 56 | — | — | ⬜ |
| `TourAssembly.cs` | 41 | — | — | ⬜ |
| `TourAssetPaths.cs` | 43 | — | — | ⬜ |
| `TourDirector.cs` | 333 | — | — | ⬜ |
| `TourIdLists.cs` | 26 | — | — | ⬜ |
| `TourRegionPolicy.cs` | 120 | — | — | ⬜ |
| `TourScanPolicy.cs` | 161 | — | — | ⬜ |
| `TourSceneLifecycle.cs` | 87 | — | — | ⬜ |
| `TourVolumeTrigger.cs` | 25 | — | — | ⬜ |

### ite-tour/Components（18 文件）

`Packages/com.uality.ite-tour/Runtime/Components`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `ActionComponents.cs` | 400 | — | — | ⬜ |
| `ActionSettings.cs` | 212 | — | — | ⬜ |
| `AnimationAudioMap.cs` | 51 | — | — | ⬜ |
| `AutoRotate.cs` | 28 | — | — | ⬜ |
| `BaseActionComponents.cs` | 30 | — | — | ⬜ |
| `BaseComponent.cs` | 40 | — | — | ⬜ |
| `BaseElementComponent.cs` | 13 | — | — | ⬜ |
| `BaseTriggerComponent.cs` | 54 | — | — | ⬜ |
| `ComponentLoadOrder.cs` | 72 | — | — | ⬜ |
| `ComponentRegistry.cs` | 47 | — | — | ⬜ |
| `EMWModelRenderElement.cs` | 106 | — | — | ⬜ |
| `ElementComponents.cs` | 134 | — | — | ⬜ |
| `IteTourElementPrefabs.cs` | 18 | — | — | ⬜ |
| `PrimitiveShapes.cs` | 77 | — | — | ⬜ |
| `RichTextElement.cs` | 161 | — | — | ⬜ |
| `RichTextLayout.cs` | 23 | — | — | ⬜ |
| `TriggerComponents.cs` | 75 | — | — | ⬜ |
| `VideoPlaneElement.cs` | 214 | — | — | ⬜ |

### ite-tour/Internal（14 文件）

`Packages/com.uality.ite-tour/Runtime/Internal`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `AnimationAudioController.cs` | 57 | — | — | ⬜ |
| `BoxColliderWireframeDrawer.cs` | 86 | — | — | ⬜ |
| `ContentAssetLoader.cs` | 166 | — | — | ⬜ |
| `EventEmitter.cs` | 48 | — | — | ⬜ |
| `HierarchyBoundsCalculator.cs` | 89 | — | — | ⬜ |
| `LegacyAnimationController.cs` | 104 | — | — | ⬜ |
| `Matrix4x4Extensions.cs` | 47 | — | — | ⬜ |
| `RoundedBoxUIProperties.cs` | 88 | — | — | ⬜ |
| `SpacePackageEtagCache.cs` | 23 | — | — | ⬜ |
| `TourVersionCache.cs` | 26 | — | — | ⬜ |
| `ZipContentDownloader.cs` | 204 | — | — | ⬜ |
| `ZipEntryPath.cs` | 63 | — | — | ⬜ |
| `ZipTopLevel.cs` | 26 | — | — | ⬜ |
| `ZipTopLevelResolver.cs` | 82 | — | — | ⬜ |

### ite-tour/Data（5 文件）

`Packages/com.uality.ite-tour/Runtime/Data`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `ActionData.cs` | 117 | — | — | ⬜ |
| `ElementData.cs` | 146 | — | — | ⬜ |
| `TriggerData.cs` | 37 | — | — | ⬜ |
| `IteSpaceScene.cs` | 112 | — | — | ⬜ |
| `IteTourData.cs` | 114 | — | — | ⬜ |

### ite-tour/Convert（3 文件）

`Packages/com.uality.ite-tour/Runtime/Convert`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `AssetConverter.cs` | 54 | — | — | ⬜ |
| `ComponentActionConverter.cs` | 58 | — | — | ⬜ |
| `ComponentConverter.cs` | 64 | — | — | ⬜ |

### ite-tour/Config（1 文件）

`Packages/com.uality.ite-tour/Runtime/Config`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `IteRuntimeConfig.cs` | 53 | — | — | ⬜ |

## 疑似但未坐实

（第二遍产出：全部轴均为 `[推测轴]` 的类列在这里，不得悄悄丢弃）

## 重构优先级

（第二遍产出：按「坐实轴数 × 消费者数」排序）

## 测试台子重复（产品类证据）

| 产品类 | 重复的 fixture | 说明 |
|---|---|---|
| `IteHostBootstrap` | `IteHostBootstrapTests.cs:84-126` 的 `HostFixture` / `IteHostCameraResolutionTests.cs:43` 的 `BuildHost` | 同一台子写两遍；`HostFixture` 需 `SerializedObject` 反射写五个私有字段才能立起来 → 构造路径不可直接调用 |
