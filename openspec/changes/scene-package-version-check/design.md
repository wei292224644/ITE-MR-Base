## Context

`IteContentPipeline.FetchSpaceSceneAsync` 目前只要 `networkAvailable` 为真就无条件下载解压空间场景包（L74-82 的 TODO 明确记着这一点），而 `FetchTourAsync` 走 `UpdateTourPackageIfStaleAsync` 的版本比对。两者不一致的根因是后端只给了 tour 的版本 API：

```
tour   api.uality.cn/ITE/Tour/LatestVersion?tourId={0}   → {data:{version}}
场景包  ite-spatial-config.uality.cn/{scene}.zip          → 静态直链，无版本号
```

本次实测（`curl -I`）确认场景包直链是阿里 OSS + CDN，响应头带 `ETag` 与 `Last-Modified`。校验器在 HTTP 层已经存在，不必等后端补接口。

同一次实测还确认真包是 macOS Finder 压缩产物，含 0 字节目录条目：

```
thirdDemo/                              ← 目录条目
thirdDemo/thirdDemo.json
thirdDemo/assets/                       ← 目录条目
thirdDemo/assets/logo.png
__MACOSX/thirdDemo/._thirdDemo.json
```

而 `ZipContentDownloaderTests` 现有 5 条用例构造的 zip 只有文件条目，从未覆盖目录条目。

## Goals / Non-Goals

**Goals:**

- 场景包内容未变时不重复下载解压，与 tour 包的缓存行为对齐
- 校验器不可得时不比现状更差——退回本地缓存，不因为查不到版本就让整条加载链失败
- 补齐 Strip 语义对真包形状（含目录条目）的测试覆盖

**Non-Goals:**

- 不改 tour 包的版本比对路径
- 不改 `ZipTopLevel` 的 Strip / Preserve 语义。本次已实测确认 Strip 对真包是必需且正确的（真包带顶层 `thirdDemo/`，目标目录 `IteSpaceScene_thirdDemo/` 已含场景名，不剥会多一层）
- 不改离线路径。`networkAvailable == false` 时仍然不发任何请求，包括本次新增的校验器请求
- 不做内容完整性校验（校验器只用于判断"要不要重下"，不用于判断"下下来的对不对"）

## Decisions

### D1：用 `ETag` 作校验器，不用 `Last-Modified`

`ETag` 是强校验器，`Last-Modified` 只有秒级精度，同一秒内的两次覆盖上传无法区分。实测该 OSS 两者都返，但没有理由选弱的那个。

两者都取不到时按 D5 处理，不做「ETag 缺失就退而用 Last-Modified」的降级——两级降级会让"为什么这次重下了"变得难以归因，而这条路径本来就是为了可归因才做的。

**替代方案**：请后端为场景包补一个和 tour 对称的 `LatestVersion` 接口。否决理由不是技术上更差（对称性其实更好），而是它把本次改动阻塞在外部依赖上；ETag 方案不依赖后端改动即可落地。若后端将来真的补了该接口，可以把 D3 的纯决策函数换个入参来源，替换成本很低。

### D2：用 HEAD 请求探测，不用 `If-None-Match` + 304

| 方案 | 内容未变（常态） | 内容已变（少数） |
|---|---|---|
| HEAD 探测 | 1 次 HEAD（仅头） | 1 次 HEAD + 1 次 GET |
| `If-None-Match` GET | 1 次 GET → 304（仅头） | 1 次 GET（带体） |

常态开销两者相同；`If-None-Match` 只在"内容已变"这个少数分支上省一次往返。

选 HEAD 的理由是 `UnityWebRequest` 对 304 的处理：304 不是 2xx，`result` 会是 `ProtocolError`，必须靠 `responseCode == 304` 把它和真正的协议错误区分开。把「成功」伪装成错误码去判断，正是本仓库要避免的"碰巧能跑"的形状。HEAD 的成功就是成功、失败就是失败，语义不需要解释。

代价明确：内容变更时多一次 HEAD 往返。可接受——那条路径后面紧跟着一次几百 KB 的下载。

### D3：比对决策留在 `IteContentPipeline`，`ZipContentDownloader` 保持无状态

沿用 tour 侧已有的分层：`UpdateTourPackageIfStaleAsync` 在 pipeline 里决定要不要下，`ZipContentDownloader` 只管下和解压、不认识"版本"这个概念。场景包照此办理，不把校验器逻辑塞进 `DownloadAndExtractAsync`。

比对本身做成纯函数，与既有的 `ShouldDownloadTourPackage`（design D25 的"纯决策"）对齐，使其不依赖网络即可测试：

```
ShouldDownloadSpacePackage(cachedEtag, serverEtag) -> bool
```

### D4：新建场景包校验器缓存，不改造 `TourVersionCache`

`TourVersionCache` 的键形如 `ite.tour.{tourId}.version`（design D10：不占用宿主全局 PlayerPrefs 键名空间）。场景包用平行的一个小静态类，键形如 `ite.space.{sceneName}.etag`。

**替代方案**：把 `TourVersionCache` 泛化成 `ContentVersionCache.Get(scope, id)`，两类包共用。否决理由：它要改一个已经正确且有测试覆盖的类型、并重命名其公开名字，换来的只是省下八行；两类包的键名空间本来就该是分开的，两个小类型比一个带 scope 参数的类型更直白。若将来出现第三类内容包，届时再泛化。

### D5：校验器取不到时退回本地缓存，但本地无缓存时必须下载

与 `ShouldDownloadTourPackage` 在 `serverVersion` 为空时返回 false（退回缓存）的既有语义一致：网络抖动不该让一份完好的本地缓存失效。

但要区分两种"不下载"：

```
serverEtag 取不到 + 本地有缓存  → 不下载，读缓存        （退回，与 tour 一致）
serverEtag 取不到 + 本地无缓存  → 下载                  （否则必然读不到文件而抛异常）
```

第二种情况 tour 侧不存在同等风险（tour 版本查询失败时若无缓存，后续 `LoadJsonAsync` 抛异常是唯一结果，也确实该抛）。场景包这里多下一次即可自愈，不该为了对称而放弃自愈。

"本地有无缓存"以校验器缓存记录是否存在为准，不去 stat 文件——文件存在性判断会把"内容下过"和"内容完整"混为一谈，而校验器记录按 D6 只在解压成功后才写。

### D6：只在下载并解压成功之后才写入校验器记录

沿用 tour 侧 `UpdateTourPackageIfStaleAsync` 的顺序（`TourVersionCache.Set` 在 `DownloadAndExtractAsync` 之后）。`ExtractAsync` 在 Strip 声明与 zip 实际不符时会抛 `InvalidDataException`，异常向上冒泡，写记录这一步自然被跳过，下次冷启动会重试。

### D7：补一条含目录条目的解压用例，不改被测代码

真包每次解压都会走到 `ZipContentDownloader.TryStrip` 的 `stripped.Length == 0 → return false` 分支（顶层目录条目 `thirdDemo/` 剥掉前缀后为空串），现有用例一条都没覆盖到。

读代码判断该分支是安全的（返回 false → `continue` 跳过；`thirdDemo/assets/` 剥成 `assets/` 后由 `entry.IsDirectory` 分支建目录后跳过），但"读代码觉得安全"和"跑过"是两回事，而这恰好是真包唯一未被测试形状覆盖的地方。

用例构造的 zip 条目形状必须与实测的真包一致：显式目录条目 + 顶层目录 + `__MACOSX/` 影子条目。

## Risks / Trade-offs

**CDN 返回的 ETag 与源站不一致，或不同 CDN 节点之间不一致** → 后果是偶发多下一次，不会下错内容或读到坏缓存。实测响应头含 `x-oss-request-id`、`ETag: "...-1"`（OSS 分片上传对象的形态），ETag 由对象本身决定而非节点。判为可接受，不额外加固。

**后端以相同内容重新上传导致 ETag 变化** → 多下一次，无正确性影响。

**HEAD 请求被中间层拒绝或返回与 GET 不同的头** → 落到 D5 的"取不到"分支，退回缓存或下载，不会失败。若实测发现该直链不支持 HEAD，则回退到 D2 的替代方案（`If-None-Match`），此时需按 D2 所述显式处理 304 的 `responseCode`。

**多下一次 vs 读到过期内容** → 本设计一律偏向多下一次。场景包 366 KB，重下的代价远低于让用户看到过期场景。

## Open Questions

- 该直链是否支持 HEAD，需在实现的第一步实测确认（`curl -I` 已确认返回 200 与完整响应头，但那是通过 curl；`UnityWebRequest.Head` 经由 Unity 的网络栈，仍需在真机或 Editor 内验证一次）
- 是否要为「校验器命中、跳过下载」加一条可见日志。倾向加：本仓库的失败模式以静默为主，"这次为什么没重下"应当能从日志读出来
