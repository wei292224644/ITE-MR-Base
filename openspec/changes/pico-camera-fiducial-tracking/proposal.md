## Why

`cross-platform-marker-tracking` 为 PICO 定的基线（design D4：只注册原生 ArUco 回调、连续观察）已被真机证伪：

- `GetSwitchSystemFunctionStatus(SFS_TRACKING_ENABLE_DYNAMIC_MARKER)` 读回 `0`（关闭），回调从未被启用；
- 该能力必须先完成大空间扫描才能工作，不满足"非侵入式、随开随用"的前提。

替代路线（企业相机流 `OpenCameraAsyncfor4U` + ZXing 解 QR）已在真机跑通解码，能稳定读出夹具载荷 `0` 与 `250`，但**位姿不可用**：

- QR Version 1（21×21）无 alignment pattern，ZXing 只返回 3 个 `ResultPoint`（三个 finder 中心）；
- 第 4 点由 `imagePoints[3] = imagePoints[2] + (imagePoints[0] - imagePoints[1])` 外插，该式假设投影四边形是平行四边形，而正方形斜视投影是梯形——透视信息在进求解器之前即被抹除，单应退化为仿射；
- 定位基线是三个 finder 中心的 77 mm，而非码宽 116 mm，按 `ΔZ ∝ Z²·σ/(f·L)` 深度误差再劣化约 1.5 倍；
- 为把 0.8 m 处的 2.8 px/module 拉到可用区间，采集分辨率被提到 2048×1536，解码延迟随之劣化。

真机现象与上述推导一致：盒子贴不紧二维码、稍有角度即无法识别、识别耗时高。

需要一条在 PICO 上**不依赖大空间扫描、不进入系统扫码界面、位姿可用**的 marker 定位路线。

## What Changes

- PICO 侧检测器由 ZXing QR 换为 AprilTag（`tagStandard41h12`），接入 `jp.keijiro.apriltag`（BSD-2，npm scoped registry `jp.keijiro`）。该包 `Plugin/Android/libAprilTag.so` 为预编译 aarch64，`.meta` 已配置 `Android / CPU: ARM64`，无需自建 NDK 产物。
- 只使用该包的 `AprilTag.Interop` 底层（`Detector` / `Family` / `ImageU8` / `DetectionArray`），**不使用** `AprilTag.TagDetector` 封装：后者的 `PoseEstimationJob` 走 Unity Job System 且写死 `fx = fy = height/2/tan(fov/2)`、`cx,cy = 图像中心`。
- 位姿由 `Detection` 的四个真角点喂入既有 `PlanarPoseSolver`，使用 `GetCameraParametersNewfor4U` 返回的实测 `fx/fy/cx/cy`。
- 删除 ZXing 解码路径与第 4 点外插；采集分辨率由 2048×1536 改为 **1280×960**——按最远工作距离 2 m 反推，不是退回某个固定值。
- 取帧保持官方 CameraRendering 样例的 4U 路径（`OpenCameraAsyncfor4U` + `SetCameraFrameBufferfor4U` + `StartGetImageDatafor4U`，RGB32）。**不**改走 `AcquireVSTCameraFrameAntiDistortion`：该接口依赖 `OpenVSTCamera` 置位的 `camOpenned`，与 4U 不是同一会话，真机混用连续 `result=-1`。去畸变若仍需要，只对四个角点做自行标定校正。
- 灰度转换按 4U 的 RGB32（4 字节/像素）写入 `ImageU8`，不复用包内只接受 `Color32` 的 `ImageConverter`。
- 检测器调参（`QuadDecimate`、`QuadSigma`、`RefineEdges`、`DecodeSharpening`、`ThreadCount`）暴露为可序列化旋钮，不写死。
- 检测核心抽为单一组件，`PicoQrCameraProbe`（度量台架）与 `PicoMarkerProvider`（生产）共用。
- `PicoMarkerProvider` 更换快照来源：由 `PXR_Enterprise.SetMarkerInfoCallback` 改为当帧 AprilTag 检测结果，保留"全量快照 + 差集"的既有实现与公共契约；并为 `MarkerLost` 增加 **1.0 s 时间滞回**，使其在两端表达同一件事（"marker 真的不在了"）而非"这一帧没看见"。
- 世界位姿用该帧的 `frame.pose` 经 `(x,y,-z)` / `(x,y,-z,-w)` 翻进 Unity 追踪系后再与 `markerInCamera` 合成，替换 `Camera.main` 当前位姿快照。样例给 `FrameTarget` 赋原样只约束预览物体，不约束这条合成链（design D14）。不把 `GetCameraExtrinsicsfor4U` 乘进合成。
- Registry 的 `picoArUcoId` 字段更名以承载 AprilTag ID —— 键类型已是 `int`（`MarkerProbeRegistry.cs:17`），故为字段重命名而非 schema 扩展；沿用 ID 0 与 250 则映射内容不变。Quest 的 `QrPayload → 业务 ID` 不变。
- 夹具生成器把原 ArUco 的 160 mm 槽位改绘 `tagStandard41h12`。注意 `width_at_border=5` 而 `total_width=9`，**检测器认的四边形只有图幅的 5/9**：160 mm 图幅 → module 17.8 mm、检测框 88.9 mm（原 ArUco 在同槽位是 160 mm）。凑满 160 mm 检测框需 288 mm 图幅，同页加 QR 共 448 mm，A3 横版 420 mm 放不下，故接受 88.9 mm。同页 QR 保留，供 Quest 与人工核对使用。
- Quest 侧不变，继续走 MRUK QR Trackable。跨平台的非对称性收敛在 `IMarkerTrackingProvider` 之内，几何差异由既有 `PlatformOffsetConfig` 的两个 per-platform offset 吸收。
- 新增 EditMode 集成测试，用**真原生检测器**（`Plugin/macOS/AprilTag.bundle` 在 Editor 启用）对合成标图断言 ID、角点顺序、位姿轴向与行序约定，把三项原本只能真机验的事前移；仅 macOS 可跑，其它平台条件跳过。
- 工作包络定为**距离 0.5–2 m、入射角 ±45°、首次识别 < 1 s**，位姿误差 ≤5 cm / ≤5°。
- 分两段验收：段一只替换取帧与检测管线并在真机度量（命中率、角度上限、距离上限、延迟、位姿误差）；段二接入 Provider 与 Registry 并做跨端对齐验收。
- 不在本 change 内实现：Quest 侧改用相机流、生产级生命周期与多目标管理、业务对象创建、一次性触发与重复扫描定位模式。

## Capabilities

### New Capabilities

- `pico-camera-fiducial-tracking`: PICO 企业相机流上的 AprilTag 检测与单目平面位姿求解，含真机度量判据、Provider 接入契约与打印夹具。

### Modified Capabilities

- （无。`openspec/specs/` 下当前没有已归档的 marker tracking 能力规格；`cross-platform-marker-tracking` 仍为进行中的 change，其 PICO 半边的证伪结论由该 change 自行记录。）

## Impact

- 代码：`Assets/Scripts/Localization/Native/Probe/PicoQrCameraProbe.cs`（删 ZXing 与外插、换检测核心、退分辨率）、`Assets/Scripts/Localization/Native/PicoMarkerProvider.cs`（换快照来源）、`Assets/Scripts/Localization/Native/Probe/MarkerProbeRegistry.cs`（加 ID 映射）、新增检测核心与灰度转换组件。
- 不动：`PlanarPoseSolver.cs`、`PlanarPoseSolverTests.cs`、`PoseMath.cs`、`MarkerStabilizer.cs`、`PlatformOffsetConfig.cs`、`IMarkerTrackingProvider.cs`、Quest 侧全部。
- 依赖：新增 `jp.keijiro.apriltag`（BSD-2）及 scoped registry；其 `com.unity.burst 1.6.5` 依赖已由项目现有 `com.unity.burst 1.8.29` 满足（`Packages/packages-lock.json`）。
- 资产：`Tools/MarkerFixtures/generate_pico_qr_aruco_fixtures.swift` 改绘并更名；需重新打印夹具并记录哈希与实测尺寸。ZXing.Net 若无其它使用者可一并移除。
- 前置：`cross-platform-marker-tracking` 的 PICO 半边应先落 `not_feasible` 结论并保留证据，再由本 change 承接。
