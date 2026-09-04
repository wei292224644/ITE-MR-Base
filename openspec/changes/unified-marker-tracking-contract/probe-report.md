# Probe Report: unified-marker-tracking-contract

> Generated: 2026-09-04
> Summary: 12 questions asked · 12 decisions confirmed · 7 open assumptions

本次 probe 发生在 change 已有完整 artifacts 之后，用途是**校正范围**：原设计要在契约层抹平两端身份差异（三层 `MarkerIdentity` + `LogicalId` + 生产级 `IMarkerIdParser`），probe 过程中被否决。现在的范围是：hook 吐最原始信息，只统一**节奏 / 丢失滞回 / 暂停**三件事，并新建一个专测 hook 行为的场景。

**被本次 probe 推翻的原有决策：D3（身份分层）、D4（parser 提升到生产）、D7（稳定阈值改秒）。**

---

## Confirmed decisions

### Scope & intent

- **Question**: hook 要不要在契约层抹平 Quest / PICO 的身份差异？
  - **AI recommendation**: 原设计主张抹平（三层 `MarkerIdentity`，业务层只认 `LogicalId`），依据是 `AnchorRegistry.cs:18` 的 `QuestPayload == rawId || PicoMarkerId.ToString() == rawId` 双条件兜底属于"把一个决策藏在两个处理器的相互作用里"。
  - **User confirmation**: **否决。** 「现在 Marker 的行为不牵扯到解析和抹平 Quest 与 Pico 的差异，hook 返回最原始的信息即可。」解析与映射归业务层。

- **Question**: 本 change 的验收面是什么？
  - **User confirmation**: 新建一个专门的扫描测试场景，主要测 hook 行为是否正确——扫到内容时在扫描位置放一个 box，并显示扫描出来的内容。不需要复杂逻辑。

- **Question**: ITE 导览包怎么处理？
  - **AI recommendation**: 机械迁移到新契约，保证继续编译、行为不变。
  - **User confirmation**: **不采纳迁移。**「现在所有的行为都不考虑 ITE，这是为了降低耦合度。如果报错了，则先注释掉关于 ITE 的行为，不要写中间层来解决错误。」

### Impact

- **Question**: 换掉 `IMarkerTrackingProvider` 后，现有消费方怎么办？
  - **AI recommendation**: (a) 接口整体替换 + 消费方机械迁移；不推荐 (b) 新旧两套契约并存——两套契约同时活着，分叉会重新长出来。（evidence: `MarkerAnchorService.cs:10,27`、`IteHost/MarkerSourceAdapter.cs:23,41-43`、`MarkerTrackingBootstrapper.cs:15,29`、`MockMarkerProvider.cs:4` 全部持有旧接口，接口一换即编译失败）
  - **User confirmation**: 选 (a)。

- **Question**: `MarkerAnchorService` 那条「标记 → 内容」链路是迁移还是下线？
  - **AI recommendation**: 我倾向 (a) 机械迁移保住一条可编译的完整链路。
  - **User confirmation**: **选 (b) 下线**，并可删除相关旧测试。理由链：这条链没有任何真机验收面——`anchor_registry.json` 在仓库里不存在（`MarkerTrackingBootstrapper.cs:6` 默认引用它，无 `StreamingAssets/`，真机上 registry 恒为空）；唯一驱动它的 `LocalizationDemo.unity` 是 mock + 手点按钮（`DemoMarkerTrigger.cs:24-31` 手动喂 30 次假位姿凑 `Stabilized`）；而它做的正是被划出范围的身份解析。

- **Question**: 确认删除清单。
  - **AI recommendation**: 如下三类分开处理。
  - **User confirmation**: 全部认可。

  **删源码**：`IMarkerTrackingProvider.cs`、`PicoMarkerProvider.cs`、`QuestMarkerProvider.cs`、`MockMarkerProvider.cs`、`MarkerAnchorService.cs`、`AnchorRegistry.cs`、`AnchorEntity.cs`、`AnchorEntityData.cs`、`IAnchorDataSource.cs`、`LocalJsonAnchorDataSource.cs`、`IContentLoader.cs`、`ImageContentLoader.cs`、`DemoMarkerTrigger.cs`、`MarkerTrackingBootstrapper.cs`

  **删场景**：`Assets/Scenes/LocalizationDemo.unity`

  **删测试**：`AnchorEntityTests.cs`、`AnchorRegistryTests.cs`、`LocalJsonAnchorDataSourceTests.cs`、`MarkerAnchorServiceTests.cs`、`MockMarkerProviderTests.cs`、`Ite/MarkerSourceAdapterTests.cs`

  **注释不删**：`Scripts/IteHost/MarkerSourceAdapter.cs` 整个文件、`IteHostBootstrap.cs:42,69`

  **保留不动**：`MarkerStabilizer.cs` + `MarkerStabilizerTests.cs`（下线后暂时零消费方，但自洽且有测试，内容链路回来时要用）、`AprilTagDetectorCore.cs` / `PlanarPoseSolver.cs` / `PicoEnterpriseCameraPose.cs` / `PoseMath.cs` / `PlatformOffsetConfig.cs` 及各自测试、整个 `Localization/Probe/` 与 `Localization/Native/Probe/`

### Design

- **Question**: 事件载荷的形状？
  - **AI recommendation**: 初版建议 `Platform` + `RawPayload`(string, Quest) + `NativeId`(int, PICO) 两字段并存，理由是 PICO 的 ID 本来就是 int，合成 string 等于又抹一次差异。
  - **User confirmation**: **修正为单字段。**「PICO 也返回 `RawPayload`，将 int 转换成 string。」最终形状：

    ```csharp
    public enum MarkerPlatform { Quest, Pico }

    public readonly struct MarkerObservation
    {
        public readonly MarkerPlatform Platform;
        public readonly string RawPayload;   // Quest: QR 原文；PICO: iMarkerId.ToString()
        public readonly Pose Pose;           // 世界系
    }
    ```

    `NativeId` 不存在，`RawPayload` 一律非空。`MarkerLost(MarkerPlatform, string rawPayload)` 同构。消费方要 int 自己 `int.Parse`。

- **Question**: 测试场景的盒子走不走 `MarkerStabilizer`？
  - **AI recommendation**: 不走，直接跟每次 `MarkerObserved` 更新。（evidence: `MarkerAnchorService.cs:44` 把 `MarkerResolved` 喂进 `stabilizer.Feed`，`MarkerStabilizer.cs:60` 要连续 30 帧才发 `Stabilized`——Quest 上 `Feed` 只被调一次，故障从外部看就是"什么都没发生"，分不清 hook 没发还是稳定器没判稳）
  - **User confirmation**: 「1 跟着走。」

- **Question**: `MarkerLost` 之后再看到同一标记，算新一次 `MarkerObserved` 吗？
  - **AI recommendation**: 算。
  - **User confirmation**: 「2 算。」

- **Question**: PICO 的观测源怎么来？
  - **AI recommendation**: 把 `PicoQrCameraProbe` 的相机会话与检测循环（`:148 OnServiceBound` / `:186 StartCameraStream` / `:243 OnImageAvailable` / `:259 Update` 采样节流 / `:314 DispatchDetection` / `:365 DrainResults`）抽成 `PicoFiducialObservationSource`；探针改成消费它、只留自己的遥测与 `markerBox`；删掉 `PicoMarkerProvider.cs`。（evidence: `PicoMarkerProvider.cs:79` 走 TOB `SetMarkerInfoCallback`，该路线已被真机证伪——`SFS_TRACKING_ENABLE_DYNAMIC_MARKER` 读回 0，开关不开就不产生任何回调；提交 5286d01 标题即"用 AprilTag 相机流替代被证伪的原生 ArUco"）
  - **User confirmation**: 认可。接受"要重测 PICO 探针"的代价。

- **Question**: Quest 侧的 MRUK 运行时对象怎么装？
  - **AI recommendation**: 把 `Native/Probe/QuestMarkerProbeRuntimeBootstrap.cs` 移到 `Native/QuestMrukRuntimeInstaller.cs`，去掉 `internal`，触发条件从"找到 `MarkerProbeEntry`"放宽为"`MarkerProbeEntry` 或 `MarkerHookTestRig`"；删掉 `QuestMarkerProvider.cs`。（evidence: 该文件 `:6-8` 注释写明"共享场景里故意不放任何 Meta 预制体，PICO 构建才不会创建这些对象"，`:36-42` 运行时装 `OVRCameraRig`/`OVRManager`/`MRUK`，`:37` 先 `SetActive(false)` 再配、`:43` 才激活——顺序有讲究，复制会漂）
  - **User confirmation**: 认可。接受"Quest 探针要重跑确认没退化"的代价。

- **Question**: 派发频率两端压到同一个 Hz 吗？
  - **AI recommendation**: 不做人为节流；业务层要的语义是"可见期间持续拿到最新位姿"，不是"两端每秒恰好 N 次"。把 Quest 从 72–90 Hz 压到 PICO 的 6 Hz，等于为对齐而把好的一端降级。（evidence: `PicoQrCameraProbe.cs:30` `sampleHz` 默认 6、`[Range(2,15)]`；Quest 的 `MRUKTrackable.transform` 每帧都新）
  - **User confirmation**: 「先不限流。」`maxDispatchHz` 旋钮也先不做。

- **Question**: `Pause()` / `Resume()` 还在范围内吗？真机怎么触发？
  - **AI recommendation**: 保留；`Pause()` 只停检测与派发、不释放 4U 相机会话，`Close()` 才释放。真机上在 HUD 面板加一个按钮，**不**走 `MarkerProbeXrControls.cs`——它 `:33-38` 依赖 Diagnostics 场景里叫 `RayTargetButton` 的模板对象，找不到就禁用自己，是"碰巧能跑"的耦合。（evidence: 现状唯一的"停"是 `PicoMarkerProvider.cs:69` 的 `StopTracking()`，它 `UnBindEnterpriseService()` 解绑整个企业服务）
  - **User confirmation**: 「保留 `Pause()`/`Resume()` + HUD 上一个按钮。」

- **Question**: `activeAnchorIds` 是不是 Lost 机制，要不要保留？
  - **AI correction**: 用户认为 `activeAnchorIds` 是"PICO 内部维护的 lost 机制"。**这不成立**，是两个不同的集合：`MarkerAnchorService.cs:13,52,70` 的 `activeAnchorIds` 平台无关，作用是内容创建去重（`HandleStabilized` 里 `Contains` 就 `return`），它从不参与 Lost；真正的 Lost 机制是 `PicoMarkerProvider.cs:115,133-142` 的 `visibleIds`/`currentIds` 差集，PICO 专属且无滞回。
  - **User confirmation**: 用户的实质判断成立并已纳入设计——「谁现在还活着」这份状态必须存在，否则 Lost 发不出来；而模型消不消失是订阅方的事，不是机制的事。新设计把这份状态从 `PicoMarkerProvider` 上移到 `MarkerTrackingSession`：两端共用一份、带 1.0 s 滞回，`PicoObservationSource` 只吐当帧快照不再自算差集。Lost 机制从"PICO 专属 + 无滞回"升级为"两端统一 + 有滞回"。`activeAnchorIds` 随下线一并消失。

### 新测试场景

- **Question**: 做一个场景还是两个？入口怎么加？
  - **AI recommendation**: 一个场景两端共用。（evidence: `BuildScript.cs:89` 与 `:109` 里 Quest 与 PICO 的 Marker Probe 构建指向的都是同一个 `k_MarkerProbeScene`）
  - **User confirmation**: 认可，`applicationIdSuffix` 用 `.markerhook`。

  确定形状：
  - `Assets/Scenes/MarkerHookTest.unity`，两端共用；
  - 场景里挂 `MarkerHookTestRig`，`Awake` 按 `#if MRBASE_QUEST / MRBASE_PICO` 建对应 source，订 `MarkerObserved` / `MarkerLost`；
  - 走 `ProbeScenes(k_MarkerHookTestScene)`（`MRCore` + 测试场景，`BuildScript.cs:45`）；
  - 两个菜单项 `MRBase/Build/Marker Hook Test/Quest Development` 与 `/PICO Development`，PICO 那条带 `excludePluginRoot: k_MetaPackageRoot`（照 `BuildScript.cs:114-125`）。

- **Question**: 扫描内容怎么显示？
  - **AI recommendation**: 盒子 + 世界空间 TMP 标签（两行：平台 / payload，billboard 朝相机）+ 每事件一条 `[MarkerHook]` 前缀 Console 日志 + 常驻 HUD 列活跃标记与 `Observed`/`Lost` 累计次数。（evidence: TMP 全项目在用，`MRBootstrap.cs`、`DiagnosticsHud.cs` 有现成写法；盒子在不在只能回答"现在有没有"，回答不了"刚才发了几次"，而滞回是唯一靠时间判定的东西）
  - **User confirmation**: 认可，HUD 加上。

### Success criteria

- **AI recommendation（未逐条确认，按上述决策推导）**:
  - **EditMode**：`MarkerTrackingSession` 的节流、滞回（<1 s 不丢 / >1 s 丢且只丢一次 / 平台移除信号走同一判定 / 丢失后重现重置计时）、`Pause` 期间不派发且不累计缺席时长——全部靠注入 `deltaTime`，不依赖真机。
  - **PICO 真机**：`MarkerHookTest` 场景扫 ID 0 / 250，盒子出现在标记上并跟随；遮挡 <1 s 盒子不消失、>1 s 消失且日志只记一次 Lost；HUD 上 Pause 后 CPU 下降、Resume 后无权限申请、无相机会话重建。
  - **Quest 真机**：扫 QR，盒子跟随 Transform 持续更新（而非停在首次发现那一帧）——这是 D5 有效的唯一判据；TMP 标签显示 QR 原文。
  - **回归**：PICO 探针与 Quest 探针各重跑一次，确认抽取与移动没有造成退化。

---

## Open assumptions [NEEDS CLARIFICATION]

- [ ] `[ASSUMED]` 上述 Success criteria 未逐条与用户确认，是按已确认决策推导的。— affects: tasks 的真机验收段
- [ ] `[ASSUMED]` `MarkerHookTest` 场景与 `PicoQrCameraProbe` 场景**不能同时运行**：4U 相机会话不共享（第二方拿不到 `camOpenned`，`StartGetImageDatafor4U` 持续 `result=-1`，无错误提示）。当前靠场景分离规避，但 `MRSceneDirector` 能在运行时切场景，切换时是否会撞上未验证。— affects: design Non-Goals, 真机验收步骤
- [ ] `[ASSUMED]` `Pause()` 在 PICO 上"停检测但保留 4U 相机会话"可行——即停止 `StartGetImageDatafor4U` 的消费或跳过检测，不调 `UnBindEnterpriseService()`。具体停在哪一层（不取帧 / 取帧不检测）未定，性能收益也未测。— affects: design D8, tasks 4.5
- [ ] `[ASSUMED]` `PicoQrCameraProbe` 的相机管线抽取后，探针只保留遥测与 `markerBox`，其余行为等价。抽取边界（`latestFramePose` 的持有方、`cameraBufferHandle` 的 `GCHandle` 生命周期归谁）未细化。— affects: tasks 的 PICO 抽取段
- [ ] `[ASSUMED]` Quest 的 `MRUKTrackable.IsTracked` 为 false 时应当计入"缺席"而进入滞回，而不是当作已移除立即丢。未在真机上确认该字段的实际行为。— affects: spec 的丢失判定, `QuestObservationSource`
- [ ] `[ASSUMED]` HUD 的世界空间 Canvas 在两端都能正常渲染与交互（Pause 按钮需要射线可点）。PICO 侧的 XR 射线交互未验证。— affects: 新场景装配
- [ ] `[ASSUMED]` AprilTag 路线的端到端延迟、检测率、CPU 占用、多标记并发这四项仍无实测数字，本 change 接通后才第一次落在生产路径上。— affects: tasks 0.5, design Risks

---

## 现有 artifacts 需要的修改

probe 推翻了原设计的三条决策，`/opsx:propose` 时需据此重写：

| artifact | 位置 | 动作 |
|---|---|---|
| `design.md` | D3（身份分层） | 删除，替换为"载荷只带平台 + 原始 payload，不做身份收敛" |
| `design.md` | D4（`IMarkerIdParser` 提升到生产） | 删除，parser 留在探针 |
| `design.md` | D7（稳定阈值改秒） | 删除，`MarkerStabilizer` 本次不动 |
| `design.md` | Non-Goals / Risks / Open Questions | 按新范围重写；补新场景与删除清单 |
| `spec.md` | Requirement「标记身份分层且显式」 | 整条替换为"载荷保留平台与原始 payload" |
| `spec.md` | Requirement「统一派发节奏」 | 措辞改为"可见期间每个 `Tick` 派发最新位姿"，去掉单一频率的要求 |
| `spec.md` | Requirement「稳定判定以时长表达」 | 删除 |
| `spec.md` | — | 新增 Requirement：测试场景对 hook 行为的可观察性 |
| `tasks.md` | 0.2 / 0.3 | 删除（随 D7 与限流决策撤销） |
| `tasks.md` | 1.1 / 1.6 / 2.8 / 2.9 / 3.1 / 3.2 / 5.3 | 删除（身份收敛与稳定器换单位） |
| `tasks.md` | 5.1 / 5.2 / 5.5 / 5.6 | 替换为删除清单与 ITE 注释 |
| `tasks.md` | — | 新增：新场景、`MarkerHookTestRig`、HUD、构建入口、PICO 抽取、Quest 安装器移动 |

## Suggested next step

- [ ] 运行 `/opsx:propose unified-marker-tracking-contract` 重生成 artifacts（它会读本报告，并把上表的修改与 open assumptions 带进去）
