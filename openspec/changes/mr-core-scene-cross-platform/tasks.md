## 1. M0 前置：分支与依赖

- [x] 1.1 打 tag 备份当前分支状态，把 `feature/qrcode-marker-localization`（30 个提交）合并进 `master`，确认 Editor 打开无报错
- [x] 1.2 从 `master` 拉 `feature/mr-core-scene` 作为本变更的工作分支
- [x] 1.3 处理 `Assets/_Recovery/0.unity`（17MB 未跟踪，疑似崩溃恢复残留）：确认无用后删除，或加入 `.gitignore`
- [x] 1.4 升级依赖至 design D12 表格：`xr.hands` → 1.8.1、`interaction.toolkit` → 3.5.1、`meta-openxr` → 2.5.1、`compositionlayers` → 2.5.0；**`xr.openxr` 保持 1.16.1 不动**；URP / AR Foundation / Meta SDK 不动。确认 Console 零编译错误
- [x] 1.5 从 `Packages/manifest.json` 移除误装的 `com.unity.purchasing` 5.4.2（内购包，与项目无关）
- [x] 1.6 通过 Package Manager 的 git URL 安装 PICO Integration SDK：`https://github.com/Pico-Developer/PICO-Unity-Integration-SDK.git#release_3.4.0`
- [x] 1.7 **闸门**：确认 PICO SDK 3.4.0 + Unity 6000.4.4 编译通过（零错误）。失败则原样记录错误内容，暂停后续任务并回到 design 修订 PICO 路径假设
- [x] 1.8 **闸门**：核验 `PXR_HandSubSystem` 在 XR Hands 1.8.1 下无 `must implement inherited abstract member` 类错误（design 硬约束 #3 针对 1.7.3，升级后须重做）。失败则将 `xr.hands` 退回 1.7.3 并在 design 中记为版本上限
- [x] 1.9 确认 `ProjectSettings > Audio > Spatializer Plugin` 未指向任何厂商专属插件

## 2. M0 打包脚本（design D14，后续所有出包都经由它）

- [x] 2.1 新建 `Assets/Scripts/Editor/`（`MRBase.Build.Editor` 程序集）
- [x] 2.2 实现 `BuildQuest()` / `BuildPico()`：激活对应 Build Profile（带入其 defines 与 scene 列表）
- [x] 2.3 在构建前设置 Android XR loader（Quest → `OpenXRLoader`，Pico → `PXR_Loader`），使用 `XRPackageMetadataStore.AssignLoader` / `RemoveLoader`
- [ ] 2.4 在构建前设置 OpenXR feature 开关 —— **有意跳过**：Pico 走 PXR_Loader 时 OpenXR 未进 loader 列表，其 feature 与 manifest 注入均不生效；Quest 走 OpenXRLoader 时现有 feature 配置已正确。加此代码属投机，等出现真实需要再补
- [x] 2.5 实现构建前一致性校验：构建意图 define 与 loader 对应关系、Spatializer 未指向厂商插件；不一致则中止构建并输出不匹配项
- [x] 2.6 在 `finally` 中还原第 2.3 / 2.4 步修改的 XR 设置，确认打包后 `git status` 中不出现 `XRGeneralSettingsPerBuildTarget.asset` 等改动
- [x] 2.7 暴露菜单项 `MRBase/Build/Quest` 与 `MRBase/Build/Pico`
- [x] 2.8 暴露命令行入口（`Unity -batchmode -quit -executeMethod ...`），确认退出码可用于判定成败
- [x] 2.9 写一个外层 shell 脚本分两次调用 Unity 完成两端构建，作为「两端编译回归」的执行方式；**不得实现单次执行内连续构建两端的 `BuildBoth()`**（切换 defines 触发重编译会打断脚本执行）
- [ ] 2.10 可选：构建成功后自动 `adb install -r` 到已连接设备
- [x] 2.11 验证：故意把 loader 留成错的一个，经打包入口构建，确认脚本自行纠正而非产出错包

## 3. M0 诊断工具

- [ ] 3.1 新建 `Assets/Scenes/Diagnostics.unity`，以 XRI 的 `XR Origin Hands (XR Rig).prefab` 为基础搭一个最小 rig
- [ ] 3.2 实现 `DiagnosticsHUD`：世界空间 Canvas 怼在相机前约 0.5 米，逐帧刷新
- [ ] 3.3 HUD 呈现 `XRHandSubsystem` 的 descriptor `id`（用于区分 OpenXR provider 与 `"PICO Hands"`）
- [ ] 3.4 HUD 呈现左右手 `isTracked` 与有效关节计数
- [ ] 3.5 HUD 呈现 `InputSystem.devices` 全部设备名列表
- [ ] 3.6 HUD 呈现手部交互相关 action 的当前值与 `activeControl`（**双 binding 验证的直接证据**）
- [ ] 3.7 HUD 呈现自算的平滑 fps（两端同一算法，保证可比）
- [ ] 3.8 HUD 呈现 passthrough 启用状态与相机 clear 配置
- [ ] 3.9 同一份数据同时 `Debug.Log` 输出，便于用 `adb logcat -s Unity:V` 留证据（项目已装 `com.unity.mobile.android-logcat`）

## 4. M0 Editor 验证（不需设备）

- [ ] 4.1 打开 `XRDeviceSimulatorSettings`，启用 `AutomaticallyInstantiateSimulatorPrefab`
- [ ] 4.2 在 Editor 中运行 `Diagnostics.unity`，确认键鼠可控头部与双手、HUD 的 `isTracked` 变为 true、预设手势可切换
- [ ] 4.3 确认 Editor 下 `XRHandSubsystem` descriptor 为模拟器 provider（验证 design 硬约束 #8：三种装配复用同一场景）

## 5. M0 Quest 真机验证（对照组）

- [ ] 5.1 经 `MRBase/Build/Quest` 出包并装机（不手动改任何 XR 设置，同时验证打包脚本可用）
- [ ] 5.2 确认 HUD 显示双手 tracked、关节数 26
- [ ] 5.3 确认 `InputSystem.devices` 列表中出现 Meta 的手部 aim 设备
- [ ] 5.4 捏合，确认 Select action 数值跳变且 `activeControl` 指向 Meta 设备
- [ ] 5.5 场景内放一个世界空间 Canvas 按钮，确认远场射线可点中
- [ ] 5.6 记录 Quest 端 fps 基线数值

## 6. M0 PICO 真机验证（真正的未知）

- [ ] 6.1 查清模式 1（PXR_Loader）下 passthrough 的启用方式，并在 design 的 Open Questions 中回填结论（`PassthroughFeature.cs` 为 `#if PICO_OPENXR_SDK`，模式 1 不编译；应走 PXR seethrough 路径）
- [ ] 6.2 在 XR Origin 上挂 `PXR_Manager` 并勾选 Hand Tracking，经 `MRBase/Build/Pico` 出包并装机
- [ ] 6.3 **闸门**：确认 HUD 的 descriptor `id` 显示 `"PICO Hands"` 且双手 tracked。手不出来则检查 `PXR_Manager` 勾选与系统设置中的手势追踪开关
- [ ] 6.4 拷一份 `XRI Default Input Actions` 到项目自有目录（不改随包资产，避免升级被覆盖）
- [ ] 6.5 为 Aim Position / Aim Rotation / Aim Flags / Select / Select Value / UI Press / UI Press Value **追加**（非替换）`<PicoAimHand>{LeftHand|RightHand}/...` 系列 binding
- [ ] 6.6 **头号闸门**：确认 `PicoAimHand` 出现在设备列表中，且捏合时 Select 数值跳变、`activeControl` 指向 `PicoAimHand`。失败则暂停并回到 design 修订 D2（需改为自建手势语义层从关节数学算捏合）
- [ ] 6.7 确认 PICO 端远场射线能否点中世界空间 Canvas 按钮（验证官方标注 5.13.0 的已知问题是否命中目标设备）
- [ ] 6.8 记录 PICO 4 Ultra 端 fps 基线数值与系统版本号（系统版本影响已知问题是否命中）
- [ ] 6.9 把 M0 全部实测结论回填到 `probe-report.md` 与 `design.md`，逐条消解或推翻对应 `[ASSUMED]` 项

## 7. M1 核心场景

- [ ] 7.1 新建 `Assets/Scenes/MRCore.unity`，加入 `EditorBuildSettings`
- [ ] 7.2 建 `Assets/Prefabs/Rig/XROrigin_Base.prefab`（平台无关部分），并预留控制器挂点但不实现降级逻辑（追踪丢失降级已明确延后）
- [ ] 7.3 建 `XROrigin_Pico.prefab` 变体，差量为 `PXR_Manager`；确认 Quest 构建产物中不含该组件
- [ ] 7.4 按任务 6.1 的结论在两端启用 passthrough，配置相机 clear 使背景不遮挡真实环境
- [ ] 7.5 从 `Assets/Samples/XR Hands/1.7.3/HandVisualizer/` 取手网格 FBX 与 `XRHandSkeletonDriver` 组合，接入左右手
- [ ] 7.6 制作半透明手部材质，采用深度预写 + 描边解决自相交排序（参考 PICO 自家 `HandEditorTransparentOutlinedHandPrepassZ.mat` 的思路）；两端共用同一材质资产
- [ ] 7.7 验证手移出视野后呈现消失、移回后在当前实际位置重新出现（无位置残留、无卡死）
- [ ] 7.8 接入 `NearFarInteractor` 与 `XRPokeInteractor`，select 输入使用任务 6.5 的多 binding 配置
- [ ] 7.9 放置测试用可抓取方块与可触碰按钮
- [ ] 7.10 实现 `MRContext`（继承既有 `Assets/Scripts/Common/StaticInstance.cs`），至少暴露相机、XR Origin、双手数据访问器
- [ ] 7.11 实现 `MRBootstrap` 最小版本：平台前置条件检查 → 装配 → 就绪；`#if MRBASE_*` 仅出现于此处
- [ ] 7.12 前置条件不满足时呈现可操作的引导信息，不静默失败、不抛未处理异常终止应用

## 8. M1 验收

- [ ] 8.1 Editor 中按 Play，模拟器能出手、能捏合（日常开发门槛）
- [ ] 8.2 经打包脚本两端出包装机，逐条走验收清单：可见真实房间 / 双手出现且不左右互换 / 移出移回可恢复 / 捏合抓取与释放 / 指尖按钮有反馈
- [ ] 8.3 重装 APK 并重启应用，确认行为一致（排除偶然跑通）
- [ ] 8.4 确认两端 fps 均不低于 72，并与 M0 基线对比留档
- [ ] 8.5 确认两端使用的手网格与材质为同一份资产（相同 GUID）
- [ ] 8.6 grep 检查：业务程序集中 `#if MRBASE_`、`OVR`、`PXR_` 命中数为 0
- [ ] 8.7 grep 检查：源码与输入配置中对 `XRCommonHandGestures` / `XRHandDevice` aim-pinch 通道、以及 `TryGetMeshData` 的引用命中数为 0
- [ ] 8.8 确认打包后工作区干净（`XRGeneralSettingsPerBuildTarget.asset` 未被打包流程留下改动）

## 9. M2 装配层成型

- [ ] 9.1 新建 `Assets/Scripts/Core/`（`MRBase.Core`），把 `MRContext` / `MRBootstrap` / 能力接口迁入
- [ ] 9.2 新建 `MRBase.Platform.Quest` / `.Pico` / `.Sim` 三个程序集，各配 `defineConstraints` 与 `versionDefines`（`MRBASE_HAS_MRUK` / `MRBASE_HAS_PICO_SDK`）
- [ ] 9.3 拆分既有 `MRBase.Localization.Native.asmdef`（现状硬引用两家 SDK 且 `defineConstraints` 为空），迁入对应平台程序集
- [ ] 9.4 验证「包未安装 + 构建意图 define 已设」时项目仍能编译（平台程序集整体跳过）
- [ ] 9.5 新建 `MRBase.Interaction`：手势判定层，输入 `XRHandSubsystem` 双手关节，输出语义事件；迟滞与稳定帧数统一在此层处理（结构参考既有 `MarkerStabilizer`）
- [ ] 9.6 实现翻手（掌心朝上）判定，使用 `XRHandShape` + `XRHandRelativeOrientation`；可先用项目已有 `HandMenuSetupVariant_MRTemplate.prefab` 验证链路
- [ ] 9.7 实现全握抓取判定（`XRFingerShape.FullCurl` 自算，因 PICO 侧无 grasp 值）
- [ ] 9.8 实现双手合十判定（双手平掌 + 掌心相对 + 掌心距离阈值，纯关节数学）
- [ ] 9.9 把两端 aim flags 中的系统手势标志位归一化为统一信号，系统手势活跃时静默自有手势判定
- [ ] 9.10 为手势判定层写 EditMode 测试（喂假关节数据），参照既有 `MarkerStabilizerTests` / `MockMarkerProvider`

## 10. M3 业务接入

- [ ] 10.1 把 `MarkerTrackingBootstrapper` 的 `throw PlatformNotSupportedException` 与 `MRBootstrap` 的前置条件闸门合并（含 PICO LBE 模式与权限探测）
- [ ] 10.2 把 marker 定位内容改造为 additive 业务场景，仅通过 `MRContext` 取核心引用
- [ ] 10.3 把圣物苏醒内容改造为 additive 业务场景
- [ ] 10.4 验证业务场景加载与卸载后核心场景无空引用异常
- [ ] 10.5 验证业务场景资产内不含指向核心场景对象的序列化引用
- [ ] 10.6 两端跑一遍完整真机回归清单
