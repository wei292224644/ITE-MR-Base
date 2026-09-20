# SRP 变更轴审计清单

> 建立：2026-09-20 · 判据：`openspec/constitution.md` 条款 I / II
> 状态：**第一遍浅扫进行中**（⬜ 未扫 / 🔍 已浅扫 / ✅ 已考古坐实）

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
| `AprilTagDetectorCore.cs` | 251 | — | — | ⬜ |
| `FiducialConfidencePolicy.cs` | 14 | — | — | ⬜ |
| `IMarkerObservationSource.cs` | 25 | — | — | ⬜ |
| `MarkerObservation.cs` | 28 | — | — | ⬜ |
| `MarkerStabilizer.cs` | 109 | — | — | ⬜ |
| `MarkerStabilizerProfile.cs` | 57 | — | — | ⬜ |
| `MarkerTrackingSession.cs` | 102 | — | — | ⬜ |
| `MockObservationSource.cs` | 60 | — | — | ⬜ |
| `AxisGizmo.cs` | 129 | — | — | ⬜ |
| `MarkerHookTestHud.cs` | 152 | — | — | ⬜ |
| `MarkerHookTestRig.cs` | 213 | — | — | ⬜ |
| `MarkerSourceFactory.cs` | 71 | — | — | ⬜ |
| `PicoFiducialObservationSource.cs` | 472 | — | — | ⬜ |
| `PicoHeadsetPresence.cs` | 60 | — | — | ⬜ |
| `QuestMrukRuntimeInstaller.cs` | 187 | — | — | ⬜ |
| `QuestObservationSource.cs` | 137 | — | — | ⬜ |
| `PicoEnterpriseCameraPose.cs` | 32 | — | — | ⬜ |
| `PlanarPoseSolver.cs` | 377 | — | — | ⬜ |
| `PlatformOffsetConfig.cs` | 11 | — | — | ⬜ |
| `PoseMath.cs` | 11 | — | — | ⬜ |

### IteHost（11 文件）

`Assets/Scripts/IteHost`

| 文件 | 行数 | 轴数 | 轴（一句话） | 状态 |
|---|---|---|---|---|
| `EditorFakeScan.cs` | 32 | — | — | ⬜ |
| `EditorFlyMotion.cs` | 44 | — | — | ⬜ |
| `HeadsetPresenceAdapter.cs` | 75 | — | — | ⬜ |
| `IteDeviceMarkerRig.cs` | 277 | — | — | ⬜ |
| `IteEditorFakeScan.cs` | 180 | — | — | ⬜ |
| `IteEditorFly.cs` | 112 | — | — | ⬜ |
| `IteEditorHud.cs` | 158 | — | — | ⬜ |
| `IteEditorHudText.cs` | 49 | — | — | ⬜ |
| `IteHmdPanel.cs` | 298 | — | — | ⬜ |
| `IteHostBootstrap.cs` | 350 | — | — | ⬜ |
| `IteMarkerBridge.cs` | 144 | — | — | ⬜ |

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
