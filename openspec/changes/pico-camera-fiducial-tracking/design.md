## Context

PICO 4 Ultra Enterprise（A9210，PICO OS 5.15.5，PICO Unity Integration SDK 3.4.0）上，marker 定位有三条候选路径，前两条已在真机上排除：

1. **系统 QR 扫描** —— 接管独立体验，不能作为当前相机流能力使用（`cross-platform-marker-tracking` proposal 已排除）。
2. **原生 ArUco 回调**（`SetMarkerInfoCallback`）—— `SFS_TRACKING_ENABLE_DYNAMIC_MARKER` 真机读回 `0`，且必须先完成大空间扫描，不满足非侵入前提。
3. **企业相机流 + 自有检测** —— 本 change 采用。`getCameraInfo` TOB 授权在 OS 5.15.5 上可获取，`OpenCameraAsyncfor4U` 会经 `RequestUserPermission` 自动申请 `android.permission.CAMERA`；相机流与内参读取（`GetCameraParametersNewfor4U`、`GetCameraExtrinsicsfor4U`）均已在真机跑通。

第 3 条的检测器初版用 ZXing 解 QR，解码可用但位姿不可用（原因见 proposal）。本 change 把检测器换掉，保留已经跑通的相机流与已经过单元测试的位姿求解。

已在仓库中、本 change 复用而不改动的件：

- `PlanarPoseSolver.cs` —— DLT + Hartley 归一化 + `K⁻¹` 分解 + Gram-Schmidt 正交化，纯 C# 无依赖。尺度来自 marker 已知物理尺寸，不需要深度传感器（PICO 无实时环境深度）。
- `PlanarPoseSolverTests.cs` —— 8 个用例，含 ±0.5 px 角点噪声下的位置误差回归护栏，模型点即 **160 mm 正方形四角**。
- `PicoMarkerProvider.cs` —— 已按"全量快照 + 无丢失事件 + 差集算 `MarkerLost`"实现。
- `MarkerStabilizer.cs`、`PlatformOffsetConfig.cs`、`PoseMath.cs`、`IMarkerTrackingProvider.cs`。

## Goals / Non-Goals

**Goals**

- PICO 上不依赖大空间扫描、不进入系统扫码界面，取得可用的 marker 6DoF 位姿。
- 位姿误差、命中率、角度上限、距离上限、延迟在真机上量出数字，作为判据而非印象。
- 跨平台的检测机制差异不外泄到 `IMarkerTrackingProvider` 之外。

**Non-Goals**

- 不改 Quest 侧（继续 MRUK QR Trackable）。
- 不实现生产级生命周期、多目标管理、业务对象创建、一次性触发或重复扫描定位模式。
- 不引入 OpenCV 运行时。
- 不自行实现镜头去畸变算法，也不做棋盘格标定（改用 SDK 的去畸变帧路径，见 D11；自行标定是其退路，不是本 change 的默认工作）。

## Decisions

### D1. 采用 AprilTag，不自研 ArUco 检测器

候选：

| 方案 | 结论 |
|---|---|
| 借鉴 PICO SDK 的 ArUco 实现 | **不可行**。解包 4 个 enterprise aar，7 个 `.so` 全扫 `aruco\|apriltag\|cv::\|opencv` 命中数均为 0；aar 是 JNI/IPC 转发壳。SDK 中唯一的 "aruco" 字样是 `PXR_Type.cs:537` 的 OpenXR 枚举表条目 `XR_TYPE_MARKER_DETECTOR_ARUCO_INFO_ML`（Magic Leap 扩展常量），非实现。 |
| 市面 Unity ArUco 方案 | 全部为 OpenCV 包装（ArucoUnity、HoloLensArucoTracking、MixedReality-SpectatorView）。无维护中的纯 C# ArUco。为单一功能引入 OpenCV 运行时不成比例。 |
| 自研 ArUco 检测器 | 可行但约 400–600 行，且鲁棒性低于 AprilTag。 |
| **`jp.keijiro.apriltag`** | **选用**。BSD-2；预编译 aarch64 `.so`；原生多线程检测器；AprilTag 的轮廓法在倾斜下的失效角明显晚于扫描线法。 |

否决自研的理由不是工作量，是结果：AprilTag 的检测器在同等条件下更鲁棒，且检测代码为零行。

### D2. 只用 `AprilTag.Interop` 底层，绕过 `TagDetector` 封装

`TagDetector.ProcessImage` 内部走 `PoseEstimationJob.Schedule(...).Complete()`（Unity Job System），并在 `PoseEstimationJob.cs:38-41` 写死：

```csharp
_focalLength = height / 2 / math.tan(fov / 2);   // 强制 fx == fy
_focalCenter = math.double2(width, height) / 2;  // 强制 cx,cy 居中
```

两个后果：

- **线程**：Unity Job 只能从主线程调度，而 `Detector.Detect()` 是阻塞调用，放主线程即掉帧。现有解码已在 worker thread 上（`decodeResults` 的 `ConcurrentQueue`）。
- **内参**：真机实测 640×480 左目为 `fx=407.01, fy=407.03, cx=319.50, cy=239.50`。当前值恰好接近该封装的假设，但假设一旦写进管线，换分辨率就会静默失真。

`AprilTag.Interop` 的 `Detector` / `Family` / `ImageU8` / `DetectionArray` 均为 `public`，`Detect()` 是纯 P/Invoke 不触碰任何 Unity API，可留在 worker thread；`Detection` 直接暴露四个角点、`Hamming`、`DecisionMargin`。

选此路同时解决线程与内参两个问题，且 `PlanarPoseSolver` 的既有精度护栏（模型点为 160 mm 正方形四角）恰好度量的就是这条输入。

**替代方案**：用 `TagDetector` 并接受其假设，在主线程按 `sampleHz` 节流。否决理由：把"碰巧当前分辨率满足假设"固化进管线，属于用"碰巧能跑"替代"明确规定"。

### D3. 位姿由角点 + 实测内参进 `PlanarPoseSolver`，不用包自带估计器

见 D2。`Detection` 的 `Corner1..Corner4` 为 `double` 像素坐标，直接构造 `imagePoints`；`modelPoints` 为**检测四边形**四角，边长 = `width_at_border` × module = 88.9 mm（见 D4），一律用打印后实测值而非标称值。

**这也意味着 `PlanarPoseSolverTests.cs` 不作废**：其 8 个用例与噪声预算此前度量的是一个实际未被使用的输入形状，本 change 之后它度量的就是生产路径。

### D4. 采集分辨率按最远工作距离反推，定为 1280×960

`tagStandard41h12` 的真实布局（`apriltag.c:1479-1491`）：图幅 `total_width = 9` 模块见方，而**检测器认的四边形只有 `width_at_border = 5` 模块**，外两圈是反转边框（`reversed_border = true`）不参与。

沿用既有 160 mm 槽位 → module 17.8 mm、**检测四边形边长 88.9 mm**（不是 160 mm）。

要凑出 160 mm 检测框需图幅 288 mm，同页再放 160 mm 的 QR 共 448 mm，A3 横版 420 mm 放不下。因此接受 88.9 mm，不动幅面。

检测四边形的像素跨度 = 88.9 mm × `fx` / 距离，AprilTag 的实用下限约 20–25 px（经验值，未验证）：

| 距离 | 640×480（`fx≈407`） | 1280×960（`fx≈814`） |
|---|---|---|
| 0.5 m | 72 px | 145 px |
| 1.0 m | 36 px | 72 px |
| 2.0 m | **18 px** | **36 px** |

工作包络定为**距离 0.5–2 m、入射角 ±45°、首次识别 < 1 s**。640×480 在 2 m 处只有 18 px 达不到，且包默认 `QuadDecimate = 2`（`TagDetector.cs:38`）会在检测阶段再降一半。故定 **1280×960**——像素量是 640×480 的 4 倍，但只有 2048×1536 的 39%。

2048×1536 当初是为救 QR 的 2.8 px/module 才上的；本决策不是"退回某个固定值"，而是**按最远工作距离反推**。若实测距离上限仍不足，加大印刷尺寸而非再提分辨率——后者代价在延迟上。

`QuadDecimate` 只降**四边形检测**的分辨率，位解码仍在全分辨率进行，因此近距可开 2 省算力、远距落到 1（旋钮见 D7）。

**对 D3 的量化修正**：先前按 L = 160 mm 估算的"深度误差降一个数量级"不成立。按 `ΔZ ∝ Z²·σ/(f·L)`，L 实际只从 QR finder 中心基线的 77 mm 涨到 88.9 mm（1.15×）；真实改善约 **7–8 倍**，主要来自 σ（边线拟合亚像素 ~0.15 px vs finder blob ~1 px）与四个真角点带来的真实透视，而非 L。

### D5. Quest 侧不动，非对称性收敛在 Provider 内

已评估的替代方案：Quest 改用 Passthrough Camera API，两端共用同一检测器与求解器，平台差异压缩为"如何取得一帧图 + 内参 + 帧位姿"。该形状结构上更正确。

**否决理由**：Quest 侧 MRUK QR 路径当前可用且已在 `cross-platform-marker-tracking` 中取得真机证据，替换需重测；PCA 有独立的权限与延迟特性。本 change 不承担该风险。记为后续候选，不是被遗漏的选项。

非对称由三处既有件吸收，无需新增缝合层：

- **几何** —— `PlatformOffsetConfig` 的 `questMarkerToTargetOffset` / `picoMarkerToTargetOffset` 是两端各自到同一内容锚点的映射，而非有向的 marker→marker 偏移。若 AprilTag 印在原 ArUco 槽位中心，`picoMarkerToTargetOffset` 的平移量不变。
- **生命周期** —— `PicoMarkerProvider` 已按"全量快照 + 无丢失事件"实现，这正是逐帧相机检测的语义，差集逻辑与公共契约可留。**但仅此不足以让两端语义一致**：Quest 的 `MarkerLost` 表示追踪对象真的没了，PICO 的差集表示这一帧没看见。补正见 D9。
- **抖动** —— `MarkerStabilizer`（`positionThreshold=0.05`、`rotationThreshold=1`、`stableFrameThreshold=30`）已负责逐帧测量的稳定化。

### D6. 自写 RGB24 → `ImageU8` 灰度转换

去畸变帧路径给的是 RGB24（每像素 3 字节，见 D11），而包内 `ImageConverter` 只接受 `Color32`，本就不适用。此外它取 `Color32.g` 作为灰度，并按 `offs_dst = stride * (height - 1)` 倒序写行——该行序假设针对 Unity 纹理的自底向上布局，PICO 相机缓冲区的行序需在真机确认。

行序错误的表现是位姿在 Y 轴镜像而非报错，属于"碰巧能跑/悄悄不对"，因此行序必须由真机事实钉死并在代码中写明，不得靠试。

### D7. 检测器调参暴露为旋钮，不写死

`Detector` 暴露 `QuadDecimate`、`QuadSigma`、`RefineEdges`、`DecodeSharpening`、`ThreadCount`。这些直接决定速度/鲁棒性权衡，真机上必然需要调（物理世界的标定旋钮不能被"最小实现"省掉）。默认值与实测选定值一并记入本 change 结论。

`Hamming` 与 `DecisionMargin` 作为置信度进日志，用于把误检与漏检分开统计。

### D8. 检测核心单件，探针与生产共用

`PicoQrCameraProbe`（度量台架，保留 OnGUI 与 JSONL）与 `PicoMarkerProvider`（生产）共用同一检测组件。

**否决方案**：各自实现一份。理由：度量出的数字若不来自生产路径，判据就不成立；两份实现会让"探针能跑而生产不能"这类问题只在真机复现。

### D9. `MarkerLost` 加时间滞回，使两端表达同一件事

`MarkerAnchorService.cs:24-28` 只订阅 `MarkerResolved`，从未订阅 `MarkerLost`；内容在 `HandleStabilized` 里靠 `activeAnchorIds.Contains(rawId)` 挂一次就不再摘。因此今天 `MarkerLost` 没有生产消费者——两端语义不一致是**潜伏**的，不是已解决的。

同一个事件名在两端含义不同：

| | `MarkerLost` 的含义 | 触发频率 |
|---|---|---|
| Quest | MRUK `TrackableRemoved`（`QuestMarkerProvider.cs:39-44`），追踪对象真的没了 | 罕见、确定 |
| PICO（未加滞回） | 当帧快照里没有——挡一下手就触发 | 常态 |

CLAUDE.md 把"同一份代码因数据不同走出不同语义（而非不同结果）"列为重构优先于照搬的情形。等到有人订阅才发现两端行为不同，只会在真机上复现。

因此：`PicoMarkerProvider` 的 `MarkerLost` **连续缺席超过 T = 1.0 s 才触发**。用时间而非帧数，因为采样率是 D7 的可调旋钮，用帧数会跟着旋钮漂。T 与工作包络中"首次识别 < 1 s"取同一尺度，进出对称。

### D10. 世界位姿用帧自带位姿合成，不用 `Camera.main` 当前位姿

现状是拿 `Camera.main` 的**当前**位姿去合成，而该帧图像是更早采到的。这是当初为绕开坐标系问题的临时写法，本身就能打穿验收：

- 帧龄约 100–200 ms；
- 头部转动 60°/s（一般假设）→ **6–12° 角度误差**；
- 判据是 5°。

正确转换在官方样例里写着，只是被注释掉了（`EnterpriseAPI.cs:155-156`）：

```csharp
// FrameTarget.position = new Vector3(frame.pose.position.x, frame.pose.position.y, -frame.pose.position.z);
// FrameTarget.rotation = new Quaternion(frame.pose.rotation.x, frame.pose.rotation.y, -frame.pose.rotation.z, -frame.pose.rotation.w);
```

即右手系↔左手系的 Z 翻转，与真机实测吻合（同一时刻 `unityCam z=-0.186` / `sensorCam z=+0.186`）。

因此：用 `frame.pose` 并施加该转换；位姿与图像同时刻，时间不对齐问题消失。相机外参（≈8 cm 平移）挂在 `frame.pose` 上，而不是挂在一个时刻不对的位姿上。

### D11. 改用 SDK 的去畸变帧路径，自行标定为退路

`PicoQrCameraProbe.cs:34` 的 `useAntiDistortion` 是**死字段**——声明后全文件再无引用，取帧实际走 `:190`/`:195` 的原始路径。所以当前吃的是**畸变帧**。

而内参结构体不给畸变系数（`RGBCameraStruct.cs:108-120`）：

```csharp
public struct RGBCameraParamsNew {
    public double fx, fy, cx, cy;                     // 无 k1/k2/p1/p2
    public Vector3 l_pos; public Quaternion l_rot;
    public Vector3 r_pos; public Quaternion r_rot;
}
```

既拿不到去畸变的图，也拿不到自己去畸变所需的系数。而 `PlanarPoseSolver` 假设纯针孔模型；76° HFOV 下边缘角点的径向位移是**系统性偏置**，且 marker 常不在画面正中，调参盖不住。

因此改用 `AcquireVSTCameraFrameAntiDistortion`（`PXR_Enterprise.cs:2017`）。**替代方案**：保留原始帧、自行棋盘格标定、只对四个角点去畸变（只有 4 个点，开销可忽略）。否决为默认路径的理由：内参结构体不给系数，说明 SDK 的意图就是"要针孔就用去畸变帧"；自行标定等于维护一套与设备批次绑定的标定数据。保留为退路。

该路径的三个后果（`PXR_EnterprisePlugin.cs:1502-1535`）：

1. **像素格式** `size = width * height * 3` → RGB24，不是现在的 RGB32（故 D6 按 3 字节/像素写）；
2. **拉取式** `Acquire...`，不是 `SetCameraFrameBufferfor4U` + `StartGetImageDatafor4U` 的填缓冲区 + 回调，采样循环形状要改；
3. **代价**：丢掉已调通的 4U 取帧路径，包括那次 binder 线程 `JNIEnv` 崩溃的修法。

有利面：门槛相同（同为 `token` + `camOpenned`，`:1506-1517`，不需另开相机），且带 `six_dof_pose`——D10 所需的帧位姿在这条路上有。

因此排成**独立可回退的一步**：先只换取帧路径、不换检测器，确认能拿到帧与位姿再往下。若该路径不通，回退点干净，直接转退路方案，届时只需改角点这一处。

### D12. 用真检测器在 EditMode 里验证，不只靠真机

`Plugin/macOS/AprilTag.bundle` 是 universal binary（x86_64 + arm64），`.meta` 中 `Editor: Editor / enabled: 1 / OS: OSX / CPU: AnyCPU`——**原生检测器可在 macOS Unity Editor 内运行**。

因此三件原本只能真机验的事挪进 EditMode：

| 事项 | 原计划 | 改为 |
|---|---|---|
| 角点顺序与模型点对应 | 未测，靠读代码 | 顺序错则测试红 |
| 位姿原点与轴向 | 真机已知方向平移旋转 | 合成图给定真值 `markerToCamera`，直接断言 |
| 行序 | 真机看盒子方向 | 合成图钉死约定，真机只剩确认缓冲区是哪一种（1 bit） |

标图真值由库自身渲染（`apriltag.c:1472` 的 `apriltag_to_image()`）；透视投影复用 `PlanarPoseSolverTests.cs:32` 的 `Project`/`ProjectAll`（已被 8 个用例验证）。另加噪声/模糊/倾斜的参数化用例，在无设备时先摸出角度上限的下界。

**限制**：仅 macOS Editor 可跑（Windows/Linux 的 plugin 分别是 dll/so），其它平台需条件跳过。

## Risks / Trade-offs

- **`Detect()` 在 worker thread 上的安全性未验证**。理论上是纯 P/Invoke，但此前已有一次 binder 线程上误用 UnityMain 的 `JNIEnv` 导致 `CheckJNI` abort 的教训，须显式验证一次而非假定。
- **相机↔头部外参尚未应用**。真机日志中的平移约 `(-0.02055, 0.08227, -0.01658)`（≈8 cm），当前靠 `Camera.main` 位姿快照绕开。本 change 须或者正确应用，或者把它作为已知偏置显式记录，不得隐式留着。
- **AprilTag 报告位姿的原点与轴向未确认**。须以已知方向的移动/旋转在真机上确认，未确认前位姿验收标记为阻塞，不得用手调 offset 掩盖。
- **识别距离上限估算未验证**。88.9 mm 黑框在 640×480 下推算约 1.5–2 m，需实测。
- **新增外部依赖**。`jp.keijiro.apriltag` 为个人维护包；已固定版本 1.0.3，BSD-2 允许必要时内联源码。
- **夹具需重新打印**，旧夹具的 ArUco 半边作废，QR 半边仍供 Quest 使用。
- **去畸变帧路径与 4U 开相机流程的兼容性未验证**（D11）。由门槛同为 `token` + `camOpenned` 推断，须实测；不通则转退路方案。
- **该路径返回的图是否确已去畸变、是否与 `GetCameraParametersNewfor4U` 属同一成像模型，未验证**。若二者不一致，全部精度判据失效。
- **切换取帧路径会丢掉已调通的 4U 缓冲路径**，包括 binder 线程崩溃的修法。故排成独立可回退的一步。
- **EditMode 集成测试仅 macOS 可跑**（D12），其它平台须条件跳过，CI 不能默认依赖它。

## Migration Plan

1. `cross-platform-marker-tracking` 的 PICO 半边落 `not_feasible` 结论，保留 `SFS_TRACKING_ENABLE_DYNAMIC_MARKER=0` 与"需先扫大空间"的证据；Quest 半边保持有效。
2. 段一：接包 → `Interop` 层封装 → 替换探针检测器 → 真机度量，通过判据见 tasks。
3. 段二：`PicoMarkerProvider` 换源、Registry 加映射、夹具生成器改绘 → 跨端对齐验收。
4. 段一未达判据则不进入段二，把实测数字与失败原因记回本 design，重新评估 D1。

## Open Questions

以下项目只能由真机日志或实测关闭：

- 去畸变帧路径（D11）是否与 4U 开相机流程兼容，返回的图是否确已去畸变且与内参同模型；
- PICO 相机缓冲区行序（D6）——D12 的 EditMode 测试会把两种行序的表现钉成可执行事实，真机上只剩确认是哪一种；
- AprilTag 报告位姿相对夹具的原点与轴向（D3）——同样由 D12 先在 Editor 收敛；
- `Interop.Detector.Detect()` 在非主线程的实际安全性（D2）；
- 1280×960 下的实际距离与角度上限（D4）；若距离不足，决定加大印刷尺寸还是收窄工作距离。

已由阅读关闭：

- **ZXing 的使用者** —— 全项目只有 `PicoQrCameraProbe.cs` 一处，移除干净。
- **Registry 是否需要扩 schema** —— `MarkerProbeRegistry.cs:17` 的键已是 `public int picoArUcoId`，AprilTag ID 同为 int。这是**字段重命名**，不是 schema 扩展。ID 0 与 250 在 `tagStandard41h12` 的 2115 个码内均合法（`tagStandard41h12.c:2153`），沿用则 Registry 内容不变。
