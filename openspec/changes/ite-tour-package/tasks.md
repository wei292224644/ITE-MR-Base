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
- [x] 3.1b 迁移 11 个组件的**可序列化数据类**与 3 个资源子类至 `Runtime/Data/Components/`（`ElementData` / `TriggerData` / `ActionData`），含各自的 `ComponentType` / `ComponentActionName` partial 常量。（实施时发现：转换器的多态分派表引用 18 个具体类型，源工程把数据类与 MonoBehaviour 混在同一文件内，属阶段 6；包只有一个程序集，无编译顺序约束，纯粹是任务切分问题。按数据/行为切开后，阶段 3 自足且可离机全测）
- [x] 3.2 迁移 3 个 JsonConverter（Asset / Component / ComponentAction），保持多态分派表完整
- [x] 3.3 EditMode 测试：3 个 Converter 对每种子类型的反序列化分派正确；未知类型的实际行为——`Asset` 与 `ComponentAction` 走默认分支不抛异常，`Component` 抛异常。（原任务文写的是"三者都不抛异常"，与源实现不符：`ComponentConverter` 未知类型返回 null 后 `Populate` 抛异常。测试按**实际行为**表征，把地雷钉住而不是假装它不存在）
- [x] 3.4 EditMode 测试：`Tour.Matrix4X4`（列式 4x4 + `ConvertToLeftHanded`）与 `Entity.Matrix4X4`（TRS + `ConvertToLeftHanded` + `FlipRotY`）的输出快照。**锁定当前行为，不"修正"两处调用组合的不一致**（design 风险表）

## 4. 配置与内容管线

- [x] 4.1 定义 `IteRuntimeConfig` ScriptableObject：空间场景地址、Tour 内容包地址模板、版本查询地址、场景名、3 个 element prefab 引用、Tour prefab 引用
- [x] 4.2 确认 `ItePropertiesUrl`（`projectDirectory.json`）的消费方。**结论：不是死配置**——`Assets/Scripts/UI/ItePropertiesDropdown.cs:21` 用它拉 `IteSpaces` 做「选择加载哪个空间场景」的下拉框。挑场景属宿主 UX，包的职责从「加载这个 sceneName」开始，因此该 URL **不进** `IteRuntimeConfig`，归宿主；`IteSpaces` 数据模型留在包内（它是 ITE 的线格式），宿主可直接复用
- [x] 4.3 包内实现下载与解压（`UnityWebRequest` + sharp-zip-lib），替代宿主 `FileUtils.DownloadAndExtractZip`。**有意偏离 D12**：补上 zip slip 路径穿越防护（`ZipEntryPath.TryResolve`）——zip 来自远端服务器属信任边界，安全防护不在「行为等价优先」的豁免范围内；源实现直接 `Path.Combine(outputFolder, entry.Name)` 无任何校验
- [x] 4.4 包内实现资源加载：JSON 文本读取、Sprite（`Texture2D.LoadImage`）、AudioClip（`UnityWebRequestMultimedia`）、glb（gltfast）
- [x] 4.5 迁移 `IteSpaceManagerAssets` 的管线**数据半段**：场景包下载解压 → 场景描述解析 → 逐 Tour 版本校验 → 内容包下载解压 → Tour 描述反序列化。（**实例化半段移至阶段 5**：源实现结尾要 `Instantiate(_tourObjectPrefab)` 再调 `IteTourObject.CreateTourObject`，而 `IteTourObject` 是 5.2；与 3.1b 同源的任务切分问题。切开后数据半段可离机全测）
- [x] 4.6 版本缓存键改为 `ite.tour.{tourId}.version`（design D10），EditMode 测试锁定键名格式
- [x] 4.7 离线路径：网络不可用时跳过全部下载与版本查询，直接读本地缓存
- [x] 4.8 迁移场景资源加载（logo、各 Tour 预览图），保持原有的 fire-and-forget 时序。（是否为此单独广播事件的决定挂在 7.4）

## 5. 运行时核心

> **执行顺序：第 6 节先于本节其余部分。** 真实依赖是 `Data(3) → Components(6) → TourObject(5.2) → Manager(5.4)`——`IteTourObject.CreateTourScene` 要调 `ComponentsUtils.GetComponent`（6.7）与 `BaseComponent.Constructor`（6.1）。编号是标识符，不代表执行次序；本节只有 5.1、5.4、5.8 不依赖第 6 节。

- [x] 5.1 迁移 `Entity`（Root 子对象 + `OnPreEnable` / `OnPreDisable`）
- [ ] 5.2 迁移 `IteTourObject`：`CreateTourObject`、`LoadAssets`、`CreateTourScene`、`DestroyTourScene`、`Enable`/`Disable`、二次锚定许可、`GetAsset`
- [ ] 5.2b 管线的**实例化半段**（从 4.5 移来）：逐 Tour 实例化 prefab、`CreateTourObject`、维护 liveTours、按 Tour 数推进进度、触发 `OnInitialized`
- [ ] 5.3 `IteTourObject` 的锚点与偏移 Transform 改为由装配注入，移除 `FindGameObjectWithTag`
- [ ] 5.4a 实现 `TourScanPolicy` **纯决策函数**（design D14）：输入当前状态 + Tour 描述表 + 标记 ID，输出 `Ignore | Activate | Reanchor` 及是否消费 `mustScan`、是否标记二次锚定。覆盖强制扫码、normal 切换、regionalTrigger 二次锚定、待扫描集合过滤
- [ ] 5.4b 实现区域进出决策：待扫描集合增删 + 进出后的激活重选（`MatchAndChangeTour` 的判定部分），同样是纯函数
- [ ] 5.4c 薄效果层：把决策落到 `IteTourObject` 上（`Enable`/`Disable`/`ChangeTourObjectTransform`/`SecondAnchored`），并从单一入口 `SubmitMarkerScan` 驱动。**不保留**源实现的三订阅者结构与 `_canAnchor` 依赖 `await` 时机的写法
- [ ] 5.5 把 `OnVolumeTriggerEnter/Exit` 的相机识别从 tag `ARCamera` 改为与注入的相机 Transform 比对
- [ ] 5.6 把 `NeedsToShowAnchorPreviewUI` 协程的**决策**保留在包内，**渲染**改为广播 `OnScanPromptChanged`（design D5），删除对 `ScanPreviewUI` 的全部引用
- [ ] 5.7 移除 `OnGUI` 调试输出与 `_uiDirector` 开场动画（`PlayableDirector` / `SignalEvents` 属宿主 UI，不迁入）
- [ ] 5.8 EditMode 测试：`TourScanPolicy` 的**完整决策面**（D14 使其可行）——暂停忽略推入、强制扫码消费一次、ID 无匹配、待扫描集合过滤、normal 切换、regionalTrigger 首次激活与二次锚定、已锚定后重复扫描不动、重复推入不去重、区域进出的集合增删与重选。**并单独钉住 D14 所选语义**：强制扫码激活 regionalTrigger 后，本次扫码不同时消耗其二次锚定许可

## 6. 组件层

- [ ] 6.1 迁移 `BaseComponent` / `BaseElementComponent` / `BaseTriggerComponent` / `BaseActionComponent<T>`
- [ ] 6.2 迁移 4 个 Element 的 **MonoBehaviour 部分**（数据类已在 3.1b 落地）：`EMWModelRender`(+Element)、`RichText`(+Element)、`VideoPlane`(+Element)、`PrimitiveModelRender`
- [ ] 6.3 `EMWModelRenderElement` 中 gltfast 类型写全限定名 `GLTFast.ComponentType.Camera` / `.Light`，消除与 `ITETourComponent.ComponentType` 的 CS0104 二义性（design D9）
- [ ] 6.4 `RichTextElement` / `VideoPlaneElement` 改用包内自实现的圆角 UI 组件（任务 2.3）
- [ ] 6.5 迁移 3 个 Trigger 的 **MonoBehaviour 部分**（数据类已在 3.1b 落地）：`LoadTrigger`、`TapTrigger`、`ApproximateTrigger`。`VolumeTrigger` / `CustomGestureTrigger` 原为注释状态，不实现
- [ ] 6.6 迁移 4 个 Action 的 **MonoBehaviour 部分**（数据类与参数类已在 3.1b 落地）：`PlayAnimationAction`、`PlayAudioAction`、`SpinAction`、`ToggleVisibilityAction`
- [ ] 6.7 迁移 `ComponentsUtils`（注册表机制 + 类型表，从 2.1 移来），确认 11 个组件类型键与源工程一致
- [ ] 6.8 `SpinActionUnityComponent.Oestroy()` 拼写错误：**原样保留**，记入 TODO（design D12）

## 7. 对外 API 面

- [ ] 7.1 定义 `IteBootstrap`：`Config` / `AnchorRoot` / `TourRoot` / `Camera`（必需）+ `IsNetworkAvailable`（可选，默认 true）
- [ ] 7.2 实现 `IteRuntime.Create` + `StartAsync`；必需项缺失时拒绝启动并记录具体缺失项，不抛空引用
- [ ] 7.3 实现推入方法 ×2：`SubmitMarkerScan(string, Pose)`、`SetHeadsetMounted(bool)`
- [ ] 7.4 实现广播事件：`OnLoadProgress` / `OnSpaceSceneLoaded` / `OnInitialized` / `OnTourActivated` / `OnTourDeactivated` / `OnTourSceneLoaded` / `OnScanPromptChanged`。**待决**：源工程另有 `OnLoadedIteSpaceSceneAssets`，因为 logo 与 Tour 预览图是 fire-and-forget 加载的、在 `OnSpaceSceneLoaded` 之后才就绪；而 spec 写的是该事件发出时「供宿主展示标题、图标与预览」。二者对不上。要么加第 8 个事件 `OnSpaceSceneAssetsLoaded`，要么让 `OnSpaceSceneLoaded` 等 sprite 加载完（改变既有时序）。在此处决定并同步 design 与 spec
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
  - **`ApproximateTrigger` 的近距离判定从未实现**：`Position`（且错用 `System.Numerics.Vector3` 而非 `UnityEngine.Vector3`）与 `Radius` 无任何消费方，组件只读 `Actions`，当前行为等同 `LoadTrigger`。**用户 2026-08-04 决定：先记录，后续单独改**
  - `ComponentConverter` 未知组件类型返回 null → `Populate` 抛 `ArgumentNullException` → 整个 Tour 加载失败（前向兼容地雷，修法是一句 null 判断）。**用户 2026-08-04 决定：当前可接受，保持现状**；已由 `ConverterTests` 中的 `..._KnownForwardCompatibilityLandmine` 钉住，将来修改时该测试会变红
  - 三个 Converter 的 `WriteJson` 与 `ReadJson` 键不对称（写 `"type"`，读 `"Type"` / `"Action"`）；`ComponentConverter.WriteJson` 只处理了 11 种中的 1 种。内容管线只做反序列化，这些路径当前无消费方
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
