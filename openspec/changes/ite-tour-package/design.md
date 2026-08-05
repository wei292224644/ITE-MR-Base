## Context

`ite-space-tour/Assets/Scripts/ITE` 共 3061 行，是 ITE 空间导览的完整业务：数据模型、3 个多态 JsonConverter、Tour 生命周期状态机、12 个运行时组件（4 Element / 3 Trigger / 4 Action + Base）。它当前零 asmdef、全局命名空间，并通过以下方式硬绑宿主与 Quest：

| 耦合点 | 形态 |
|---|---|
| `OVRManager.HMDMounted/HMDUnmounted` | Quest 专用静态事件 |
| `AnchorObject.Instance.whenQrCodeScanned` | 宿主单例，Quest QR 实现 |
| `MainConstants.Instance` | 宿主单例：3 个 URL、3 个 prefab 引用、4 个全局 Action、加载状态 |
| `SettingsManager.Gameplay` | 宿主单例：`sceneName`、`isNetworkAvailable` |
| `ScanPreviewUI.Instance.Show()/Hide()` | 宿主 UI 单例 |
| `FileUtils.Instance` / `FetchUtils` | 宿主工具单例 |
| `ColyseusClientManager.Instance` | 宿主网络单例 |
| tag `ARCamera` / `AnchorObject` / `AnchorOffsetObject` | 场景结构约定 |
| `RoundedBoxUIProperties` | **Meta SDK sample**（见 D8） |

本项目（MR_Base，Unity `6000.4.4f1`）已有 8 个 asmdef 的模块化骨架、`MRBASE_QUEST` / `MRBASE_PICO` 双 define 轴、`PlatformRuntime` passthrough 抽象，以及 `cross-platform-marker-tracking` 产出的 `IMarkerTrackingProvider`（Quest MRUK QR + PICO ArUco 两端实现）。地基已具备，缺的是把 ITE 摘下来的边界。

目标：ITE 可跨平台（Quest + PICO）、可与其它模块共存、可整体移植到别的工程。

## Goals / Non-Goals

**Goals:**

- ITE 业务成为自包含 package，程序集引用面零 `MRBase.*`、零平台 SDK
- 平台差异 100% 落在宿主适配层，包内无任何 `#if MRBASE_*` / `OVR*` / `PXR_*`
- 外部交互全部走事件与委托，调用方不实现任何 `interface`
- 迁移后 ITE 业务语义与原项目等价
- 可离机测的部分（转换器、坐标数学、匹配状态机、缓存键）有 EditMode 覆盖

**Non-Goals:**

- 多人共址网络（Colyseus）——见 D6
- 改动既有 `MarkerAnchorService` 消费链
- 把 `MRBase.Common` / `MRBase.Localization` 提升为 package
- 拆独立 git 仓库 / submodule
- 顺手修复迁移中发现的历史缺陷——见 D12

## Decisions

### D1：embedded package，不是 asmdef

**决定**：`Packages/com.uality.ite-tour/`，单一运行时程序集 `Uality.IteTour`。

**理由**：三个目标里 asmdef 能满足前两个（编译期隔离、模块共存），唯独「可移植」满足不了——asmdef 无法阻止后来者往 `references` 里加一行 `MRBase.Localization`，加完编译通过、功能正常、无任何告警，直到真要移植那天才发现拖着隐藏依赖。

Unity 强制 `Packages/` 下程序集不得引用 `Assets/` 下程序集（编译顺序 Packages 先于 Assets）。这条硬规则是唯一能让「可移植」不依赖人的记性维持住的机制：**违反在写下的那一刻编译失败，而不是移植的那一天。**

代价接近零：适配层本来就要写（外部服务以接口/hook 嵌入是既定要求），package 边界只是给已经打算写的那层加了强制力。

**替代**：`Assets/Scripts/ITE/` + asmdef——否决，理由如上。

**注**：UPM 不支持 `^` / `~` 语义范围，`package.json` 中的版本号语义是「最低版本」，依赖图取最高请求值。不存在 npm 的 lockfile 漂移问题。

### D2：依赖方向单向，适配层落在宿主

```
┌── Packages/com.uality.ite-tour/ ─────────────────────┐
│                                                       │
│  数据 / 转换器 / 内容管线 / Tour 状态机 / 12 个组件    │
│  内部工具 / 4 个 prefab / 圆角 UI 实现                 │
│                                                       │
│  引用面：Unity 官方包 only                             │
│    newtonsoft-json · gltfast · sharp-zip-lib          │
│    video · unitywebrequest* · ugui · animation · audio│
│                                                       │
│  对外：IteRuntime（推入 ×2 / 广播 ×7 / 装配 ×1）       │
└───────────────────────▲───────────────────────────────┘
                        │ 单向（包对宿主零知识）
┌───────────────────────┴───────────────────────────────┐
│  Assets/Scripts/IteHost/  —— MRBase.Ite.Host           │
│                                                        │
│  MarkerSourceAdapter    IMarkerTrackingProvider        │
│                            → ite.SubmitMarkerScan      │
│  HeadsetPresenceAdapter OpenXR userPresence            │
│                            → ite.SetHeadsetMounted     │
│  IteHostBootstrap       装配 + 事件订阅 + 生命周期      │
│                                                        │
│  引用：MRBase.Localization · MRBase.Common · Uality.IteTour
└────────────────────────────────────────────────────────┘
```

**理由**：Packages→Assets 的引用禁令使这个方向成为唯一可能，而它恰好就是正确方向。所有平台 `#if` 都在适配层，包内一处没有。

### D3：外部 API = 推入方法 + 广播事件 + 装配对象，接口数量 0

**决定**：

```csharp
// 装配（一次）
var ite = IteRuntime.Create(new IteBootstrap {
    Config     = iteRuntimeConfigSO,   // 必需
    AnchorRoot = anchorTransform,      // 必需，原 tag "AnchorObject"
    TourRoot   = offsetTransform,      // 必需，原 tag "AnchorOffsetObject"
    Camera     = xrCameraTransform,    // 必需，原 tag "ARCamera"
    IsNetworkAvailable = () => bool,   // 可选，默认 true
});

// 宿主推入 ×2
ite.SubmitMarkerScan(string markerId, Pose pose);
ite.SetHeadsetMounted(bool mounted);

// 包广播 ×7
event Action<float>          OnLoadProgress;
event Action<IteSpaceScene>  OnSpaceSceneLoaded;      // 元数据就绪（含 logo / 预览图）
event Action                 OnInitialized;           // 全部 tour 实例化完成
event Action<string>         OnTourActivated;
event Action<string>         OnTourDeactivated;
event Action<string>         OnTourSceneLoaded;       // tour 内 entity 树构建完
event Action<ScanPromptState, string[]> OnScanPromptChanged;

await ite.StartAsync();
```

**理由**：两个方向语义不同，形态也应不同。

- **包 → 外（通知）**：纯广播、无返回值 → `event` 完美
- **外 → 包（提供能力）**：请求-应答、有返回值 → `event` 不适用（多播委托只取最后一个返回值；且**泛型方法无法声明为委托字段**——`Task<T> LoadJson<T>()` 接口能表达，`Func<string, Task<T>>` 的 `T` 无处声明）

但方向二可以整体消灭：见 D4。消灭之后剩下的「外 → 包」只有两条**单向推送**（扫码结果、佩戴状态），单向推送不需要契约，一个 public 方法就够——连接口都不需要。

**替代**：为每类能力定义 `IIteMarkerSource` / `IIteAssetIO` / `IIteSessionEvents` / `IIteNetworkSession`——否决，全部是伪需求（D4/D6）或过度形式化。

### D4：IO 边界下移到字节，内容管线整体归包

**决定**：包不向宿主索取任何 IO 能力，自带完整内容管线：

```
空间场景 zip ──下载──解压──► {scene}.json
                                  │
每个 tour：查服务端最新版本 ──► 比对本地缓存版本
              │                        │
           版本不同                 版本相同
              ▼                        │
        下载 tour zip ──解压───────────┤
              │                        │
        写入版本缓存                    │
              └────────────┬───────────┘
                           ▼
        {tourId}.json ──Newtonsoft + 3 个 Converter──► IteTour
                           ▼
        资源加载：glb / mp3 / png / mp4
                           ▼
        实例化 TourObject + Entity + Component 树
```

**理由**：重新盘点后发现宿主的 `FileUtils` / `FetchUtils` 大部分是**伪需求**——不是包做不到，只是它恰好在那里：

| 能力 | 包内靠什么 |
|---|---|
| JSON 反序列化 | Newtonsoft + 包内 Converter（**Converter 本就是包的内部知识，不该泄漏给宿主**）|
| 图片 / Sprite | `Texture2D.LoadImage`（内置）|
| 音频 | `UnityWebRequestMultimedia`（内置模块）|
| glb | gltfast（包依赖）|
| 视频 | `VideoPlayer`（内置模块）|
| HTTP | `UnityWebRequest`（内置）|
| zip 解压 | `com.unity.sharp-zip-lib`（包依赖，见下）|

zip 解压是唯一原本需要外部提供的能力。决策为**包直接依赖 `com.unity.sharp-zip-lib`**（Unity 官方包、纯托管、无平台风险），理由是内容分发协议属 ITE 业务范畴，宿主不该知道 tour 是以 zip 分发的。代价是宿主被迫多装一个官方包。

结果：装配对象里**一个行为委托都不必需**，`IsNetworkAvailable` 是唯一可选委托（离线模式是宿主的产品策略，不是 ITE 的）。

### D5：扫码提示 UI 的依赖方向反转

**决定**：删除对 `ScanPreviewUI` 的调用，改为广播 `OnScanPromptChanged(state, tourIds[])`。

```
原：包 ──调用──► 宿主 UI 单例        包必须知道 UI 存在
新：包 ──广播──► （宿主可选订阅）    不订阅也不影响功能
```

**理由**：原 `NeedsToShowAnchorPreviewUI` 协程每 0.75s 计算「该不该显示提示、显示哪些 tourId」——这个**决策**是 ITE 业务，**渲染**不是。把决策结果广播出去，宿主爱画什么画什么。依赖随方向反转一并消失。

### D6：网络能力不进包

**决定**：`Assets/Scripts/Network/`（13 文件 2313 行）与 `IteSpaceManagerNetwork.cs` 均不迁入，`io.colyseus.sdk` 不安装。为其预留的 `OnFirstMarkerScanned` / `OnTourAnchorChanged` / `TryGetTourAnchor` 一并不实现。

**背景**：该模块是**多人共址（co-location）**——MR 里两台头显各自的世界原点互不相干，ITE 拿 QR 码当共享原点解决对齐：

```
本地上报    offset = Inverse(anchorRot) * (myWorldPos - anchorPos)   世界 → 锚点局部
远端还原    target = anchorPos + anchorRot * offset                  锚点局部 → 我的世界
```

**理由**：依赖方向本就是「网络 → ITE」——`PlayerNetworkSync.cs:132` 与 `Role.cs` 都调 `IteSpaceManager.Instance.GetFirstTourObject()` 取锚点，而 ITE 完全不需要网络。把一个下游消费者塞进上游包是倒置。用户本次亦明确不需要该能力。

将来要做时，包只需补一条只读参考系查询（`TryGetTourAnchor(out Pose, out string)`），宿主网络层拿 Pose 自行换算，仍拿不到包的任何内部对象。记入 TODO。

### D7：头显佩戴状态用 OpenXR 通用路径，不分平台

**决定**：适配层用 `InputDevice.TryGetFeatureValue(CommonUsages.userPresence)` 轮询 HMD 节点，边沿变化时调 `ite.SetHeadsetMounted(bool)`。不使用 `OVRManager.HMDMounted` / `PXR_Plugin.System`。

**理由**：一套实现覆盖 Quest 与 PICO，适配层无需分叉。`OVRManager` 路线会立刻在 PICO 上失效，而 PICO 私有 API 又要求再写一份。

**风险**：PICO 侧 `userPresence` 的上报行为无既有实证，需真机验证。若不可用，退回平台分支实现（此时分支仍只在适配层内）。记入 TODO 与验收任务。

**注**：包内该状态的业务语义保持原样——摘下时禁用当前 tour 并置暂停；戴回时解除暂停并要求重新扫码（`_mustScanQrCode = true`）。

### D8：圆角 UI 自实现，切断 Meta SDK sample 依赖

**决定**：包内自带圆角矩形 UI 实现（`IMeshModifier` 把半径写入 `uv1` + SDF shader 读取），不引用 Meta 的 `RoundedBoxUIProperties`。

**理由**：探查发现该类型来自
`com.meta.xr.sdk.interaction/Runtime/Sample/Scripts/ComprehensiveRigExampleUI/RoundedBoxUIProperties.cs`（96 行）+ 配套 `RoundedBoxUI.shader`（173 行）。三重问题：

1. **平台**：Meta SDK 是 Quest 专用，包依赖它等于放弃 PICO
2. **可用性**：`com.meta.xr.sdk.interaction` 不在 MR_Base 的 manifest 里（本项目只有 `sdk.core` 与 `mrutilitykit`）
3. **来源**：Meta SDK 的 `Runtime/Sample/` 属示例代码，受 Oculus SDK 许可约束，复制进可移植包不合适

`RichText` 与 `VideoPlane` 两个 element 都用它（`.borderRadius = Vector4`），漏掉会导致运行时渲染异常。实际耦合面只有一个 `Vector4` 属性，SDF 圆角矩形是标准技法，自实现规模可控。

**替代**：9-slice 圆角图——否决，`CornerRadius` 是每个 asset 数据驱动的，9-slice 做不了动态半径。砍掉圆角——否决，改变既有视觉表现。

### D9：命名空间收敛，显式处理 `ComponentType` 二义性

**决定**：全部代码收入 `Uality.IteTour.*`（`.Data` / `.Convert` / `.Components` / `.Internal` / `.Config`）。`EMWModelRenderElement` 中的 gltfast 类型写全限定名。

**理由**：`EMWModelRenderElement.cs` 有

```csharp
Mask = ~ComponentType.Camera & ~ComponentType.Light   // 这是 GLTFast.ComponentType
```

而 ITE 自己也定义了 `ITETourComponent.ComponentType`（装 `"EMWModelRender"` 等字符串常量的 partial class）。原项目侥幸没炸是因为文件在全局命名空间且只 `using GLTFast`。一旦两者收进同一可见作用域 → **CS0104 二义性，编译失败**。改为 `GLTFast.ComponentType.Camera`。

### D10：持久化键加命名空间

**决定**：版本缓存键从裸 `tourId` 改为 `ite.tour.{tourId}.version`。

**理由**：原 `PlayerPrefs.SetString(tour.tourID, version)` 直接占用全局 key 空间。包既然要与其它模块共存、要移植进任意工程，不能污染宿主的 key 命名空间。这是移植性的必要条件，不是洁癖。

### D11：prefab 随包走，放 `Runtime/Prefabs/`

**决定**：4 个 prefab（`Tour` / `Rich Text Element` / `Video Plane Element` / `EMW Model Render Element`）迁入 `Runtime/Prefabs/`，由 `IteRuntimeConfig` 默认引用，宿主可覆盖。不放 `Samples~/`。

**理由**：`Samples~` 语义是「可选示例」，而 ITE 没有这些 prefab 就根本不能运行——它们是运行时资产，不是样例。放 `Samples~` 会让「装了包但没导入样例」变成一类运行时 null 故障。

### D12：行为等价是默认，不是最高优先级（2026-08-04 修订）

**决定**：迁移的默认目标是语义等价，发现的既有缺陷写进 `Documentation~/TODO.md` 不顺手修。**但当既有结构本身是问题时，重构优先于照搬**——判据是结构是否正确，不是改动是否最小（见 `.claude/CLAUDE.md` 的决策原则）。

触发重构而非照搬的情形：

- 行为依赖**隐式**因素：调用顺序、标志位被谁先清、`await` 会不会真的挂起
- 同一份代码因数据不同走出**不同语义**（而非不同结果）
- 跨平台会走出不同时序，且故障只能在真机上复现
- 信任边界缺输入校验、缺防止数据丢失的错误处理

**理由**：默认行为等价，是为了让迁移与修 bug 保持可分离——这本身是结构理由，不是省事。但当"等价"意味着把一处偶然行为固化成契约时，它就不再服务于结构，反而在掩盖问题。

**本次已按此判据偏离的地方**：D8（切断 Meta SDK sample 依赖）、4.3（补 zip slip 防护）、D14（扫描决策抽为纯状态机）。每处都在本文件或 `tasks.md` 中单独记录。

**原始理由（保留）**：迁移与修 bug 混在一起，出问题时无法区分是搬错了还是改坏了。

已发现待记录项：

- 空间场景 zip **无版本校验**，只要联网每次冷启动都重新下载解压（tour zip 有校验，两者不一致）
- `SpinActionUnityComponent.Oestroy()` 拼写错误，`OnDestroy` 从未被调用 → 事件与 `OnPreDisable` 未反注册
- `MatchAndChangeTourCoroutine` 用 `OrderBy(t => Guid.NewGuid())` 在多个候选 regionalTrigger 中随机选一个
- 硬编码域名 `https://api.uality.cn/ITE/Tour/LatestVersion`（本次配置化，但端点契约仍是外部约定）

### D13：ITE 消费原始 Marker 事件流，不接 `MarkerAnchorService`

**决定**：适配层订阅 `IMarkerTrackingProvider.MarkerResolved`（必要时经 `MarkerStabilizer`）转发给 `ite.SubmitMarkerScan`，**不经过** `MarkerAnchorService`。

**理由**：语义不匹配。ITE 需要**可重复触发的原始事件流**——`regionalTrigger` 类型的 tour 允许扫第二次做二次锚定，`ChangeTourObjectTransform` 每次都要新 pose。而 `MarkerAnchorService` 是「首次稳定 → 创建实体 → `activeAnchorIds` 去重 → 之后永不再触发」。

```
IMarkerTrackingProvider ──► MarkerStabilizer ──┬──► MarkerAnchorService ──► AnchorEntity
      (原始流 ✅)                               │        (去重 ❌)
                                                └──► IteHost → ite.SubmitMarkerScan ✅
```

两条并行消费链互不影响，既有 `MarkerAnchorService` 零改动——这也正是「与其它模块同时使用」的实证。

### D14：扫描决策抽为纯状态机，不照搬三处理器 + async 竞态（2026-08-04）

**决定**：`SubmitMarkerScan` 只做一件事——把当前状态与标记 ID 交给纯函数 `TourScanPolicy`，拿回一个显式决策，再由薄效果层执行。不保留源实现的「三个订阅者依次执行 + `_canAnchor` 在 `await` 之后赋值」结构。

```
源：  whenQrCodeScanned ──┬─► OnMustScanQrCodeScanned   （命中则 ChangeTour，并清 _mustScanQrCode）
                          ├─► OnQrCodeScanned           （开头查 _mustScanQrCode —— 但刚被上一个清掉）
                          └─► OnNetworkQrCodeScanned    （本次不迁）

本次：SubmitMarkerScan(id, pose)
        └─► TourScanPolicy.Decide(state, tours, id) → Ignore | Activate | Reanchor   ← 纯函数，可测
        └─► Apply(decision, pose)                                                     ← 薄效果层
```

**被否决的替代方案（方案 A：照搬）**：包内保留三个私有处理器并依次调用。否决理由——

1. **行为写在相互作用里，代码里没有一处说明意图**。第一个处理器清掉 `_mustScanQrCode`，导致第二个在**同一次扫码**里也会执行；第二个是否生效又取决于 `IteTourObject.Enable()` 中 `_canAnchor = true` 是否已在 `await CreateTourScene()` 之后跑到。
2. **同一份代码因数据不同走出不同语义**。`CreateTourScene` 内的 `await` 只在实体/组件存在时真正挂起。有内容的 Tour → 第二个处理器空操作；**空 Tour 会同步走完** → 第二个处理器执行重锚并消耗二次锚定许可。这不是推测，是 C# async 的确定规则：没有真正挂起的 `await` 不让出控制流。
3. **跨平台会走出不同时序**。Quest 走 MRUK QR Trackable 持续回调；PICO 按 `cross-platform-marker-tracking` 的结论是「扫一次 QR → 等同 ID 的 ArUco」。A 的行为依赖回调节奏与 async 完成时机的相对关系，换平台即换一套时序，而这类故障在真机上表现为「偶尔锚不上」。
4. **A 把宿主事件总线的形状留在了包里**。三处理器是宿主 C# event 多播的产物，不是 ITE 的业务结构；包对外已经收敛成单一推入方法，内部再维持多订阅者形状是一个已不存在之物的影子。

**所选语义**：采用源实现在**内容异步加载路径**下的行为作为规范——即强制扫码激活某 Tour 后，本次扫码**不**同时消耗其二次锚定许可。该路径是真机上的常规情况（Tour 总有内容、总要加载 glb）。空 Tour 在源实现中因同步完成而走出另一分支，本次统一为前者。

**这不是改行为，是把偶然固化成契约。** 常规路径与源实现一致；边界情况从「碰巧」变成「明确规定」。

**已知局限**：纯函数层可离机测死，但「决策 → 效果」的接线仍需真机确认（任务 10.2 / 10.4）。可测面从约 0 提到约七成，不是十成。

**实现期发现（正是 D14 要防的东西）**：源实现里 `SecondAnchored()` 在「扫码激活一个 regionalTrigger Tour」这个调用点上是**死代码**。

```
OnQrCodeScanned 的 else 分支：  ChangeTour(tour);  tour.SecondAnchored();

ChangeTour → EnableActiveTour → _ = newTour.Enable()      // async 启动，不等待
                                     └─ await CreateTourScene()
                                     └─ _canAnchor = true       ← 稍后执行
             ↓
tour.SecondAnchored()          →  _canAnchor = false            ← 先执行
             ↓
        （await 完成）          →  _canAnchor = true             ← 把上面那行覆盖掉
```

按 D14 所选语义（以内容异步加载路径为规范），结论是：**扫码激活 regionalTrigger 不消耗其二次锚定许可**。二次锚定许可只在「目标就是当前激活 Tour」的重锚路径上被真正消耗——那条路径不触发 `Enable()`，所以置位不会被覆盖。

照搬方案会把这个失效调用当成有意义的语义原样保留，并写进测试，从而把一处竞态固化成契约。

**顺带**：`IteTourObject.Disable()` 中 `ResetSecondAnchor()`（置 `_canAnchor = true`）紧接着被 `_canAnchor = false` 覆盖，同样是死调用。迁移时不保留这行无效果的调用。

### D15：组件层与 `IteTourObject` 的循环引用**保留**，靠任务次序落地（2026-08-05）

`BaseComponent._iteTourObject` ↔ `IteTourObject.CreateTourScene`（要调 `ComponentsUtils` 与 `BaseComponent.Constructor`）构成类型循环。

**选定**：保留循环，把任务拆成无环的三步落地——`IteTourObject` 外壳（5.2，不含 `CreateTourScene`）→ 组件层（第 6 节）→ 回填 `CreateTourScene`（5.2c）。同一程序集内的类型循环是合法 C#，不影响编译，只影响任务次序。

**否决的替代**：抽 `ITourContext` 之类的接口切断循环。组件从 Tour 对象拿的只有三样：`.transform`（路由用）、`OnTourSceneLoaded`、`GetAsset(id)`。为这三个方法造一个**只有一个实现**的接口，正是 CLAUDE.md 判据里点名拒绝的形状；换来的只是任务可以并行，而任务次序本来就免费。

**顺带删掉**：`IteTourObject.CanAnchor` 属性。它在源工程里无人调用，却是 `IteTourObject` 对 `IteSpaceManager.Instance` 单例的唯一依赖——删掉之后 Tour 对象不再反向依赖管理器，「谁是当前 Tour」只由 `TourScanPolicy` 判定（D14）。

**顺带删掉**：`ResetSecondAnchor()`。D14 已论证它在 `Disable()` 里的唯一调用是死调用，删掉调用后它就没有调用方了。

### D16：资源相对路径抽成纯函数 `TourAssetPaths`，且一律用 `/`（2026-08-05）

源实现把七处 `Path.Combine` 散在 `IteTourObject.LoadAssets` 的 switch 分支里。两个问题：

1. **路径拼错只表现为「资源没出现」**，且必须上机才看得到——`ContentAssetLoader` 按约定对缺文件静默返回 null。
2. `Path.Combine` 在 Windows 编辑器下拼出反斜杠，与 Android 真机不一致。属于「跨平台走出不同行为、且故障只在真机复现」，是 CLAUDE.md 里列明的重构触发条件。

**选定**：抽成 `TourAssetPaths` 纯静态函数，一律用 `'/'`（Unity 全平台路径 API 都接受），7 个 EditMode 测试钉住形状。期望值取自源实现，不取自本包实现。

固化下来的两处**不一致但有意保留**的形状：
- EMW 音频在**资源目录**下，而 publish.json / glb 在**版本目录**下
- 富文本位图在 `raster/` 子目录下，音频不在

**一处偏离**：`LoadEmwModelAsset` 对 `json == null` 与 `json.audio == null` 加了判空。源实现在这两处会 NRE，导致整个 Tour 加载失败。`ContentAssetLoader` 的契约明确写着「缺文件静默返回 null」，源实现没有兑现这个契约的消费端；补齐属于「信任边界缺防止数据丢失的错误处理」。

### D17：动作派发抽成静态函数，非泛型 `BaseActionComponent` 收敛为泛型实例化（2026-08-05）

**派发**：源实现的 `BaseTriggerComponent.EmitActions()` 是实例方法，靠 `Awake` 填的 `_iteTourObject.transform` 取 Tour 根节点。EditMode 不跑 `Awake`，整段路由逻辑（按 `EntityId` 在 `Group/` 下找实体、投递到它的 `EventEmitter`）因此无法离机验证。

抽成 `static Dispatch(Transform tourRoot, IReadOnlyList<ComponentAction>)`，`EmitActions()` 退化成一行转发。三个触发器共用，规则本身可测。

顺带补了两处判空：目标实体不存在、或实体上没有 `EventEmitter` 时跳过而非抛异常——Tour 描述引用已删除的实体是正常情况，不该让整个触发器炸掉。

**非泛型基类**：源工程的 `BaseActionComponent` 与 `BaseActionComponent<T>` 是逐行重复的两个类。改为 `BaseActionComponent : BaseActionComponent<BaseElementComponent>`，行为一致而只有一份实现。

### D18：动画配音映射越界跳过，不打死整个 Tour（2026-08-05）

源实现在 `EMWModelRenderElement.Constructor` 里内联了一段双重下标：`clips[i]` 配 `json.audio[animationAudio[i].audio]`。两个下标**都直接取自服务端数据**，任一越界抛出 `IndexOutOfRangeException`，向上冒泡打死整个 Tour 的加载；`animationAudio` 为 null 则 NRE。

**选定**：抽成纯函数 `AnimationAudioMap.Build`，越界与 null 条目跳过。一条配错的配音不该有「整个导览打不开」这么大的杀伤力。属于「信任边界缺输入校验」，是 CLAUDE.md 列明的重构触发条件。

4 个 EditMode 测试钉住：正常配对、配音多于动画、音频下标越界、无配音配置。

### D19：几何体的形状决策收敛成一处（2026-08-05）

源实现把「是什么形状」查了两次，且两处口径不一致：

| | 未知类型 |
|---|---|
| `GetPrimitiveType()` | 兜底成 `PrimitiveType.Cube` |
| 设置缩放的 switch | 只打警告，**不设缩放** |

合起来的实际行为是「Cube + 默认 (1,1,1)」——但这个行为是两处 switch 各说各话**碰巧**凑出来的，改任何一处都会悄悄改变另一半。

**选定**：一次 `PrimitiveShapes.Resolve(string) → PrimitiveShape`，两个投影 `ToUnity`（未知→Cube）与 `TryGetLocalScale`（未知→false，调用方保持默认缩放）。行为与源实现逐点一致，但决策只有一处。11 个测试钉住，含大小写与 null。

顺带修掉 `PrimitiveType` 为 null 时 `ToLower()` 的 NRE。

## Risks / Open Questions

| 项 | 风险 | 缓解 |
|---|---|---|
| PICO `userPresence` | 无实证，可能不上报 | 真机验证；失败则平台分支，仍限于适配层 |
| 圆角 shader 自实现 | 视觉与原表现有差异 | 与原项目并排比对；仅两个 element 受影响 |
| gltfast 6.15.1 → 6.19.0 | 同大版本，API 应兼容 | 迁移后立即验证 glb 加载 + 动画 + 音频绑定 |
| PICO 上的 `VideoPlayer` | 视频解码行为未验证 | 真机验证，记 TODO |
| 坐标系转换 | `ConvertToLeftHanded` / `FlipRotY` 的调用组合在 `Tour` 与 `Entity` 两处不一致，属既有行为 | 原样搬运 + 单测锁定当前输出，不"修正" |
| 4 个 prefab 的隐藏引用 | 可能还挂着未发现的宿主/Meta 脚本 | 迁移后逐个打开检查 Missing Script |

**待定**：`IteRuntimeConfig` 中的 `ItePropertiesUrl`（`projectDirectory.json`）在现有 ITE 代码中未见消费，迁移时确认是否为死配置。
