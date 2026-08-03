# Probe Report: mr-core-scene-cross-platform

> Generated: 2026-07-30
> Summary: 11 questions asked · 13 decisions confirmed · 13 open assumptions

本报告记录「跨 Quest / PICO 的核心 MR 场景」在 propose 之前的对齐结果。
大量结论来自**源码级核验**（读 SDK 实现，非文档推断），核验依据逐条列出。

---

## Confirmed decisions

### Scope & intent

**Q1 / 交付形态：两个 APK 还是一个 APK 跑两端？**
- **AI recommendation**: 两个 APK（Quest 一个、Pico 一个）
  - 证据：`Assets/Settings/Build Profiles/Quest.asset` → `m_ScriptingDefines: [MRBASE_QUEST]`；
    `PICO.asset` → `[MRBASE_PICO]`；
    `docs/superpowers/specs/2026-07-27-qrcode-marker-localization-design.md:131`
    —「两套 Build Profile，各自 XR Plug-in Management 只启用对应 loader」
- **User confirmation**: 确认。两个 APK，一个 Pico 一个 Quest。
- **后果**：编译期 `#if` 方案成立；asmdef 可用 `defineConstraints` 排除另一端；
  「手法 D 构建期切 loader」保留为必需工作。

**Q6 / M1 是 passthrough 还是黑背景？**
- **AI recommendation**: M1 先关 passthrough（纯 VR 黑背景），理由是半透明手会与真手重影、
  passthrough 是平台层分叉会污染 M1 的验证目标
- **User confirmation**: **否决 AI 建议。** 项目本身是 MR，M1 必须带 passthrough。
- **后果**：M0 增加两端 passthrough 验证项；`IPassthroughController` 从「以后」提前到 M1 考虑；
  相机 clear / alpha 配置进入 M1 范围。

**Q7 / 半透明手给用户看什么用？**（四选一：①追踪可视化 ②轮廓/指尖 ③特效增强 ④纯遮挡代理）
- **AI recommendation**: ① 追踪可视化 —— 完整手网格 + 半透明，真手透出来，
  偏差可见反而是有用的调试信息。④ 需要 `XROcclusionSubsystem`，而 PICO 侧实现为 0（已核验）。
- **User confirmation**: 认 ①，「按 hand sample 的效果来就行」
- **落地**：`Assets/Samples/XR Hands/1.7.3/HandVisualizer/Prefabs/{Left,Right} Hand Tracking.prefab`
  的手网格 + 半透明材质（**不是** `Joint.prefab` 关节小球那种）

**Q8 / M1 要不要带手势？**
- **AI recommendation**: 带「第一类」（XRI 现成的 pinch / poke，只需配 binding），
  不带「第二类」（翻手 / 全握 / 双手合十，需新建 `MRBase.Interaction` 并真机调参）。
  理由：第一类成本近零，但它验证整套架构最关键的未验证假设 —— 双 binding 是否两端各自命中。
- **User confirmation**: 认，M1 就按这个范围。
- **M1 交付定义**：
  ```
  一个 MRCore.unity，passthrough 开着
    半透明手（sample 手网格）跟随真手，左右不反
    捏合能选中测试方块
    指尖能按测试按钮
  两个 APK：Quest / Pico，行为一致
  ```

### Impact

**Q9 / 核心 scene 建在哪个分支？**
- **AI recommendation**: 先把 `feature/qrcode-marker-localization` 的 30 个提交合进 master，
  再从 master 拉新分支。理由：核心 scene 是所有功能的地基，
  sacred-relic 和 marker 定位最终都要挂到它上面（M3）；地基不该埋在未合并分支里。
  - 证据：`git log --oneline master..HEAD | wc -l` = 30；
    `openspec status` 显示 sacred-relic-shell-awaken 19/19 完成
- **User confirmation**: 可以。
- **附带确认**：
  - 现有三个 demo scene（`LocalizationDemo.unity` / `SacredRelicAwakenDemo.unity` /
    `SampleScene.unity`）M1 全部保留不动，M3 迁移时再处理
  - `Assets/_Recovery/0.unity`（17MB，未跟踪，疑似 Unity 崩溃恢复残留）建议删除或加 `.gitignore`

**Q11-b / 依赖升级策略**
- **User instruction**: 本次开发把用到的插件都升到最新，有版本冲突就选配套版本
- **AI 应用该规则后的结果**（依据 PICO `Runtime/Unity.XR.PICO.asmdef` 的 versionDefines）：

  | 包 | 当前 | 动作 | 依据 |
  |---|---|---|---|
  | `com.unity.xr.hands` | 1.7.3 | → **1.8.1** | 最新稳定；PICO 声明 `>= 1.1.0` 无上限 |
  | `com.unity.xr.interaction.toolkit` | 3.4.1 | → **3.5.1** | 最新稳定；**不取 3.6.0-pre.1** |
  | `com.unity.xr.meta-openxr` | 2.5.0 | → **2.5.1** | 补丁版 |
  | `com.unity.xr.compositionlayers` | 2.4.0 | → **2.5.0** | 最新稳定 |
  | `com.unity.xr.openxr` | 1.16.1 | **停住** | 1.17.1 可用，但 PICO 只声明 `UNITY_OPENXR_1_16_0`；这是「选配套版本」 |
  | URP `17.4.0` / AR Foundation `6.4.2` | — | **不动** | 由 Unity 6000.4.4 绑定，不可自由升 |
  | Meta XR Core / MRUK | 205.0.0 | 查 `npm.developer.oculus.com` 后升 | 需实施时查最新 |
  | PICO Integration SDK | 未装 | 装 **3.4.0** | GitHub API 确认为最新（2026-03-25） |

- **升级带出的强制核验**：AI 核验过的是「PICO provider 签名 vs XR Hands **1.7.3**」。
  升到 1.8.1 后必须重新对 `GetHandLayout` / `TryUpdateHands` 签名 → 进 M0。

**受影响的代码模块**（读代码得出，未询问）
- 新增：`Assets/Scripts/Core/`（`MRContext` / `MRBootstrap` / 接口）、
  `Assets/Scripts/Platform/{Quest,Pico,Sim}/`、`Assets/Scripts/Editor/`（构建前处理）
- 重构：现有 `Assets/Scripts/Localization/Native/MRBase.Localization.Native.asmdef`
  硬引用 `meta.xr.mrutilitykit` + `Oculus.VR` 且 `defineConstraints: []`
  → 加 PICO 后会同时硬引用两家 SDK，任一包缺失即编译崩，需拆分并加 `defineConstraints`
- 复用：`Assets/Scripts/Common/StaticInstance.cs`（`MRContext` 直接继承，已带
  `BindInstanceForTesting`）；`MarkerStabilizer` 的迟滞/稳定帧数结构（手势判定层照抄）

### Design

**Q2 / SDK 选择：为什么不用各家自带的手部追踪？**
- **AI recommendation**: 走 Unity XR Hands 中立通道，不走 `OVRHand` / PICO hand prefab。三条理由：
  1. **XR Hands 在 PICO 上就是原生 API 的薄包装，零精度损失** —
     `PXR_HandSubsystem.cs:225` → `PXR_HandTracking.GetJointLocations(HandType.HandLeft, ...)`
  2. **XRI 整条交互链路建在 `XRHandSubsystem` 上** —
     `XR Origin Hands (XR Rig).prefab` 里 `XRHandTrackingEvents` → `NearFarInteractor` /
     `Poke Interactor` → `XRUIInputModule`。走各家 SDK = poke/抓取/UI 射线全部自己实现两遍
  3. 两条数据源会污染所有下游（渲染、手势、interactor、将来的遮挡/物理/同步各 ×2）
- **User confirmation**: 接受（提问确认后未反对）
- **例外条款**：厂商独占的手部能力（Meta `OVRHandTrackingWideMotionModeSample`、
  `MetaXRSimultaneousHandsAndControllersShim`；PICO `ControllerWithHand`）
  若需要，走 L2 平台层做**增强**，但不换基础数据源

**Q4 / 手模型 FBX 从哪来？**
- **AI recommendation**: 先用 Unity XR Hands sample 自带的手。理由：随 Unity 包分发、
  两端授权干净、`XRHandSkeletonDriver` 已接好、`TransparentRed.mat` 现成。
  不从厂商 SDK 拷 FBX —— `PXR_HandSubsystem.cs` 文件头写着
  `proprietary to PICO Technology Co., Ltd.`，跨平台分发有合规风险。
- **User confirmation**: 同意，先用 sample 自带的手。

**Q5 / PICO 端后端模式**
- **AI 发现（改结论级）**: PICO Integration SDK 3.4 是**双后端**的，两个 SDK 不互斥：
  ```
  Runtime/Scripts/Hand/PXR_HandSubsystem.cs:1        → #if !PICO_OPENXR_SDK
  Runtime/Scripts/OpenXRFeatures/Features/
    PassthroughFeature.cs:1                          → #if PICO_OPENXR_SDK
    PICO/PICOSpatialAnchor.cs · PICOSpatialMesh.cs   → 同上
  ```
  模式 1（默认）= PXR_Loader 后端，`PXR_HandSubsystem` 供 XR Hands；
  模式 2（装 PICO OpenXR SDK 后）= OpenXR 后端，企业能力经 OpenXRFeatures 继续可用。
  模式 2 曾被 AI 判断为潜在大收益（两端同为 OpenXRLoader，可能免去切 loader）。
- **User decision**: **不需要统一模式，按各自更新最频繁的来。**
  → Quest = OpenXR + Meta features；**Pico = Integration SDK 3.4 模式 1（PXR_Loader）**
  - 依据：Integration SDK 3.4.0 = 2026-03-25（活跃）；
    PICO OpenXR SDK 1.4.x 最后提交 2025-06-03（停滞 13 个月）
- **后果**：M0 去掉模式 2 的全部验证项；两端 XR 初始化路径不同；
  「手法 D 构建期切 loader」为**必需**而非可选

**架构主体**（对话中确立，未逐条询问）
- 四层：L1 通用运行时（XR Hands / XRI / Input System / URP / Composition Layers）→
  L2 平台适配（三个 asmdef：Quest / Pico / Sim）→ L3 能力抽象 + 交互 → L4 业务
- Scene 划分：**1 个常驻 `MRCore.unity` + N 个 additive 业务 scene**
  （不是 2 个平台 scene。理由：跨 scene 反向引用问题；且 Editor 模拟器会需要第三个 scene）
- 平台差异四手法：A 输入多 binding / B prefab 变体或运行时挂载 /
  C provider 接口 + `#if` 工厂 / D 构建期配置切换
- 两条正交 define 轴：`MRBASE_QUEST|PICO`（构建意图，Build Profile 给）
  × `MRBASE_HAS_*`（包是否存在，asmdef `versionDefines` 给，项目已有 `MRBASE_HAS_MRUK` 先例）
- **禁令**：`#if MRBASE_*` 只允许出现在 `MRBootstrap` 和 `MRBase.Platform.*` 内部；
  业务层出现任何 `OVR` / `PXR` 符号即为架构违规（可做 CI grep 检查）

**关键技术约束**（源码核验，必须写进 design 防止后人踩坑）
1. **不要依赖 `XRCommonHandGestures` / `<XRHandDevice>/pinchValue` 作为中立捏合通道。**
   `XRHandSubsystemProvider.cs:131` `canSurfaceCommonPoseData => false`，PICO 未 override
   （连 `TryGetPinchValue` / `TryGetAimPose` 也没 override）→ 该通道在 Pico 上是空的。
   **真正的中立点是 InputAction 挂两条 binding**：
   `<MetaAimHand>{LeftHand}/indexPressed` + `<PicoAimHand>{LeftHand}/indexPressed`
2. **运行时手部网格是 Meta 独占。** `detectedHandMeshLayout => XRDetectedHandMeshLayout.OpenXRMetaQuest`
   默认值，PICO 未实现 `TryGetMeshData` → 手模型必须自备（已决策 Q4）
3. **AR Foundation 不是跨端方案，只有四项交集。**
   | Subsystem | Meta (meta-openxr 2.5) | PICO Integration 3.4 |
   |---|---|---|
   | Session / Camera / Anchor / Raycast | ✅ | ✅ |
   | Plane | ✅ `MetaOpenXRPlaneSubsystem` | ❌ 仅 `SensePack/PXR_PlaneDetectionManager`（自有 API） |
   | Occlusion / BoundingBox | ✅ | ❌ 无 |
   | Mesh | ✅ | ⚠️ 接法不同 |
   | ImageTracking | ❌ | ❌ |
4. **XR Composition Layers 是罕见的「厂商差异被官方抽象干净」的一处。**
   `com.unity.xr.compositionlayers` 依赖里没有 openxr，是 provider-based；
   PICO 侧 `PXR_CustomLayerHandler.cs` 实现了它（且含 `#if UNITY_VIDEO`）。
   Layer 类型两端几乎逐个对齐（Quad / Cylinder / Cube / Equirect / Default）。
   → UI 和视频若要走 composition layer 提画质，**是通用的**
5. **空间音频必须用 Unity 内置 spatializer。** Spatializer Plugin 是 ProjectSettings 全局单选，
   一旦用厂商插件（Meta XR Audio / PICO `SpatialAudio/Pico.Spatializer.asmdef`）
   就是构建期全局差异，且 Editor 里看不出来、只在真机上错

### Success criteria

**Q10 / M1 验收清单**
- **AI recommendation + User confirmation**（用户认可并追加模拟器一项）：

  ```
  0. Editor 里按 Play，XR Interaction Simulator 能出手、能捏合   ← 日常开发门槛
  1. 戴上头显 → 能看到真实房间（passthrough 开着）
  2. 举起双手 → 半透明手出现，跟随真手，左右手不反
  3. 手移出视野再移回 → 能重新出现（不卡死、不残留）
  4. 捏合 → 选中测试方块，松开 → 放下
  5. 指尖点 → 按下测试按钮，有视觉反馈
  6. 重装 APK / 重启应用 → 行为一致（排除偶然跑通）
  7. 双 binding 在两端各自命中                                  ← 必须真机，模拟器不算
  性能：两端稳定 72fps（Quest 3 / PICO 4 系列共同默认刷新率），记录为基线
  ```
- 第 7 项是 M1 真正的价值：它验证整套「一份 rig 两端通吃」的关键假设。
  过了则架构成立；不过则方案要重做，越早暴露越好。

**Q11 / 防止「改一端弄坏另一端」的三道防线**
- **AI recommendation + User confirmation**: 三道全要
  1. **锁版本 + 两端编译回归**：本次统一升级到最新配套版本后锁定；
     升级当作独立的、需双端验证的改动，不夹在功能提交里。
     加本地脚本或 CI：两个 Build Profile 都能成功出包（纯编译级，无需设备，
     能抓 asmdef 引用断裂 / define 写错 / 包缺失 —— 双端项目最常见的坏法）
  2. **手势判定层写 EditMode 测试**：`MRBase.Interaction` 是纯关节数学，可喂假数据测；
     照抄 `MarkerStabilizerTests` / `MockMarkerProvider` 的套路
  3. **真机回归清单**：Q10 清单每个里程碑在两台设备各跑一遍，人工

**调试链路三层**（回答用户的 simulator 提问）
- ① `XR Interaction Simulator`（XRI 3.x 自带，项目已有 `XRDeviceSimulatorSettings.asset`）
  —— 键鼠模拟头 + 双手 + 预设手势。
  **白送的收益**：`XRDeviceSimulatorHandsSubsystem.cs` 是一个 `XRHandSubsystem` provider，
  与 Meta / PICO 的 provider 平级 → 因为架构走 XR Hands，模拟器零成本可用。
  走各家 SDK 就用不了（Meta 要另装 Meta XR Simulator，PICO 只能串流）。
  M1 建议把 `m_AutomaticallyInstantiateSimulatorPrefab` 打开
- ② PC 串流（Meta Quest Link / PICO Developer Center streaming）—— 真手部追踪，不打包
- ③ 打包真机 —— passthrough 观感、真实性能、追踪质量差异只能这样验
- **模拟器验不了**：passthrough（可用 `XRSimulationPreferences.m_FallbackEnvironmentPrefab`
  的虚拟房间当替身）、两端追踪质量差异、**双 binding 命中**（模拟器造的是第三种设备）

---

## Open assumptions [NEEDS CLARIFICATION]

以下是未经确认或被明确延后的判断，会显式带进 artifacts：

- [ ] `[DEFERRED]` **手部追踪丢失的降级路径**未定（是否保留控制器通道、是否做 `trackingLost` 视觉提示）。
  用户明确「后面再考虑，但手势一定会有」。
  影响：proposal 非目标 / design 的 rig 结构（建议留控制器挂点但 M1 不实现）
- [ ] `[ASSUMED]` **MR 下半透明手的产品化形态**未定。M1 取「①追踪可视化」，
  但②轮廓/③特效/④遮挡代理的最终取舍留到真机上看。影响：design 的美术与 shader 章节
- [ ] `[ASSUMED]` **PICO Integration SDK 3.4.0 + Unity 6000.4.4 兼容性未实测**。
  `package.json` 只写 `"unity": "2021.3"`（下限）；PICO 官网仍有陈旧的「Unity 2020-23，
  Unity 6 支持中」表述；但 3.4 代码里已有 Unity 6 + URP + GLES + Multipass 的 MSAA 检查。
  影响：M0 头号闸门，不过则整个 Pico 路径要改
- [ ] `[ASSUMED]` **XR Hands 1.8.1 下 PICO provider 的签名兼容性未核验**。
  AI 核验过的是 1.7.3（`GetHandLayout` / `TryUpdateHands` 逐字匹配）。影响：M0
- [ ] `[ASSUMED]` **双 binding 在真机两端各自命中未实测**。
  推断依据是 PICO 官方绑定表 + `PXR_HandSubsystem.cs:410-428` 的 `PicoAimHand` 控件定义
  （`indexPressed` / `aimFlags` / `pinchStrengthIndex`），逻辑成立但无人真机验过。
  影响：整套「一份 rig 两端通吃」方案的成立与否
- [ ] `[ASSUMED]` **Pico 模式 1 下 passthrough 如何开启未查清**。
  `PassthroughFeature.cs` 是 `#if PICO_OPENXR_SDK`，模式 1 下不编译；
  模式 1 走的应是 PXR 的 seethrough（`Utils/PXR_VstModelPosCheck.cs` 那套）。影响：M0 + M1
- [ ] `[ASSUMED]` **两端 passthrough 下相机 clear / alpha 的具体配置未定**。影响：M1 实施细节
- [ ] `[ASSUMED]` **PICO「捏合射线打不到 UI」的已知问题是否命中目标设备未验**
  （官方标注 5.13.0）。影响：验收项 5 / UI 设计（关键操作不要只放远场射线）
- [ ] `[ASSUMED]` **目标设备型号清单未确认**。PICO 4 是 XR2 Gen 1，PICO 4 Ultra 与
  Quest 3 是 Gen 2 —— 影响性能基线与视频规格上限（规格须按最低设备定）
- [ ] `[ASSUMED]` **Meta XR Core / MRUK 的最新版本未查**（当前 205.0.0，
  需上 `npm.developer.oculus.com` 确认）。影响：升级任务
- [ ] `[ASSUMED]` **`IPassthroughController` 是否 M1 就抽象成接口，还是 M1 直接 `#if`**。
  倾向：M1 直接 `#if` 在 `MRBootstrap` 里，M2 再抽接口（避免过早抽象）。影响：design
- [ ] `[ASSUMED]` **手模型 FBX 产品化阶段的来源**（自制 / 采购）未定。M1 用 sample 自带。
- [ ] `[ASSUMED]` **分发渠道与企业授权**未讨论。PICO ArUco 需 LBE 模式 + 企业授权，
  这会影响 M3 的 marker 接入（design 文档记「已确认拿到 PICO 企业开发授权」，
  但商店分发 vs 现场侧载的差异未确认）。本 change 不涉及，但需在 proposal 的依赖里标注

---

## Suggested next step

- [ ] 运行 `/opsx:propose mr-core-scene-cross-platform` 生成 artifacts（会读取本报告）
- [ ] 生成的 tasks.md 应把 M0 闸门排在最前，每项标注「不过则修订 design 的哪条假设」：
  ```
  M0-1  合并 feature/qrcode-marker-localization 到 master，拉新分支
  M0-2  依赖升级到上表的配套版本，两端编译通过
  M0-3  装 PICO Integration SDK 3.4.0，Unity 6000.4.4 编译通过     ← 头号闸门
  M0-4  重新核验 PICO provider 签名 vs XR Hands 1.8.1
  M0-5  Editor 里 XR Interaction Simulator 能出手、能捏合
  M0-6  Pico 真机跑 HandsDemoScene，手出来吗
  M0-7  查清 Pico 模式 1 下 passthrough 的开启方式
  M0-8  双 binding 在两端各自命中                                  ← 决定架构成立与否
  M0-9  Pico 端世界空间 Canvas 远场射线 UI 能点吗（验已知问题）
  M0-10 两端 72fps 基线记录
  ```
