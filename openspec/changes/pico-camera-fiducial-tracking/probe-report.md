# Probe Report: pico-camera-fiducial-tracking

> Generated: 2026-08-24
> Summary: 8 questions, 8 decisions, 9 open assumptions
> 本轮 probe 在 proposal/design/spec/tasks 已存在之后执行，目的是收敛。结果推翻了原 design 的 3 处陈述（D4 的分辨率与倍率、Non-Goals 的去畸变前提、D5 的生命周期"已被吸收"），并新增 4 条决策。

## Confirmed decisions

### Scope & intent

- **Question**: 段一判据里的 0.8 m 与 45° 是我拍的，没有出处（tasks.md 5.5-5.6）。实际工作包络是什么？
- **AI recommendation**: 距离 0.5–2 m、入射角 ±45°、首次识别 < 1 s。证据：`PlatformOffsetConfig.cs` 注释"marker 局部坐标系到**面板内容锚点**的固定偏移"表明用法是把内容钉在面板上；具体数值为一般假设，未验证。
- **User confirmation**: 可以。

### Impact

- **Question**: `tagStandard41h12` 在 160 mm 图幅下检测四边形只有 88.9 mm（不是我先前说的 160 mm），是接受还是给 PICO 单独印大图？
- **AI recommendation**: 接受 88.9 mm，不动幅面。证据：`apriltag.c:1479-1491`，图幅 `total_width=9`、检测框 `width_at_border=5`，即 5/9。要凑 160 mm 检测框需 288 mm 图幅，同页加 QR 共 448 mm，A3 横版 420 mm 放不下。主症（第 4 点外插抹掉透视）是定性问题，不是倍率问题。
- **User confirmation**: 认。
- **连带修正**: 原 design 称"深度误差降一个数量级"基于 L=160 mm，实际 L 只从 77 mm 涨到 88.9 mm（1.15×）。真实改善约 **7–8 倍**，主要来自 σ（边线拟合亚像素 vs finder blob）与四个真角点，不是 L。

### Design

- **Question**: 88.9 mm 检测框在 640×480 下 2 m 处只有 18 px，低于实用下限，与刚确认的最远距离冲突。抬分辨率、印大、还是降要求？
- **AI recommendation**: 抬到 **1280×960**（`fx≈814`，2 m 处 36 px）。证据：检测框像素跨度 = 88.9 mm × fx / 距离；`TagDetector.cs:38` 默认 `QuadDecimate=2` 会在检测阶段再降一半。D4 应改写为"按最远工作距离反推分辨率"，而非"退回某个固定值"。
- **User confirmation**: 可以。

- **Question**: D5 称生命周期差异"由 Provider 的差集吸收"，但 `MarkerLost` 在两端含义不同。要不要加滞回？
- **AI recommendation**: 加，T = 1.0 s。证据：`MarkerAnchorService.cs:24-28` 只订阅 `MarkerResolved`，从未订阅 `MarkerLost`，故今天无生产消费者——差异是**潜伏**的而非已解决的。Quest 侧 `QuestMarkerProvider.cs:39-44` 的 `MarkerLost` 来自 MRUK `TrackableRemoved`（罕见、确定），PICO 逐帧差集则是"这帧没看见"（常态）。CLAUDE.md 明确把"同一份代码因数据不同走出不同语义"列为重构优先于照搬的情形。用时间而非帧数，因为采样率是 D7 的可调旋钮。
- **User confirmation**: 加。

- **Question**: 世界位姿现在用 `Camera.main` 的**当前**位姿合成，而帧是更早采的。要不要改用帧自带位姿？
- **AI recommendation**: 改用 `frame.pose` 并按官方转换修正 Z 翻转。证据：`EnterpriseAPI.cs:155-156` 有被注释掉的 `new Vector3(x, y, -z)` / `new Quaternion(x, y, -z, -w)`；真机实测同一时刻 `unityCam z=-0.186` / `sensorCam z=+0.186` 与之吻合。时间不对齐的量级：帧龄 100–200 ms × 头部转动 60°/s（一般假设）= 6–12°，已超 5° 判据。外参（≈8 cm 平移）也须挂在 `frame.pose` 上而非时刻不对的位姿上。
- **User confirmation**: 加。
- **后续推翻（D13）**: 样例**正在跑**的代码把 `FrameTarget` 赋成 `frame.pose` 原样；Z 翻转是注释掉的试探，不是现行标准。改为 `Compose(frame.pose, markerInCamera)`，禁止再施加该转换，也禁止把外参乘进合成。时间对齐仍成立：必须用该帧的 `frame.pose`，禁止 `Camera.main`。

- **Question**: design Non-Goals 称"采集侧 `useAntiDistortion` 已开启"——实为死字段。畸变怎么处理？
- **AI recommendation**: 改用 `AcquireVSTCameraFrameAntiDistortion`（SDK 去畸变帧），退路是自己标定后只对四个角点去畸变。证据：`PicoQrCameraProbe.cs:34` 声明 `useAntiDistortion` 后全文件再无引用，取帧走 `:190`/`:195` 的原始路径；`RGBCameraStruct.cs:108-120` 的 `RGBCameraParamsNew` 只有 `fx/fy/cx/cy` + 外参，**无 `k1/k2/p1/p2`**，说明 SDK 的意图是"要针孔就用去畸变帧"。`PlanarPoseSolver` 假设纯针孔；76° HFOV 下边缘径向位移是**系统性偏置**，调参盖不住。
- **User confirmation**: 1（走去畸变帧路径）。
- **后续推翻（D13）**: VST 与 4U 不是同一会话（`camOpenned` 只由 `OpenVSTCamera` 置位）；混用后 Acquire 连续 `result=-1`。官方 CameraRendering 样例不走 VST。畸变退回：留在 4U RGB32 上，只对四个角点做自行标定校正。

- **Question**: 走去畸变帧路径的三个具体后果（RGB24、拉取式、丢掉已调通的 4U 缓冲路径）确认后，如何排任务？
- **AI recommendation**: 排成独立可回退的一步——先只换取帧路径、不换检测器，确认能拿到帧与位姿再往下。证据：`PXR_EnterprisePlugin.cs:1502-1535`，`size = width*height*3`（RGB24）、`Acquire` 语义（拉取）、门槛同为 `token` + `camOpenned`（`:1506-1517`，不需另开相机）、且带 `six_dof_pose`（D10 所需）。回退点干净则可直接转路线 2。
- **User confirmation**: 认可。
- **后续推翻（D13）**: 独立换取帧那一步已证伪，不再作为本 change 的默认路径。任务 2a 保持 4U。

### Success criteria

- **Question**: macOS bundle 能在 Editor 跑真检测器，要不要把三条"只能真机验"的事挪进 EditMode？
- **AI recommendation**: 要。证据：`Plugin/macOS/AprilTag.bundle` 是 universal binary（x86_64 + arm64），`.meta` 中 `Editor: Editor / enabled: 1 / OS: OSX / CPU: AnyCPU`；标图真值可由 `apriltag.c:1472` 的 `apriltag_to_image()` 渲染；投影管线复用 `PlanarPoseSolverTests.cs:32` 的 `Project`/`ProjectAll`（已被 8 个用例验证）。可覆盖：角点顺序、位姿轴向、行序（两种各喂一次断言差一个 Y 镜像）、加噪/倾斜下的角度上限下界。限制：仅 macOS Editor 可跑，其它平台需条件跳过。
- **User confirmation**: 加。

## Open assumptions [NEEDS CLARIFICATION]

- [ ] `[ASSUMED]` `Interop.Detector.Detect()` 在非主线程安全 —— 由"纯 P/Invoke、不触碰 Unity API"推断，未验证。影响：design D2、tasks 5.2
- [ ] `[ASSUMED]` AprilTag 检出四边形的实用像素下限约 20–25 px —— 经验值，无出处。影响：D4 的分辨率推导、tasks 5.7
- [ ] `[ASSUMED]` PICO 相机缓冲区行序 —— 未确定。影响：D6、tasks 5.3
- [ ] `[ASSUMED]` AprilTag 报告位姿相对夹具的原点与各轴符号 —— 未确定。影响：D3、tasks 5.4
- [ ] `[ASSUMED]` 头部转动 60°/s 作为典型速度 —— 一般假设，用于估算时间不对齐的量级。影响：D10 的论证强度（结论方向不依赖该数）
- [ ] `[ASSUMED]` 保留 tag ID 0 与 250 —— `tagStandard41h12` 有 2115 个码（`tagStandard41h12.c:2153`），二者合法；沿用可使 Registry JSON 内容不变，只需重命名字段 `picoArUcoId`。影响：夹具、`MarkerProbeRegistry.cs:17-22`
- [ ] `[ASSUMED]` `tagStandard41h12` 为唯一可选 family —— `Family.cs:23` 只提供 `CreateTagStandard41h12()`。若需其它 family 需自行扩展 Interop

## 已由阅读关闭（无需再问）

- ZXing 在项目中**只有一个使用者**（`PicoQrCameraProbe.cs`），移除干净。原 design Open Questions 中该项可关闭。
- Registry 键已是 `int`（`MarkerProbeRegistry.cs:17` `public int picoArUcoId`），AprilTag ID 同为 int —— 这是**字段重命名**而非 schema 扩展。原 proposal 称"增加映射"需修正。
- `AcquireVSTCameraFrameAntiDistortion` 与 4U **不兼容**（D13）：`camOpenned` 只由 `OpenVSTCamera` 置位，混用连续 `result=-1`。去畸变帧是否与 4U 内参同一模型不再需要验证。

## Suggested next step

- [ ] 按本报告修订 `design.md`（D4 改写；Non-Goals 去畸变陈述作废；新增 D9–D12）、`proposal.md`（分辨率、图幅、取帧路径、Registry 措辞）、`spec.md`（滞回、时间对齐、成像模型、EditMode 验证的需求与场景）、`tasks.md`（取帧路径切换、EditMode 测试节、判据数值）
- [ ] 修订后运行 `openspec validate pico-camera-fiducial-tracking`
