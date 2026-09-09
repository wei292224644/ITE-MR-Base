## 1. 先确认 HEAD 可用（design 的 Open Question，不通则整个 D2 要换方案）

> 排在第一组：D2 选 HEAD 而非 `If-None-Match`，前提是该直链经 `UnityWebRequest` 也能正常返回 `ETag`。`curl -I` 已确认服务端支持，但那走的是 curl 的网络栈。这一条不通，后面全部要按 D2 的替代方案重做。

- [x] 1.1 在 Editor 里发一次 `UnityWebRequest.Head("https://ite-spatial-config.uality.cn/thirdDemo.zip")`，打印 `responseCode` 与 `GetResponseHeader("ETag")`
  - 经 `unity command run_tests` 在 live Editor 里以临时 `[UnityTest]` 跑通，记录后即删除（网络依赖的用例不留在常驻套里）
- [x] 1.2 **验证点**：`responseCode == 200` 且 `ETag` 非空。拿到的值应与 `curl -I` 一致
  - `result=Success code=200 ETag="B6A43FCA26644FF44FA21BBADCC8D5B3-1" Last-Modified=Thu, 25 Dec 2025 03:41:43 GMT`
  - 与 `curl -I` 的返回值**逐字符一致**，含外层双引号。D2 成立，无需换方案
  - 顺带敲掉 probe 的两条开放假设：HEAD 经 Unity 网络栈可用；两侧校验器值一致
  - `ETag` 字面量含双引号（`"B6A4..."` 而非 `B6A4...`）。存取两侧都来自本入口，形态一致，按原样存即可，不做规范化
- [x] 1.3 若 HEAD 不通：停下来，按 design D2 的替代方案（`If-None-Match` + 显式判 `responseCode == 304`）改写 D2 与 D5，再继续
  - **不适用** —— HEAD 可用，未触发该分支

## 2. 补齐解压测试（design D7，独立于版本比对，可先落地）

- [ ] 2.1 `ZipContentDownloaderTests` 新增用例：zip 含显式目录条目（`thirdDemo/`、`thirdDemo/assets/`）+ 文件条目 + `__MACOSX/` 影子条目，按 `ZipTopLevel.Strip` 解压
- [ ] 2.2 断言落盘为 `{out}/thirdDemo.json`、`{out}/assets/logo.png`，且 `{out}/thirdDemo/` 与 `{out}/__MACOSX/` 均不存在
- [ ] 2.3 **验证点**：新用例通过，且不改动 `ZipContentDownloader` 任何一行——该分支本就该是安全的，用例若逼着改被测代码，说明真包早就解压错了，此时停下来重新评估
- [ ] 2.4 跑整个 `Uality.IteTour.Tests` 确认无回归（当前基线 195 passed / 0 failed）

## 3. 场景包校验器缓存（design D4）

- [ ] 3.1 新增 `SpacePackageEtagCache`（`Runtime/Internal/`），键形如 `ite.space.{sceneName}.etag`，接口对齐 `TourVersionCache`：`KeyFor` / `Get` / `Set` / `Clear`
- [ ] 3.2 `Get` 在无记录时返回空串（PlayerPrefs 缺省行为），与 `TourVersionCache` 一致
- [ ] 3.3 补 EditMode 测试：键名不占用宿主全局命名空间（design D10）、无记录时返回空串、`Set` 后可读回

## 4. 比对决策与接入（design D1/D3/D5/D6）

- [ ] 4.1 新增纯函数 `IteContentPipeline.ShouldDownloadSpacePackage(cachedEtag, serverEtag)`，与既有 `ShouldDownloadTourPackage` 并列
- [ ] 4.2 实现 D5 的三分支语义：`serverEtag` 为空且 `cachedEtag` 非空 → false（退回缓存）；`serverEtag` 为空且 `cachedEtag` 为空 → true（必须下载）；两者非空且不等 → true；相等 → false
- [ ] 4.3 补 EditMode 测试覆盖 4.2 的全部四种组合——这是本次唯一与 tour 侧语义不同的地方（tour 在版本查不到时一律 false），必须逐条钉死
- [ ] 4.4 新增只取响应头的请求入口（`ContentAssetLoader` 或 `Runtime/Internal/` 下），返回 `ETag`；请求失败或响应头缺失时返回 null，不抛异常
- [ ] 4.5 跳过下载的条件除 4.2 的判定外，**同时**要求 `File.Exists(IteSpaceScene_{scene}/{scene}.json)`（design D5 收窄）。这个判断放在 pipeline 调用点，`ShouldDownloadSpacePackage` 保持纯函数、不碰文件系统
- [ ] 4.6 `FetchSpaceSceneAsync` 的联网分支改为：查 `ETag` → 4.2 判定 + 4.5 文件检查 → 需要才下载
- [ ] 4.7 下载解压成功后才 `SpacePackageEtagCache.Set`（design D6）。解压抛 `InvalidDataException` 时异常向上冒泡，写记录自然跳过——确认没有 `try/catch` 把它吞掉
- [ ] 4.8 删掉 `IteContentPipeline.cs` L74-75 的 TODO 注释
- [ ] 4.9 日志按 design D11 双向打：命中时输出场景名与命中的 `ETag`；需要更新时输出**本地与服务端两个值**，使"本地无记录"与"两值不匹配"可区分

## 5. 删除旧内容与删除前校验（design D9/D10 — 本组含唯一的破坏性操作，逐条对照 design 再写）

> ⚠️ D9 的坑：tour 包的 `DownloadAndExtractAsync` 传的 `relativeFolder` 是空串，其 `outputFolder` 是 `persistentDataPath` **根**。删除目标必须由 pipeline 显式给出，**绝不能**用 `outputFolder`。

- [ ] 5.1 删除逻辑写在 `IteContentPipeline`，`ZipContentDownloader` 不加任何删除代码
- [ ] 5.2 删除目标显式指定：场景包 `IteSpaceScene_{sceneName}/`，tour 包 `{tourId}/`
- [ ] 5.3 删除时机：下载成功之后、解压之前。下载失败时不得触发删除
- [ ] 5.4 删除前用 `ZipEntryPath.TryResolve(persistentDataPath, name, out var dir)` 校验（design D10），返回 false 则拒绝删除并记一条含该目录名的错误日志
- [ ] 5.5 补 EditMode 测试：`TryResolve` 对 `""`、`"."`、`"../.."` 均返回 false。这三条已在既有 `ZipEntryPathTests` 覆盖的范围内，确认无遗漏即可，不重复造用例
- [ ] 5.6 补 EditMode 测试：目录名为空时删除路径不被执行（用一个可观测的替身或临时目录断言目录仍在）

## 6. tour 侧同类缺陷（design D8）

- [ ] 6.1 `UpdateTourPackageIfStaleAsync` 的跳过条件增加 `File.Exists({tourId}/{tourId}.json)`，与 4.5 同构
- [ ] 6.2 tour 包重下时按第 5 组的规则删除 `{tourId}/`，含 5.4 的校验
- [ ] 6.3 tour 侧日志按 D11 双向打
- [ ] 6.4 补 EditMode 测试：版本一致但内容文件不存在时判定为「需要下载」

## 7. 真机/Editor 验证（缓存状态是这组的全部难点，按顺序跑）

> `persistentDataPath` 在 macOS 编辑器下为 `~/Library/Application Support/响堂山/响堂山`。

- [ ] 7.1 清空 `IteSpaceScene_thirdDemo/` 与 `SpacePackageEtagCache` 的 PlayerPrefs 键，构造"首跑"状态
- [ ] 7.2 **验证点（联网首跑）**：`networkAvailable = true` 跑一次，场景包被下载解压，布局为 `IteSpaceScene_thirdDemo/thirdDemo.json`（不多一层），`ETag` 记录被写入
- [ ] 7.3 **验证点（联网复跑命中缓存）**：不清缓存再跑一次，控制台出现 4.9 的命中日志，**无**场景包下载请求，加载链正常完成
- [ ] 7.4 **验证点（缓存失效重下）**：手工把 PlayerPrefs 里的 `ETag` 记录改成一个假值再跑，日志打出本地假值与服务端真值两侧，场景包被重新下载，记录被更新回真实值
- [ ] 7.5 **验证点（目录被删能自愈 — 场景包）**：只删 `IteSpaceScene_thirdDemo/`、**保留** PlayerPrefs 记录再跑。改动前这里会永久卡在 `No tours found`；改动后应重新下载并正常完成
- [ ] 7.6 **验证点（目录被删能自愈 — tour）**：只删其中一个 tour 目录、保留 `TourVersionCache` 记录再跑，该 tour 被重新下载，其余 tour 未被重下
- [ ] 7.7 **验证点（重下无孤儿文件）**：在 `IteSpaceScene_thirdDemo/assets/` 里手工放一个 `orphan.png`，把 `ETag` 记录改成假值触发重下，重下完成后 `orphan.png` 不存在
- [ ] 7.8 **验证点（删除不越界）**：触发一次 tour 重下，确认只有该 `{tourId}/` 被清空，其余 tour 目录与 `IteSpaceScene_*/` 完好。这条是 D9 那个坑的兜底检查
- [ ] 7.9 **验证点（校验器查不到 + 有缓存）**：把配置里的 `spaceSceneBaseUrl` 临时指向一个不可达地址再跑，MUST NOT 重新下载，直接读缓存并正常完成
- [ ] 7.10 **验证点（离线路径未受影响）**：`networkAvailable = false` 跑一次，加载链完成，且**无任何 HTTP 请求**——包括本次新增的 HEAD。这条最容易漏：新加的校验器查询若没被 `networkAvailable` 分支包住，离线路径就破了
- [ ] 7.11 跑整个 `Uality.IteTour.Tests` 确认无回归（改动前基线 195 passed / 0 failed）

## 8. 收尾

- [ ] 8.1 `openspec validate scene-package-version-check --strict` 通过
- [ ] 8.2 主 spec `openspec/specs/ite-content-acquisition/spec.md` 按 delta 同步
- [ ] 8.3 归档本 change
