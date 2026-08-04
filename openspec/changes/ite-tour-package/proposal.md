## Why

`ite-space-tour` 的 ITE 导览业务是本项目的核心逻辑之一，但它当前的形态阻碍复用：零 asmdef 全部编译进 `Assembly-CSharp`、全局命名空间、通过 `OVRManager` 与 tag 查找硬绑 Quest、业务代码直接抓取宿主的 `MainConstants` / `SettingsManager` / `ScanPreviewUI` / `FileUtils` 单例。结果是既不能上 PICO，也不能与本项目其它模块共存，更不能移植到别的工程。

本次要把 ITE 的业务能力抽成一个自包含的 Unity embedded package。选择 package 而非 asmdef 的唯一理由是**可移植性会腐烂**：asmdef 无法阻止后来者往 references 里加一行宿主程序集，加完编译通过、功能正常、没人报警，直到真要移植时才发现拖着一堆隐藏依赖。而 Unity 强制 `Packages/` 下的程序集不得引用 `Assets/` 下的程序集——违反的那一刻就编译失败，而不是移植的那一天。

## What Changes

- 新建 embedded package `Packages/com.uality.ite-tour/`，迁入 `ite-space-tour/Assets/Scripts/ITE` 的全部业务代码，以及它私有依赖的内部工具（`EventEmitter`、`ComponentsUtils`、`Matrix4x4Extensions`、`LegacyAnimationController`、`AnimationAudioController`、`HierarchyBoundsCalculator`、`BoxColliderWireframeDrawer`、`RoundedBoxUIProperties`）与 4 个 element prefab。
- 包内代码全部收入 `Uality.IteTour.*` 命名空间与单一程序集 `Uality.IteTour`，程序集引用面只含 Unity 官方包，**不含任何 `MRBase.*`**。
- 平台耦合点全部外移：`OVRManager.HMDMounted/HMDUnmounted` 与 `AnchorObject.whenQrCodeScanned` 改为宿主向包**推入**（`SetHeadsetMounted` / `SubmitMarkerScan`）；tag 查找（`ARCamera` / `AnchorObject` / `AnchorOffsetObject`）改为装配时显式注入 Transform。
- 宿主 UI 依赖方向反转：原先包调用 `ScanPreviewUI.Instance.Show()/Hide()`，改为包广播 `OnScanPromptChanged` 事件，宿主自行决定渲染，不订阅也不影响功能。
- 宿主配置外移：`MainConstants` / `SettingsManager` 的相关字段收进包定义的 `IteRuntimeConfig` ScriptableObject（资产实例放宿主 `Assets/`），硬编码域名 `https://api.uality.cn` 配置化。
- 内容管线（下载 zip → 版本校验 → 解压 → 缓存 → 反序列化 → 资源加载 → 实例化）整体归包所有，包直接依赖 `com.unity.sharp-zip-lib` 与 `com.unity.cloud.gltfast`，不向宿主索取 IO 委托。
- 外部 API 面收敛为：装配对象 ×1、推入方法 ×2、广播事件 ×7。**接口数量 0，必需行为委托数量 0。**
- 宿主侧新增薄适配层 `Assets/Scripts/IteHost/`（`MRBase.Ite.Host` 程序集），把既有 `IMarkerTrackingProvider` 与头显佩戴状态转发进包；Quest/PICO 差异 100% 落在此层，包内零平台分支。
- 新增依赖 `com.unity.cloud.gltfast` 与 `com.unity.sharp-zip-lib`（均取当前最新 release），声明在包自己的 `package.json` 中由 UPM 传递解析，不写进工程 `manifest.json`——依赖声明只有一个来源，移植时才会跟着包走。
- 迁移期已知缺口以 `Documentation~/TODO.md` 记录，不静默吞掉。

## Non-Goals

- **不迁移多人共址（co-location）网络能力**：`Assets/Scripts/Network/` 13 个文件 2313 行与 `IteSpaceManagerNetwork.cs` 均不迁入，`io.colyseus.sdk` 不安装。该模块的依赖方向本就是「网络 → ITE」，属下游消费者，塞进包是倒置。为其预留的 `OnFirstMarkerScanned` 与 `OnTourAnchorChanged` 一并不实现。
- 不重构或改动现有 `MarkerAnchorService` / `AnchorRegistry` / `AnchorEntity` 这条既有消费链；ITE 与它并行消费同一个 `IMarkerTrackingProvider`，互不影响。
- 不把 `MRBase.Common` / `MRBase.Localization` 提升为 package。
- 不把 package 拆成独立 git 仓库或 submodule；本次只到 embedded 形态。
- 不改变 ITE 的既有业务语义。行为等价优先，发现的历史缺陷记 TODO 不顺手修。
- 不实现 `VolumeTrigger` / `CustomGestureTrigger`（原项目中即为注释状态）。
- 不做 ITE 内容的编辑器创作工具。

## Capabilities

### New Capabilities

- `ite-tour-package`: ITE 空间导览业务的可移植封装——包边界与依赖方向约束、外部装配与推入/广播契约、跨平台内容管线、Tour 生命周期与组件运行时。

### Modified Capabilities

- （无。`openspec/specs/` 下无已归档能力被本次改动语义覆盖；`cross-platform-marker-tracking` 提供的 `IMarkerTrackingProvider` 本次只被消费，其契约不变。）

## Impact

- 新增目录：`Packages/com.uality.ite-tour/`（包本体）、`Assets/Scripts/IteHost/`（宿主适配层）。
- 修改文件：`Packages/manifest.json` 仅新增 `testables` 条目（使 Test Runner 发现包内测试），依赖不写在此；`packages-lock.json` 由 UPM 自动更新；核心场景需挂载适配层组件与 `IteRuntimeConfig` 资产引用。
- 现有 `MRBase.*` 8 个程序集的引用关系不变。
- 包依赖：`com.unity.nuget.newtonsoft-json`（工程已有）、`com.unity.cloud.gltfast`（新增）、`com.unity.sharp-zip-lib`（新增）、Unity 内置模块（video / unitywebrequest* / ugui / animation / audio）。
- 测试：包内 `Tests/` 覆盖可离机部分（3 个多态 JsonConverter、Matrix 坐标转换、Tour 匹配切换状态机、缓存键命名）；平台推入路径与真机行为不在 EditMode 覆盖范围。
- 风险：PICO 的头显佩戴事件无既有实现，需以 OpenXR `CommonUsages.userPresence` 落地并真机验证；ITE 的元素渲染依赖自定义 shader（`RoundedBoxUIProperties`），迁移遗漏会导致运行时渲染异常。
