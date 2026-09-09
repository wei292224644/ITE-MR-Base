## Why

`com.uality.ite-tour` 已经完整移植进来——`IteRuntime` / `IteContentPipeline` / `IteTourAssembler` / `IteTourObject`，11 个内容组件类型、元素预制体、着色器与贴图全都在，40+ 条编号决策，一整套 EditMode 测试。但**这条链从未被端到端执行过**：所有测试都是纯数据、纯决策的离机测试，真正建 GameObject 的 `IteTourObject.CreateTourScene` 在 codegraph 里标着 ⚠️ no covering tests found。

更具体地说，现在把它跑起来会连着撞上三堵墙，而且**三堵墙都不报错**：

1. 空间场景包的联网获取从来就是坏的。`thirdDemo.zip` 的条目顶层是 `thirdDemo/`，解压目标是 `IteSpaceScene_thirdDemo/`，落盘多一层，`FetchSpaceSceneAsync` 读不到 json。源工程 `FileUtils.ExtractZipAsync` 有同样的行为——这解释了源工程为什么默认 `isNetworkAvailable: 0`。
2. 区域触发是死的。`IteTourAssembler.SetAllVolumesActive` 注释写明"显隐由宿主决定"，但全仓库零调用方，`IteRuntime` 也没暴露它。体积物体压根没启用，走进去不会有任何事件。
3. 扫码激活不可用。`IteHostBootstrap` 里接标记源的字段是注释掉的，注释原文："ITE 导览的扫码激活暂时不可用，只能靠 ActivateTour 手动激活。重新接入见新契约（MarkerTrackingSession）。"

与此同时，`MRCore.unity` 里那个已接线的 `ITE Host` 引用的 `IteRuntimeConfig.asset` 的 `sceneName` 是空串——每次启动**任何**场景，核心场景都会去请求 `https://ite-spatial-config.uality.cn/.zip` 然后抛异常。

本 change 要的是：**在编辑器里把整个 tour space 按 json 的原始语义完整跑通一次**——5 个 tour 全部装配、扫码激活、区域自动切换、内容元素 UI 真实渲染——并把宿主装配层做成真机接入可以直接复用的形状。

## What Changes

**修复内容获取（D1）**

- `ZipContentDownloader.DownloadAndExtractAsync` 增加显式声明顶层目录语义的参数。空间场景包与 tour 包对"顶层目录"的期望是**相反**的（前者要剥掉、后者必须保留），而代码里对此没有任何表述。两处调用各自明确传值。**不做**读取侧"两个路径都试一下"的 fallback。

**补齐包的宿主接口（D2）**

- `IteRuntime` 在 `LoadAsync` 末尾**默认打开**触发体积，并额外暴露 `SetTriggerVolumesActive(bool)` 供宿主延后开启或临时关闭。源工程是让宿主自己调（`IteSpaceManager.cs:70` 的 `_liveTours.ForEach(t => t.SetVolumeObjectActive(true))`），这边的 `SetAllVolumesActive` 照抄了那个形状却一直**零调用方**——把"不调就静默失效"的开关交给宿主已经失败过一次，所以默认值改成能工作的那一侧。

**新建宿主接入层**

- `IteMarkerBridge`：订阅 `MarkerTrackingSession.MarkerObserved` → 剥掉 `******{tourId}******` 外壳 → 转 `IteRuntime.SubmitMarkerScan`。这段按 `unified-marker-tracking-contract` 的明文要求必须在会话层之外（"业务层若需要将 RawPayload 转换或匹配到业务对象，SHALL 在会话层之外自行完成"），而 `TourScanPolicy` 又是拿 payload 与 tourId 做字面比对，中间没有任何解析——桥不存在，链就断在这里。
- `IteHostBootstrap` 恢复标记源接线：暴露 `AttachMarkerSession(MarkerTrackingSession)`，会话由输入层构造后单向注入。装配点自己不构造会话、不持有任何观测源实现的字段——它是 `MonoBehaviour`，序列化字段只能是具体类型，放上去就等于把某个观测源焊死在这一层。

**新建编辑器驱动层**（可整块禁用，不进 build）

- `Assets/Scenes/IteTourSpace.unity`：桌面相机（带 `CapsuleCollider` + kinematic `Rigidbody`——缺了刚体 Unity 一个 trigger 事件都不发，也不报错）、WASD/鼠标走位、按键假扫码、调试 HUD。
- 假扫码走完整契约链 `MockObservationSource` → `MarkerTrackingSession` → `IteMarkerBridge`，不走捷径直调 `SubmitMarkerScan`。会话由驱动层构造并注入装配点，引用方向只有驱动层 → 装配点。真机接入时只换观测源实现与谁来注入，中间段一行不动。

**从核心场景摘除 ITE**

- `MRCore.unity` 删除 `ITE Host`（GameObject 1597420651 / Transform 1597420652 / MonoBehaviour 1597420653），并从父节点 Transform 614014891 的 `m_Children` 移除。ITE 导览是内容，不是核心装配。

**配置**

- `Assets/Settings/ITE/IteRuntimeConfig.asset` 的 `sceneName` 填 `thirdDemo`——`projectDirectory.json` 列出的 4 个正式项目的空间场景包逐个 `HEAD` 实测**全部 404**，`thirdDemo` 是唯一实际存在的空间场景包。

## Capabilities

### New Capabilities

- `ite-content-acquisition`: ITE 内容包的获取与落盘契约——两类包（空间场景包 / tour 包）对 zip 顶层目录的相反期望必须被显式声明而非隐式碰巧；离线与在线两条路径的行为边界；版本比对与缓存命中。
- `ite-tour-space-host`: ITE 导览在本工程里的宿主装配契约——场景层级约束（锚定数学成立的前提）、标记事件到 ITE 的桥接、触发体积的开启时机、加载与激活事件的对外转发、以及 ITE 不驻留核心场景。
- `ite-tour-space-editor-harness`: 编辑器内的导览验收面——不依赖 XR 设备、不依赖真实二维码，把扫码激活与区域切换的完整状态机跑到可观察为止。

### Modified Capabilities

（无。`openspec/specs/` 下只有 `unified-marker-tracking-contract`，本 change 只**消费**它，不改变它的任何要求：观测源仍只提供查询、会话层仍不解析身份、payload 到 tourId 的映射发生在业务层。）

## Impact

**新增**

- `Assets/Scenes/IteTourSpace.unity`
- `Assets/Scripts/IteHost/IteMarkerBridge.cs`
- `Assets/Scripts/IteHost/`（编辑器驱动层：走位控制器、假扫码驱动、调试 HUD）
- `Assets/Tests/EditMode/` 下新增宿主侧纯逻辑测试（payload 剥壳）

**修改**

- `Packages/com.uality.ite-tour/Runtime/Internal/ZipContentDownloader.cs` — 顶层目录语义参数
- `Packages/com.uality.ite-tour/Runtime/Core/IteContentPipeline.cs` — 两处调用各自传值
- `Packages/com.uality.ite-tour/Runtime/Core/IteRuntime.cs` — 暴露触发体积开关
- `Assets/Scripts/IteHost/IteHostBootstrap.cs` — 恢复标记源接线（`AttachMarkerSession` 注入入口）、事件转发、激活超时
- `Assets/Scenes/MRCore.unity` — 删除 `ITE Host`
- `Assets/Settings/ITE/IteRuntimeConfig.asset` — `sceneName: thirdDemo`

**不动**

- `BuildScript.cs`、`EditorBuildSettings.asset`（新场景带桌面相机与假扫码，不进真机产物）
- `Assets/Scripts/Localization/` 下的标记契约实现（只消费）
- 包内 11 个内容组件与元素预制体（代码与资源已齐备，本次是第一次真实执行它们）

**外部依赖**

- CDN：`ite-spatial-config.uality.cn`（空间场景包）、`ite-pkg.uality.cn`（tour 包）、`api.uality.cn`（版本 API）。首跑需联网下载约 290 MB，其中 `4kvhqwvp_12f` 单包 151 MB。

## Open Assumptions

probe 阶段带过来的 11 条假设与 propose 阶段新增的 2 条，已在 propose 收尾时逐条走查完毕，**全部确认为原样**，无一被推翻。analyze 阶段又补定了一条（会话归属），记录如下：

| 曾经的假设 | 确认结果 | 落在哪 |
|---|---|---|
| 触发体积由谁打开 | 包在 `LoadAsync` 末尾默认打开，同时暴露 `SetTriggerVolumesActive(bool)` 供宿主延后/关闭 | design D2、tasks 3.1–3.2 |
| 锚定层级约束在哪层校验 | 包侧 `IteBootstrap.Validate()`——约束来自包内部的数学，换宿主也得遵守 | design D4、tasks 3.3–3.4 |
| 二维码 payload 外壳格式 | 做成 `IteHostBootstrap` 上可序列化的正则字段，默认 `^\*{6}(.*?)\*{6}$`，不写死 | design D3、tasks 4.3 |
| `MarkerTrackingSession` 归谁构造 | 归输入层（编辑器里是驱动层，真机上是 XR 那侧），经 `AttachMarkerSession` 单向注入；装配点不构造、不持有观测源字段 | design D3、host spec 解耦要求、tasks 4.3 / 5.2 |
| `MarkerLost` 在 ITE 侧的语义 | 不做任何动作，仅在 HUD 显示（`IteRuntime` 无对应入口，源工程亦无此语义） | host spec、tasks 4.1 |
| zip 顶层目录参数的形状 | 枚举 `ZipTopLevel { Preserve, Strip }`，不用 bool | design D1、tasks 2.2 |
| 编辑器假扫码的位姿 | 相机前方 1.5 米、法线朝向相机的世界位姿 | design D6、tasks 5.2 |
| 能力拆几个 | 保持 3 个 | specs/ 目录结构 |
| 151 MB 首跑的处理 | 本次接受，只记入后续（改流式落盘是另一件事） | design 风险表、tasks 8.2 |
| 要不要补 EditMode 测试 | 补三处：payload 剥壳、zip 顶层目录剥离、层级校验 | tasks 2.7 / 3.4 / 4.2 |
| 走位控制器的复杂度 | 只做 WASD + 鼠标视角，无重力无碰撞 | tasks 5.1 |
| 调试 HUD 的形式 | 屏幕空间文字，仿 `MarkerHookTestHud` | design D5、tasks 5.3 |
| 新脚本的落位 | 全放 `Assets/Scripts/IteHost/`，不单开 asmdef，靠子目录区分两层 | tasks 4–5 |
| `sceneName` 与 `tourObjectPrefab` | 已核实：`tourObjectPrefab` 的 guid 与包内 `Tour.prefab` 一致，已连对；只需把空的 `sceneName` 填成 `thirdDemo`（另外 4 个正式项目的包逐个 `HEAD` 实测全部 404，无第二选项） | tasks 2.8 |

**仍然未知（关于外部世界，不是关于本设计）**：真机上实际印制的二维码是不是 `******{tourId}******` 这个格式。本设计已通过"格式可配置 + 不匹配时打印原始 payload"把它变成一个可在真机上五分钟内查明并改配置的问题，而不是需要改代码的问题。

## Non-Goals

- 真机验收（Quest / PICO 打包、passthrough 真扫 QR、gltfast 运行时 URP 材质在 Android + XR 下的表现）
- 接入 `QuestObservationSource` / `PicoFiducialObservationSource`
- 产品 UI：扫码提示界面、加载页、tour 预览列表、开场 Timeline。包侧已刻意把渲染排除在外（`ScanPromptPolicy` 类注释："决策是 ITE 业务，渲染不是"），UI 是宿主侧可后接的一层。**注意**：tour 内容自身的元素 UI（富文本面板、圆角框、播放按钮）**在范围内**，且是本 change 的主要验收对象之一。
- 验证 `VideoPlane` / `PrimitiveModelRender` / `ApproximateTrigger` / `PlayAudioAction`——thirdDemo 的 5 个 tour 不含这 4 个组件类型，不为它们造合成数据（合成数据只能验出"我们自己写的数据能渲染"，且 `ApproximateTrigger` 的近距离判定在包里本来就没实现）
- 网络多人（源工程 `IteSpaceManagerNetwork` / Colyseus 那条线）
- 摘戴头显的真实行为（编辑器无 HMD，`HeadsetPresenceAdapter` 读不到 `userPresence` 时按"已佩戴"处理）
