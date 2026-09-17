# Probe Report: ite-tour-space-device

> Generated: 2026-09-10
> Summary: 8 questions asked · 9 decisions confirmed · 4 open assumptions
> 说明：本次 probe 在 artifacts 已生成之后补做，用于收敛。下列决策已回写 proposal / design / specs / tasks。

## Confirmed decisions

### Scope & intent

- **Question**: 现场用哪个空间场景？`IteRuntimeConfig.asset` 只有一份且 `sceneName: thirdDemo`，真机包里等于烧死。
- **AI recommendation**: 本 change 维持一份 config，`sceneName` 作为部署期配置项；"headset 里选空间场景"是独立产品功能，不进本 change（evidence: `Assets/Settings/ITE/IteRuntimeConfig.asset:18`；`IteRuntimeConfig.cs` 注释明确排除 `ItePropertiesUrl`，理由是"挑哪个场景属宿主 UX"）
- **User confirmation**: 认可。补充方向：后续会有**系统级配置菜单**，支持重新下载与切换场景——另开 change。

### Impact

- **Question**: 应用生命周期（摘下头显、灭屏休眠、切系统菜单）零处理，恢复后观测源状态两端不同。
- **AI recommendation**: 设备输入 rig 接 `OnApplicationPause` → `MarkerTrackingSession.Pause()/Resume()`（契约中"暂停期间不累计缺席时长"正是为此留的）（evidence: `grep OnApplicationPause/OnApplicationFocus` 在 `Assets/Scripts` 与包 `Runtime` 全空）
- **User confirmation**: 认可。
- **AI 自我纠正**: 最初断言"PICO 恢复必须重开相机会话"**未经验证**。归档 change 任务 7.4 实测：`Resume()` 后**无权限申请、无相机会话重建**。但那测的是应用内 Pause 按钮，非 Android 生命周期——故降级为待实测项，不写进 spec 当既定事实。

- **Question**: PICO 是否上报佩戴状态（原 design D7 的待决风险）。
- **AI recommendation**: 不必只靠实测——PICO 有原生通道（evidence: `PXR_Loader.cs:527-533` 处理 `XR_TYPE_EVENT_DATA_USER_PRESENCE_CHANGED_EXT` → `PXR_Plugin.System.UserPresenceChangedAction`；另有 `Pxr_GetPSensorState(ref bool isUserPresent)`，`PXR_Plugin.cs:2898`）。`HeadsetPresenceAdapter` 在 PICO 上改读这条。
- **User confirmation**: 随 recenter 一题一并认可。

### Design

- **Question**: recenter 会不会让内容与实物错开？
- **AI 自我纠正**: 最初说"recenter 挪的是 XR Origin"**是错的**。`XROrigin` 订阅 `trackingOriginUpdated`，处理器只有 `MoveOffsetHeight()`（evidence: `XROrigin.cs:363-367`）。真正变的是追踪空间原点：同一物理头位报出不同位姿，Unity 相机在世界里跳，钉在世界坐标的内容不动 → 与实物错开。净效果同，机制不同。
- **AI recommendation**: 语义统一成"世界原点变了就要求重扫"。Quest 走 `XRInputSubsystem.trackingOriginUpdated`，PICO 走 `PXR_Plugin.System.RecenterSuccess`（evidence: `PXR_Loader.cs:536-539`，由 `XR_TYPE_EVENT_KEY_EVENT` 触发，即长按 Home）；另在 Quest 侧显式 `OpenXRSettings.SetAllowRecentering(false)` 加固（evidence: `OpenXRSpaceSettings.cs:24`，文档明确该 API 只控制是否跟随，不触发 recenter 事件）。PICO 侧无对应开关（PXR 设置资产内无该字段）。
- **User confirmation**: 认可。

- **Question**: 同一帧多张码同时判稳时谁赢？
- **AI 证据**: 链路上无任何一处收敛到"一个"——`MarkerTrackingSession.tracked` 与 `MarkerStabilizer.tracked` 都是 `Dictionary`，迭代顺序不保证；`TourDirector.SubmitMarkerScan` 逐次处理，`normal` 类型下后到者会把先到者刚激活的 tour 停用销毁。现状是**未定义行为**，不是"顺序"。M5 实测两端多标记并发无相互干扰（evidence: 归档 design `## Measurements` M5），即两个都会稳、都会提交。
- **User confirmation**: 暂不做"取最近"的收敛判据（该逻辑很可能变），改为**按扫描优先顺序**；经追问明确为 **A：先判稳的赢**——同帧内后到的忽略，已激活期间其他码不抢，要抢得等当前码丢失。实现是桥接里一个"本帧已提交"标志位。

- **Question**: PICO 的误检要不要在本 change 里修？
- **AI 证据**: 归档 design M6（2026-09-04 PICO 4 Ultra 实测）：445 次检测中出现一次假标记 `tag 64 hamming=2 margin=3.8`，位姿解在相机前 22 cm，**照常派发给业务层**并在 1.010 s 后正常 Lost。margin 分布干净双峰：真检测 76.5–99.6（n=444, p50=91.1）vs 假检测 3.8。归档原话："一条 `margin >= 20` 的下限即可滤掉，代价为零"，当时按 D3 未加，记入 Open Questions。放到 ITE 上后果升级：假 ID 撞上某 tour 的 AprilTag ID = 激活错误 tour 并把内容重锚到人脸前 22 cm；且与"先判稳者赢"叠加会锁住真码约 1 秒。
- **AI recommendation**: 在 `PicoFiducialObservationSource` 内按 `margin` 下限过滤（阈值可调，起点 20），**不**给 `MarkerObservation` 加质量字段——契约层保持"只吐原始信息"（D3 不动），且 Quest 的 QR 没有 margin 概念，塞进公共载荷等于为一端污染契约。
- **User confirmation**: 收进本 change。

- **Question**: 稳定判定的 `stableFrameThreshold = 30` 在两端是同一件事吗？
- **AI 证据**: M1 实测派发速率 **PICO 5.6 Hz / Quest 70.0 Hz，差 12.5 倍**（PICO 是 4U 推送式相机出帧率，Quest 是 72fps 下每帧一次 `Poll()`）。`MarkerStabilizer` 数的是 `Feed()` 次数：Quest 上 30 次 ≈ 0.43 s，PICO 上 30 次 ≈ **5.4 s**——举着码不动五秒半，现场表现仍是"扫不到"。同一条链路上，`MarkerTrackingSession` 的丢失滞回是时间制（1.0 s，`deltaTime` 外部注入），稳定判定却是帧计数制。
- **AI recommendation**: 稳定判定改**时间制**（"连续稳定 X 秒"），两端仍各一套参数，起点 0.4–0.5 s。
- **User confirmation**: 认可。

### Success criteria

- **Question**: PICO 上导览全程开着 AprilTag 检测的代价要不要现在定策略？
- **AI 证据**: M3 实测单帧检测 1280×960：p50 **71.7 ms**、p95 79.6、max 124.4；且**命中 45–52 ms、未命中 68–79 ms**——无早退路径，"待机（无标记）是这条管线的最坏功耗工况"。该数字来自只渲染几个方块的探针场景，不能外推到带 glb + 富文本面板 + 透传的导览工况。
- **AI recommendation**: 本 change **先量后定**——真机验收加一条"扫描开/关"对比的帧率与温升测量；`sampleHz`（2–15，默认 6）与 `quadDecimate` 明确记为部署期可调项。不凭感觉先落降频策略。
- **User confirmation**: 认可。

- **Question**: 区域自动切换出来的 tour 不重锚，会带着累计漂移，要不要补偿或加验收门槛？
- **AI 证据**: 扫码本身就是漂移的自动校正（每次扫码把整套布局重新钉到那张码上）；但 `TourRegionPolicy` 决策出的 Activate 不扫码，用的是上一次锚定的坐标系。thirdDemo 全程约 10 m。
- **User confirmation**: **按现有 tour space 的逻辑即可，该逻辑已定好**——不改语义、不做补偿、不加验收门槛。

## Open assumptions [NEEDS CLARIFICATION]

- [ ] `[ASSUMED]` Quest 的 QR 印制格式当前仍是 `******{tourId}******`，真机测量（tasks 1.1）核实；解析器按"会变成一个地址"预留 — affects: `specs/ite-marker-identity`、design D8
- [ ] `[ASSUMED]` PICO 在 Android 生命周期恢复后是否需要重开 4U 相机会话未验证（归档 7.4 只验了应用内 `Pause()/Resume()`） — affects: design D17、tasks 生命周期项
- [ ] `[ASSUMED]` `OpenXRSettings.AllowRecentering` 的原生默认值未知（工程内无序列化该设置），需真机确认 Quest 侧默认是否跟随系统 recenter — affects: design D18
- [ ] `[ASSUMED]` `margin >= 20` 的下限取自单次实测的双峰间隔（n=445，真检测最低 76.5、假检测 3.8），阈值做成可调，长期需更多样本 — affects: `specs/unified-marker-tracking-contract`、design D21

## Suggested next step

- [ ] artifacts 已按本报告回写，直接跑 `/opsx:apply ite-tour-space-device` 开始实现；建议先跑 tasks 组 1（前置真机测量）
