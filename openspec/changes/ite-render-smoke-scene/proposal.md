## Why

`com.uality.ite-tour` 包已经完整移植进来（`IteRuntime` / `IteContentPipeline` / `IteTourAssembler` / `IteTourObject`，含 40+ 条编号决策与一整套 EditMode 测试），但**从来没有一次端到端跑起来过**：没有任何场景把它跑到"tour 内容出现在眼前"这一步。包里所有测试都是纯数据/纯逻辑的离机测试，`IteTourObject.CreateTourScene`（真正建 GameObject 那一段）在 codegraph 里标着 ⚠️ no covering tests found。

现状还更糟一点：`MRCore.unity` 里有一个已接线的 `ITE Host`（GameObject 1597420651，`IteHostBootstrap`，config/anchorRoot/tourRoot/xrCamera 全部填好），而它引用的 `Assets/Settings/ITE/IteRuntimeConfig.asset` 的 `sceneName` 是**空字符串**。于是每次启动任何场景，都会走一遍 `FetchSpaceSceneAsync("")` → 请求 `https://ite-spatial-config.uality.cn/.zip` → 下载失败 → `throw`。核心场景常驻一条必然失败的联网链路，而 `MRCore` 的职责是 XR rig 与就绪闸门。

本 change 要的东西很小：**一个编辑器里就能按 Play 看到 tour 内容渲染出来的最小场景**，以及把 ITE 从核心场景里搬走。

同时记录一个在调研中查实、但**不在本次修复范围内**的包级缺陷（见 D5）：空间场景包的联网路径从未跑通过，且代码里没有任何痕迹说明这一点。

## What Changes

- 新建 `Assets/Scenes/IteRenderTest.unity`——自包含的 ITE 渲染冒烟场景：自带 Main Camera、`AnchorRoot` / `TourRoot` 空物体、`ITE Host`（`IteHostBootstrap`）。编辑器里直接打开按 Play 即可，不经 `MRCore`，不进 `EditorBuildSettings`，不进 `BuildScript`。
- 从 `MRCore.unity` **删除** `ITE Host` 物体（含其 `IteHostBootstrap`）。ITE 导览是内容，不是核心装配（D1）。
- 新增 `IteRenderSmokeDriver`（`MRBase.Ite.Host` 下，约 30 行）：订阅 `IteRuntime` 的 `OnLoadProgress` / `OnInitialized` / `OnTourSceneLoaded`，把状态打成 `[ITE Smoke]` 日志；`OnInitialized` 之后按配置的 tourId 调一次 `IteRuntime.ActivateTour(tourId)`。**没有它就没有任何东西会被渲染出来**（D3）。
- `Assets/Settings/ITE/IteRuntimeConfig.asset` 的 `sceneName` 填 `thirdDemo`（D2）。
- 记录内容缓存的落盘布局与手工准备步骤（`openspec/changes/ite-render-smoke-scene/cache-layout.md`），供他人复现。

## Non-Goals

- **不修** 空间场景 zip 的解压层级不匹配（D5）。本 change 用手工摆好的离线缓存绕开它，理由见 D5：内容获取与内容渲染是两件事，混在一起修会让"渲染跑没跑通"这个结论失去意义。修复另开 change。
- 不上真机。gltfast 运行时导入的 URP 材质在 Quest / PICO 上的表现（黑模 / 粉模 / 单通道立体）只能在设备上暴露，本 change 不覆盖（D6）。
- 不接标记扫码激活。`MarkerSourceAdapter` 已随 `unified-marker-tracking-contract` 下线，重新接入走新契约（`MarkerTrackingSession`），与本 change 无关。
- 不动 `IteHostBootstrap` 里已注释的 `markerTracking` 字段与相关注释。
- 不做加载 UI、不做进度条、不做错误提示面板。日志足够。

## Capabilities

### New Capabilities

- `ite-render-smoke-scene`: ITE 内容管线的渲染验收面——一个编辑器内可运行、自包含、离线的最小场景，把"下载/解析/装配/建实体树"这条从未被端到端验证过的链路跑到可见为止。

### Modified Capabilities

（无。`openspec/specs/` 下只有 `unified-marker-tracking-contract`，与本 change 无交集。）

## Impact

**新增**
- `Assets/Scenes/IteRenderTest.unity`
- `Assets/Scripts/IteHost/IteRenderSmokeDriver.cs`
- `openspec/changes/ite-render-smoke-scene/cache-layout.md`

**修改**
- `Assets/Scenes/MRCore.unity` — 删除 `ITE Host`（GameObject 1597420651 / Transform 1597420652 / MonoBehaviour 1597420653），并从父节点 614014891 的 `m_Children` 移除
- `Assets/Settings/ITE/IteRuntimeConfig.asset` — `sceneName: thirdDemo`

**不动**
- 整个 `Packages/com.uality.ite-tour`（D5 划出范围）
- `Assets/Scripts/IteHost/IteHostBootstrap.cs`（现有装配点够用，不加字段）
- `BuildScript.cs`、`EditorBuildSettings.asset`

## 已查实的外部事实

调研阶段实测确认，记录在此以免后人重查：

| 事实 | 证据 |
|---|---|
| 项目目录 `projectDirectory.json` 列 4 个项目：Wartsila / DazuRockCarvings / aiysz / ZhongYue | 源工程 `Assets/Scripts/Utils/MainConstants.cs:7` 的 `ItePropertiesUrl` |
| 这 4 个项目的空间场景包**全部 404** | `HEAD {base}/{name}.zip` 逐个实测 |
| 源工程默认加载的是 `thirdDemo`，且默认**离线** | `ite-space-tour/Assets/ScriptableObjects/GameplaySettings.asset`：`sceneName: thirdDemo`、`isNetworkAvailable: 0` |
| `thirdDemo.zip` 存在，358 KB，含 5 个 tour | 实测下载解压 |
| 5 个 tour 全部有内容包，版本 API 全部正常 | `HEAD ite-pkg`、`GET api.uality.cn/ITE/Tour/LatestVersion` 逐个实测 |
| 5 个 tour **无一** 是 `alwaysDisplayed` | `thirdDemo.json`：4 × `regionalTrigger` + 1 × `normal` |
| tour 内容包体积：29 / 31 / 37 / 44 / **151** MB，合计约 290 MB | `content-length` 逐个实测 |
| tour 包的 zip 条目顶层是 `{tourId}/`，与 `relativeFolder=""` 的解压目标**吻合** | 对 `4kvhqwvp_12f_wx.zip` 做 range 请求读中央目录 |
| 空间场景包的 zip 条目顶层是 `thirdDemo/`，与解压目标 `IteSpaceScene_thirdDemo/` **不吻合**（多一层） | `unzip -l thirdDemo.zip`，且含 `__MACOSX/` 条目——macOS Finder 对文件夹压缩的产物 |
| `TourAssetPaths` 的全部路径形状与真实包布局吻合 | 比对 `wm0l5qcn_ibd` 与 `4kvhqwvp_12f` 的实际条目 |
