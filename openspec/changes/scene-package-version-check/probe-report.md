# Probe Report: scene-package-version-check

> Generated: 2026-09-09
> Summary: 6 questions asked · 6 decisions confirmed · 5 open assumptions
> 注意：本次 probe 在四件规划产物**已写完并提交**（`27848ec`）之后进行，因此结论不是"待生成"，而是"已有产物需要按此修订"。修订清单见文末。

## Confirmed decisions

### Scope & intent

- **Question**: tour 包存在与场景包同构的缓存失效缺陷（版本记录在、目录被删 → 永久卡死），本次要不要一并补？
- **AI recommendation**: 补。同一缺陷、同一改法；只修场景包是按 change 标题划线而不是按问题划线。且 tour 侧更易踩——归档记录里五个 tour 目录为 38/168/42/66/47 MB，人腾磁盘删的正是这些，不会去删几百 KB 的场景包（证据：`openspec/changes/archive/2026-09-09-ite-tour-space-integration/tasks.md` 任务 2.9）
- **User confirmation**: 补
- **影响**：`proposal.md` 里"非目标：不改 tour 包的版本比对路径"作废，scope 扩大到两类包

### Impact

- **Question**: 删旧目录这个动作放哪一层？
- **AI recommendation**: 放 `IteContentPipeline`，由调用方显式指定要删的目录；`ZipContentDownloader` 不碰删除。原因是两类包的 `outputFolder` 语义不同——场景包的是自己的目录，tour 包的是 `persistentDataPath` **根**（证据：`IteContentPipeline.cs:201-202`，`DownloadAndExtractAsync(url, relativeFolder: "", ZipTopLevel.Preserve)`）。把"解压前清空 outputFolder"塞进下载器，tour 那次调用会递归删掉整个缓存目录，且外观像缓存清理而非 bug
- **User confirmation**: 同意分层；tour 包重下时也删自己的 `{tourId}/`
- **删除目标**：场景包删 `IteSpaceScene_{sceneName}/`，tour 包删 `{tourId}/`（**不是** outputFolder）

### Design

- **Question**: 内容变更重新下载时，旧文件要不要先清掉？
- **AI recommendation**: 要。`ZipContentDownloader.ExtractSync`（`ZipContentDownloader.cs:82-136`）全程只逐条目覆盖写、从不清空 `outputFolder`。今天不明显是因为每次冷启动都全量重下；加了 ETag 比对之后，重下只发生在内容真的变了的时刻，而那正是新旧文件会不一致的时刻，旧包里被删掉的资源会永久留在磁盘上
- **User confirmation**: 删除。顺序为 **下载成功 → 删旧目录 → 解压 → 写记录**
- **顺序理由**：网络是最易失败的一段，先下完再删，失败时旧缓存原封不动；解压失败（`InvalidDataException`）时按 D6 记录也不写，下次冷启动重下，能自愈

---

- **Question**: 有人手动删了内容目录但版本/ETag 记录还在，怎么办？
- **AI recommendation**: 跳过下载的条件增加 `File.Exists`。当前 design D5 写死"以校验器记录是否存在为准，不去 stat 文件"，留下的洞是：记录命中 → 跳过下载 → 读不到 json → 抛异常 → **下次启动重复同样路径，永久卡死**，除非有人想到去清 PlayerPrefs。这不是假想场景：归档 `tasks.md` 任务 2.1 的"清掉本机现有的 ITE 缓存"做法正是删目录（证据：`openspec/changes/archive/2026-09-09-ite-tour-space-integration/tasks.md`）
- **User confirmation**: 加
- **与 D5 的关系**：不是推翻而是收窄。ETag 记录仍只在解压成功后写、仍是"下全了"的凭据；`File.Exists` 补的是"现在还在不在"。两个条件各管一件事。`ShouldDownloadSpacePackage` 保持纯函数，判断放在 pipeline 调用点

---

- **Question**: 递归删除的目录名来自服务器，要不要过路径校验？
- **AI recommendation（初版被驳回）**: 按路径穿越（`../..`）防护。**用户驳回，理由正确**：tourId 由后端生成，永远是 shortid，路径穿越不是真实威胁模型。此条撤回
- **AI recommendation（修订版）**: 仍需最小校验，但担心的不是攻击者而是**空值**——`tourId = ""` 时 `Path.Combine(persistentDataPath, "")` 返回 `persistentDataPath` 本身，递归删除会清空整个缓存。shortid 保证"不是恶意路径"，保证不了"字段一定有值"。而这个字段在实践中确实可能为空：同一文件里 `LoadSceneSpritesAsync` 已经在防它（证据：`IteContentPipeline.cs:159`，`if (tour == null || string.IsNullOrEmpty(tour.tourID)) continue;`）。之前空了只少加载一张预览图，现在空了会删掉 151 MB
- **User confirmation**: 可以。复用 `ZipEntryPath.TryResolve`
- **落地**：任何递归删除前先过 `ZipEntryPath.TryResolve`（`Runtime/Internal/ZipEntryPath.cs`，8 条测试已绿），false 则拒绝删除并记错误日志。零新代码。已核边界：`""`→false、`"."`→false（解析成根本身，前缀检查拦下）、`"../.."`→false、`"a/b"`→true

### Success criteria

- **Question**: 这个改动做错了，怎么发现？
- **AI recommendation**: 它的失败形态是**静默退化**——ETag 比对若因任何原因永不相等（存取引号不一致、`W/` 弱校验器前缀、键名拼错），结果是每次都重下，也就是今天的行为：功能全对、无报错、只是白干。原 tasks 4.8 只要求命中时打日志，于是"一次都没命中"与"日志还没接上"在观感上完全相同
- **User confirmation**: 加
- **落地**：命中与未命中**都**记日志，未命中要打出两侧的值，使"没有记录"与"值不匹配"可区分。tour 侧同理

```
[IteTour] 场景包 thirdDemo 命中缓存，跳过下载 (ETag "B6A4...-1")
[IteTour] 场景包 thirdDemo 需要更新：本地 "" / 服务端 "B6A4...-1"
```

### 会话中自行查证、未占用提问的项

- **并发重入**：`IteRuntime.StartAsync` 有 `_started` 一次性守卫，重复调用记警告后返回（`IteRuntime.cs:117-121`），不存在两次 `FetchSpaceSceneAsync` 并发删同一目录的风险。无需为删除加锁
- **zip 落盘名与删除目标不冲突**：场景包 zip 为 `{sceneName}.zip`、tour 为 `{tourId}_wx.zip`，均在 `persistentDataPath` 根，与被删的 `IteSpaceScene_{sceneName}/`、`{tourId}/` 不同名

## Open assumptions [NEEDS CLARIFICATION]

- [ ] `[ASSUMED]` `UnityWebRequest.Head` 对该直链可用，且返回的 `ETag` 与 GET 一致。`curl -I` 已确认服务端支持（返回 200 与完整响应头），但走的是 curl 的网络栈；Unity 网络栈未验证 — 影响：design D2 整条，tasks 第 1 组即为此设的 kill switch
- [ ] `[ASSUMED]` tasks 1.2 目前只断言 `ETag` **非空**，未断言它与 GET 返回值一致。若 CDN 对 HEAD 与 GET 返回不同的校验器，比对会永远不命中，退化成今天的行为（由 Q6 的未命中日志兜底可见，但不会自动失败）— 影响：tasks 1.2 的验证强度
- [ ] `[ASSUMED]` `ETag` 值的字面形态（含双引号、可能的 `W/` 前缀）按原样存取即可，不做规范化。前提是存和取都来自同一来源，两侧形态一致 — 影响：tasks 4.4 的实现细节
- [ ] `[ASSUMED]` 校验器记录存 PlayerPrefs（沿用 `TourVersionCache`，design D4）。应用卸载/清除数据时 PlayerPrefs 与 `persistentDataPath` 是否同步失效，未在 Quest / PICO 上验证。若二者不同步，会重演 Q2 修掉的那类不一致 — 影响：D4，以及 Q2 的 `File.Exists` 兜底是否足够
- [ ] `[ASSUMED]` 磁盘缓存整体无清理策略：多场景切换会让 `IteSpaceScene_*/` 与 tour 目录持续累积，从场景描述里被移除的 tour 目录永远不会被回收。本次的删除只覆盖"重下同一个包"，不覆盖"这个包不再被引用"— 影响：超出本 change 范围，需另开

## 已有产物待修订清单

本次 probe 在产物提交后进行，以下需回改。**已于同日全部完成**（`openspec validate --strict` 通过）：

- [x] `proposal.md` — 删除"非目标：不改 tour 包的版本比对路径"；What Changes 按四个小标题重组，增补重下前清空目标目录、`File.Exists` 兜底、删除前路径校验、双向日志；Impact 增补 tour 侧改动与「重下会清空该包目录」这一行为变更
- [x] `design.md` — D5 收窄为两个并列条件（附收窄理由与不收窄时的卡死链路）；Goals/Non-Goals 重写；新增 D8（tour 一并修、scope 扩大）、D9（删除归 pipeline、显式目标、时机与 `outputFolder` 陷阱）、D10（复用 `ZipEntryPath.TryResolve` 防空值，附边界表）、D11（双向日志与静默退化）
- [x] `specs/ite-content-acquisition/spec.md` — R2 增补「跳过下载需两个条件同时成立」与日志要求，新增 3 条 Scenario（场景包/tour 目录被删须重下、日志可区分两种原因）；新增 2 条 Requirement：重下后不得残留上一版内容（3 Scenario）、递归删除前必须校验目录名（2 Scenario）
- [x] `tasks.md` — 4.5 增补 `File.Exists` 条件、4.9 改双向日志；新增第 5 组（删除与校验，含 D9 陷阱的醒目提示）、第 6 组（tour 侧同类缺陷）；验证组扩到 11 条，新增目录被删自愈（场景包/tour 各一）、重下无孤儿文件、删除不越界；原第 5/6 组顺延为 7/8

## Suggested next step

- [x] 按上表修订四件产物（本 change 已有完整产物，不需要重新 `/opsx:propose`）
- [x] 修订后重跑 `openspec validate scene-package-version-check --strict` — 通过
- [ ] 实施前重跑 `/opsx:analyze`，确认 4 条 CRITICAL 已消
- [ ] 实施从 tasks 第 1 组开始——它是 HEAD 可用性的 kill switch，不通则 D2 整条要换
