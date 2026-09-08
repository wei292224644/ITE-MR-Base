## Context

`com.uality.ite-tour` 的渲染链是这样一条线：

```
IteHostBootstrap.Start()                       ← 宿主唯一装配点
  └─ IteRuntime.Create(config, anchorRoot, tourRoot, camera)
       └─ StartAsync() → LoadAsync()
            ├─ FetchSpaceSceneAsync(config.SceneName)
            │     下载 {base}/{sceneName}.zip → 解压到
            │     persistentDataPath/IteSpaceScene_{sceneName}/
            │     读 IteSpaceScene_{sceneName}/{sceneName}.json
            ├─ foreach tour in scene.tours:            ← 串行 await，一个失败全链断
            │     FetchTourAsync(tourId)               ← 版本查询 + 下载 {tourId}_wx.zip
            │     assembler.CreateAsync(tour, data)
            │       └─ IteTourObject.CreateTourObject()
            │            ├─ LoadAssets()               ← glb(gltfast) / mp3 / png / mp4 路径
            │            └─ alwaysDisplayed ? await Enable() : Disable()
            │                     ↓                          ↓
            │              建实体树（真渲染）           内容树是空的，不报错
            └─ director.RequireScan()
```

`Enable()` 是唯一建 GameObject 的入口（`CreateTourScene` → 逐 entity `AddComponent` + `await Constructor`）。整个包的 EditMode 测试没有一条覆盖它——codegraph 对 `IteTourObject`、`IteTourAssembler`、`CreateTourObject` 三处都标 ⚠️ no covering tests found。这条链**从未被端到端执行过**。

约束：

- 内容全部来自远端 CDN（`ite-spatial-config.uality.cn` / `ite-pkg.uality.cn` / `api.uality.cn`），三台实测可达。
- tour 内容包很大：29 / 31 / 37 / 44 / 151 MB，合计约 290 MB。`LoadAsync` 串行 `await` 全部下完才 `OnInitialized`。
- `ContentAssetLoader` 的既定约定是"文件不存在静默返回 null"，所以缺资源不炸、只是不显示——**排障时看不到红字，只看到空**。

## Goals / Non-Goals

**Goals:**

- 在 Unity 编辑器里打开一个场景、按 Play，能看到 tour 的模型渲染出来。
- 迭代循环不含任何网络等待。
- 失败时能从日志判断断在哪一段（下载 / 解析 / 装配 / 建实体树）。
- 把 ITE 从 `MRCore` 里搬走。

**Non-Goals:**

- 真机验证（D6）。
- 修联网路径（D5）。
- 多 tour、区域触发、扫码激活、加载 UI。

## Decisions

### D1 — `ITE Host` 从 `MRCore.unity` 移出，落到 `IteRenderTest.unity`

**选了什么**：删除 `MRCore.unity` 里的 `ITE Host`（GameObject 1597420651），ITE 运行时的装配点改由内容场景自己持有。

**替代方案**：留在 `MRCore`，测试场景只放 `AnchorRoot` / `TourRoot`，靠 `MRSceneDirector` 叠加。

**为什么否决**：`MRCore` 是永不卸载的核心场景，职责是 XR rig + `MRBootstrap` 就绪闸门。把一条会联网下载 290 MB 的内容管线焊在里面，意味着**每一个**内容场景（GsplatBench、SacredRelicDemo、MarkerHookTest…）都在替 ITE 付启动代价。而且现状更直接：`sceneName` 为空，`FetchSpaceSceneAsync("")` 会去请求 `{base}/.zip` 并抛异常——核心场景现在每次启动都在跑一条必然失败的联网链路。ITE 导览是内容，不是核心装配。

**代价**：叠加到 `MRCore` 之上时，测试场景自带的 Main Camera 会与 XR rig 的相机冲突。本 change 是编辑器阶段、不叠加、不进 build，冲突不发生；上真机时按 D6 的后续 change 处理。

### D2 — `sceneName` 定为 `thirdDemo`

**选了什么**：`Assets/Settings/ITE/IteRuntimeConfig.asset` 的 `sceneName` 填 `thirdDemo`。

**替代方案**：用 `projectDirectory.json` 里列出的 4 个正式项目之一（Wartsila / DazuRockCarvings / aiysz / ZhongYue）。

**为什么否决**：这 4 个的空间场景包**全部 404**（逐个 `HEAD` 实测）。那份目录只是宿主选择下拉框的数据源，不代表包在 CDN 上。源工程 `GameplaySettings.asset` 里的默认值就是 `thirdDemo`——它是唯一实际存在的空间场景包（358 KB，含 5 个 tour，5 个 tour 的内容包与版本 API 全部正常）。

### D3 — 必须写一个显式激活器，不靠 `alwaysDisplayed`

**选了什么**：新增 `IteRenderSmokeDriver`，在 `OnInitialized` 之后调一次 `IteRuntime.ActivateTour(tourId)`。

**替代方案**：挑一个 `displayType == alwaysDisplayed` 的 tour——那样 `CreateTourObject` 内部就会 `await Enable()`，零额外代码。

**为什么否决**：`thirdDemo` 的 5 个 tour **无一** 是 `alwaysDisplayed`（4 × `regionalTrigger` + 1 × `normal`）。非 `alwaysDisplayed` 的 tour 在 `CreateTourObject` 末尾走 `Disable()`，内容树是空的，且**不报任何错**。不写激活器的结果是：日志一路正常、进度到 1、`OnInitialized` 触发，而眼前一片空——正是这个包最典型的静默失效形状。

`ActivateTour` → `TourDirector.ActivateById` → `Activate(tour, pose: null)`：`pose` 为 null 时跳过 `ChangeTourObjectTransform`，tour 保持 json 里的 transform（`wm0l5qcn_ibd` 是单位矩阵，即落在 `TourRoot` 原点）。这正是无标记、无位姿时想要的行为，`ActivateById` 的文档注释也明说了它就是为这个场景存在的（包 design D30）。

**代价**：`Activate` 里 `_ = tour.Enable()` 是 fire-and-forget，`CreateTourScene` 抛异常会变成没人接的 `UnobservedTaskException`。激活器因此必须订阅 `OnTourSceneLoaded` 并在超时未收到时打日志，否则"建实体树失败"这一段是黑的。

### D4 — 全离线跑，缓存手工摆好；且只保留一个 tour

**选了什么**：`IteHostBootstrap.networkAvailable = false`；`persistentDataPath` 下手工摆好空间场景与 **一个** tour 的缓存；缓存里的 `thirdDemo.json` 只保留 `wm0l5qcn_ibd` 一个 tour。

**替代方案 A**：联网跑。**否决**：踩 D5 的解压层级 bug，空间场景那一步必炸。

**替代方案 B**：离线，但摆齐 5 个 tour。**否决**：`LoadAsync` 对 `scene.tours` 逐个 `await FetchTourAsync`，任何一个读不出来就 `throw`，整链断——所以要么 5 个全摆（约 290 MB，含一个 151 MB 的），要么裁剪 tour 列表。裁剪后单个 tour 只要 29 MB，且**渲染验收不需要第二个 tour**：要验的是"一个 tour 的实体树能不能建出来并显示"，第二个 tour 只是把同一段代码再跑一遍。

**为什么这么选**：把网络彻底移出 Play 循环。这也正是源工程一直以来的跑法（`GameplaySettings.asset` 里 `isNetworkAvailable: 0`）。

**代价**：缓存是手工产物，不在仓库里，换台机器要重摆。因此步骤写进 `cache-layout.md`，不靠口口相传。

选中的 tour：`wm0l5qcn_ibd`（「宝顶山--孔雀明王经变相」，29 MB）——5 个里最小，且结构最简单：1 个 scene、1 个 entity、组件为 `EMWModelRender` + `LoadTrigger` + `PlayAnimationAction`、1 个 `EMWModel` 资源（glb + mp3）。一个 glb 渲染出来就是验收通过。

### D5 — 空间场景 zip 的解压层级不匹配：本次**只记录，不修**

**查实的事实**：

```
thirdDemo.zip 的条目:   thirdDemo/thirdDemo.json      （另有 __MACOSX/ 条目）
解压目标:               persistentDataPath/IteSpaceScene_thirdDemo/
实际落盘:               IteSpaceScene_thirdDemo/thirdDemo/thirdDemo.json
FetchSpaceSceneAsync 读: IteSpaceScene_thirdDemo/thirdDemo.json          ← 差一层
  → File.Exists false → LoadJsonAsync 返回 default(null)
  → throw "No tours found in IteSpaceScene: thirdDemo"
```

`ZipContentDownloader.ExtractAsync` 不剥顶层目录，源工程的 `FileUtils.ExtractZipAsync` 也不剥——**两边同一个行为，所以源工程联网同样会炸**。这解释了源工程为什么默认 `isNetworkAvailable: 0`。zip 里的 `__MACOSX/` 条目说明该包是 macOS Finder 对文件夹右键压缩的产物，即"多这一层"是打包侧的习惯，不是偶发。

注意 tour 包**没有**这个问题：条目顶层是 `{tourId}/`，而它的解压目标是 `relativeFolder = ""`（即 `persistentDataPath` 根），正好对上。所以两类包对"顶层目录"的期望是**相反**的，而代码里对此没有任何表述。

**选了什么**：本 change 不修，只把事实记录在案。

**替代方案**：顺手修掉。

**为什么否决**：内容获取与内容渲染是两件事。本 change 要回答的问题是"实体树能不能建出来、模型能不能显示"，用离线缓存回答它是完整的。把一个联网解压路径的修复混进来，会让"渲染跑通了"这个结论同时依赖一处新改的下载逻辑，两件事一起变，哪件事导致的结果就说不清了。项目原则里"迁移与修 bug 保持可分离"在这里同样成立。

**留给后续 change 的形状约束**（防止后人选错）：不要在读取侧做"两个路径都试一下"的 fallback——那是把两类包的差异藏进一次运气好的 `File.Exists`。正确的形状是让解压方**显式声明**期望：`DownloadAndExtractAsync` 增加一个说明顶层目录语义的参数，空间场景包与 tour 包各自明确传值。"碰巧能跑"不能替代"明确规定"。

### D6 — 编辑器验收不等于真机验收，本 change 只覆盖前者

**选了什么**：验收面只到"编辑器 Play 里看到模型"。

**为什么**：tour 的模型经 gltfast 在运行时导入，材质是运行时生成的 URP 材质。编辑器 Standalone 目标下的着色器变体集合与 Android + XR（单通道立体实例化）下的不是一回事——黑模、粉模、只在一只眼里出现，这几类问题**只在设备上暴露**。把它们算进本 change 的验收，会让一个本可以在 5 分钟内闭环的验证变成一个需要打包的验证。

**代价**：编辑器通过不构成真机可用的证据。后续 change 需处理：相机归属（D1 的代价）、`IteRenderTest` 作为叠加内容场景进 `EditorBuildSettings` 与 `BuildScript`、以及 290 MB 内容在设备上的落盘策略。

## Risks / Trade-offs

| 风险 | 影响 | 应对 |
|---|---|---|
| `IteHostBootstrap.Start` 是 `async void` | 编辑器中途退 Play 时链路半途而废，可能刷 `UnobservedTaskException` | 离线后整链是本地 IO，毫秒级完成，窗口极小；不改包 |
| `_ = tour.Enable()` fire-and-forget | 建实体树失败无红字 | 激活器订阅 `OnTourSceneLoaded`，超时未收到则打日志（D3 代价） |
| `ContentAssetLoader` 缺文件静默返回 null | 缓存摆错时表现为"什么都不显示"，与"渲染失败"无法区分 | `cache-layout.md` 给出精确到文件的落盘清单；激活器打印 `OnLoadProgress` 分段 |
| 缓存是手工产物，不进仓库 | 换机器/清缓存后不可复现 | 步骤写进 `cache-layout.md` |
| 删 `ITE Host` 后 `MRCore` 的 diff 是手改 YAML | 改坏场景文件 | 三个对象 + 父节点 `m_Children` 一处引用，改完在编辑器里打开 `MRCore` 确认无 missing |

## Open Questions

- 那 4 个正式项目（Wartsila / DazuRockCarvings / aiysz / ZhongYue）的包 404 是已下线还是换了路径？影响的是"这个包最终要加载谁"，不影响本 change。
- `LatestVersionJsonResult.Data.version` 声明为 `string`，而 API 实际返回 JSON 数字（`"version": 7`）。Newtonsoft 会做数字→字符串转换，联网路径修复时需一并确认 `TourVersionCache` 里存的形态一致。本 change 离线，不触及。
