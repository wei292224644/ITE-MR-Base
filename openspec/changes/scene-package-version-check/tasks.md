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

- [x] 2.1 `ZipContentDownloaderTests` 新增用例：zip 含显式目录条目（`thirdDemo/`、`thirdDemo/assets/`）+ 文件条目 + `__MACOSX/` 影子条目，按 `ZipTopLevel.Strip` 解压
  - `ExtractSync_Strip_HandlesExplicitDirectoryEntries`
- [x] 2.2 断言落盘为 `{out}/thirdDemo.json`、`{out}/assets/logo.png`，且 `{out}/thirdDemo/` 与 `{out}/__MACOSX/` 均不存在
- [x] 2.3 **验证点**：新用例通过，且不改动 `ZipContentDownloader` 任何一行
  - `ZipContentDownloaderTests` 6/6 通过，被测代码零改动。`TryStrip` 的空串分支确认安全
- [x] 2.4 跑整个 `Uality.IteTour.Tests` 确认无回归（当前基线 195 passed / 0 failed）
  - **196 passed / 0 failed**（195 基线 + 本次新增 1 条）
  - 操作要点：改完 C# 必须先 `unity command recompile` 并轮询 `recompile_status` 到 `completed`，再 `run_tests`；否则跑的是旧程序集（本组踩过一次，新用例没进去却显示全绿）

## 3. 场景包校验器缓存（design D4）

- [x] 3.1 新增 `SpacePackageEtagCache`（`Runtime/Internal/`），键形如 `ite.space.{sceneName}.etag`，接口对齐 `TourVersionCache`：`KeyFor` / `Get` / `Set` / `Clear`
- [x] 3.2 `Get` 在无记录时返回空串（PlayerPrefs 缺省行为），与 `TourVersionCache` 一致
- [x] 3.3 补 EditMode 测试：键名不占用宿主全局命名空间（design D10）、无记录时返回空串、`Set` 后可读回
  - `SpacePackageEtagCacheTests` 5/5 通过。两轮 TDD：先 `KeyFor`（RED = 类不存在 → GREEN），再 `Get`/`Set`/`Clear`（RED = 成员不存在 → GREEN）
  - 额外钉住一条：与 `TourVersionCache.KeyFor` 不得相等——同名的 scene 与 tour 若共用键会互相覆盖
  - 往返用例特意存**带双引号**的真实 ETag 字面量，确认不做规范化（见 1.2 的结论）

## 4. 比对决策与接入（design D1/D3/D5/D6）

- [x] 4.1 新增纯函数 `IteContentPipeline.ShouldDownloadSpacePackage(cachedEtag, serverEtag)`，与既有 `ShouldDownloadTourPackage` 并列
- [x] 4.2 实现 D5 的三分支语义：`serverEtag` 为空且 `cachedEtag` 非空 → false（退回缓存）；`serverEtag` 为空且 `cachedEtag` 为空 → true（必须下载）；两者非空且不等 → true；相等 → false
  - 与 tour 版只差一行：`serverEtag` 为空时 tour 直接 `return false`，场景包 `return string.IsNullOrEmpty(cachedEtag)`
- [x] 4.3 补 EditMode 测试覆盖 4.2 的全部四种组合
  - `IteContentPipelineTests` 23/23 通过，其中 `ShouldDownloadSpacePackage_*` 共 10 个参数化用例
  - 「两侧都没值 → 下载」这条用 `(null,null) ("","") (null,"") ("",null)` 四种写法钉死，避免将来有人把 `null` 与 `""` 当两回事
- [x] 4.4 新增只取响应头的请求入口（`ContentAssetLoader` 或 `Runtime/Internal/` 下），返回 `ETag`；请求失败或响应头缺失时返回 null，不抛异常
  - `ContentAssetLoader.FetchEtagAsync`。不写常驻单测（网络 I/O，会 flaky），改用 `unity command eval` 活体验证两条路径：
  - 真实 URL → `"B6A43FCA26644FF44FA21BBADCC8D5B3-1"`；404 URL → `NULL` 且不抛异常
- [x] 4.5 跳过下载的条件除 4.2 的判定外，**同时**要求 `File.Exists(IteSpaceScene_{scene}/{scene}.json)`（design D5 收窄）
  - 两参纯函数保持不碰文件系统；新增三参重载 `ShouldDownloadSpacePackage(cachedEtag, serverEtag, contentPresent)` 承载复合判定，提成 public static 以便测（沿用 `ShouldDownloadTourPackage` 的既有做法）
- [x] 4.6 `FetchSpaceSceneAsync` 的联网分支改为：查 `ETag` → 4.2 判定 + 4.5 文件检查 → 需要才下载
  - 抽出 `UpdateSpacePackageIfStaleAsync`，与既有 `UpdateTourPackageIfStaleAsync` 同构
- [x] 4.7 下载解压成功后才 `SpacePackageEtagCache.Set`（design D6）
  - 另加一条：`serverEtag` 为空时也不写，否则会把空值当成一个"版本"记下来，下次比对永远不匹配
- [x] 4.8 删掉 `IteContentPipeline.cs` L74-75 的 TODO 注释
- [x] 4.9 日志按 design D11 双向打
  - `DescribeValidator` 把空值渲染成 `<无>`，使"本地无记录"与"两值不匹配"在日志里一眼可分
  - 内容文件不存在时日志追加"；本地内容文件不存在"，区分第三种触发原因

## 5. 删除旧内容与删除前校验（design D9/D10 — 本组含唯一的破坏性操作，逐条对照 design 再写）

> ⚠️ D9 的坑：tour 包的 `DownloadAndExtractAsync` 传的 `relativeFolder` 是空串，其 `outputFolder` 是 `persistentDataPath` **根**。删除目标必须由 pipeline 显式给出，**绝不能**用 `outputFolder`。

- [x] 5.1 删除逻辑写在 `IteContentPipeline`，`ZipContentDownloader` 不加任何删除代码
  - `DownloadAndExtractAsync` 只多了一个 `Action onDownloaded` 钩子（下载成功后、解压前调用）。下载器仍不认识"删除"，也不知道哪个目录归哪个包管——只是让出时机
- [x] 5.2 删除目标显式指定：场景包 `IteSpaceScene_{sceneName}/`，tour 包 `{tourId}/`
- [x] 5.3 删除时机：下载成功之后、解压之前。下载失败时不得触发删除
  - 钩子在 `www.result != Success` 的早返回之后，下载失败走不到
- [x] 5.4 删除前用 `ZipEntryPath.TryResolve` 校验（design D10），false 则拒绝删除并记错误日志
  - `ClearCachedPackageDirectory(cacheRoot, relativeFolder)`，`cacheRoot` 提成参数以便用临时目录测试，生产调用点传 `Application.persistentDataPath`
- [x] 5.5 补 EditMode 测试：`TryResolve` 对 `""`、`"."`、`"../.."` 均返回 false
  - **发现并修复一个真缺陷**：`""` 与 `"../.."` 原本已被拒，但 **`"./"` 被放行**。原因是 `Path.GetFullPath(root + "./")` 得到**带尾分隔符**的根目录，与 `root` 逐字符相等，`StartsWith` 成立
  - 后果：`ClearCachedPackageDirectory("./")` 会解析到 `persistentDataPath` 并递归删除，整个内容缓存没了。解压路径上这只是个无用条目，删除路径上却是致命的
  - 修在 `ZipEntryPath.TryResolve`（所有调用方共用的根因处）：比较前 `TrimEnd` 尾分隔符；返回的 `fullPath` 不变，解压行为零影响
  - 新增 `TryResolve_RejectsPathsResolvingToTheRootItself`，覆盖 `.` / `./` / `a/..`
- [x] 5.6 补 EditMode 测试：目录名为空时删除路径不被执行
  - `ClearCachedPackageDirectoryTests`，全程在临时目录里跑，绝不碰真实 `persistentDataPath`
  - 覆盖：清掉指名目录、不波及兄弟包、`"" / null / . / ./ ..` 一律拒绝且根目录与已有内容完好、目录不存在时不抛

## 6. tour 侧同类缺陷（design D8）

- [x] 6.1 `UpdateTourPackageIfStaleAsync` 的跳过条件增加 `File.Exists({tourId}/{tourId}.json)`，与 4.5 同构
  - 同样以三参重载 `ShouldDownloadTourPackage(cachedVersion, serverVersion, contentPresent)` 承载
- [x] 6.2 tour 包重下时按第 5 组的规则删除 `{tourId}/`，含 5.4 的校验
  - 删的是 `tourId`，**不是** `relativeFolder`（那是空串，指缓存根）。调用点写了注释钉住这一点
- [x] 6.3 tour 侧日志按 D11 双向打
- [x] 6.4 补 EditMode 测试：版本一致但内容文件不存在时判定为「需要下载」
  - 场景包与 tour 各一条；另补两条反向用例（一致且文件在 → 跳过）与一条"文件在但校验器过期仍要下"，防止把 `contentPresent` 写成抑制更新的条件

## 7. 真机/Editor 验证（缓存状态是这组的全部难点，按顺序跑）

> `persistentDataPath` 在 macOS 编辑器下为 `~/Library/Application Support/响堂山/响堂山`。

- [x] 7.1 清空 `IteSpaceScene_thirdDemo/` 与 `SpacePackageEtagCache` 的 PlayerPrefs 键，构造"首跑"状态
- [x] 7.2 **验证点（联网首跑）**：场景包被下载解压，布局为 `IteSpaceScene_thirdDemo/thirdDemo.json`（不多一层），`ETag` 记录被写入
- [x] 7.3 **验证点（联网复跑命中缓存）**：控制台出现命中日志，**无**场景包下载，加载链正常完成
- [x] 7.4 **验证点（缓存失效重下）**：`ETag` 记录改成 `"bogus-etag"` 后重下，记录被更新回真实值
- [x] 7.5 **验证点（目录被删能自愈 — 场景包）**：只删目录、保留记录，重新下载并正常完成
- [x] 7.6 **验证点（目录被删能自愈 — tour）**：删 `wm0l5qcn_ibd/`、保留版本记录，该 tour 被重下
- [x] 7.7 **验证点（重下无孤儿文件）**：`assets/orphan.png` 在重下后消失
- [x] 7.8 **验证点（删除不越界）**：tour 重下期间其余 tour 目录与 `IteSpaceScene_*/` 全部完好
- [x] 7.9 **验证点（校验器查不到 + 有缓存）**：`spaceSceneBaseUrl` 临时指向不可达地址，未重下，正常完成
- [x] 7.10 **验证点（离线路径未受影响）**：`networkAvailable = false` 读到完整缓存，未触发任何下载
- [x] 7.11 跑整个 `Uality.IteTour.Tests` 确认无回归
  - **227 passed / 0 failed**（改动前基线 195）

> **本组的跑法**：没有建 Play 验收场景，而是写临时 `[UnityTest]` 直接驱动 `IteContentPipeline`
> （真网络、真 `persistentDataPath`，按顺序跑完整条缓存状态机，14.4 秒通过，验完即删）。
> 理由：本次改动全在获取管线这一半，tour GameObject 装配那半段未改动，不必为它拉起 XR rig；
> 且只重下最小的 `wm0l5qcn_ibd`（38 MB）而非全部 151 MB。
>
> **未覆盖**：tour GameObject 的实例化与事件广播（未改动，由既有验收覆盖）。
>
> D11 双向日志的实测输出，最能说明问题的是这条——版本一致却仍然重下，原因写在日志里：
> ```
> [IteTour] 场景包 thirdDemo 需要更新：本地 <无> / 服务端 "B6A4...-1"
> [IteTour] 场景包 thirdDemo 命中缓存，跳过下载 (ETag "B6A4...-1")
> [IteTour] tour wm0l5qcn_ibd 需要更新：本地 7 / 服务端 7；本地内容文件不存在
> ```
> 最后一条正是 D8 修掉的缺陷：改动前这里会跳过下载，随后读取失败并永久卡死。

## 8. 收尾

- [x] 8.1 `openspec validate scene-package-version-check --strict` 通过
- [ ] 8.2 主 spec `openspec/specs/ite-content-acquisition/spec.md` 按 delta 同步
- [ ] 8.3 归档本 change
