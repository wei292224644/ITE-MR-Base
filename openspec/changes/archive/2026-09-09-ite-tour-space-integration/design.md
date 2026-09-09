## Context

`com.uality.ite-tour` 的渲染链是这样一条线：

```
IteHostBootstrap.Start()                        ← 宿主唯一装配点
  └─ IteRuntime.Create(bootstrap)
       └─ StartAsync() → LoadAsync()
            ├─ FetchSpaceSceneAsync(config.SceneName)
            │     下载 {base}/{sceneName}.zip → 解压到
            │     persistentDataPath/IteSpaceScene_{sceneName}/
            ├─ foreach tour in scene.tours:            ← 串行 await，一个失败全链断
            │     FetchTourAsync(tourId)               ← 版本查询 + 下载 {tourId}_wx.zip
            │     assembler.CreateAsync(tour, data)
            │       └─ IteTourObject.CreateTourObject()
            │            ├─ 设 local 位姿（来自 space json 的 transform）
            │            ├─ 配触发体积（配完 SetActive(false)）
            │            ├─ LoadAssets()               ← glb(gltfast) / mp3 / png / mp4
            │            └─ alwaysDisplayed ? await Enable() : Disable()
            └─ director.RequireScan()                  ← 冷启动后必须先扫一次码
```

`Enable()` 是唯一建 GameObject 的入口（`CreateTourScene` → 逐 entity `AddComponent` + `await Constructor`）。codegraph 对 `IteTourObject`、`TourDirector`、`CreateTourObject` 都标 ⚠️ no covering tests found——**这条链从未被端到端执行过**。

内容是 `thirdDemo`（唯一实际存在的空间场景包，358 KB），5 个 tour 沿 Z 轴每 2 米一个、互不重叠：

| tourID | displayType | 位置 | 实体 | 组件 | 包大小 |
|---|---|---|---|---|---|
| wm0l5qcn_ibd | regionalTrigger | z=0 | 1 | EMWModelRender, LoadTrigger, PlayAnimationAction | 29 MB |
| 4kvhqwvp_12f | normal | z=2, 绕Y 45° | **14** | +**RichText×10**, ToggleVisibility×13, Spin×5, TapTrigger×4 | **151 MB** |
| earyserh_i5x | regionalTrigger | z=4 | 2 | EMWModelRender×2, TapTrigger×2, ToggleVisibility×2 | 31 MB |
| azdugaax_xry | regionalTrigger | z=6 | 1 | EMWModelRender, LoadTrigger, PlayAnimationAction | 44 MB |
| hkdaowxy_0hu | regionalTrigger | z=8, 绕Y 90° | 1 | EMWModelRender, LoadTrigger, PlayAnimationAction | 37 MB |

约束：

- 5 个 tour **无一** 是 `alwaysDisplayed`（4×`regionalTrigger` + 1×`normal`），所以内容出现必须经过扫码或区域触发，没有捷径。
- 内容元素 UI（富文本面板）**只存在于 151 MB 的 `4kvhqwvp_12f` 里**。
- `ContentAssetLoader` 的既定约定是"文件不存在静默返回 null"——缺资源不炸、只是不显示。这个包的典型失效形状是**看不到红字，只看到空**。
- 总下载约 290 MB，`LoadAsync` 串行 `await` 全部下完才 `OnInitialized`。

## Goals / Non-Goals

**Goals:**

- 在编辑器里按 Play，把 thirdDemo 的完整导览流程走通：加载 → 扫码激活 → 区域自动切换 → 内容元素 UI 渲染 → 二次锚定。
- 宿主装配层做成真机可原样复用的形状，编辑器专用部分可整块摘除。
- 每一段失败都能从日志区分（下载 / 解析 / 装配 / 建实体树 / 资源缺失）。
- 把 ITE 从 `MRCore` 里搬走。

**Non-Goals:**

- 真机验收（D9）。
- 产品 UI（扫码提示界面、加载页、预览列表）。注意 tour **内容自身**的元素 UI 在范围内。
- 接 `QuestObservationSource` / `PicoFiducialObservationSource`。
- 验证 thirdDemo 未使用的 4 个组件类型。

## Decisions

### D1 — zip 顶层目录语义用枚举参数显式声明，不用 bool

**选了什么**：`ZipContentDownloader.DownloadAndExtractAsync(url, relativeFolder, ZipTopLevel topLevel)`，枚举取值 `Preserve` / `Strip`。空间场景包传 `Strip`，tour 包传 `Preserve`。声明为 `Strip` 但 zip 条目并非全部位于同一顶层目录之下时，报错而非静默按原样落盘。

**替代方案 A**：`bool stripTopLevelDirectory`。**否决**：调用点会变成 `DownloadAndExtractAsync(url, folder, true)`——那个 `true` 在阅读时不携带任何信息，而这两类包的差异恰恰是最容易记错的东西。

**替代方案 B**：不改下载侧，在 `FetchSpaceSceneAsync` 的读取侧依次尝试 `{folder}/{name}.json` 与 `{folder}/{name}/{name}.json`。**否决**：那是把两类包的结构差异藏进一次运气好的 `File.Exists`。真实布局从此无法从代码中读出，下一个人只能靠试。

**为什么必须修**：`thirdDemo.zip` 的条目顶层是 `thirdDemo/`，解压目标是 `IteSpaceScene_thirdDemo/`，落盘后是 `IteSpaceScene_thirdDemo/thirdDemo/thirdDemo.json`，而 `FetchSpaceSceneAsync` 读的是 `IteSpaceScene_thirdDemo/thirdDemo.json`——差一层，`File.Exists` 为 false，`LoadJsonAsync` 返回 null，抛 "No tours found"。源工程 `FileUtils.ExtractZipAsync` 同样不剥，**两边同一个行为**，这解释了源工程为什么默认 `isNetworkAvailable: 0`。zip 里的 `__MACOSX/` 条目说明多这一层是 macOS Finder 对文件夹右键压缩的产物，即打包侧的习惯而非偶发。

tour 包**没有**这个问题：条目顶层是 `{tourId}/`，解压目标是 `relativeFolder = ""`（persistentDataPath 根），正好对上。两类包的期望相反，而代码里对此没有任何表述——这才是要修的东西，不是那个差一层的路径。

### D2 — 触发体积默认在装配完成时打开，同时保留宿主关闭的能力

**选了什么**：`IteRuntime.LoadAsync` 在 `RequireScan()` 之前调一次 `_assembler.SetAllVolumesActive(true)`；同时 `IteRuntime` 暴露 `SetTriggerVolumesActive(bool)` 供宿主延后或关闭。

**替代方案 A**：只暴露公开方法，由宿主负责调用（源工程的做法：开场 Timeline 结束后 `_liveTours.ForEach(t => t.SetVolumeObjectActive(true))`）。**否决**：这正是现状，而现状是**零调用方**——`SetAllVolumesActive` 存在、注释也写明"由宿主决定"，但从没有人调过，于是区域触发整体是死的，走进去什么都不发生，也不报错。把一个"不调就静默失效"的开关交给宿主，已经被证明会失败一次。

**替代方案 B**：只在 `LoadAsync` 里打开，不暴露方法。**否决**：源工程要在开场动画结束后才打开是有道理的——加载页还盖在眼前时不该被区域触发抢走控制权。后续接产品 UI 时需要这个能力。

**为什么这么选**：安全的默认值 + 保留控制权。默认值的选择原则是"忘了配置时应该退化成能工作，而不是静默失效"。

### D3 — 标记桥接是纯 C# 类，不是 MonoBehaviour

**选了什么**：`IteMarkerBridge`——构造时注入 `MarkerTrackingSession` 与一个 `Action<string, Pose>`（实际接 `IteRuntime.SubmitMarkerScan`），内部只做订阅、剥壳、转发。与 `HeadsetPresenceAdapter` 同一形状（那个类的注释已经写明理由："不是 MonoBehaviour：它只需要有人每帧调一次 Poll……这样场景里少一个组件，也能离机测"）。

**为什么**：剥壳是本次唯一新增的**纯逻辑**，也是最容易出错的一段（格式、大小写、空白、不匹配时的行为）。做成纯 C# 才能在 EditMode 里穷举它，而不必进 Play 模式手动扫。

**外壳格式**：默认 `^\*{6}(.*?)\*{6}$`（源工程 `AnchorObject.OnQrCodeScanned` 的正则），作为 `IteHostBootstrap` 上的可序列化字段，不写死。理由见风险表——真机上实际印制的码是什么格式尚未核实。

**为什么必须有这一层**：`unified-marker-tracking-contract` 明文规定"业务层若需要将 `RawPayload` 转换或匹配到业务对象，SHALL 在会话层之外自行完成，契约本身不提供、不假设任何解析规则"；而 `TourScanPolicy.Decide` 里的 `TryFind` 是 `tours[i].TourId == markerId` 的字面比对。两端都不做转换，中间必须有人做。

**会话归属**：`MarkerTrackingSession` 由**输入层**构造（编辑器里是 `Editor Rig`，真机上是 XR 那一侧），经 `IteHostBootstrap.AttachMarkerSession(MarkerTrackingSession)` 单向注入；装配点自己**不**构造会话、**不**持有观测源字段。`IteHostBootstrap` 是 `MonoBehaviour`，可序列化字段只能是具体类型，一旦在上面放 `MockObservationSource source`，"更换观测源不需改装配层"当场作废——而这正是宿主 spec 里那条解耦要求的字面内容。注入发生在装配点 `Start()` 之前或之后都要能工作：`AttachMarkerSession` 若在 runtime 就绪前调用则暂存，就绪后补接。未注入时加载链照常跑完，只是没有扫码输入，打一条说明性日志。

### D4 — 锚定的层级约束由包在 `IteBootstrap.Validate()` 里校验

**选了什么**：包侧校验 `TourRoot.parent == AnchorRoot`，以及 `AnchorRoot.parent` 为 null 或其 `localToWorldMatrix` 为单位矩阵。不满足时进入既有的"装配不完整"错误路径。

**替代方案**：在宿主 `IteHostBootstrap` 里校验。**否决**：这个约束来自**包内部的数学**，不是宿主的偏好。`ChangeTourObjectTransform` 把 `TourRoot` 的 local 设为被扫中 tour 的 local 矩阵之逆、把 `AnchorRoot` 的 local 设为扫到的位姿，两者相乘才让被扫中的 tour 落在标记上。换任何一个宿主都得遵守同一条，校验放在包里才不会随宿主复制而丢失。而 `IteBootstrap.Validate()` 本来就在做装配校验（缺项检查），加这两条是同类。

**为什么必须校验**：`MarkerObservation.Pose` 是**世界**位姿（`QuestObservationSource.cs:111` 直接取 `trackable.transform.position/rotation`，PICO 走 `TrySolveWorldPose`），却被 `SetLocalPositionAndRotation` 写进 `AnchorRoot`。父级上的任何变换都会被二次施加。而层级搭错的表现是"模型出现在错误的位置"——没有异常、没有红字，在 MR 里甚至可能飘到身后看不见的地方。

### D5 — 场景分两层，编辑器驱动层可整块停用

**选了什么**：`IteTourSpace.unity` 的层次是

```
IteTourSpace
├─ AnchorRoot                      ← 必须是根级（D4）
│   └─ TourRoot                    ← 必须是 AnchorRoot 的直接子物体（D4）
├─ ITE Host                        ← 宿主装配层，真机原样复用
│     IteHostBootstrap（config / anchorRoot / tourRoot / camera / payload 格式）
├─ Editor Rig                      ← 编辑器驱动层，可整块停用/删除
│   └─ Camera（CapsuleCollider isTrigger + Rigidbody isKinematic）
│         走位控制器、假扫码驱动（MockObservationSource + Tick）
└─ Debug HUD                       ← 屏幕空间，仿 MarkerHookTestHud
```

停用 `Editor Rig` 与 `Debug HUD` 之后，加载链仍完整执行，只是没有输入源与可视化。

**为什么这么切**：两层在真机上的差异**只有输入源**——`MockObservationSource` 换成 `QuestObservationSource` / `PicoFiducialObservationSource`，相机换成 XR rig 的主相机。中间的桥接、体积开启时机、事件转发、层级约束完全相同。`IteHostBootstrap` 的类注释已经把自己定义为"**唯一**知道 `Uality.IteTour` 与 `MRBase.*` 两边的类型"，它天生是复用件。如果做成一次性验收场景，真机 change 得把这套装配重连一遍——而 Unity 场景的手工连线正是最容易连错又不报错的地方。

**代价**：场景里多一层组织结构，比"全平铺"啰嗦。值得。

### D6 — 假扫码的位姿取相机前方 1.5 米、法线朝向相机

**选了什么**：按键触发时，构造一个位于相机前方 1.5 米、旋转为"面向相机"的世界位姿，塞进 `MockObservationSource`。

**为什么**：这模拟一张贴在墙上、被人正对着扫的二维码。扫完之后被扫中的 tour 正好出现在眼前（D4 的数学保证被扫中的 tour 落在 `AnchorRoot`，而 `AnchorRoot` 被移到该位姿），再往前走 2 米能进下一个 tour 的触发体积——thirdDemo 沿 Z 轴每 2 米一个的走廊布局刚好能被完整走完。

**替代方案**：固定世界位姿（比如原点）。**否决**：那样第一次扫码不会把内容带到眼前，操作者得自己走过去找，而"扫完看不到东西"与"渲染失败"在观感上无法区分——正是本 change 要消除的那类歧义。

### D7 — 三个能力分开写 spec，不合并

**选了什么**：`ite-content-acquisition` / `ite-tour-space-host` / `ite-tour-space-editor-harness`。

**为什么**：三者的生命周期不同。内容获取是纯逻辑、可被 EditMode 覆盖、与渲染无关；宿主装配是场景契约，会随真机接入而**扩充**；编辑器 harness 会随真机接入而**消亡**。写在一个 spec 里，归档时无法分辨哪些要求还活着。

### D8 — 一次做完，但实施顺序保持归因可分离

**选了什么**：D1 的内容获取修复与宿主接入放在同一个 change（用户决定），但 tasks 的顺序强制先把获取跑通并单独确认（联网首跑落盘正确、离线复跑一致），再接宿主与 harness。

**为什么**：内容获取与内容渲染是两件事。如果同时改、同时验，一旦跑不通，"是下载改错了还是宿主接错了"没法分离。分成两个 change 是最干净的做法，但用户明确要求一次做完；退而求其次，用**实施顺序**保住归因能力——第一步做完时，`persistentDataPath` 下的落盘布局是可以脱离 Unity 直接用 `ls` 验证的。

### D9 — 编辑器验收不等于真机验收

**选了什么**：验收面只到编辑器。

**为什么**：tour 的模型经 gltfast 在运行时导入，材质是运行时生成的 URP 材质。编辑器 Standalone 目标下的着色器变体集合与 Android + XR（单通道立体实例化）下的不是一回事——黑模、粉模、只在一只眼里出现，这几类问题**只在设备上暴露**。把它们算进本 change，会把一个秒级迭代的验证变成一个需要打包的验证，而这条链的头几十次迭代必然全是"哪一段断了"。

**代价**：编辑器通过不构成真机可用的证据。后续 change 需处理：接真实观测源、相机归属、`IteTourSpace` 进 `EditorBuildSettings` 与 `BuildScript` 的产品版本、290 MB 内容在设备上的落盘与首启策略。

### D10 — `ITE Host` 从 `MRCore.unity` 移出

**选了什么**：删除 `MRCore.unity` 里的 `ITE Host`（GameObject 1597420651 / Transform 1597420652 / MonoBehaviour 1597420653），并从父节点 Transform 614014891 的 `m_Children` 移除。

**为什么**：`MRCore` 是永不卸载的核心场景，职责是 XR rig 与就绪闸门。把一条会联网下载 290 MB 的内容管线焊在里面，意味着**每一个**内容场景（GsplatBench、SacredRelicDemo、MarkerHookTest…）都在替 ITE 付启动代价。而且现状更直接：`sceneName` 为空，`FetchSpaceSceneAsync("")` 会去请求 `{base}/.zip` 并抛异常——核心场景现在每次启动都在跑一条必然失败的联网链路。

## Risks / Trade-offs

| 风险 | 影响 | 应对 |
|---|---|---|
| `DownloadHandlerBuffer` 把整个 zip 读进内存，`4kvhqwvp_12f` 是 151 MB，随后 `File.WriteAllBytes` 再拷一份 | 峰值内存约 300 MB。编辑器可承受，真机存疑 | 本 change 不改下载形态（改成流式落盘是另一件事）。记入 D9 的后续 change 清单 |
| 首跑串行下载 290 MB，期间 Play 无响应 | 操作者可能误以为卡死 | HUD 显示 `OnLoadProgress` 分段；首跑一次之后都是本地 IO |
| `_ = tour.Enable()` 是 fire-and-forget（`TourDirector.cs:278`） | 建实体树失败不冒泡、无红字 | 宿主在 `OnTourActivated` 后启动超时计时，超时未收到 `OnTourSceneLoaded` 则报错（spec 要求） |
| `ContentAssetLoader` 缺文件静默返回 null | "缓存不全"与"渲染失败"表现相同（都是空） | 事件序列分段可归因；`OnTourSceneLoaded` 已触发即说明问题在资源或渲染，不在下载或装配 |
| 二维码 payload 格式未经真机核实 | 真机接入时可能全部匹配失败 | 外壳格式做成可序列化字段（D3），不写死；不匹配时打印原始 payload |
| `IteHostBootstrap.Start` 是 `async void` | 编辑器中途退 Play 时链路半途而废，可能刷 `UnobservedTaskException` | 首跑之外整链是本地 IO、毫秒级完成，窗口极小；不改包 |
| 删 `ITE Host` 后 `MRCore` 的 diff 是手改 YAML | 改坏场景文件 | 三个对象 + 父节点 `m_Children` 一处引用，改完在编辑器里打开 `MRCore` 确认无 missing |
| 触发体积尺寸取描述值的一半（`IteTourObject.cs:106-108`，既有行为原样保留） | 走位时实际触发范围比 json 描述小一半 | 已知并接受；`BoxColliderWireframeDrawer` 会画出实际体积，走位时以画出来的为准 |
| `SetAllVolumesActive` 改为默认打开（D2） | 改变了包的既有默认行为 | 该行为此前从未生效过（零调用方），不存在依赖它的既有场景 |

## Migration Plan

1. **先摘除 `MRCore` 的 `ITE Host`**，确认核心场景无 `[ITE]` 输出。必须排在最前：下一步会把 `sceneName` 从空串改成 `thirdDemo`，顺序反了之后每个内容场景一进 Play 都会开始串行下载 290 MB。
2. **再修内容获取，单独确认**：改 `ZipContentDownloader` 与两处调用点，联网首跑一次，用 `ls` 直接验证 `persistentDataPath` 下的落盘布局（空间场景不多一层、tour 保留顶层目录），再断网复跑确认一致。此步完成前不碰宿主。
3. **再补包的宿主接口**：`IteRuntime` 在 `LoadAsync` 里默认打开体积并暴露开关；`IteBootstrap.Validate` 加层级校验。
4. **再接宿主层**：`IteMarkerBridge` + `IteHostBootstrap.AttachMarkerSession` 接线，配 EditMode 测试覆盖剥壳。
5. **最后做 harness 与场景**，按 spec 的验收序列逐条走。

**回滚**：包内改动（步骤 1、2）是三个文件的局部修改，可单独还原；宿主与场景是纯新增，删除即回滚；`MRCore.unity` 的 YAML 改动单独成一次提交，便于 revert。

## Open Questions

- `LatestVersionJsonResult.Data.version` 声明为 `string`，API 实际返回 JSON 数字（`"version": 7`）。Newtonsoft 会做数字→字符串转换，联网路径首次真正跑通时需确认 `TourVersionCache` 里存的形态与比对逻辑一致，否则会表现为"每次都重新下载 290 MB"。
- 空间场景包没有版本校验，每次联网启动都重新下载解压（358 KB，可接受，但与 tour 包的处理不一致，`IteContentPipeline.cs:75` 已记 TODO）。
- 那 4 个正式项目（Wartsila / DazuRockCarvings / aiysz / ZhongYue）的包 404 是已下线还是换了路径？影响"这个包最终要加载谁"，不影响本 change。
