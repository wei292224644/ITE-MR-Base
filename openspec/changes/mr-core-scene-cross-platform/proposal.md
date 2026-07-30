## Why

项目目前没有核心场景：三个 scene 全是各自独立的 demo（`LocalizationDemo` / `SacredRelicAwakenDemo` / `SampleScene`），`EditorBuildSettings` 里只挂了 `SampleScene`。已完成的两个功能（marker 定位、圣物苏醒）没有共同的宿主，无法组合成一个应用。

同时项目要同时交付 Quest 与 PICO 两个 APK，但**跨端差异的隔离机制还不存在** —— 现有 `MRBase.Localization.Native.asmdef` 硬引用 `meta.xr.mrutilitykit` + `Oculus.VR` 且 `defineConstraints` 为空，一旦加入 PICO SDK 就会同时硬引用两家 SDK，任一包缺失即编译失败。

本变更建立**跨端核心场景与平台差异隔离机制**，作为后续所有功能的地基。首个里程碑（M1）的可见交付是「两台设备上都渲染出一只跟随真手的半透明手，并能捏合选中、指尖点按」——它的真实价值是验证「一份 rig 两端通吃」这一架构假设。

## What Changes

**场景结构**
- 新增常驻核心场景 `MRCore.unity`：平台无关的 XR Origin、半透明手、交互器、`MRContext` 服务、passthrough
- 业务内容以 additive 方式挂载到核心场景（依赖方向：业务 → `MRContext` 单向，业务场景内不含跨场景引用）
- **不采用两个平台场景**：平台差异下沉到装配层与构建层，Editor 模拟器作为第三种装配复用同一场景

**平台差异隔离（四种手法，各有明确适用层）**
- A 输入层：一个 `InputAction` 同时挂 `<MetaAimHand>` 与 `<PicoAimHand>` 两条 binding，两端各自命中，零 `#if`
- B 组件层：prefab 变体或运行时挂载平台专属组件（目前已知仅 `PXR_Manager`）
- C 能力层：provider 接口 + `#if` 工厂，延续既有 `IMarkerTrackingProvider` / `MarkerTrackingBootstrapper` 范式
- D 构建层：统一打包脚本完整拥有构建流程（激活 Build Profile → 设置 XR loader 与 OpenXR feature → 一致性校验 → 构建 → 还原），人不再手动改 XR 设置也不再点 Build 按钮

**装配机制**
- 两条正交 define 轴：`MRBASE_QUEST|MRBASE_PICO`（构建意图，Build Profile 提供）× `MRBASE_HAS_*`（包是否存在，asmdef `versionDefines` 提供，延续既有 `MRBASE_HAS_MRUK` 先例）
- 平台代码拆为三个 asmdef（`MRBase.Platform.Quest` / `.Pico` / `.Sim`），各自带 `defineConstraints`，包缺失则整体跳过编译
- 新增 `MRBase.Core`（`MRContext` / `MRBootstrap` / 能力接口）、`MRBase.Interaction`（手势判定，M2 落地）与 `MRBase.Build.Editor`（打包脚本，M0 落地）
- **约束**：`#if MRBASE_*` 只允许出现在 `MRBootstrap` 与 `MRBase.Platform.*` 内部；业务层出现 `OVR` / `PXR` 符号视为架构违规

**手部呈现与交互**
- 手部数据统一走 Unity XR Hands（`XRHandSubsystem`），不使用 `OVRHand` / PICO hand prefab
- 手模型使用 XR Hands sample 自带 FBX + 半透明材质（不使用厂商 SDK 内的手模型，避免跨平台分发的授权风险）
- M1 交互限于 XRI 现成能力：捏合（select）、指尖触碰（poke）

**依赖升级（本次一次性升到最新配套版本后锁定）**
- `com.unity.xr.hands` 1.7.3 → 1.8.1；`com.unity.xr.interaction.toolkit` 3.4.1 → 3.5.1；`com.unity.xr.meta-openxr` 2.5.0 → 2.5.1；`com.unity.xr.compositionlayers` 2.4.0 → 2.5.0
- `com.unity.xr.openxr` **停在 1.16.1**（1.17.1 可用，但 PICO SDK 只声明 `UNITY_OPENXR_1_16_0`）
- 新增 `com.unity.xr.picoxr` 3.4.0（PICO Integration SDK，模式 1 / PXR_Loader）
- URP 17.4.0 与 AR Foundation 6.4.2 由 Unity 6000.4.4 绑定，不动
- Meta XR Core / MRUK 已是最新 205.0.0，不动

**非目标（本变更明确不做）**
- 不做自研手势（翻手出面板、全握抓取、双手合十）—— 留 M2
- 不做手部追踪丢失的降级路径（控制器通道、`trackingLost` 提示）—— 用户明确延后
- 不做 AR Foundation 的平面检测 / 遮挡 / 包围盒（PICO 侧未实现对应 subsystem）
- 不做 composition layer 呈现路径（M1 全部走 in-scene 渲染）
- 不引入任何厂商空间音频插件（Spatializer Plugin 锁定为 Unity 内置）
- 不迁移现有三个 demo 场景（M1 保留不动）

## Capabilities

### New Capabilities

- `mr-core-scene`: 常驻核心场景的结构、passthrough 呈现、`MRContext` 服务契约与 additive 业务场景加载规则
- `cross-platform-assembly`: 平台差异隔离机制 —— define 双轴、asmdef 边界、provider 装配顺序、启动前置条件闸门、构建期 loader 切换
- `hand-presentation`: 基于 `XRHandSubsystem` 的半透明手部渲染行为（含追踪丢失/恢复时的显示规则）
- `hand-interaction`: 捏合与指尖触碰的输入契约（多 binding 中立化、禁止依赖 `XRCommonHandGestures`）

### Modified Capabilities

- （无。`openspec/specs/` 当前为空，本变更是项目首个能力规格）

## Impact

**新增代码**
- `Assets/Scripts/Core/`（`MRBase.Core`）：`MRContext`（继承既有 `StaticInstance<T>`）、`MRBootstrap`、能力接口
- `Assets/Scripts/Platform/{Quest,Pico,Sim}/`：三个平台 asmdef
- `Assets/Scripts/Editor/`（`MRBase.Build.Editor`）：构建前处理与配置校验
- `Assets/Scenes/MRCore.unity`、`Assets/Scenes/Diagnostics.unity`、`Assets/Prefabs/Rig/`

**重构既有代码**
- `Assets/Scripts/Localization/Native/MRBase.Localization.Native.asmdef` 拆分并加 `defineConstraints`（现状硬引用两家 SDK 会在加入 PICO 后崩）
- `MarkerTrackingBootstrapper` 的 `throw PlatformNotSupportedException` 与前置条件闸门合并（M3）

**配置影响**
- `Packages/manifest.json`：上述升级 + 新增 PICO SDK
- `XRGeneralSettingsPerBuildTarget.asset`：Android 的 loader 需按 Build Profile 切换（Quest = OpenXRLoader，Pico = PXR_Loader）
- `ProjectSettings > Audio > Spatializer Plugin`：锁定 Unity 内置
- `EditorBuildSettings`：加入 `MRCore.unity`
- `.gitignore` 或直接删除：`Assets/_Recovery/0.unity`（17MB，未跟踪，疑似崩溃恢复残留）

**分支**
- 先将 `feature/qrcode-marker-localization` 的 30 个提交合并至 master，再从 master 拉 `feature/mr-core-scene`（核心场景是地基，不应埋在未合并分支中）

**下游依赖本变更的工作**
- marker 定位（`MRBase.Localization`）与圣物苏醒（`MRBase.SacredRelic`）在 M3 迁移为 additive 业务场景
- 未来任何跨端功能都依赖本变更建立的四手法与 define 双轴

**设备与授权**
- 目标设备：**Quest 3 / 3S + PICO 4 Ultra**，且始终采用最新型号。两端同为骁龙 XR2 Gen 2，性能同级 —— 性能基线与内容规格上限可按 Gen 2 统一制定，无需按更弱设备下调
- PICO ArUco 需 LBE 模式 + 企业授权（本变更不涉及，但 M3 依赖）

## Open Assumptions

以下为 `probe-report.md` 中未消解的判断，逐条原样带入，实施时须显式处理：

- [ ] `[DEFERRED]` 手部追踪丢失的降级路径未定（是否保留控制器通道、是否做 `trackingLost` 视觉提示）。用户明确「后面再考虑，但手势一定会有」。影响：design 的 rig 结构（建议留控制器挂点但 M1 不实现）
- [ ] `[ASSUMED]` MR 下半透明手的产品化形态未定。M1 取「追踪可视化」，轮廓/特效/遮挡代理的最终取舍留到真机上看。影响：design 的美术与 shader 章节
- [ ] `[ASSUMED]` PICO Integration SDK 3.4.0 + Unity 6000.4.4 兼容性未实测。`package.json` 只写 `"unity": "2021.3"`（下限）；PICO 官网仍有陈旧的「Unity 2020-23，Unity 6 支持中」表述；但 3.4 代码里已有 Unity 6 + URP + GLES + Multipass 的 MSAA 检查。影响：M0 头号闸门，不过则整个 Pico 路径要改
- [ ] `[ASSUMED]` XR Hands 1.8.1 下 PICO provider 的签名兼容性未核验。已核验的是 1.7.3（`GetHandLayout` / `TryUpdateHands` 逐字匹配）。影响：M0
- [ ] `[ASSUMED]` 双 binding 在真机两端各自命中未实测。推断依据是 PICO 官方绑定表 + `PXR_HandSubsystem.cs:410-428` 的 `PicoAimHand` 控件定义（`indexPressed` / `aimFlags` / `pinchStrengthIndex`），逻辑成立但无人真机验过。影响：整套「一份 rig 两端通吃」方案的成立与否
- [ ] `[ASSUMED]` Pico 模式 1 下 passthrough 如何开启未查清。`PassthroughFeature.cs` 是 `#if PICO_OPENXR_SDK`，模式 1 下不编译；模式 1 走的应是 PXR 的 seethrough（`Utils/PXR_VstModelPosCheck.cs` 那套）。影响：M0 + M1
- [ ] `[ASSUMED]` 两端 passthrough 下相机 clear / alpha 的具体配置未定。影响：M1 实施细节
- [ ] `[ASSUMED]` PICO「捏合射线打不到 UI」的已知问题是否命中目标设备未验（官方标注 5.13.0）。影响：验收项 / UI 设计（关键操作不要只放远场射线）
- [ ] `[ASSUMED]` `IPassthroughController` 是否 M1 就抽象成接口，还是 M1 直接 `#if`。倾向：M1 直接 `#if` 在 `MRBootstrap` 里，M2 再抽接口（避免过早抽象）。影响：design
- [ ] `[ASSUMED]` 手模型 FBX 产品化阶段的来源（自制 / 采购）未定。M1 用 sample 自带
- [ ] `[ASSUMED]` 分发渠道与企业授权未讨论。PICO ArUco 需 LBE 模式 + 企业授权；design 文档记「已确认拿到 PICO 企业开发授权」，但商店分发 vs 现场侧载的差异未确认。本变更不涉及，M3 依赖

起草本提案时新增的未确认判断：

- [ ] `[ASSUMED]` `Diagnostics.unity` 体检场景作为 M0 的首个交付物纳入本变更范围（而非当作一次性临时工具）。理由：M0 的多条验证项都需要在设备上读运行时状态，且该场景在后续调试中长期有用
- [ ] `[ASSUMED]` `Diagnostics.unity` 与打包脚本这两个 M0 交付物的具体形态（HUD 呈现哪些字段、脚本入口命名）未逐项确认，按 design D10 / D14 的清单实施

已消解、无需再确认的项：

- [x] Meta XR Core SDK / MRUK 最新版本 = 205.0.0（`npm.developer.oculus.com` 查询确认），项目当前即最新，无需升级
- [x] **目标设备 = Quest 3 / 3S + PICO 4 Ultra，始终采用最新型号**（用户确认）。两端同为 XR2 Gen 2，性能同级，规格上限按 Gen 2 统一制定；不需支持 Quest 2 / PICO 4 非 Ultra / PICO Neo3 等 Gen 1 设备
- [x] **构建期差异由统一打包脚本承担，且提前到 M0**（用户确认）。否决了「手工切 + 构建后校验」与「`IPreprocessBuildWithReport` 自动改写」两个替代方案。依据：Build Profile 可覆盖范围经核实仅 Player / Graphics / Quality Settings，`PICO.asset` 字段中确无任何 XR 项；而 XR loader 在任何场景加载前即被读取（`Android Settings.m_InitManagerOnStart: 1`），故场景结构无法影响它。打包脚本使「忘切 loader → 黑屏包」这一失败模式从根上消失，并白送两端编译回归与 CI 基础
- [x] **能力划分保持 4 个**（用户确认）：`mr-core-scene` / `cross-platform-assembly` / `hand-presentation` / `hand-interaction`。理由：四者关心点本质不同（场景结构 / 平台隔离 / 手部渲染 / 输入契约），改手势时只动 `hand-interaction` 不耦合场景结构；`cross-platform-assembly` 独立尤其重要 —— 它是后续所有功能都要遵守的约束，埋在场景 spec 里新功能作者不容易找到
