## 1. 前置真机测量（不改代码）

- [x] 1.1 用现有 `MRBase/Build/Marker Hook Test/Quest Development` 出包，在 Quest 上读一次现场标记，记录 rawPayload 原文与位姿抖动幅度
  - 2026-09-11 实测：rawPayload 原文 `******wm0l5qcn_ibd******`（与 `QrPayloadFormat` 外壳一致）；静置 404 次观测，位置极差 x 0.000 / y 0.001 / z 0.003 m。探针须出 Release 包，见 `BuildScript` 注释
- [ ] 1.2 用 `MRBase/Build/Marker Hook Test/PICO Development` 出包，在 PICO 上读一次现场标记，记录 AprilTag ID、位姿抖动幅度、以及 `tagSizeMeters` 与工作距离的实测关系
- [ ] 1.3 Quest 侧读一次 `InputDevices` 的 `userPresence`；PICO 侧改验 `PXR_Plugin.System.UserPresenceChangedAction` 是否到达（原生通道已确认存在，见 design D19）
- [ ] 1.5 两端各触发一次系统重定位，记录内容与实物错开多少、以及 Quest 侧 `OpenXRSettings.AllowRecentering` 的实际默认值
- [ ] 1.6 PICO 侧灭屏/摘下再戴回，记录 4U 相机会话是否需要重开（归档 7.4 只验了应用内 `Pause()/Resume()`）
- [ ] 1.4 把 1.1–1.3 的实测值写进 `design.md` 的 Open Questions 对应条目（测量结果决定 2.3 的解析器首版实现与 8.2 的调参起点）

## 2. 包侧：标记身份解析（`Packages/com.uality.ite-tour`）

> 2.4/2.5/2.6 的改动落在 `IteRuntime.cs` 上，而该文件带着**他人未提交的改动**，
> 因此这三项已写完但未随本 change 提交。待那批 WIP 落地后一并入库。

- [x] 2.1 `IteSpaceScene.Tour` 增加与 `tourID` 平级的 AprilTag ID 字段，可缺省；补 EditMode 测试覆盖"字段缺失仍装配"与"旧版描述文件可加载"
- [x] 2.2 定义包自有的标记种类枚举（QR 文本 / AprilTag ID），MUST NOT 引用宿主 `MarkerPlatform`
- [x] 2.3 实现标记身份解析：QR 文本解析器（首版按 1.1 的实测格式，收敛在单一入口可替换）与 AprilTag ID 反查
- [x] 2.4 `IteRuntime` 扫码入口改为接受"原始 payload + 标记种类 + 位姿"，内部解析后再走 `TourScanPolicy`
- [x] 2.5 解析失败与 tourId 不在场景中两种情况各输出一条含原始 payload 的日志
- [x] 2.6 EditMode 测试：两端 payload 形状各自解析、无法解析的 payload、解析出的 tourId 不存在
- [x] 2.7 跑 `PackageBoundaryTests`，确认包仍零宿主依赖、`Runtime` 非注释行零平台字样

## 3. 宿主：观测源工厂

- [x] 3.1 新增 `MarkerSourceFactory`，承担全部平台分支与 Quest 侧 `QuestMrukRuntimeInstaller.EnsureInitialized`
- [x] 3.2 "平台未配置"与"平台 SDK 未安装"报成两条可区分的错误
- [x] 3.3 `MarkerHookTestRig` 改用工厂，删除其 `#if MRBASE_*` 分支
- [x] 3.4 全库检索确认观测源装配路径上只剩工厂一处平台条件编译
- [x] 3.5 `PicoFiducialObservationSource` 增加 margin 下限过滤（可调字段，起点 20），低于下限的检测不进 `Poll()` 返回；`MarkerObservation` 的字段形状不变
- [x] 3.6 EditMode 测试：低 margin 检测被滤、真检测不被误伤

## 4. 宿主：桥接改造

- [x] 4.1 `IteMarkerBridge` 删除正则与 tourId 剥壳，改为原样透传"原始 payload + 标记种类 + 位姿"
- [x] 4.2 接入 `MarkerStabilizer`：`MarkerObserved` 喂入、`MarkerLost` 时 `Reset`
- [x] 4.3 修掉 smoothTime 失效：平滑时间常数必须大于典型帧间隔，否则平滑退化为逐帧跳变（当前默认 0.01 使 `t` 恒为 1）
- [x] 4.4 防抖参数按平台各存一套，落在可编辑的配置资产上（阈值、smoothTime、稳定帧数）
- [x] 4.5 接上 `PlatformOffsetConfig`：稳定之后、透传之前施加平台偏移；identity 时行为不变
- [x] 4.6 `MarkerStabilizer` 的稳定判定由帧计数改为时间制（"连续稳定 X 秒"），`Feed` 已收 `deltaTime`；两端参数起点 0.4–0.5 s
- [x] 4.7 同帧多个标记判稳时只提交最先判稳的那个（桥接里一个"本帧已提交"标志位）
- [x] 4.8 EditMode 测试：持续可见只提交一次、丢失后重新稳定再提交一次、偏移生效、identity 偏移不改变行为、同帧两个标记只提交第一个、时间制窗口在不同派发速率下等价

## 5. 宿主：装配点

- [x] 5.1 `MRBase.Ite.Host.asmdef` 增加对 `MRBase.Core` 的引用
- [x] 5.2 `IteHostBootstrap` 相机改为"序列化覆盖 → 解析 `MRContext.Camera` → 报错停用"三段
- [x] 5.3 `networkAvailable` 改为运行时可达性判定，保留注入入口供测试与强制离线调试
- [x] 5.4 增加"由外部触发启动"的入口，编辑器场景保持自启动
- [x] 5.5 `HeadsetPresenceAdapter` 在 PICO 上注入读 `PXR_Plugin.System.UserPresenceChangedAction` / `Pxr_GetPSensorState` 的实现（构造函数已有 `Func<bool?>` 注入点，不改其形状）
- [x] 5.6 EditMode 测试：三段解析的三条分支各自可观察

## 6. 宿主：设备输入 rig

- [x] 6.1 新增设备输入组件：经工厂取观测源、构造 `MarkerTrackingSession`、每帧 `Tick`、注入装配点
- [x] 6.2 解析到相机后挂载 trigger 碰撞体 + kinematic `Rigidbody`，销毁时只撤除自己挂的那些
- [x] 6.3 校验相机 layer 与触发体积 layer 在碰撞矩阵中互相碰撞，不满足时报错
- [x] 6.4 XR 就绪后再触发 `IteRuntime.StartAsync`；XR 始终未就绪时报错停用，不静默等待
- [x] 6.5 观测源打开失败时输出与 3.2 两类错误可区分的第三条日志
- [x] 6.6 接 `OnApplicationPause`：暂停 `MarkerTrackingSession.Pause()`、恢复 `Resume()`；PICO 侧按 1.6 的实测结论决定是否需要重开相机会话
- [x] 6.7 订阅重定位通知并触发强制重扫：Quest 走 `XRInputSubsystem.trackingOriginUpdated`，PICO 走 `PXR_Plugin.System.RecenterSuccess`
- [x] 6.8 Quest 侧显式 `OpenXRSettings.SetAllowRecentering(false)` 加固

## 7. 场景、构建与 MRCore 清理

- [x] 7.1 把装配层（AnchorRoot → TourRoot + `IteHostBootstrap`）抽成 prefab
- [x] 7.2 `IteTourSpace.unity` 改用该 prefab，编辑器假扫码改为推"原始 payload + 种类"，与真机走同一条解析链
- [x] 7.3 新建设备内容场景：prefab 实例 + 设备输入 rig + HMD UI，不含桌面相机与 Overlay HUD
- [x] 7.4 `BuildScript` 场景清单加入设备场景；确认 `IteTourSpace.unity` 仍不出包
- [x] 7.5 全库检索对 `MRCore.unity` 中 ITE 对象的引用，确认为零
- [x] 7.6 删除 `MRCore.unity` 的 `-- ITE --` / `Tour Anchor` / `Marker Frame Offset` / `Tour Root`
- [ ] 7.7 加载 `MRCore` 单独进 Play，确认控制台无 `[ITE]` / `[IteTour]` 日志、无对 `ite-spatial-config.uality.cn` 的请求

## 8. HMD 内 UI

- [x] 8.1 新建世界空间面板，订阅 `OnLoadProgress` / `OnSpaceSceneLoaded` / `OnScanPromptChanged` / `OnTourActivated` / `OnTourSceneLoaded`
- [x] 8.2 渲染加载进度与场景名、`ScanPrompt` 状态与 TourIds
- [x] 8.3 失败可见：下载失败与 `OnTourSceneLoaded` 超时在头显中出现提示，不只落在日志
- [x] 8.4 重定位后在面板上提示"请重新扫码"
- [x] 8.5 确认面板不使用屏幕空间 Overlay，且不挂在 `MRCore.unity` 上

## 9. 真机验收与调参

- [x] 9.1 Quest 出包，跑通：联网首跑 → `OnInitialized`
  - 2026-09-11：清空缓存首跑 3 分钟内 `OnInitialized`；复跑空间场景（ETag）与已下 tour（版本号）命中缓存跳过下载。途中发现两处：切场景时序致扫码与区域触发静默失效（已修，D26）；159 MB 的 tour 包下载一度断流、下载器无超时而永久等待（待修）
- [x] 9.2 PICO 出包，跑通：联网首跑 → `OnInitialized`
  - 2026-09-17：清空缓存首跑 44 s 到 `OnInitialized`（空间场景 + 5 个 tour 全下），随后 `OnScanPromptChanged Visible`。途中修了三处：内容方重传的空间场景包是扁平布局、`Strip` 解压抛异常打死整条链（D29）；MRUK 包的进程级 hook 在 PICO 上每帧抛 `DllNotFoundException: OVRPlugin`，30 s 内 8712 行日志冲爆 logcat（已在 `PlatformRuntime` 停用该对象）；标记位姿绕标记 X 轴翻 180°，内容上下翻转且背面朝人（D30）
- [ ] 9.3 两端调防抖参数至稳定判定可靠触发，实测值回填 4.4 的配置资产
- [x] 9.4 两端验收：未扫码时区域触发不生效（强制扫码语义）
  - Quest 侧 2026-09-11：`OnInitialized` 后提示 `Visible`，扫码前无任何 tour 激活
  - PICO 侧 2026-09-17：同样 `OnInitialized` → `OnScanPromptChanged Visible`，扫码前无任何 tour 激活；扫 AprilTag id00 后才 `OnTourActivated` 并转 `Hidden`
- [ ] 9.5 两端验收：首次扫真码激活并渲染，tour 世界位姿落在标记位姿（含偏移）上
  - Quest 侧 2026-09-11：扫 `marker_id00` 二维码 → `OnTourActivated wm0l5qcn_ibd` → `OnTourSceneLoaded`，提示收起；位姿是否落在标记上待目视确认
  - PICO 侧 2026-09-17：扫 AprilTag id00（`margin≈105`、`hamming=2`、0.49 m）→ `OnTourActivated wm0l5qcn_ibd` → `OnTourSceneLoaded`，提示收起。首轮方向反了（`rot=(357.9, 1.4, 179.3)`，物体系换算漏了一半，见 D30）；修复后目视确认方向正确。**Quest 半边的位姿仍待目视确认**
- [ ] 9.6 两端验收：区域自动切换，前一个 tour 被停用销毁
- [ ] 9.7 两端验收：内容元素 UI 渲染（圆角框材质、按钮贴图、中文富文本无缺字）
- [ ] 9.8 两端验收：`regionalTrigger` 类型 tour 的标记持续在视野内不消耗二次锚定许可；丢失后重扫才消耗且不重建内容
- [ ] 9.9 两端验收：离线复跑结果与联网首跑一致
- [ ] 9.10 两端验收：两张码同框时只有最先判稳的那个生效，另一个不抢
- [ ] 9.11 两端验收：触发系统重定位后进入强制扫码，面板出现提示；重扫后内容重新对齐
- [ ] 9.12 两端验收：灭屏/摘下再戴回，MUST NOT 出现虚假 `MarkerLost`；PICO 上扫码仍可用
- [ ] 9.13 PICO 实测：扫描开启 vs 关闭两种状态下的帧率与机身温升，作为后续降频策略的依据；`sampleHz` 与 `quadDecimate` 的现场取值一并记录
- [ ] 9.14 PICO 实测：假标记过滤生效，运行期间无 margin 低于下限的检测进入业务层
- [ ] 9.15 记录一次真机实测：glTFast 运行时纹理内存、TMP 中文字体表现、5 个 tour 常驻开销。只记录，不在本 change 内调优
