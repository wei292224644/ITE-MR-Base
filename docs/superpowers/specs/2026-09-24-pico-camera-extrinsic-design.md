# PICO 扫码定位：补乘「头 → 左 RGB 相机」外参

> 2026-09-24。bug 修复，用户选择直接改，不走 spec → plan 审核；本文只记决策。
> 取代旧 `pico-camera-fiducial-tracking` design D13 中「不乘外参」那一半（openspec 已删）。

## 问题

PICO 扫码后 `AnchorRoot` 的 gizmo 原点偏在码中心右侧约 3–4 cm（`docs/handoff/2026-09-24-pico-marker-pose-offset.md`）。

## 根因

`Frame.pose` 是曝光时刻的**头**位姿（SDK `RGBCameraStruct.cs` 注释：`The head Pose at the time of
image production`），`PicoEnterpriseCameraPose.ComposeWorld` 却把它当左 RGB 相机光心，直接与
`markerInCamera` 相乘，漏了头 → 左相机那一段。SDK 在 `GetCameraParametersNewfor4U` 的
`l_pos`/`l_rot` 里给了它：实测 `l_pos=(-0.03, 0.00, -0.07)`，即左 3 cm、前 7 cm。

漏掉的是一个固定在头系、随头转的 7.6 cm 向量：横向 3 cm 在截图里表现为「偏右」，
前后 7 cm 大体沿视线、表现为 gizmo 浮在码上方、离人更近。

**离线验证**（不依赖显示/透视）：用真机日志里同一张静止码的 259 帧求解结果，对每帧补
`R_head · t` 再看世界位置离散度；对 t 做最小二乘盲拟合得 `(−3.0, +0.5, +7.5) cm`，与 SDK
的 `(−3, 0, +7) cm` 在舍入内一致；补上后离散度 0.9 → 0.4 cm（另一簇 133 帧 1.5 → 0.7 cm）。

## 决策

**D1 — 合成链乘头 → 左相机外参。** `world = head ∘ headToCamera ∘ markerInCamera`。
旧 D13 以「官方样例只打印外参」为由不乘；样例的 `FrameTarget` 是预览物体、不是位姿合成链，
这个依据不成立。

**D2 — 外参在运行时取自 `GetCameraParametersNewfor4U` 的 `l_pos`/`l_rot`。**
- 否决写死常量：逐台设备标定值，日志里还只有两位小数。
- 否决 `GetCameraExtrinsicsfor4U`：它是相机 → IMU（L/R 平移只在矩阵 Y 上差 6.4 cm 基线），参考系不是 `frame.pose` 的头。
- 否决 `PlatformOffsetConfig`：它是码系里的常量，而这段误差固定在头系、随头转，常量补不了。

**D3 — 坐标换算。** SDK 头系右手（X 右 / Y 上 / Z 朝后），SDK 相机系是 OpenCV（X 右 / Y 下 /
Z 朝前），`markerInCamera` 已是 Unity 相机系。
- 位置：`(x, y, −z)`。
- 旋转：`S_H · R · S_C`，`S_H = diag(1,1,−1)`、`S_C = diag(1,−1,1) = S_H · Rx(180°)`，
  所以是 `翻转(l_rot) · Rx(180°)`。`l_rot` 的绕 X 180° 由此抵掉，只剩约 0.4° 的标定残差，一并乘入。

**D4 — 外参不可信时不出位姿，并报 Error。** 判据：四元数模长偏离 1 超过 0.01、平移超过
15 cm，或者换算后的旋转残差超过 5°。SDK 取参失败时回填 `identity()`，换算后等于「相机朝后」，
会被残差判据拦下。拦下时 `poseStatus = "外参不可信"`，启动日志报 `LogError`。
- 否决「退回不乘外参 + Warning」：码照样能扫上但位置是错的，是一条静默失败路径。

## 不在范围

- 位姿锁存与图像拷贝之间的竞态（`Update` 先读 `latestFramePose` 再 `BlockCopy`）：只在头动时出现一帧错配；
  这次的静态偏移已被外参完整解释，单独处理。
- PICO 手柄错位：不同根因（XRI rig 按 OpenXR aim 位姿调的模型偏移，PXR 原生手柄布局没有 aim 位姿）。

## 验证

- EditMode：`PicoEnterpriseCameraPoseTests` 钉死 D3 换算（实测值 → `(−0.03, 0, 0.07)`、残差 < 1°）、
  D4 的三种拒绝，以及偏移随头转。
- 真机（待打包）：
  - 从正面、左、右、近、远各截一张图，gizmo 原点都在码中心；
  - 日志里同一张静止码的世界位置离散度 ≤ 约 0.5 cm；
  - 启动日志 `headToCamera≈(-0.03, 0, 0.07)`，`residual` < 1°。
