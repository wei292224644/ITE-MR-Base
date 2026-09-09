## Why

空间场景包目前只要联网就每次冷启动重新下载解压（`IteContentPipeline.FetchSpaceSceneAsync` L74-82 的 TODO），而 tour 包走版本比对。这个不一致是从源工程 `ite-space-tour` 一起带过来的，不是迁移引入的。

之所以一直没修，是因为后端只给了 tour 的版本 API（`api.uality.cn/ITE/Tour/LatestVersion`），场景包只有静态直链 `ite-spatial-config.uality.cn/{scene}.zip`，没有可比的版本号。

本次实测确认该直链是阿里 OSS 静态托管，响应头带强校验器：

```
ETag: "B6A43FCA26644FF44FA21BBADCC8D5B3-1"
Last-Modified: Thu, 25 Dec 2025 03:41:43 GMT
```

即 HTTP 层已经提供了版本依据，不必等后端补接口。thirdDemo 场景包 366 KB，每次冷启动白下一次。

同时本次实测暴露了解压侧一个测试缺口：真包（macOS Finder 压缩产物）含 0 字节目录条目 `thirdDemo/`、`thirdDemo/assets/`，而 `ZipContentDownloaderTests` 现有 5 条用例构造的 zip 全部只有文件条目，`ZipContentDownloader.TryStrip` 中 `stripped.Length == 0 → return false` 这一分支从未被测试覆盖，却是真包每次解压必经的路径。

## What Changes

**缓存命中判定**

- 空间场景包下载前先做条件请求，拿到的校验器与本地缓存记录一致时**跳过下载与解压**，直接读缓存
- 跳过的条件不止校验器一致，还要求内容文件确实在磁盘上（见下）
- 校验器查不到（请求失败、响应头缺失）时**退回本地缓存**而非重下或抛错——与 `ShouldDownloadTourPackage` 在 `serverVersion` 为空时的既有语义对齐
- 缓存中无记录（首跑）或校验器不一致时下载 + `ZipTopLevel.Strip` 解压，成功后才写入校验器记录（沿用 tour 侧「先落盘后写版本」的顺序）

**两类包共同的缓存失效缺陷（probe 期间发现，scope 因此扩大到 tour 包）**

- 内容目录被手工删除、而版本/ETag 记录仍在时，当前逻辑会跳过下载并在随后读取时抛异常，**且每次冷启动重复同一路径，无法自愈**。修法是把「内容文件是否存在」加进跳过条件。空间场景包与 tour 包**各自**都要改
- 重新下载时旧内容不再原地覆盖，而是**先清空该包的目录再解压**，避免上一版被移除的文件永久滞留。顺序为下载成功 → 删除 → 解压 → 写记录，使网络失败不会毁掉一份能用的缓存
- 删除的目标目录由 `IteContentPipeline` 显式指定（场景包 `IteSpaceScene_{sceneName}/`、tour 包 `{tourId}/`），**不是** `ZipContentDownloader` 的 `outputFolder`——tour 包的 `outputFolder` 是 `persistentDataPath` 根
- 递归删除前校验目录名，为空或解析后逃出根目录时拒绝删除。复用既有的 `ZipEntryPath.TryResolve`

**可观测性**

- 命中与未命中缓存都记日志，未命中要打出本地与服务端两侧的值。本改动的失败形态是静默退化成今天的行为（每次都重下），不打日志无法与「日志尚未接上」区分

**测试**

- `ZipContentDownloaderTests` 补一条用例：zip 含显式目录条目时 Strip 语义仍正确，覆盖 `TryStrip` 的空串分支

非目标：不改 `ZipTopLevel` 的 Strip/Preserve 语义（本次已实测确认 Strip 对真包是必需且正确的）；不引入内容包的整体回收策略（从场景描述中被移除的 tour 目录仍不会被回收，另见 probe-report 的开放假设）。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

- `ite-content-acquisition`: 「联网与离线是两条明确的路径」这条 Requirement 当前写死了"空间场景包每次重新下载"，改为按校验器比对决定；跳过下载的条件同时要求内容文件存在；新增校验器查询失败退回缓存、重下时旧内容不得残留、删除前校验目录名等场景。解压相关 Requirement 增补真包的目录条目形状，语义不变。

## Impact

- `Packages/com.uality.ite-tour/Runtime/Core/IteContentPipeline.cs`
  - `FetchSpaceSceneAsync` 的下载分支，消掉 L74-75 的 TODO
  - `UpdateTourPackageIfStaleAsync` 的跳过条件（tour 侧同类缺陷）
  - 两处新增删除旧目录的动作与目录名校验
- `Packages/com.uality.ite-tour/Runtime/Internal/` — 新增场景包校验器缓存（参照既有 `TourVersionCache`），以及一个只取响应头的条件请求入口
- `Packages/com.uality.ite-tour/Runtime/Internal/ZipEntryPath.cs` — 仅复用，不修改
- `Packages/com.uality.ite-tour/Tests/Editor/ZipContentDownloaderTests.cs` — 新增目录条目用例
- 无 API 破坏；离线路径（`networkAvailable == false`）行为完全不变，仍不发任何请求——包括本次新增的校验器请求
- **行为变更**：内容包重新下载时该包目录会被先清空。使用方若在这些目录里放过自己的文件（不应该，但值得说明），会随重下丢失
