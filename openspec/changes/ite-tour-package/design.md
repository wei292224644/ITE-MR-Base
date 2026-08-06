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
event Action<IteSpaceScene>  OnSpaceSceneLoaded;      // 描述解析完，图未就绪（D28）
event Action<IteSpaceScene>  OnSpaceSceneAssetsLoaded;// logo 与预览图就位（D28）
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

**唯一的例外，且不违背本条**（2026-08-06 核验）：`Data.Component` 是个只有 `ComponentType` 的**数据判别器**，用于组件列表的多态反序列化。它由包内 11 个数据类实现，宿主只读不实现——不是行为契约。抽象类替不掉它：`PlayAnimationAction : PlayAnimation, Component` 这类已经继承了参数基类，C# 单继承下没有第二个位置。本条禁的是「让宿主实现行为」的接口，不是数据形状。

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

- ~~`ComponentConverter` 未知类型抛异常~~ → 已按 D24 修正
- ~~版本查询失败 NRE~~ → 已按 D25 修正
- 空间场景 zip **无版本校验**，只要联网每次冷启动都重新下载解压（tour zip 有校验，两者不一致）
- `SpinActionUnityComponent.Oestroy()` 拼写错误，`OnDestroy` 从未被调用 → 事件与 `OnPreDisable` 未反注册
- `MatchAndChangeTourCoroutine` 用 `OrderBy(t => Guid.NewGuid())` 在多个候选 regionalTrigger 中随机选一个
- 硬编码域名 `https://api.uality.cn/ITE/Tour/LatestVersion`（本次配置化，但端点契约仍是外部约定）
- `PlayAudioAction` 的 `Repetition` / `Delay` / `AutoPlay` 解析出来但**无任何消费方**，触发时一律直接 `PlayAudio()`
- `EMWModelRenderElement` 存下 `_onTap` 却**从不触发**（模型上没有 Button，也没有射线命中回调）→ EMW 模型上挂 `TapTrigger` 不生效
- `PrimitiveModelRenderUnityComponent` 继承 `BaseComponent` 而非 `BaseElementComponent` → 几何体上挂 `TapTrigger` 会报错退出
- 几何体挂在**组件自身的 transform** 下而不是 `Entity.Root` 下 → `Entity.SetActive(false)` 隐不掉它，`ToggleVisibilityAction` 对几何体无效

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

### D20：元素 prefab 由 `IteTourObject` 预制体持有，不进 Resources、不进 `IteRuntimeConfig`（2026-08-05）

源实现用 `MainConstants.Instance.IteTourElementRichTextPrefab` —— 宿主单例。包里不能留这个形状。

问题的根子在于：元素组件是 `CreateTourScene` 用 `AddComponent(componentType)` **运行时挂上去**的，运行时添加的组件拿不到序列化引用。所以 prefab 只能来自某个**被作者化过的祖先**。

包里唯一被作者化的祖先就是 `IteTourObject` 预制体（D11 已定 prefab 随包走）。于是：

```
IteTourObject.prefab
  └─ [SerializeField] IteTourElementPrefabs _elementPrefabs   ← 作者时连线，随包发布
       ├─ EmwModelRender
       ├─ RichText
       └─ VideoPlane
                ↑
     组件通过已持有的 _iteTourObject 读取
```

**否决 `Resources.Load`**：Unity 官方最佳实践明确反对 —— Resources 目录整体进包、启动时统一反序列化、无法按需卸载；更要命的是**包往宿主的 Resources 命名空间里塞路径**，多个包之间会撞名。路径还是字符串，没有编译期检查。

**否决 Addressables**：这是 Unity 当前对运行时资源加载的官方答案，但它要求宿主建 Addressable 组、加一条 `com.unity.addressables` 依赖，与验收项 12.6「拷进空工程、只带 3 条依赖、能编译」直接冲突。三个 prefab 不值这个代价。

**否决放进 `IteRuntimeConfig`**：那是**宿主面向**的配置（URL、场景名）。元素 prefab 是包的内部实现，放进去等于邀请宿主去改它。

**否决独立 ScriptableObject 目录**：会多出一份要发布、要连线、要保持同步的资产，而这些 prefab 与 Tour 对象一一对应、不跨所有者共享、也不需要按宿主换皮。多一个资产就多一处能连断的地方。用嵌套的 `[Serializable]` 类，预制体之间的引用在导入期解析，运行时零加载。

**否决改 `Constructor` 签名**：把 prefab 目录作为参数传下去更显式，但会让 11 个组件都背上一个只有 3 个用得着的参数。

**顺带说明为什么不把元素折叠成独立 prefab**：看似能一步到位（prefab 自带引用），但触发器与动作组件靠**与元素组件同挂一个 GameObject** 来工作（`BaseActionComponent.GetComponent<T>()`、`EventEmitter` 的实体级路由）。折叠会破坏这个共址契约。当前的两层结构是被共址设计逼出来的，不是随手写的。

### D21：视频的 `RenderTexture` 在停用时释放（2026-08-05）

`VideoPlaneElement.Constructor` 每次都 `new RenderTexture(width, height, 0)`；`OnDisable` 只把 `targetTexture` 置空并销毁 `VideoPlayer`，**从不释放这张贴图**。

1080p 一张约 8MB 显存。Tour 每激活一次泄一张，头显上的显存预算撑不住几轮切换。这是「缺防止资源泄漏的清理」，属 CLAUDE.md 列明的重构触发条件。

**选定**：`OnDisable` 里 `Release()` + `Destroy()`，字段置空。

### D22：动作参数的合并规则抽成纯值类型（2026-08-05）

四个动作各写各的 `ProcessData`，而它们的**口径并不一致**——这件事在源码里完全看不出来，却决定了「连续触发两次、第二次只带部分参数」时的结果：

| 动作 | 缺省字段 | 效果 |
|---|---|---|
| `Spin` / `PlayAudio` | `?? 当前值` | 沿用上次，**状态跨次累积** |
| `PlayAnimation` / `ToggleVisibility` | `?? 固定默认值` | 每次重置 |

同一族组件对同一件事有两种语义，而两种都藏在 MonoBehaviour 的私有字段里，`Awake` 在 EditMode 不跑，一行都测不了。属于「同一份代码因数据不同走出不同语义」。

**选定**：抽成四个纯值类型（`SpinSettings` / `PlayAudioSettings` / `PlayAnimationSettings` / `ToggleVisibilitySettings`），累积口径给 `Merge`（实例方法，从当前值出发），重置口径给 `From`（静态，从固定默认值出发）——**方法名本身就把两种语义区分开了**。16 个测试钉住，含跨次累积、`-1` 哨兵值、以及轴名/方向名/`ToggleCount` 的**大小写敏感**。

行为与源实现逐点一致，一处未改。

### D23：组件注册表与实例化次序改为编译期常量表（2026-08-05）

源实现的 `ComponentsUtils` 是个 `StaticInstance<T>` 单例 MonoBehaviour，靠 `executionOrder: 500` 保证在有人用它之前 `Awake` 完成建表——**一张内容完全固定的表，却被绑在场景对象的生命周期与执行顺序上**。包里不能留这个形状：它要求宿主场景里必须有这么个物体，且执行顺序配错就静默失效。

**选定**：`ComponentRegistry`（静态字典）+ `ComponentLoadOrder`（静态次序表），无实例、无生命周期。

实例化次序单独抽出来是因为**它是正确性条件而非美观问题**：`TapTrigger.Constructor` 要 `GetComponent<BaseElementComponent>()`，动作组件要 `GetComponent<T>()` 拿元素——元素必须先在，否则触发器直接报错退出。源实现把这个次序写成 `CreateTourScene` 里的三层嵌套循环，与 GameObject 创建、`AddComponent`、`await` 混在一起，无法单独验证。

两个**防漏**测试值得单列，它们挡的是同一类最难查的故障——「组件静默不出现，没有任何报错」：

- 每个 `ComponentType` 常量都必须能在注册表里解析出类型（加了常量忘了登记）
- 每个 `ComponentType` 常量都必须出现在次序表里（登记了却排不上队）

### D24：未知组件类型跳过，不再打死整个 Tour（2026-08-05，复议旧裁定）

`ComponentConverter` 遇到未知 `ComponentType` 时返回 null，随后 `Populate(reader, null)` 抛 `ArgumentNullException`，向上冒泡使**整个 Tour 加载失败**。

这条曾按旧 D12「行为等价优先」裁定为保留。在「以架构最优为判据」下复议后**修正**：这不是偶发 bug，是**边界契约缺陷**——客户端无法容忍服务端的增量式 schema 变更，服务端上线任何新组件类型，所有已发布客户端即刻整体加载不出内容。版本化线格式的标准做法是跳过未知项。

**选定**：`result == null` 时直接返回 null。未知项只损失它自己，同一实体上的其余组件照常解析（`ComponentLoadOrder` 已对 null 元素判空）。

### D25：版本查询失败退回本地缓存（2026-08-05，复议旧裁定）

`UpdateTourPackageIfStaleAsync` 直接解引用查询结果的 `data.version`，查询一失败就 NRE 冒泡，**整个场景加载失败——即便本地已有完整可用的缓存**。

同样在新判据下复议后修正：网络抖动不该让导览打不开，这是「信任边界缺防止数据丢失的错误处理」。

**选定**：抽出纯决策 `ShouldDownloadTourPackage(cachedVersion, serverVersion)`——服务端版本未知时返回 false（用缓存），相同时返回 false，其余返回 true。6 个测试钉住三种输入。

### D26：相机识别从 tag 改为注入的 Transform（2026-08-05）

源实现用 `other.CompareTag("ARCamera")` 判断谁进出了触发体积。**tag 是工程级全局配置**——包移植到别的工程时那边没有这个 tag，判定永远为假、区域触发整体失效，而且**不报任何错**。同类问题还有 `FindGameObjectWithTag("AnchorObject")`（已在 5.3 改为注入）。

**选定**：`BindScene(anchor, offset, camera)` 注入相机 Transform，用纯函数 `SceneRoles.IsCamera` 判定。上下都认——碰撞体既可能挂在相机的子物体上，也可能挂在整个 rig 上。5 个测试。

**顺带**：触发体积的物理回调改由包内的 `TourVolumeTrigger` 自己找父节点转发，替掉源实现「宿主 `TriggerEvents` + 预制体里连的 `UnityEvent`」。预制体少一处能连断的引用——`UnityEvent` 目标丢了不报错，只是区域触发不再工作。

**区域进出的合并时机**：源实现用 `WaitForSeconds(0.01f)` 的协程合并同一物理步内的「退出 A + 进入 B」，避免中间空一帧。改为帧末结算（`FlushRegionTransitions`）——同样合并，但不必解释 0.01 秒是怎么来的。

### D27：帧驱动由包自持，扫码提示按状态变动重算（2026-08-06）

`TourDirector.FlushRegionTransitions` 需要每帧末调一次，扫码提示需要在状态变化后重算。谁来驱动？

**否决「宿主每帧调 `ite.Tick()`」**：忘了调不报错，只是区域触发从此不再结算——与 D26 的 tag 问题同一类静默失效，而且只在真机上现形。公开面上多一个「必须记得调」的义务，等于把包的正确性押在宿主的记性上。

**选定**：`IteRuntime` 装配时创建 `[ITE] Runtime Driver` GameObject，挂 `internal IteRuntimeDriver`，`Shutdown()` 时销毁。宿主没有忘的机会。

**扫码提示的重算时机**：源实现是每 0.75 秒轮询的协程，每次都无条件调 UI 的 `Show`/`Hide`。提示是状态的**纯函数**（`ScanPromptPolicy.Decide` 只读 `ScanState` 与各 Tour 的 `DisplayType`），状态没变就不可能变——所以改为各状态变更方法置 `_promptDirty`，帧末结算时才重算，且**只在结果变化时**广播。既去掉了 0.75 秒这个魔数，也避免了每帧重复分配候选列表。

**`OnScanPromptChanged` 的签名**：D3 写的是 `Action<ScanPromptState, string[]>`，实际改为 `Action<ScanPrompt>`——`ScanPrompt` 结构体携带的正是这两项，单参数、字段有名字、日后加字段不破坏订阅方签名。

### D28：场景图片单独广播，事件数 7 → 8（2026-08-06）

源工程里场景 logo 与各 Tour 预览图是 **fire-and-forget** 加载的（`_ = FetchIteSpaceSceneAssets(scene)`，不 await），与 Tour 内容包的下载解压并行，完成后另发一个 `OnLoadedIteSpaceSceneAssets`。而 spec 原先写的是 `OnSpaceSceneLoaded` 发出时「供宿主展示标题、图标与预览」——订阅方此刻读 `SpriteLogo` 拿到的是 null。二者对不上。

**否决「让 `OnSpaceSceneLoaded` 等图加载完」**：加载界面的标题与 Tour 列表会被排在 N 张图的下载之后，且把源工程并行的两件事串行化了。为了少一个事件，牺牲首屏时间。

**选定**：加第 8 个事件 `OnSpaceSceneAssetsLoaded`。理由是**事件携带的必须是当下真实就绪的东西**——图确实晚到，用一个 await 把它藏起来是假装它没晚。宿主要简单处理，只订第二个事件即可；要首屏快，就两个都订。

### D29：`alwaysDisplayed` 的 Tour 在装配时就构建内容（2026-08-06，修既有缺陷）

源工程 `CreateTourObject` 对 `alwaysDisplayed` 不调 `Disable()`，但**也没有任何地方对它调 `Enable()`**——`IteSpaceManagerScan` 里那个 `Enable()` 只在扫码路径上。内容树由 `Enable()` 构建，所以「一直显示」的 Tour 在源工程里**内容永远是空的**。要么这个类型的数据从没上过线，要么这是个没人发现的死分支。

这不属于 D12 的「行为等价照搬」范围：`alwaysDisplayed` 的字面语义就是不需要触发条件，当前实现与该语义直接矛盾，且矛盾**不报错**。

**选定**：`CreateTourObject` 末尾，`alwaysDisplayed` 走 `await Enable()`，其余两型仍 `Disable()`。不碰 `normal` / `regionalTrigger` 的任何时序。

**副作用**：线上若存在内容很重的 `alwaysDisplayed` Tour，冷启动会把它们全建出来。记入 TODO，真机验证时确认。

### D30：新增 `ActivateTour(tourId)` 显式激活入口（2026-08-06）

扫码与区域进出之外，宿主需要一条**不依赖任何传感器**的激活路径：没有真机、没有二维码时，这是验证内容管线唯一的手段。源工程的同类需求靠 `IteSpaceManager` 的 `OnGUI` 调试面板解决（已按 5.7 删除）。

**选定**：`IteRuntime.ActivateTour(string tourId)` → `TourDirector.ActivateById`，与扫码路径共用同一个 `Activate`，不改任何既有语义。找不到 ID 时报错返回 false，不抛。

### D31：UI 交互层从 Meta Interaction SDK 换成 XRI，圆角用九宫格 sprite（2026-08-06）

迁移时发现 `Rich Text Element` 与 `Video Plane Element` 各带 **10 类 Meta Interaction SDK 组件**（`PokeInteractable` / `RayInteractable` / `PointableCanvas` + `...UnityEventWrapper` / `PlaneSurface` ×2 / `ClippedPlaneSurface` ×2 / `BoundsClipper` ×2 / `RectTransformBoundsClipperDriver` ×2 / `RoundedBoxUIProperties` ×2 + `RoundedBoxUI.mat`）。MR_Base **根本没装 `com.meta.xr.sdk.interaction`**（只有 `sdk.core` 与 `mrutilitykit`），原样拷入即 14 个 Missing Script。这也正是源工程跑不了 PICO 的根。

**选定**：包依赖加 `com.unity.xr.interaction.toolkit` 3.5.1（Unity 官方，Quest/PICO 都经 OpenXR），Canvas 上 `GraphicRaycaster` → `TrackedDeviceGraphicRaycaster`。Meta 那一大摞存在的理由是它**不走 Unity 的 EventSystem**；XRI 走，所以 10 类组件塌成 1 个，`Button` 原生就响应，`ISDK_PokeInteraction` / `ISDK_RayInteraction` 两棵子树整棵删除。`TrackedDeviceGraphicRaycaster` 自身实现 `IPokeStateDataProvider`，戳与射线都由它接管，不需要额外的碰撞体或 filter。

**否决「宿主自己往 Canvas 上加」**（可保住 3 依赖）：忘了加就是按钮点不动、不报错——与 D26/D27 同一类静默失效。**否决 asmdef `versionDefines` 做可选依赖**：为省一个依赖引入条件编译，是用复杂度换依赖数字。

**圆角**：`RoundedBoxUI.mat` 是 Meta sample 的 shader，不迁。接回**任务 2.3 已经建好**的包内实现——`Runtime/Internal/RoundedBoxUI.shader` + `RoundedBoxUIProperties`（`IMeshModifier` 把每角半径写进 `uv1`，片元里做 SDF 裁边），新建 `Runtime/Prefabs/Materials/RoundedBoxUI.mat` 挂到 4 个 `Front`/`Back` 节点上。

**不能用九宫格 sprite**（XRI 自己的 `Hands Interaction Demo` 与 `Starter Assets` 用的是 `Round Radius N.png` 九宫格）：ITE 的 `CornerRadius` 是**每个富文本 / 视频资源自带的数据**，`RichTextElement.ApplySprite` / `VideoPlaneElement.ApplyTexture` 运行时按内容逐个写 `borderRadius`（Vector4，四角可不同）。九宫格是死半径，做不到。这是本包唯一偏离「照抄 XRI 做法」的地方，理由是数据驱动的动态半径。

**代价**：任务 12.6 的移植验证从「3 个依赖」变成 4 个。

**迁移后核验**：4 个 prefab 均 `missingScripts=0`、`AssetDatabase.GetDependencies` 的包外依赖数为 0；两个按钮的 `onClick` 仍指向 `RichTextElement.ToggleAudio` / `VideoPlaneElement.ToggleVideo`；6 张图标 sprite 引用全部存活。

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
