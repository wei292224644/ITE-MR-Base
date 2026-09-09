## 1. 先确认 HEAD 可用（design 的 Open Question，不通则整个 D2 要换方案）

> 排在第一组：D2 选 HEAD 而非 `If-None-Match`，前提是该直链经 `UnityWebRequest` 也能正常返回 `ETag`。`curl -I` 已确认服务端支持，但那走的是 curl 的网络栈。这一条不通，后面全部要按 D2 的替代方案重做。

- [ ] 1.1 在 Editor 里发一次 `UnityWebRequest.Head("https://ite-spatial-config.uality.cn/thirdDemo.zip")`，打印 `responseCode` 与 `GetResponseHeader("ETag")`
- [ ] 1.2 **验证点**：`responseCode == 200` 且 `ETag` 非空。拿到的值应与 `curl -I` 一致（当前为 `"B6A43FCA26644FF44FA21BBADCC8D5B3-1"`，内容若被重新上传会变，比对的是"两边一致"而非这个字面量）
- [ ] 1.3 若 HEAD 不通：停下来，按 design D2 的替代方案（`If-None-Match` + 显式判 `responseCode == 304`）改写 D2 与 D5，再继续

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
- [ ] 4.5 `FetchSpaceSceneAsync` 的联网分支改为：查 `ETag` → `ShouldDownloadSpacePackage` 判定 → 需要才 `DownloadAndExtractAsync(..., ZipTopLevel.Strip)`
- [ ] 4.6 下载解压成功后才 `SpacePackageEtagCache.Set`（design D6）。解压抛 `InvalidDataException` 时异常向上冒泡，写记录自然跳过——确认没有 `try/catch` 把它吞掉
- [ ] 4.7 删掉 `IteContentPipeline.cs` L74-75 的 TODO 注释
- [ ] 4.8 加一条命中缓存时的日志（design Open Question 的倾向结论）：跳过下载时输出场景名与命中的 `ETag`，使"这次为什么没重下"可从日志读出

## 5. 真机/Editor 验证（缓存状态是这组的全部难点，按顺序跑）

> `persistentDataPath` 在 macOS 编辑器下为 `~/Library/Application Support/响堂山/响堂山`。

- [ ] 5.1 清空 `IteSpaceScene_thirdDemo/` 与 `SpacePackageEtagCache` 的 PlayerPrefs 键，构造"首跑"状态
- [ ] 5.2 **验证点（联网首跑）**：`networkAvailable = true` 跑一次，场景包被下载解压，布局为 `IteSpaceScene_thirdDemo/thirdDemo.json`（不多一层），`ETag` 记录被写入
- [ ] 5.3 **验证点（联网复跑命中缓存）**：不清缓存再跑一次，控制台出现 4.8 的命中日志，**无**场景包下载请求，加载链正常完成
- [ ] 5.4 **验证点（缓存失效重下）**：手工把 PlayerPrefs 里的 `ETag` 记录改成一个假值再跑，场景包被重新下载，记录被更新回真实值
- [ ] 5.5 **验证点（校验器查不到 + 有缓存）**：把配置里的 `spaceSceneBaseUrl` 临时指向一个不可达地址再跑，MUST NOT 重新下载，直接读缓存并正常完成
- [ ] 5.6 **验证点（离线路径未受影响）**：`networkAvailable = false` 跑一次，加载链完成，且**无任何 HTTP 请求**——包括本次新增的 HEAD。这条最容易漏：新加的校验器查询若没被 `networkAvailable` 分支包住，离线路径就破了
- [ ] 5.7 跑整个 `Uality.IteTour.Tests` 确认无回归

## 6. 收尾

- [x] 6.1 `openspec validate scene-package-version-check --strict` 通过
- [ ] 6.2 主 spec `openspec/specs/ite-content-acquisition/spec.md` 按 delta 同步
- [ ] 6.3 归档本 change
