## Context

Quest 与 PICO 的原生 Marker 能力不对称：

- Quest 的 MRUK QR Trackable 同时提供 QR 原文和 QR 的 6DOF Pose。
- PICO 的 `ScanQRCode` 会启动独立系统扫码体验并影响当前操作流程；它不是当前相机流。`SetMarkerInfoCallback` 则持续返回 ArUco 整数 ID 与 Pose。本 change 不调用 `ScanQRCode`，而由外部 MarkerRegistry 建立 ArUco ID、QR ID 和业务对象的关系。

当前仓库已有 `QuestMarkerProvider`、`PicoMarkerProvider` 和 `IMarkerTrackingProvider`，但本 change 不应在平台行为未经真机确认前把它们扩展成完整生产架构。本次只建立可移除的诊断探针，用原生 API、版本化夹具和持久化日志回答“扫描架构是否可行”。

PICO 官方夹具已逐位确认使用 OpenCV `DICT_4X4_1000`：静态 ID 0 的内部码字为 `0xB532`，动态 ID 250 为 `0x7E80`。仓库已生成对应 QR+ArUco A3 横版单页，并保留双 A4 备用版。

## Goals / Non-Goals

**Goals:**

- 在 Quest 真机上取得 QR RawPayload、MarkerID、有效 6DOF Pose 和真实回调节奏。
- 在 PICO 真机上完成连续 ArUco 识别，并通过版本化外部 MarkerRegistry 命中静态 ID 0 和动态 ID 250。
- 持久化足够详细的 JSONL，使测试结束后仍可重放时序、分析错误并比较 Pose。
- 让测试期间可通过 Unity 日志实时监控，同时保留设备文件作为完整证据。
- 使用固定、可测量的 A3/A4 夹具，记录打印与安装条件，避免把物理误差误判为追踪误差。
- 形成明确的通过/不通过/受限可行结论，为后续一次性触发、重复扫描定位和生产 Provider 设计提供输入。

**Non-Goals:**

- 不实现生产级 `Tracked → Tracking → Lost` 生命周期、宽限期、自动恢复或会话管理。
- 不实现多目标生产语义、重复 ID 策略、容量管理或业务固定上限。
- 不迁移 `MarkerAnchorService`，不创建业务对象，不决定扫描后执行什么业务。
- 不设计一次性触发和重复扫描辅助定位的最终 API；它们是后续 change。
- 不实现相机帧访问、OpenCV 运行时识别、自定义视觉算法或软件降级。
- 不调用 PICO 系统 QR 扫描，不把系统扫码界面纳入本 change；若未来需要运行时读取 QR，另立相机帧能力 change。
- 不以本探针的临时控制入口和日志模型作为最终公共 API。

## Decisions

### D1. 采用独立诊断探针，不先重构生产 Provider

实现一个仅在开发/诊断场景启用的 Probe Runner，组合四个边界：

1. Quest 原生观察适配器；
2. PICO 原生 Marker 回调适配器与外部 MarkerRegistry 适配器；
3. 单一 JSONL 记录器；
4. 最小手动控制与状态显示入口。

探针可以复用现有 Provider 中已经验证无副作用的原生调用方式，但不修改现有业务事件契约，也不要求下游迁移。这样能先收集平台事实；如果架构不可行，删除探针即可，不留下半套生产抽象。

替代方案是直接实现统一 Marker Tracking 服务。该方案会把尚未确认的并发、回调节奏、Pose 轴向和权限行为固化成 API，因此不采用。

**但「隔离」不等于「可以和现有 Provider 同时活着」。** PICO 的 Marker 回调是**单槽注册**：

```csharp
// PXR_EnterprisePlugin.cs:1373
value = tobHelper.Call<int>("setMarkerInfoCallback", new MarkerInfoCallback(...));
```

`set` 语义，一个回调引用，第二次注册覆盖第一次，且 TOB 没有反注册 API（现有 `PicoMarkerProvider` 的注释已记录这一点）。此外 `PicoMarkerProvider.StopTracking()` 会调用 `UnBindEnterpriseService()` 解绑**整个**企业服务。

因此如果 `MarkerTrackingBootstrapper` 与 Probe 在同一次运行里都活着，谁后注册谁赢——静默地，没有任何返回值或日志能说明另一方已失效；而任一方停止都会把对方的企业服务连带解绑。

约束写死在入口而不是留给「验证」：

- Probe 场景 MUST NOT 挂载 `MarkerTrackingBootstrapper`，或 Probe 启动前必须确认生产 Provider 处于未启动状态；
- Probe 自己负责 `InitEnterpriseService` + `BindEnterpriseService`，并在会话头记录二者结果；
- Probe 停止时**不**调用 `UnBindEnterpriseService()`，只用会话代次吞掉后续回调——探针没有全局企业服务的所有权；
- 启动时若检测到生产 Provider 已在运行，Probe 拒绝启动并给出明确原因，不静默抢注册。

本 change 不建立共享企业服务门面。那属于后续 production change；探针只需要「同一时刻只有一个注册方」这一条纪律。

### D2. PICO ArUco ID、QR ID 与业务身份由外部 Registry 映射

本次夹具仍使用 QR 原文 `"0"` 和 `"250"`，但 PICO 不读取这些 QR。PICO SDK 返回的整数必须记录为 `PicoArUcoId`；外部 Registry 负责把它映射到 `QrId`、`LogicalMarkerId` 和业务对象。Quest 可独立从 QR Trackable 取得 QR 原文，再通过同一 Registry 解析业务身份。

Registry 必须版本化、可审计并记录文件哈希；PICO 运行时只做整数 `PicoArUcoId` 查表，不从 QR 原文推导 ID。QR URL、JSON 或其他格式的解析只属于 Registry/Quest 侧配置流程，不进入 PICO 采集链路。

### D3. Quest 流程只观察原生 QR Trackable

Quest 测试流程：

1. 记录系统、应用、MRUK/Meta XR 版本及权限状态；
2. 启动 QR Trackable 观察；
3. 对 Added/Updated/Removed 或等价原生状态逐条记录 RawPayload、解析结果、Trackable 状态和当前 Transform；
4. 记录每次样本的 Unity World Pose、线程、原生时间（若有）、单调时间和相邻回调间隔；
5. 每轮测试由操作者明确开始和结束，不由探针推导生产 Lost 语义。

为直接观察 Pose 是否可用于空间锚定，Quest Probe 额外提供可移除的诊断可视化：每个有效 MRUK QR Trackable 按实例身份拥有独立盒子和标签，盒子位于 QR 本地上方并随当前 Transform 更新，标签显示内存中的 QR 原文和解析后的 MarkerID。ID 0 与 ID 250 必须能够同时存在；static/dynamic/dual 夹具选择只影响测试轮期望值和日志分类，不过滤可视化目标。单个 Trackable 失效或移除时只隐藏对应可视物，会话结束时清空全部可视物。QR 原文只显示在开发构建的头显诊断标签中，不因此改变 JSONL 默认脱敏策略。

这里的“QR 本地上方”严格按 MRUK 平面坐标定义，而不是固定猜测局部 Y 偏移：QR 平面是 Trackable 局部 XY，局部 `+Z` 是用于摆放可视物的表面法线。盒子中心 X/Y 对齐 `PlaneRect.center`，中心 Z 为 `cubeSize / 2`，因此盒子底面紧贴 QR 平面并像放在纸面上一样向外立起，不再沿 QR 图案平面上下漂移。若运行时缺少 `PlaneRect`，布局回退到 Trackable 局部原点作为平面中心。

这些盒子是 Probe 场景内的临时诊断几何体，不查询业务注册表、不创建 `AnchorEntity`，也不形成生产多目标或生命周期契约。

Quest 没有 ArUco 配对步骤。打印纸上的 ArUco 只用于 PICO，Quest 的逻辑参考点天然是 QR 中心。

### D4. PICO 基线是连续 ArUco 观察与外部身份映射

PICO 单轮测试状态仅用于诊断流程：

```text
Idle → TrackingRequested → MarkerObserved → RegistryResolved
     → RunCompleted / RunEnded
```

操作者显式启动或停止 Marker 观察。探针记录 Marker 全量回调，并用当前 Registry 解析身份：

- Registry 命中产生 `registry_resolved`；
- 未知 ID 产生 `registry_miss`，但不伪造业务身份；
- 重复 ID、无效 Pose、SDK 异常和回调静默只记录事实，不实现生产自动重试；
- 每轮完成后由操作者开始下一轮。
PICO 系统扫码不属于该状态机。需要重新确认 QR 时，由上层业务显式进入独立扫码体验，完成后再更新 Registry 或业务上下文；这属于后续 change。

### D5. 静态和动态 PICO Marker 必须分别留下事实

使用两套官方图案：

- `QR 0 + ArUco 0`：PICO static，`DICT_4X4_1000` ID 0；
- `QR 250 + ArUco 250`：PICO dynamic，`DICT_4X4_1000` ID 250。

两套测试都包含静止观察、缓慢平移、缓慢旋转、短暂遮挡和重新入镜。日志记录 PICO 实际返回的 ID、Pose、有效标志、回调频率和静默区间。设计不依据 static/dynamic 文件名预设移动行为。

### D6. 同时保存原生事实和派生 Pose，不隐藏坐标问题

每个 Pose 样本至少保存：

- SDK 回调提供的原始位置、旋转、有效标志和原生时间戳；
- 进入 Unity 后的 Pose；
- 当前 XR Origin Transform（PICO）；
- 若探针执行了空间转换，保存转换后 World Pose 和转换版本；
- 若应用了候选 `ArUcoToQrOffset`，同时保存应用前后 Pose 与 Offset 值。

**注册 Marker 回调时传入的两个参数必须一并记录，它们直接改变每个样本的 posY。**

```csharp
// PXR_Enterprise.cs:1939
public static int SetMarkerInfoCallback(
    TrackingOriginModeFlags trackingMode, float cameraYOffset, Action<List<MarkerInfo>> markerInfos)
```

```csharp
// MarkerInfoCallback.cs:88-97
if (TrackingMode == Device || TrackingMode == Floor) { OriginHeight = -trackingorigin_height; }
else { OriginHeight = 0; YOffset = 0; }
model.posY = double.Parse(...) + OriginHeight + YOffset;
```

也就是说 `trackingMode` 与 `cameraYOffset` 是**每个 Pose 的隐式输入**，而 `trackingMode` 在现有实现里是运行时探测的（`PicoMarkerProvider.ResolveTrackingOriginMode()`），取不到时静默回退 `Floor`。两次会话的探测结果不同，同一块码的 posY 就会差出一个人高，而日志里看不出任何原因。

因此会话头必须记录：

- 实际传入的 `trackingMode`，以及它是**探测到的**还是**回退的**；
- 实际传入的 `cameraYOffset`；
- `SetMarkerInfoCallback` 的返回值（`0` 成功，非 `0` 失败；Editor 恒为 `-1`）；
- 探测时可见的 `XRInputSubsystem` 列表与各自的 `GetTrackingOriginMode()` 结果。

这是 D6 想防的「隐藏的派生 Pose 参数」中最关键的一组：它不在 `MarkerInfo` 里，不在 XR Origin 里，只存在于注册那一刻。

Quest QR Pose 已在 Unity 场景中，以 QR 为物理参考点。PICO Pose 以 ArUco 为参考点，且具体轴、符号和原点约定必须由真机日志确认。

A3 与双 A4 夹具的物理关系固定：QR 在左、ArUco 在右、朝向一致、垂直偏移为 0、中心距为 210 mm。这个物理向量不能在真机轴向未确认前直接硬编码成 Unity/PICO 局部坐标向量。

跨设备不比较原始世界坐标，因为两台设备拥有独立追踪原点。Pose 验收使用同一端内部的相对变换，再比较 Quest 与 PICO 得到的相对量。

**A→B 相对变换需要一个单独的双码采样模式。** D3 与 D4 的单轮流程都只跟一个 MarkerID，而 A→B 要求同一时刻拿到两个码的 Pose。原始数据是够的——PICO 的 Marker 回调本来就是全量快照，MRUK 也能同时持有两个 Trackable——但运行模型里必须显式有这个模式：

- 双码轮同时记录 A（ID 0）与 B（ID 250）的 Pose 样本，各自带自己的 MarkerID 和有效标志；
- PICO 侧按 Registry 预配置的两个 ArUco ID 直接进入双码轮；两个 ID 都从同一份快照里读；
- 只有**同一份快照/同一帧**内两个码都有效的样本才用于计算 A→B，跨时刻配对的样本一律丢弃并记录原因。

三条前提必须写进操作步骤，否则测出来的数没有意义：

1. **A/B 两块夹具在 Quest 测量与 PICO 测量之间不得移动。** 两端测的是同一个未知相对量，动过就不是同一个量了。安装、测量、拆除的时间顺序要记进日志。
2. **每个样本记录当前 XR Origin，轮末检查它在整轮内是否恒定。** A→B 对 XR Origin 的刚性变换不变（`trackA⁻¹·T⁻¹·T·trackB = trackA⁻¹·trackB`），但这条不变性在 XR Origin 中途被改动、或带非单位缩放时都不成立。轮内变动过则该轮作废。
3. **A→B 通过不代表世界坐标转换正确。** 正因为它对 T 不敏感，这项验收**不覆盖**追踪原点→世界坐标那一步。结论摘要必须写明这一点，别让后续 production change 误以为坐标管线已经被验证过。

### D7. JSONL 是完整证据，Unity Console 是实时镜像

每次运行创建独立日志文件：

```text
Application.persistentDataPath/MarkerProbe/<utc-session-id>.jsonl
```

每行是一个完整 JSON 对象，包含公共字段：

- `schemaVersion`、`sessionId`、单调递增 `sequence`；
- `eventType`、`platform`、`runId`、`fixtureId`；
- UTC 时间、单调时间、原生时间（若有）、相邻回调间隔；
- Unity 帧号、线程 ID、当前 Probe 状态；
- 系统/设备/应用/Unity/SDK 版本和能力/权限/授权快照；
- MarkerID、RawPayload 长度与 SHA-256；
- 原生 Pose、Unity Pose、XR Origin、Offset 及有效性字段；
- SDK 返回值、异常类型、错误码和可读详情；
- 夹具格式、文件 SHA-256、实测边长、平整度/安装说明。

关键事件与节流后的 Pose 摘要使用统一前缀 `[MarkerProbe]` 镜像到 Unity Console，便于 `adb logcat` 或 Editor Console 实时监控。完整回调数据只写 JSONL，避免 Console 限速改变回调节奏。

记录器在主线程按顺序写入，周期性 flush，并在错误、单轮结束、应用暂停和会话结束时强制 flush。日志头记录实际文件路径；会话尾记录各事件计数、丢弃计数、最大静默间隔和结束原因。

替代方案是只用 `Debug.Log`。设备日志可能截断、重排或被系统轮转，无法作为完整时序证据，因此不采用。

### D8. RawPayload 默认脱敏，代表日志可安全入库

设备日志默认不保存 QR 原文，只保存 UTF-8 字节长度和 SHA-256。开发构建可通过显式配置记录原文，并在日志头写明 `rawPayloadCaptured=true`。

入库代表日志必须：

- 删除设备序列号、账户、网络标识和可能的业务 QR 原文；
- 将 MarkerID 匿名化，夹具测试 ID 0/250 可保留；
- 将世界 Pose 转换为相对本轮首个有效样本的坐标；
- 保留时间间隔、状态、错误码、版本和变化量。

### D9. A3 为首选刚性夹具，双 A4 只作备用

首选 PDF 是 A3 横版单页 420 × 297 mm：

- QR 外框 160 × 160 mm，包含四模块静区；
- ArUco 外黑框 160 × 160 mm；
- QR 左、ArUco 右，中心距 210 mm；
- 两码垂直中心与朝向一致。

双 A4 版本保持相同几何关系，但需要把两页边缘无缝贴合并固定到同一刚性平面。打印必须使用 100% / Actual Size，关闭 Fit/Scale。每次真机测试记录打印格式、ArUco 实测边长、**实测 QR→ArUco 中心距**、纸面平整度和夹具文件哈希。

**中心距必须单独实测，不能靠量 ArUco 边长推出来。**

A3 单页上两者相关：整页同一缩放，量准 160 mm 的 ArUco 就说明 210 mm 也在 ±1.3 mm 内。**双 A4 上这条推理不成立**——210 mm 完全由「把两页边缘贴合」这个手工步骤决定，贴歪 3 mm 就偏 3 mm，而 ArUco 边长依然完美。

而 210 mm 正是将来变成 `ArUcoToQrOffset` 的那个数。它偏 3 mm 时，5 cm 的 Pose 目标照样通过，系统性误差被验收放过去，后续所有 marker 定位都带着它。

因此：双 A4 必测中心距，A3 每批抽测；日志记实测值而非标称 210；超差则用实测值参与 Pose 验收，或该夹具不进入验收。

夹具文件与重生成说明位于 `docs/test-fixtures/cross-platform-marker-tracking/`，生成器位于 `Tools/MarkerFixtures/`。

### D10. 验收以重复获取、字段完整和相对 Pose 为核心

**一轮「独立获取」的定义**：目标先完全离开视野、平台报告不再追踪，再重新入镜并重新建立追踪。目标一直在视野里时反复按开始/结束**不算**——PICO 每轮必然重新扫码，天然满足；Quest 没有触发点，不写死这条的话 10 轮可以是同一个 `MRUKTrackable` 从头到尾没断过，什么都证明不了。

最低真机矩阵：

1. Quest：同一 QR 连续完成 10 轮独立获取，每轮得到正确 MarkerID 和有限、可归一化的 6DOF Pose；
2. PICO static ID 0：连续完成 10 轮 ArUco 识别与 Registry 命中；
3. PICO dynamic ID 250：连续完成 10 轮 ArUco 识别与 Registry 命中；
4. 每类至少一轮执行移动、旋转、遮挡和重新入镜观察；
5. 每轮产生字段完整、可提取、会话尾计数闭合的 JSONL。

Pose 对齐使用固定放置的两套组合码 A/B：Quest 与 PICO 分别计算各自的 A→B 相对变换，位置差目标不超过 5 cm，角度差目标不超过 5°。若尚未确认 PICO Marker 局部坐标轴，先保留原始数据并将 Pose 对齐标记为 `blocked_by_axis_mapping`，不得用手调 Offset 掩盖坐标错误。

架构判定：

- `feasible`：两端重复路径稳定，PICO 两类码可识别并命中 Registry，日志完整，Pose 目标通过；
- `feasible_with_constraints`：顺序路径稳定，但并发、授权、特定系统版本或 Pose 映射存在明确限制；
- `not_feasible`：任一平台原生顺序路径无法稳定取得身份与有效 Pose，且日志证明不是权限、打印或操作问题。

PICO 系统 QR 扫描不属于本 change；需要运行时读取 QR 时必须另立相机帧或独立体验 change。

### D11. 并发测试只观测，不预先实现策略

在 PICO 已收到 Marker 回调时启动一次 QR 扫描，记录扫描期间：

- Marker 回调是否继续；
- 回调间隔和有效标志是否改变；
- 扫描取消/成功后是否恢复；
- 是否需要重新注册回调；
- SDK/系统版本和错误返回。

探针不实现“暂停 Tracking”“并发开关”或自动恢复策略。后续 production change 根据日志选择顺序、暂停或并发架构。

## Risks / Trade-offs

- **[探针行为被误当成生产契约]** → 类型、场景和日志均使用 Probe/Diagnostic 命名；proposal/spec 明确不承诺生产生命周期。
- **[Console 输出干扰回调节奏]** → 完整样本只写 JSONL，Console 仅镜像关键事件和节流摘要。
- **[日志写入中断]** → 周期 flush，并在错误、暂停和结束时强制 flush；会话尾提供计数核对。
- **[RawPayload 泄露业务数据]** → 默认只记长度与 SHA-256，原文需开发构建显式开启，入库前强制脱敏。
- **[打印缩放或翘曲污染 Pose]** → A3 单页优先，记录实测边长、平整度和哈希，不满足打印容差的测试不进入 Pose 验收。
- **[PICO static/dynamic 名称诱导错误结论]** → 两类使用相同动作矩阵，结论只引用实测回调。
- **[跨设备世界坐标直接比较得出假误差]** → 只比较各端内部计算的相对变换。
- **[PICO 并发失败]** → 仍以顺序路径判定基础架构；并发结果作为后续业务限制。
- **[权限或 TOB 授权缺失]** → 日志明确标记环境闸门，不把未授权等同于算法失败。

## Migration Plan

1. 在独立诊断入口接入 Probe Runner 和 JSONL 记录器，不替换现有 Marker 业务入口。
2. 先完成 Editor 可执行的日志序列化与夹具元数据检查，再分别构建 Quest/PICO 开发包。
3. 使用 A3 夹具执行 Quest、PICO static、PICO dynamic 重复测试和 PICO 并发观测。
4. 提取脱敏代表日志与结论摘要入库，回填四项真机观察结果。
5. 根据结论另立 production change；本探针保持隔离，确认不再需要时可整体删除。

回滚只需关闭或移除诊断入口和 Probe 组件；现有 Provider 契约、`MarkerAnchorService` 与业务数据不发生迁移，因此不需要业务回滚。

## Open Questions

以下项目不再通过规划问答推测，只能由真机日志关闭：

- PICO 4 Ultra Enterprise 上连续 Marker 回调、Registry 命中和权限/TOB 授权行为；
- PICO Marker Pose 的局部轴、符号、原点与 210 mm 物理偏移的映射；
- 目标 Quest/PICO 系统版本、运行时权限和 PICO TOB 企业授权是否满足前置条件；
- PICO Marker 正常回调频率、最大间隔及静默停滞表现。
