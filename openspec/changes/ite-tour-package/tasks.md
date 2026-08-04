> 源工程：`/Users/wwj/Desktop/unity/ite-space-tour`（只读参考，不修改）
> 迁移原则：行为等价优先。发现的历史缺陷记入 `Documentation~/TODO.md`，本次不修（见 design D12）。

## 1. 包骨架与依赖

- [x] 1.1 创建 `Packages/com.uality.ite-tour/` 与 `package.json`（name / version / displayName / unity / dependencies: newtonsoft-json 3.2.2 + gltfast 6.19.0 + sharp-zip-lib 1.4.2）
- [x] 1.2 确认 UPM 自动发现该 embedded 包并**传递解析**出 gltfast 与 sharp-zip-lib（体现在 `packages-lock.json`），Unity 6000.4.4f1 下无解析错误与编译错误。**不要**把本包或其依赖重复写进工程 `Packages/manifest.json` 的 `dependencies`——依赖声明的唯一来源是包自己的 `package.json`，这样移植时依赖才跟着走
- [x] 1.3 创建 `Runtime/Uality.IteTour.asmdef`，references 只含官方包，确认**不含任何 `MRBase.*`**
- [x] 1.4 创建 `Tests/Uality.IteTour.Tests.asmdef`，并在工程 `manifest.json` 加 `"testables": ["com.uality.ite-tour"]`，确认 Test Runner 能发现包内测试
- [x] 1.5 建立目录骨架：`Runtime/{Config,Data,Convert,Core,Components,Internal,Prefabs}`、`Tests/`。（`Documentation~/` 带波浪号不被 Unity 导入、无 `.meta`，git 也不跟踪空目录，随任务 11 的文档内容一并落地）

## 2. 内部工具迁移（下游依赖，先做）

- [x] 2.1 迁移 `EventEmitter`、`Matrix4x4Extensions`、`HierarchyBoundsCalculator`、`BoxColliderWireframeDrawer` 至 `Runtime/Internal/`，收入 `Uality.IteTour.Internal` 命名空间。（`ComponentsUtils` 原列于此，实施时发现其泛型约束与注册表都依赖组件层类型，无法在组件层之前独立编译，已移至 6.7）
- [x] 2.2 迁移 `LegacyAnimationController`、`AnimationAudioController`（含 `AudioData`）至 `Runtime/Internal/`
- [x] 2.3 自实现圆角 UI：`IMeshModifier` 把 `borderRadius` 写入 `uv1` + SDF shader 读取（design D8）。**不得**引用 Meta `com.meta.xr.sdk.interaction` 的 `RoundedBoxUIProperties` 及其 shader
- [x] 2.4 EditMode 测试守卫包边界：断言 `Uality.IteTour` 程序集的引用集合中无 `MRBase.*` 与任何平台 SDK（Oculus / Meta.XR / PICO / PXR），并 grep 确认源码无 `OVR` / `PXR_` / `MRBASE_` 字样。（原计划只做 grep；改为测试是因为 Packages→Assets 的编译禁令拦不住"引用另一个平台**包**"这条路径——`com.meta.xr.sdk.core` 就是包，加进来能编译通过却会同时毁掉可移植性与 PICO 支持）

## 3. 数据层与转换器（可离机测）

- [x] 3.1 迁移 `Datas/IteTourObject.cs`（`IteSpaces` / `IteSpaceScene` / `Tour` / `TriggerVolume` / `DisplayType`）与 `Datas/IteTourScene.cs`（`IteTour` / `Scene` / `Entity` / `Asset` / `Component` / `ComponentAction`）
- [ ] 3.2 迁移 3 个 JsonConverter（Asset / Component / ComponentAction），保持多态分派表完整
- [ ] 3.3 EditMode 测试：3 个 Converter 对每种子类型的反序列化分派正确，未知类型走默认分支不抛异常
- [ ] 3.4 EditMode 测试：`Tour.Matrix4X4`（列式 4x4 + `ConvertToLeftHanded`）与 `Entity.Matrix4X4`（TRS + `ConvertToLeftHanded` + `FlipRotY`）的输出快照。**锁定当前行为，不"修正"两处调用组合的不一致**（design 风险表）

## 4. 配置与内容管线

- [ ] 4.1 定义 `IteRuntimeConfig` ScriptableObject：空间场景地址、Tour 内容包地址模板、版本查询地址、场景名、3 个 element prefab 引用、Tour prefab 引用
- [ ] 4.2 确认 `ItePropertiesUrl`（`projectDirectory.json`）在源工程 ITE 代码中是否有消费方；无则不迁入并记 TODO（design 待定项）
- [ ] 4.3 包内实现下载与解压（`UnityWebRequest` + sharp-zip-lib），替代宿主 `FileUtils.DownloadAndExtractZip`
- [ ] 4.4 包内实现资源加载：JSON 文本读取、Sprite（`Texture2D.LoadImage`）、AudioClip（`UnityWebRequestMultimedia`）、glb（gltfast）
- [ ] 4.5 迁移 `IteSpaceManagerAssets` 的管线逻辑：场景包下载解压 → 场景描述解析 → 逐 Tour 版本校验 → 内容包下载解压 → Tour 描述反序列化 → 实例化
- [ ] 4.6 版本缓存键改为 `ite.tour.{tourId}.version`（design D10），EditMode 测试锁定键名格式
- [ ] 4.7 离线路径：网络不可用时跳过全部下载与版本查询，直接读本地缓存
- [ ] 4.8 迁移场景资源加载（logo、各 Tour 预览图），保持原有的 fire-and-forget 时序

## 5. 运行时核心

- [ ] 5.1 迁移 `Entity`（Root 子对象 + `OnPreEnable` / `OnPreDisable`）
- [ ] 5.2 迁移 `IteTourObject`：`CreateTourObject`、`LoadAssets`、`CreateTourScene`、`DestroyTourScene`、`Enable`/`Disable`、二次锚定许可、`GetAsset`
- [ ] 5.3 `IteTourObject` 的锚点与偏移 Transform 改为由装配注入，移除 `FindGameObjectWithTag`
- [ ] 5.4 迁移 `IteSpaceManagerScan` 的 Tour 匹配与切换状态机：强制扫码、normal 切换、regionalTrigger 二次锚定、待扫描集合增删、进出重选
- [ ] 5.5 把 `OnVolumeTriggerEnter/Exit` 的相机识别从 tag `ARCamera` 改为与注入的相机 Transform 比对
- [ ] 5.6 把 `NeedsToShowAnchorPreviewUI` 协程的**决策**保留在包内，**渲染**改为广播 `OnScanPromptChanged`（design D5），删除对 `ScanPreviewUI` 的全部引用
- [ ] 5.7 移除 `OnGUI` 调试输出与 `_uiDirector` 开场动画（`PlayableDirector` / `SignalEvents` 属宿主 UI，不迁入）
- [ ] 5.8 EditMode 测试：Tour 匹配切换状态机的纯逻辑部分（强制扫码消费一次、暂停忽略推入、ID 无匹配、待扫描集合过滤、重复推入不去重）

## 6. 组件层

- [ ] 6.1 迁移 `BaseComponent` / `BaseElementComponent` / `BaseTriggerComponent` / `BaseActionComponent<T>`
- [ ] 6.2 迁移 4 个 Element：`EMWModelRender`(+Element)、`RichText`(+Element)、`VideoPlane`(+Element)、`PrimitiveModelRender`
- [ ] 6.3 `EMWModelRenderElement` 中 gltfast 类型写全限定名 `GLTFast.ComponentType.Camera` / `.Light`，消除与 `ITETourComponent.ComponentType` 的 CS0104 二义性（design D9）
- [ ] 6.4 `RichTextElement` / `VideoPlaneElement` 改用包内自实现的圆角 UI 组件（任务 2.3）
- [ ] 6.5 迁移 3 个 Trigger：`LoadTrigger`、`TapTrigger`、`ApproximateTrigger`。`VolumeTrigger` / `CustomGestureTrigger` 原为注释状态，不实现
- [ ] 6.6 迁移 4 个 Action：`PlayAnimationAction`、`PlayAudioAction`、`SpinAction`、`ToggleVisibilityAction`
- [ ] 6.7 迁移 `ComponentsUtils`（注册表机制 + 类型表，从 2.1 移来），确认 11 个组件类型键与源工程一致
- [ ] 6.8 `SpinActionUnityComponent.Oestroy()` 拼写错误：**原样保留**，记入 TODO（design D12）

## 7. 对外 API 面

- [ ] 7.1 定义 `IteBootstrap`：`Config` / `AnchorRoot` / `TourRoot` / `Camera`（必需）+ `IsNetworkAvailable`（可选，默认 true）
- [ ] 7.2 实现 `IteRuntime.Create` + `StartAsync`；必需项缺失时拒绝启动并记录具体缺失项，不抛空引用
- [ ] 7.3 实现推入方法 ×2：`SubmitMarkerScan(string, Pose)`、`SetHeadsetMounted(bool)`
- [ ] 7.4 实现广播事件 ×7：`OnLoadProgress` / `OnSpaceSceneLoaded` / `OnInitialized` / `OnTourActivated` / `OnTourDeactivated` / `OnTourSceneLoaded` / `OnScanPromptChanged`
- [ ] 7.5 确认公开 API 面**不含任何 `interface`**，且无必需的行为委托
- [ ] 7.6 无订阅者时全流程无空引用异常（事件均以 `?.Invoke` 触发）

## 8. Prefab 与资产

- [ ] 8.1 迁移 4 个 prefab 至 `Runtime/Prefabs/`：`Tour`、`Rich Text Element`、`Video Plane Element`、`EMW Model Render Element`
- [ ] 8.2 逐个打开 prefab 检查 Missing Script 与残留的宿主/Meta 组件引用，替换为包内等价实现
- [ ] 8.3 迁移 prefab 依赖的材质与图标资源（含圆角 UI 材质、视频播放/暂停图标、音频控制动画）
- [ ] 8.4 创建 `IteRuntimeConfig` 资产实例放在宿主 `Assets/`，默认引用包内 prefab

## 9. 宿主适配层

- [ ] 9.1 创建 `Assets/Scripts/IteHost/MRBase.Ite.Host.asmdef`，references 含 `MRBase.Localization`、`MRBase.Common`、`Uality.IteTour`
- [ ] 9.2 `MarkerSourceAdapter`：订阅 `IMarkerTrackingProvider.MarkerResolved`（经或不经 `MarkerStabilizer`）转发至 `SubmitMarkerScan`。**不经过 `MarkerAnchorService`**（design D13）
- [ ] 9.3 `HeadsetPresenceAdapter`：轮询 HMD 节点的 `CommonUsages.userPresence`，仅在状态边沿调用 `SetHeadsetMounted`
- [ ] 9.4 `HeadsetPresenceAdapter` 在平台不支持 `userPresence` 时记录并保持「已佩戴」，不阻断导览
- [ ] 9.5 `IteHostBootstrap`：装配 `IteBootstrap`、启动运行时、按需订阅广播事件
- [ ] 9.6 验证既有 `MarkerAnchorService` 消费链未被改动且仍正常工作

## 10. 场景接线与真机验证

- [ ] 10.1 在核心场景挂载适配层组件，接好锚点根、Tour 根、相机 Transform 与配置资产引用
- [ ] 10.2 Quest 真机：扫码激活 Tour、normal 切换、regionalTrigger 二次锚定、区域进出
- [ ] 10.3 Quest 真机：EMW 模型加载 + 动画 + 动画音频绑定；富文本图文与音频；视频播放
- [ ] 10.4 PICO 真机：同 10.2 的完整链路
- [ ] 10.5 PICO 真机：验证 `userPresence` 是否上报（design D7 风险）。不可用则改平台分支实现，**分支仍限于适配层内**
- [ ] 10.6 PICO 真机：验证 `VideoPlayer` 解码行为（design 风险表）
- [ ] 10.7 圆角 UI 自实现与源工程视觉并排比对
- [ ] 10.8 两端验证离线路径（关闭网络，走本地缓存）

## 11. 文档

- [ ] 11.1 `README.md`：包定位、装配示例、推入/广播 API 面、依赖列表
- [ ] 11.2 `Documentation~/TODO.md` 记录挂起项：
  - 多人共址（Colyseus）本次不迁，将来需补 `TryGetTourAnchor(out Pose, out string)`（design D6）
  - 空间场景 zip 无版本校验，联网每次冷启动重复下载（tour zip 有校验，两者不一致）
  - `SpinActionUnityComponent.Oestroy()` 拼写错误导致 `OnDestroy` 从未调用，事件与 `OnPreDisable` 未反注册
  - `MatchAndChangeTourCoroutine` 用 `OrderBy(Guid.NewGuid())` 在多个候选 regionalTrigger 中随机选取
  - 版本查询端点契约由外部约定，包只做配置化
  - `ItePropertiesUrl` 是否为死配置（任务 4.2 结论）
  - PICO `userPresence` / `VideoPlayer` 的真机结论（任务 10.5 / 10.6）
  - `Tour` 与 `Entity` 的坐标转换调用组合不一致，现为锁定的既有行为
- [ ] 11.3 `Documentation~/PORTING.md`：移植到其它工程所需的宿主契约（装配项、两个推入调用、依赖包清单）

## 12. 验收

- [ ] 12.1 对照 `specs/ite-tour-package/spec.md` 逐条勾选
- [ ] 12.2 grep 确认包内零 `MRBase.*`、零平台 SDK、零 `#if MRBASE_*`
- [ ] 12.3 确认公开 API 面接口数量为 0、必需行为委托数量为 0
- [ ] 12.4 确认包内零网络代码，工程未安装 `io.colyseus.sdk`
- [ ] 12.5 跑通包内全部 EditMode 测试
- [ ] 12.6 移植验证：把包目录复制到一个空白 Unity 工程，仅装 3 个依赖包，确认编译通过
