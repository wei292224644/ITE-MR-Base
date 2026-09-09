## 1. 先从 MRCore 摘除 ITE

> 必须排在第一组：第 2 组会把 `sceneName` 从空串改成 `thirdDemo`，而 `MRCore` 是永不卸载的核心场景。两件事顺序反了，之后**每一个**内容场景一进 Play 都会开始串行下载 290 MB。

- [x] 1.1 `MRCore.unity` 删除 `ITE Host`：GameObject 1597420651、Transform 1597420652、MonoBehaviour 1597420653（经连上的 live Editor 用 `delete_gameobject` 完成，删除会自动更新父节点子物体列表）
- [x] 1.2 从父节点 Transform 614014891 的 `m_Children` 移除 `{fileID: 1597420652}`（同上，Unity 场景 API 自动处理）
- [x] 1.3 在编辑器中打开 `MRCore.unity`，确认层级正常、无 missing 引用、Play 时 Console 无 `[ITE]` / `[IteTour]` 输出、无对 `ite-spatial-config.uality.cn` 的请求（已用 live Editor 进 Play 验证：删除前有 `[IteTour] 内容包下载失败` 与 404，删除后 Play 全程无 `[ITE]`/`[IteTour]` 输出）
- [x] 1.4 单独成一次提交，便于 revert

## 2. 内容获取修复（做完并单独确认，再碰宿主 — design D8）

- [x] 2.1 清掉本机现有的 ITE 缓存。上一个 change 在 `persistentDataPath` 下留了一份**被裁剪过**的 `IteSpaceScene_thirdDemo/thirdDemo.json`（tours 只剩 1 个）与一个 tour 目录，不清掉会让联网首跑的验证失去意义
  - macOS 编辑器：`~/Library/Application Support/响堂山/响堂山`（`companyName` / `productName` 均为 `响堂山`）
  - 已用 live Editor 确认 `Application.persistentDataPath` 即该路径；其下目前只有 `TestResults.xml` 与 `Unity/`，**无** `IteSpaceScene_*` 或 tour 目录，无需删除
- [x] 2.2 在 `ZipContentDownloader` 里加 `ZipTopLevel` 枚举（`Preserve` / `Strip`），`DownloadAndExtractAsync` 与 `ExtractAsync` 增加该参数（design D1）
- [x] 2.3 实现 `Strip`：解压时剥掉条目的公共顶层目录；条目并非全部位于同一顶层目录之下时报错并中止，MUST NOT 静默按原样落盘
- [x] 2.4 忽略 `__MACOSX/` 条目（macOS Finder 压缩产物，`thirdDemo.zip` 里就有），避免它干扰"公共顶层目录"的判定
- [x] 2.5 `IteContentPipeline` 两处调用各自传值：空间场景包传 `Strip`，tour 包传 `Preserve`
- [x] 2.6 `FetchSpaceSceneAsync` 读不到描述时，异常信息带上 `sceneName` 与被查找的完整路径
- [x] 2.7 补 EditMode 测试：`Strip` / `Preserve` 两种语义的落盘结果、以及"声明 Strip 但 zip 无公共顶层目录"时报错
  - live Editor：`ZipTopLevelResolver` 9 passed、`ZipContentDownloader` 5 passed、`FetchSpaceSceneAsync_OfflineWithoutCache_ThrowsWithSceneNameAndPath` passed
- [x] 2.8 `Assets/Settings/ITE/IteRuntimeConfig.asset` 的 `sceneName` 由空串改为 `thirdDemo`（`tourObjectPrefab` 已核实连对：guid `5a15cfba1014e4572bf41e551294e97a` 即包内 `Runtime/Prefabs/Tour.prefab`，无需改动）
- [x] 2.9 **验证点**：联网首跑一次（临时建个空场景挂 `IteHostBootstrap` 即可，不必等 harness；第 1 组做完后 `MRCore` 里已经没有可蹭的装配点了），用 `ls` 直接确认落盘布局
  - 临时场景 `Assets/Scenes/IteAcquireVerify.unity`（不进 build）。Play 完成后 `IsLoading=false`
  - `IteSpaceScene_thirdDemo/thirdDemo.json` 存在且**不**多一层 `thirdDemo/`
  - `IteSpaceScene_thirdDemo/assets/` 下有 logo 与 5 张 tour 预览图
  - 5 个 tour 目录直接落在 persistentDataPath 根：`wm0l5qcn_ibd/` `4kvhqwvp_12f/` `earyserh_i5x/` `azdugaax_xry/` `hkdaowxy_0hu/`，各自含 `{tourId}.json` 与 `asset/`（体积约 38/168/42/66/47 MB）
- [x] 2.10 **验证点（离线路径）**：把 `IteHostBootstrap` 的 `networkAvailable` 置为 **false** 后复跑，`OnInitialized` 仍触发，且无任何 HTTP 请求
  - Play 约 8 秒结束、`IsLoading=false`、5 个 `IteTourObject` 全部装配（若走了联网下载 151 MB 不可能这么快）；控制台无 `[IteTour] 内容包下载失败`
  - 这里**不是**"拔网线"。`networkAvailable` 默认为 `true`，而 `FetchSpaceSceneAsync` 在联网分支里无条件重下空间场景包（`IteContentPipeline.cs:76-81`）——留着 `true` 去物理断网，第一步就抛异常，那次失败与"本地缓存能否独立跑通"无关，却长得像离线路径不通
- [x] 2.11 **验证点（联网复跑）**：`networkAvailable` 保持 **true** 再跑一次，确认 5 个 tour 包**全部命中版本缓存、无一重新下载**
  - PlayerPrefs 已存 `ite.tour.{id}.version` 为字符串 `7/24/17/8/9`（JSON 数字经 Newtonsoft 转 string，比对成立）
  - 复跑后 5 个 tour 的 `{id}.json` mtime 不变；空间场景包 mtime 更新（每次重下，符合既有行为）
  - 这一步同时验掉版本形态问题：API 返回 JSON 数字 `"version": 7`，而 `LatestVersionJsonResult.Data.version` 声明为 `string`。若 `TourVersionCache` 存的形态与比对逻辑对不上，表现就是这一步又下了 290 MB
  - 空间场景包本来就每次重下（358 KB，包内已记 TODO，本次不改），不计入本条

## 3. 包的宿主接口补齐

- [x] 3.1 `IteRuntime.LoadAsync` 在 `RequireScan()` 之前调一次 `_assembler.SetAllVolumesActive(true)`——**默认开启**，宿主不需要为了让区域触发工作而调任何东西（design D2）
- [x] 3.2 `IteRuntime` 暴露 `SetTriggerVolumesActive(bool)`，转发给 assembler，供宿主延后开启或临时关闭（例如加载页仍覆盖视野时）
- [x] 3.3 `IteBootstrap.Validate()` 增加两条层级校验（design D4）：`TourRoot.parent == AnchorRoot`；`AnchorRoot.parent` 为 null 或其 `localToWorldMatrix` 为单位矩阵。错误信息要说清"为什么"，不只是"不满足"
- [x] 3.4 补 EditMode 测试覆盖 3.3 的两条校验
  - `Create_TourRootNotDirectChildOfAnchorRoot_RefusesAndExplainsWhy`
  - `Create_AnchorRootParentHasNonIdentityTransform_RefusesAndExplainsWhy`
  - 正向：合法层级 / 父级为单位矩阵均可 `Create`

## 4. 宿主接入层

- [x] 4.1 新增 `Assets/Scripts/IteHost/IteMarkerBridge.cs`（纯 C#，非 MonoBehaviour，形状对齐 `HeadsetPresenceAdapter` — design D3）
  - 构造注入：`MarkerTrackingSession`、外壳正则、`Action<string, Pose>` 转发目标
  - 订阅 `MarkerObserved`：正则剥壳成功 → 调转发目标；失败 → 打日志并带上原始 payload，不转发
  - 订阅 `MarkerLost`：仅记录供 HUD 显示，不对 ITE 做任何动作
  - 提供 `Dispose()` 退订
- [x] 4.2 补 EditMode 测试覆盖剥壳：合规 payload、不合规 payload（如 `"250"`）、空串、星号数量不对、内层为空
- [x] 4.3 `IteHostBootstrap` 恢复标记源接线
  - 删掉 `markerTracking` 那段注释掉的旧字段与 `MarkerSourceAdapter` 残留
  - 新增可序列化字段：外壳正则（默认 `^\*{6}(.*?)\*{6}$`）、`OnTourSceneLoaded` 超时秒数
  - 暴露 `AttachMarkerSession(MarkerTrackingSession session)`：会话**只能由外部注入**，装配点自己不构造会话、不持有任何 `IMarkerObservationSource` 具体实现的字段（design D3 会话归属）。注入后建 `IteMarkerBridge` 接到 `IteRuntime.SubmitMarkerScan`
  - `AttachMarkerSession` 在 runtime 就绪前调用则暂存、就绪后补接；调用时机不构成约束
- [x] 4.4 未注入会话时：加载链照常执行到 `OnInitialized`，触发体积照常开启，只打一条"未接入标记源，扫码激活不可用"的说明性日志，不报错中断
  - EditMode：`TryCreateRuntime_WithoutSession_LogsAndStillCreates`（加载链本身仍由 Play/`StartAsync` 覆盖）
- [x] 4.5 `IteHostBootstrap` 转发 `IteRuntime` 的全部对外事件，统一 `[ITE Host]` 前缀日志：`OnLoadProgress` / `OnSpaceSceneLoaded` / `OnSpaceSceneAssetsLoaded` / `OnInitialized` / `OnTourActivated` / `OnTourDeactivated` / `OnTourSceneLoaded` / `OnScanPromptChanged`
- [x] 4.6 `OnTourActivated` 之后启动超时计时，超时仍未收到对应 tourId 的 `OnTourSceneLoaded` 则报错（`Enable()` 是 fire-and-forget，异常不冒泡）
- [x] 4.7 `OnDestroy` 里退订全部事件并 `Dispose` 桥接
- [x] 4.8 **由 review 保证**（无自动化验收）：`IteHostBootstrap` 与 `IteMarkerBridge` 的字段与构造逻辑中不出现任何 `IMarkerObservationSource` 具体实现类型。host spec 那条解耦要求是结构性属性，唯一的自动化手段是反射断言字段类型，维护成本高于它能挡住的错误——这里明确记为人工检查项，免得它看起来有验收其实没有
  - 已人工核对：`IteHostBootstrap` 只持有 `MarkerTrackingSession _pendingSession` 与 `IteMarkerBridge`；`IteMarkerBridge` 构造只收 `MarkerTrackingSession`

## 5. 编辑器驱动层

- [x] 5.1 走位控制器：WASD 平移 + 鼠标视角，不做蹲起/跳跃/重力/碰撞
- [ ] 5.2 假扫码驱动：**由驱动层构造** `MockObservationSource` 与 `MarkerTrackingSession`，每帧 `Tick(Time.deltaTime)`，并在 `Start()` 里调 `IteHostBootstrap.AttachMarkerSession` 把会话注进去（依赖方向：输入层 → 装配层，反向不成立）
  - 序列化一个 tourId 列表（默认填 thirdDemo 的 5 个），按数字键各触发一个
  - 触发时构造相机前方 1.5 米、法线朝向相机的世界位姿（design D6），组装成 `RawPayload = "******{tourId}******"` 的观测塞进 mock 源
  - 观测持续投喂若干帧后停止，使 `MarkerLost` 的滞回可被触发
- [ ] 5.3 调试 HUD（屏幕空间，仿 `Assets/Scripts/Localization/Native/MarkerHookTestHud.cs`）：加载进度、空间场景名、已装配 tourId 列表、当前激活 tourId、`ScanPrompt` 状态与 TourIds、相机所在触发体积集合、最近一次观测与丢失
- [ ] 5.4 确认驱动层与 HUD 整块停用后，加载链仍完整执行到 `OnInitialized`（此时即 4.4 的未注入路径）

## 6. 场景

- [ ] 6.1 新建 `Assets/Scenes/IteTourSpace.unity`，层级按 design D5
  - `AnchorRoot`（**根级**）→ 直接子物体 `TourRoot`
  - `ITE Host`：`IteHostBootstrap`
  - `Editor Rig` → `Camera`：`CapsuleCollider(isTrigger)` + `Rigidbody(isKinematic)` + 走位控制器 + 假扫码驱动
  - `Debug HUD`
  - Directional Light
- [ ] 6.2 逐项连 `IteHostBootstrap` 的**全部** 7 个字段。场景连线没有编译器兜底，清单是唯一防线
  - [ ] `config` → `Assets/Settings/ITE/IteRuntimeConfig.asset`
  - [ ] `anchorRoot` → 根级的 `AnchorRoot`
  - [ ] `tourRoot` → `AnchorRoot` 的直接子物体 `TourRoot`
  - [ ] `xrCamera` → `Editor Rig` 下那台相机（包以父子链比对识别谁进出体积，不靠 tag）
  - [ ] `networkAvailable` → 首跑置 `true`；**这一项漏改不会有任何报错**，它是 bool、永远"有值"，默认值直接把你送上联网路径（W4 的另一面）
  - [ ] 外壳正则 → 保持默认 `^\*{6}(.*?)\*{6}$`
  - [ ] `OnTourSceneLoaded` 超时秒数 → 取一个比 151 MB 包建树时间宽裕的值
  - 前四项漏连会被 `IteBootstrap.Validate()` 逐项报出来；后三项不会
- [ ] 6.3 假扫码驱动上引用 `IteHostBootstrap`（注入方向是驱动层引用装配点，不是反过来）
- [ ] 6.4 确认场景**未**加入 `EditorBuildSettings`，也不在 `BuildScript` 的清单里

## 7. 验收（按序逐条走通，全部在 `IteTourSpace.unity` 的 Play 模式里）

- [ ] 7.1 清空缓存后联网首跑（`networkAvailable = true`、本地无缓存）：5 个 tour 全部下载解压，进度到 1，`OnInitialized` 触发
- [ ] 7.2 `networkAvailable = false` 复跑：全程无 HTTP 请求，走本地缓存，结果与 7.1 一致（措辞刻意不用"断网"，理由见 2.10）
- [ ] 7.3 未扫码时 HUD 显示 `ScanPrompt` 为 Visible 且 TourIds 为空；走进任一触发体积**不**激活任何 tour
- [ ] 7.4 假扫 `wm0l5qcn_ibd`：实体树建出、glb 模型可见、`ScanPrompt` 转 Hidden；tour 落在扫码位姿上（相机前方 1.5 米）
- [ ] 7.5 前行进入 `earyserh_i5x` 的触发体积：帧末重选，前一个 tour 被停用销毁、新的被激活并构建
- [ ] 7.6 假扫 `4kvhqwvp_12f`：14 个实体建出，**10 个富文本面板**渲染出来，圆角框材质与播放/暂停按钮贴图正确
- [ ] 7.7 对当前激活的 `regionalTrigger` tour 再扫一次同一个码：走 `Reanchor`，内容不销毁重建，二次锚定许可消耗一次（靠日志判定）
- [ ] 7.8 停止投喂观测超过滞回时长：`MarkerLost` 派发一次且仅一次
- [ ] 7.9 调 `SetTriggerVolumesActive(false)` 后走进体积不产生任何区域事件，再开启后恢复（验证 3.2 的开关真的接通）
- [ ] 7.10 截图与日志存档到 `openspec/changes/ite-tour-space-integration/`

## 8. 收尾

- [ ] 8.1 若 7.4 / 7.6 不通过：按 spec 的分段归因（下载 / 解析 / 装配 / 建实体树 / 资源加载）定位，把结论追加进 `design.md`
- [ ] 8.2 把本次查实但未修的问题记入后续 change 清单：`DownloadHandlerBuffer` 全量入内存（151 MB 包峰值约 300 MB）、空间场景包无版本校验
- [ ] 8.3 记录验收边界：`ComponentRegistry` 的 11 个组件类型中，thirdDemo 覆盖 7 个，`VideoPlane` / `PrimitiveModelRender` / `ApproximateTrigger` / `PlayAudioAction` 这 4 个**本 change 未验证**，需在实际用到它们的内容出现时另行验收。本次 MUST NOT 造合成数据去验它们——合成数据只验得出"我们自己写的数据能渲染"，且 `ApproximateTrigger` 的近距离判定在包里本来就没实现
- [ ] 8.4 为真机接入单开 change，形状约束见 `design.md` D9：接真实观测源、相机归属、`IteTourSpace` 的产品版本进 build、290 MB 内容的设备落盘与首启策略
