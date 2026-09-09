# Probe Report: ite-tour-space-integration

> Generated: 2026-09-08 15:10 (UTC+8)
> Summary: 9 questions asked · 9 decisions confirmed · 11 open assumptions
> 取代 `ite-render-smoke-scene`（该 change 对意图理解有误，已废弃；其调研结论并入本报告）

## 背景：为什么废掉上一个 change

`ite-render-smoke-scene` 把范围压到"一个 tour 的实体树能不能建出来"，为此做了三件事：把缓存里的 `thirdDemo.json` 裁剪成只剩 1 个 tour、写一个显式激活器绕过 `alwaysDisplayed` 的缺失、并把联网路径整个排除在外。用户的真实意图是**把整个 tour space 按 json 的语义完整跑通**——5 个 tour、扫码激活、区域切换、内容 UI 全部渲染。两者不是范围大小之差，是命题之差，因此重开。

---

## Confirmed decisions

### Scope & intent

**Q1 — "完整场景"指什么？**

- **AI recommendation**：指 thirdDemo 的 5 个 tour 全部装配并同时可见。依据：缓存 `IteSpaceScene_thirdDemo/thirdDemo.json` 顶层键只有 `tours` / `id` / `logo` / `name`，空间场景描述里没有环境模型或背景 mesh 字段，一个 ITE 空间场景在数据上就等于它的 tour 集合。
- **User confirmation**：确认。"需要把完整的 tour space 完整的渲染出来"。

**Q2 — 让多个 tour 并存的落点：改数据还是改包？**

- **AI recommendation**：起初建议在 `IteRuntimeConfig` 上加 `forceAllToursVisible` 开关，解析后在内存里把 displayType 覆写成 `alwaysDisplayed`。依据：`TourDirector.Activate`（`TourDirector.cs:273`）第一件事就是 `DeactivateCurrent()`，宿主侧 foreach 调 `ActivateTour` 只会剩最后一个且不报错；唯一的并存旁路是 `IteTourObject.CreateTourObject:115-117` 对 `alwaysDisplayed` 直接 `await Enable()`、不经 director。
- **User confirmation**：**两个都否决**。"什么都不改就按照 json 的逻辑"——不覆写 displayType、不篡改缓存数据。5 个 tour 保持 4×`regionalTrigger` + 1×`normal`，靠真实的扫码与区域触发链路激活。同时确认可以废掉原 change 重新设计，目标是"完整跑通整个 ite tour space 的逻辑"，UI 可重新设计。

**Q3 — 验收面：编辑器还是真机？**

- **AI recommendation**：编辑器优先，真机另开 change。依据：`Assets/Scripts/Localization/MockObservationSource.cs` 是可注入任意观测序列的 `IMarkerObservationSource`，且位于**运行时** asmdef `MRBase.Localization`（非 test-only），Play 模式下可直接当假扫码源；`SceneRoles.IsCamera` 是纯父子链比对、刻意不用 tag（包 design D26），普通相机挂 Collider 即可触发体积；`TourScanPolicy` / `TourRegionPolicy` / `ScanPromptPolicy` 全是平台无关的纯决策类且已有单元测试。真机独有的只有 passthrough 真扫 QR 与 gltfast 运行时材质在 Android+XR 下的表现，与"逻辑跑没跑通"是两个问题。
- **User confirmation**：认可。并追加：**本次不接真机扫描逻辑**（不接 `QuestObservationSource` / `PicoFiducialObservationSource`）。

**Q7 — 验收面是否超出 thirdDemo 实际用到的组件？**

- **AI recommendation**：不超出。依据：对 5 个 tour 的 json 做了完整普查（range 请求抠出远端 zip 里的 json，未下整包），thirdDemo 覆盖 `ComponentRegistry` 注册的 11 个类型中的 7 个；未覆盖的 4 个是 `VideoPlane` / `PrimitiveModelRender` / `ApproximateTrigger` / `PlayAudioAction`。为它们造合成数据验出来的是"我们自己写的数据能渲染"，会绕开真实数据里那些没人记得的字段形状问题；且 `ApproximateTrigger` 的近距离判定在包里本来就没实现（`TriggerData.cs:23`、`TriggerComponents.cs:61-64`），为它造数据只能验出一个已知 TODO。
- **User confirmation**：接受。未覆盖的 4 个组件类型写进 spec 边界，标注为未验证。

### Impact

**Q4 — 内容从哪来？D5（空间场景 zip 解压层级不匹配）怎么办？**

- **AI recommendation**：修 D5，并作为独立前置 change。依据：`thirdDemo.zip` 的条目顶层是 `thirdDemo/`，解压目标是 `IteSpaceScene_thirdDemo/`，实际落盘多一层导致 `FetchSpaceSceneAsync` 读不到 json；`ZipContentDownloader.DownloadAndExtractAsync(url, relativeFolder)` 对"zip 里有没有顶层目录"完全不表态，而空间场景包要剥一层、tour 包必须保留，两类包期望相反。5 个 tour 共 290 MB（含一个 151 MB），手工摆缓存换台机器就得重来。
- **User confirmation**：**修，但不分 change，一次性做完**。"代码大部分都写完了，没有写完的可以照着原项目参考"。

**Q8 — 场景定位与命名**

- **AI recommendation**：做成未来 ITE 内容场景的雏形，分两层——宿主装配层（`IteHostBootstrap` + 标记桥接 + 触发体积开启时机 + 事件转发）与编辑器驱动层（桌面相机 + 假扫码按键 + 调试 HUD），后者可整块禁用。依据：两层在真机上的差异只有输入源，中间段完全相同；`IteHostBootstrap` 的类注释已把自己定义为"**唯一**知道 `Uality.IteTour` 与 `MRBase.*` 两边的类型"，天生是复用件。场景名建议 `IteTourSpace.unity`。
- **User confirmation**：认可（含场景名）。

同时确认的两条边界（原 change D1 沿用）：

- `MRCore.unity` 里的 `ITE Host`（GameObject 1597420651 / Transform 1597420652 / MonoBehaviour 1597420653）删除，并从父节点 Transform 614014891 的 `m_Children` 移除。理由比原来更强：它引用的 `IteRuntimeConfig.asset` 的 `sceneName` 至今是空串，每次启动任何场景都会请求 `https://ite-spatial-config.uality.cn/.zip` 然后抛异常。
- 新场景**不进** `EditorBuildSettings` / `BuildScript`，与 `MarkerHookTest.unity` 同等待遇。

### Design

**Q5 — 编辑器假扫码走哪条路？**

- **AI recommendation**：走完整契约链 `MockObservationSource` → `MarkerTrackingSession` → 宿主桥接 → `IteRuntime.SubmitMarkerScan`，而不是直接调 `SubmitMarkerScan`。依据：`TourScanPolicy.Decide` 里的 `TryFind` 是 `tours[i].TourId == markerId` 的**字面比对**，无任何解析；而源工程二维码内容是 `******{tourId}******`，靠 `AnchorObject.OnQrCodeScanned` 的正则 `^\*{6}(.*?)\*{6}$` 剥壳。这段剥壳在本仓库不存在，且按 `unified-marker-tracking-contract` 明文要求也不该存在于会话层（"业务层若需要将 RawPayload 转换或匹配到业务对象，SHALL 在会话层之外自行完成"）。走捷径会让这段真机必需的桥接永远不被写出来。附带好处：`MarkerTrackingSession` 的 1 秒滞回丢失逻辑目前只有 EditMode 单测，走这条路能在运行时被真实触发。
- **User confirmation**：确认走完整链路。

**Q6 — UI 做到什么程度？**

- **AI recommendation**：只做调试 HUD（进度 / 场景名 / tour 列表 / 当前激活 tourId / `ScanPrompt` 状态与 TourIds / 相机所在触发体积），不做产品 UI。依据：`ScanPromptPolicy` 类注释明写"决策是 ITE 业务，渲染不是——这里只留决策，渲染由宿主订阅广播自行处理（design D5）"，UI 天然是宿主侧可后接的一层；`Assets/Scripts/Localization/Native/MarkerHookTestHud.cs` 是同类先例。
- **User confirmation**：同意产品 UI（扫码提示 / 加载页 / tour 预览列表 / 开场 Timeline）留到后续 change。**但明确追加：ITE 渲染用的 UI 必须完整**——即 tour 内容自身的元素组件（富文本面板、视频面板、圆角框、播放暂停按钮），"这个在之前的项目中有完整的实现"。

关于内容元素 UI 的完成度核查结论：**代码与资源已齐备，缺的是第一次真正跑起来**。`ComponentRegistry` 注册 11 个类型，源工程 `Assets/Scripts/ITE/Components/` 下也正好 11 个，一一对应；元素预制体（`Rich Text Element.prefab` / `Video Plane Element.prefab` / `EMW Model Render Element.prefab`）连同 `RoundedBoxUI.shader`、`RoundedBoxUI.mat`、`audio_play/pause` 与 `video_play/pause` 贴图、`Audio Controller` 动画控制器全部随包在，且已在 `Tour.prefab:222-225` 的 `_elementPrefabs` 上连好线。而 `IteTourObject.CreateTourScene`（真正建 GameObject 那段）至今零测试覆盖。

**Q9 — 锚定层级约束与验收序列**（见下节）

### Success criteria

**Q9 — 验收怎么算通过？**

- **AI recommendation**：8 条按顺序在编辑器里被观察到的序列。
- **User confirmation**：全部接受，含第 7 条（二次锚定，只能靠日志判定）。

1. 联网首跑，5 个 tour 全部下载解压完成（约 290 MB），`OnInitialized` 触发，进度到 1
2. 断网复跑，走本地缓存，结果一致
3. 未扫码时 `ScanPrompt` 为 `Visible` 且 `TourIds` 为空，走进任何触发体积**不**激活任何东西（`RequireScan` 语义）
4. 假扫 `wm0l5qcn_ibd`，其实体树建出、glb 显示，`ScanPrompt` 转 `Hidden`
5. 前行进入 `earyserh_i5x` 的体积，director 帧末重选：上一个 tour 被销毁、新的建出（`regionalTrigger` 自动切换）
6. 假扫 `4kvhqwvp_12f`（`normal`），14 个实体、**10 个富文本面板**渲染出来，圆角框与播放按钮贴图正确
7. 对当前 tour 再扫同一个码，走 `Reanchor` 而非重建（内容不闪、二次锚定许可消耗一次）
8. 停止投喂观测超过 1 秒，`MarkerTrackingSession` 派发 `MarkerLost`

---

## 调研中查实的事实（供 propose 直接引用，勿重查）

### thirdDemo 的空间布局

5 个 tour 沿 Z 轴每 2 米一个，不重叠，是可行走的"走廊"：

| tourID | displayType | 位置 (x,y,z) | 朝向 | 包大小 | 版本 |
|---|---|---|---|---|---|
| wm0l5qcn_ibd | regionalTrigger | 0, 0, 0 | 单位矩阵 | 29 MB | 7 |
| 4kvhqwvp_12f | normal | 0, 0, 2 | 绕 Y 45° | **151 MB** | 24 |
| earyserh_i5x | regionalTrigger | 0, 0, 4 | 单位矩阵 | 31 MB | 17 |
| azdugaax_xry | regionalTrigger | 0, 0, 6 | 单位矩阵 | 44 MB | 8 |
| hkdaowxy_0hu | regionalTrigger | 0, 0, 8 | 绕 Y 90° | 37 MB | 9 |

位姿来自 space scene 的 `IteSpaceScene.Tour.transform`（4×4，`ConvertToLeftHanded` 后作为 Tour 实例在 `TourRoot` 下的 local TR，`IteTourObject.cs:96`）。`IteTour`（tour 内容 json）**没有** transform 字段。

### 各 tour 的内容构成（range 请求抠 json 得到，未下整包）

| tour | 标题 | 实体数 | 组件构成 |
|---|---|---|---|
| wm0l5qcn_ibd | 宝顶山--孔雀明王经变相 | 1 | EMWModelRender, LoadTrigger, PlayAnimationAction |
| 4kvhqwvp_12f | 观世音菩萨 | **14** | EMWModelRender×4, **RichText×10**, ToggleVisibilityAction×13, SpinAction×5, TapTrigger×4, LoadTrigger×4, PlayAnimationAction×4 |
| earyserh_i5x | 宝顶山石窟--牧牛图 | 2 | EMWModelRender×2, TapTrigger×2, ToggleVisibilityAction×2, LoadTrigger, PlayAnimationAction×2 |
| azdugaax_xry | 宝顶山石窟--地狱变相图 | 1 | EMWModelRender, LoadTrigger, PlayAnimationAction |
| hkdaowxy_0hu | 北山石窟--观无量寿佛经变像 | 1 | EMWModelRender, LoadTrigger, PlayAnimationAction |

**富文本 UI 只存在于 151 MB 的 `4kvhqwvp_12f` 里**——它一个 tour 占了全场景 19 个实体中的 14 个。裁剪 tour 列表会直接抹掉唯一能验证内容 UI 的部分。

### 必须在本 change 里补上的三处缺口

1. **触发体积现在是死的**。`IteTourAssembler.SetAllVolumesActive(bool)` 存在且注释写明"触发体积的显隐由宿主决定（源实现在开场动画结束后统一打开）"，但**零调用方**，`IteRuntime` 也没把它暴露出去。`CreateTourObject` 结束时体积是 `SetActive(false)`（`IteTourObject.cs:104`），`ChangeDisplayType` 只负责给 `alwaysDisplayed` 关掉。对应源工程 `IteSpaceManager.cs:70` 的 `_liveTours.ForEach(t => t.SetVolumeObjectActive(true))`。
2. **标记桥接不存在**。`IteHostBootstrap` 里接标记源的字段是注释掉的，注释原文："ITE 导览的扫码激活暂时不可用，只能靠 ActivateTour 手动激活。重新接入见新契约（MarkerTrackingSession）"。需要新写：订阅 `MarkerObserved` → 剥掉 `******` 外壳 → 位姿传递 → `IteRuntime.SubmitMarkerScan`；`MarkerLost` 的语义待定（见开放假设）。
3. **D5 解压层级**。修法形状（原 change design D5 已定死，此处沿用）：`DownloadAndExtractAsync` 增加一个显式声明顶层目录语义的参数，空间场景包与 tour 包各自明确传值。**不要**在读取侧做"两个路径都试一下"的 fallback——那是把两类包的差异藏进一次运气好的 `File.Exists`。

### 锚定层级的隐性约束（代码不校验，违反不报错）

`ChangeTourObjectTransform(t, r)` 把 `TourRoot` 的 local 设为"被扫中 tour 的 local 矩阵的逆"，再把 `AnchorRoot` 的 local 设为扫到的位姿（`IteTourObject.cs:133-136`；`BindScene` 传入的 `_anchorObject = anchorRoot`、`_tourOffsetObject = tourRoot`）。两者相乘后被扫中的 tour 正好落在 `AnchorRoot` 上，其余 tour 保持相对布局。成立前提：

- `TourRoot` 必须是 `AnchorRoot` 的**直接子物体**，中间夹任何带变换的节点等式即破；
- `AnchorRoot` 必须是根级物体（或其父在世界原点、无旋转无缩放）。位姿是用 `SetLocalPositionAndRotation` 写入的，而 `MarkerObservation.Pose` 是**世界位姿**（`QuestObservationSource.cs:111` 取 `trackable.transform.position/rotation`；PICO 走 `TrySolveWorldPose`）。

两条都应写成 spec 的显式要求。

### 触发体积需要刚体

源工程 `Player.prefab` 上带 `ARCamera` tag 的节点是 `CapsuleCollider(isTrigger=1)` + `Rigidbody(isKinematic=1)`。Unity 的 trigger 回调要求两个碰撞体中至少一个带 Rigidbody，缺了就一个事件都不发、也不报错。编辑器相机装配必须照办。

另注：`IteTourObject.cs:106-108` 里触发体积尺寸取的是描述值的**一半**（"源实现在此处取半值……是既有行为，原样保留"），走位时按实际一半的体积算。

### 缓存落盘布局（沿用自废弃 change 的 cache-layout.md）

`Application.persistentDataPath` 由 `ProjectSettings.asset` 的 `companyName` / `productName` 决定，两者当前都是 `响堂山`：

| 环境 | 路径 |
|---|---|
| macOS 编辑器 | `~/Library/Application Support/响堂山/响堂山` |
| Windows 编辑器 | `%USERPROFILE%\AppData\LocalLow\响堂山\响堂山` |
| Android 真机 | `/sdcard/Android/data/<applicationId>/files` |

空间场景解压到 `IteSpaceScene_{sceneName}/`（内容**不**再多一层）；tour 包解压到 persistentDataPath **根**（内容**必须**保留顶层 `{tourId}/`）。两类包对顶层目录的期望相反——这正是 D5 要显式化的东西。

### 其他实测事实

- `projectDirectory.json` 列出的 4 个正式项目（Wartsila / DazuRockCarvings / aiysz / ZhongYue）空间场景包**全部 404**（逐个 `HEAD` 实测）。`thirdDemo` 是唯一实际存在的空间场景包（358 KB）。
- 源工程 `GameplaySettings.asset` 默认 `sceneName: thirdDemo`、`isNetworkAvailable: 0`——它一直以来就是离线跑的，因为联网路径踩 D5 必炸（源工程 `FileUtils.ExtractZipAsync` 同样不剥顶层目录）。
- `thirdDemo.zip` 含 `__MACOSX/` 条目，说明多出的那层是 macOS Finder 对文件夹压缩的产物，即打包侧的习惯而非偶发。
- `LatestVersionJsonResult.Data.version` 声明为 `string`，API 实际返回 JSON 数字（`"version": 7`）。Newtonsoft 会做数字→字符串转换，联网路径打通后需确认 `TourVersionCache` 里存的形态一致。
- 仓库里**没有任何自由飞行相机**（只有 `XrCameraAnchor`），9 个场景都没有非 XR 的桌面控制——编辑器走位控制器要现造。
- `IteRuntime` 无公开的 `DeactivateTour`；停用只发生在 `TourDirector.DeactivateCurrent()` 内部。

---

## Open assumptions [NEEDS CLARIFICATION]

以下是 AI 未经确认的假设，将显式带进后续产物：

- [ ] `[ASSUMED]` 编辑器假扫码的位姿取**相机前方 1.5 米、法线朝向相机的世界位姿**（模拟贴墙二维码）。已在 Q9 中告知用户并声明按默认处理，未收到异议。— 影响：design 的编辑器驱动层、tasks 的 harness 实现
- [ ] `[ASSUMED]` `MarkerLost` 事件在 ITE 侧**不触发任何动作**（`IteRuntime` 没有对应入口，源工程也没有丢失语义）。仅在调试 HUD 上显示。— 影响：spec 的标记桥接要求
- [ ] `[ASSUMED]` 二维码 payload 格式沿用源工程的 `******{tourId}******`，剥壳正则沿用 `^\*{6}(.*?)\*{6}$`。真机上实际印制的码是否是这个格式未经核实。— 影响：宿主桥接实现、真机 change
- [ ] `[ASSUMED]` `SetAllVolumesActive(true)` 的调用时机放在 `OnInitialized` 之后（对应源工程开场动画结束后）。是否需要暴露为 `IteRuntime` 的公开 API、还是在 `LoadAsync` 末尾直接调，未与用户确认。— 影响：包的公开面、design
- [ ] `[ASSUMED]` D5 的修法是给 `DownloadAndExtractAsync` 加参数（原 change design D5 定的形状），参数的具体名称与语义（枚举 / bool / 期望的顶层目录名）由 propose 阶段定。— 影响：design、包的内部 API
- [ ] `[ASSUMED]` 本 change 需要为新增的宿主侧逻辑（payload 剥壳、位姿换算）补 EditMode 测试，与仓库既有惯例一致（`Assets/Tests/EditMode/` 下已有 `MarkerTrackingSessionTests`）。— 影响：tasks
- [ ] `[ASSUMED]` 调试 HUD 用屏幕空间 UI（仿 `MarkerHookTestHud`），不做世界空间跟随。— 影响：design
- [ ] `[ASSUMED]` 编辑器走位控制器只做 WASD + 鼠标视角，不做蹲起/跳跃/碰撞。— 影响：tasks
- [ ] `[ASSUMED]` `IteRuntimeConfig.asset` 的 `sceneName` 填 `thirdDemo`，`tourObjectPrefab` 指向包内 `Runtime/Prefabs/Tour.prefab`。— 影响：tasks
- [ ] `[ASSUMED]` 151 MB 的 `4kvhqwvp_12f` 首次下载在编辑器里可接受（串行 `await`，期间 Play 无响应）。若不可接受需要额外的加载策略，本 change 未覆盖。— 影响：risks
- [ ] `[ASSUMED]` 废弃的 `ite-render-smoke-scene` 目录直接删除，不进 `openspec/changes/archive/`（它从未实施）。其调研结论已并入本报告。— 影响：本次落盘操作

---

## 明确的 Non-Goals

- 真机验收（Quest / PICO 打包、passthrough 真扫 QR、gltfast 运行时 URP 材质在 Android+XR 下的表现）
- 接入 `QuestObservationSource` / `PicoFiducialObservationSource`
- 产品 UI：扫码提示界面、加载页、tour 预览列表、开场 Timeline
- 验证 `VideoPlane` / `PrimitiveModelRender` / `ApproximateTrigger` / `PlayAudioAction`（thirdDemo 不含，不造合成数据）
- 网络多人（源工程 `IteSpaceManagerNetwork` / Colyseus 那条线）
- 摘戴头显的真实行为（编辑器无 HMD，`HeadsetPresenceAdapter` 读不到 `userPresence` 会按"已佩戴"处理）

## Suggested next step

- [ ] 运行 `/opsx:propose ite-tour-space-integration` 生成产物（它会读取本报告）
