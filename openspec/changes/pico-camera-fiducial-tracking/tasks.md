## 0. 前置：收尾既有 change 的 PICO 半边

- [x] 0.1 在 `cross-platform-marker-tracking/design.md` 追加一条决策，记录 D4（PICO 原生 ArUco 基线）被真机证伪：`GetSwitchSystemFunctionStatus(SFS_TRACKING_ENABLE_DYNAMIC_MARKER)` 读回 `0`，且该能力需先完成大空间扫描
- [x] 0.2 把该 change 的 PICO 半边结论标为 `not_feasible`（对应其 task 10.6），保留原始日志关联；Quest 半边保持有效不受影响
- [x] 0.3 在该 change 中标注后续由 `pico-camera-fiducial-tracking` 承接，避免两处 change 对同一路径给出冲突结论

## 1. 依赖接入

- [x] 1.1 在 `Packages/manifest.json` 添加 scoped registry（`https://registry.npmjs.com`，scope `jp.keijiro`）与 `jp.keijiro.apriltag@1.0.3`
- [ ] 1.2 确认 `Plugin/Android/libAprilTag.so` 被识别为 Android / ARM64（其 `.meta` 已配置），确认 Editor 平台被排除
- [x] 1.3 确认 `com.unity.burst` 现有版本满足包依赖（`1.6.5`），不引入版本降级
- [x] 1.4 记录包版本与 BSD-2 许可归属；确认必要时可内联源码的退路

## 2a. 取帧路径切换（段一，独立可回退，先于检测核心）

> 先只换取帧路径、不换检测器，确认能拿到帧与位姿再往下（design D11）。此步不通则转退路方案（自行标定 + 只校正四个角点），回退点干净。

- [x] 2a.1 把取帧从 `SetCameraFrameBufferfor4U` + `StartGetImageDatafor4U` 改为 `AcquireVSTCameraFrameAntiDistortion`（拉取式，`PXR_Enterprise.cs:2017`）
- [x] 2a.2 缓冲区按 **RGB24**（`width * height * 3`）处理，不再是 RGB32
- [x] 2a.3 移除死字段 `useAntiDistortion`（`PicoQrCameraProbe.cs:34`）及其场景序列化值——它从未被读取，留着会继续误导
- [x] 2a.4 确认新路径的 `frame.pose` / `six_dof_pose` 字段可用，供第 5 节的时间对齐使用
- [ ] 2a.5 真机确认该路径与既有开相机流程兼容（同为 `token` + `camOpenned` 门槛，不需另开相机）；不兼容则停在此处转退路方案
- [ ] 2a.6 真机确认该路径返回的图确已去畸变，且与 `GetCameraParametersNewfor4U` 的内参属同一成像模型；不一致则后续精度数字全部不予采信

## 2. 检测核心（段一）

- [x] 2.1 实现 RGB24 → `ImageU8` 灰度转换（3 字节/像素），行序由第 4a 节的 EditMode 测试钉死约定，不复用包内 `ImageConverter`（其只接受 `Color32`）
- [x] 2.2 封装 `AprilTag.Interop` 的 `Detector` / `Family` / `ImageU8` / `DetectionArray` 生命周期（创建、AddFamily、Dispose），确保异常路径不泄漏原生句柄
- [x] 2.3 把 `QuadDecimate`、`QuadSigma`、`RefineEdges`、`DecodeSharpening`、`ThreadCount` 暴露为可序列化字段，记录默认值
- [x] 2.4 从 `Detection` 取 `Corner1`–`Corner4` 构造 `imagePoints`，模型点用黑框四角（边长 = `width_at_border` × module），调用 `PlanarPoseSolver.TrySolve`
- [x] 2.5 `Hamming` 与 `DecisionMargin` 进日志；"未检出"与"检出但置信度低/ID 不匹配"分别计数
- [x] 2.6 检测核心抽为单件，供探针与 Provider 共用（design D8）

## 3. 探针替换（段一）

- [x] 3.1 删除 `PicoQrCameraProbe.cs` 中的 ZXing 解码路径与 `imagePoints[3]` 平行四边形外插
- [x] 3.2 采集分辨率改为 **1280×960**（按 2 m 最远工作距离反推，design D4）—— **同时改字段初始值与 `Assets/Scenes/PicoQrCameraProbe.unity` 的序列化值**，序列化值会覆盖字段初始值
- [ ] 3.3 关闭并移除 ZXing 相关配置（`TryHarder` 等）；确认 ZXing.Net 无其它使用者后从项目移除
- [ ] 3.4 保留探针的 OnGUI 与 JSONL 输出，字段改为记录 AprilTag ID、四角点、`Hamming`、`DecisionMargin`、检测耗时
- [x] 3.5 保留可视化盒子反馈；盒子沿法线偏移半个尺寸以贴合板面

## 4. 夹具

- [x] 4.1 把 `Tools/MarkerFixtures/generate_pico_qr_aruco_fixtures.swift` 的 ArUco 绘制改为 `tagStandard41h12`（`width_at_border=5`、`total_width=9`），槽位与外框尺寸沿用 160 mm，并按新内容更名
- [x] 4.2 同页保留 QR（供 Quest 与人工核对），维持既有 A3 首选 / 双 A4 备用两种版式
- [x] 4.3 生成静态与动态两套 ID 的 PDF 与 300 DPI 预览，记录 PDF 哈希
- [x] 4.3b 生成器加渲染自检：从预览 PNG 把标采回来与源标图逐模块比对，不一致直接失败。已用「去掉翻转」反向验证过守卫会红——CG 的 y 轴朝上而位图第 0 行在顶部，少翻一次就是上下镜像，而镜像的码 apriltag 解不了、纸面上又看不出来
- [ ] 4.4 打印后**实测检测四边形边长**（`width_at_border` 那一圈，标称 88.9 mm，注意不是 160 mm 图幅）并记录；位姿求解与验收一律使用实测值
- [ ] 4.5 实测 QR 中心到 AprilTag 中心的距离并记录（双 A4 必测，A3 抽测），用于回填 `picoMarkerToTargetOffset`

## 4a. EditMode 集成测试（真检测器，无需设备）

> `Plugin/macOS/AprilTag.bundle` 是 universal binary 且 `.meta` 中 Editor 已启用，原生检测器可在 macOS Editor 内运行（design D12）。本节把 5.3、5.4 的大部分前移，真机上只剩确认一个 bit。

- [x] 4a.1 用 `apriltag_to_image()` 渲染标图作为真值来源；透视投影复用 `PlanarPoseSolverTests.cs:32` 的 `Project` / `ProjectAll`
- [x] 4a.2 按已知 `markerToCamera` 变形后送入真检测器，断言 ID 正确、四角点顺序符合模型点定义、解出位姿与真值一致
- [x] 4a.3 同一张图按两种行序各送一次，断言两者相差一个 Y 镜像——把"行序错了长什么样"变成可执行事实
- [x] 4a.4 参数化用例：加噪声、模糊、逐档倾斜，在无设备时先摸出角度上限的下界
- [x] 4a.5 非 macOS 平台条件跳过而非失败；CI 不得默认依赖本节

## 5. 段一真机度量

- [ ] 5.1 构建并安装到 PICO；确认 TOB `getCameraInfo` 授权、`android.permission.CAMERA`、相机流与内参读取全部可用
- [ ] 5.2 **确认 `Detector.Detect()` 在工作线程上安全**：连续运行一整轮不因 JNI 或 Job System 线程约束终止；结果明确记录，不得假定
- [ ] 5.3 **确认图像行序属于 4a.3 断言的哪一种**：已知朝向的 marker 正对相机，看解出位姿的上方向是否镜像；只需判定一个 bit，约定本身已由 4a.3 固化
- [ ] 5.4 **复核位姿原点与轴向**：4a.2 已在 Editor 收敛，真机只需用已知方向的平移与旋转复核一次；若与 Editor 结论不符，位姿验收标记 `blocked_by_axis_mapping`，不得手调 offset 掩盖
- [ ] 5.5 量命中率：正对、0.5 m / 1.0 m / 2.0 m 各静止 ≥10 秒，判据全档 ≥90%
- [ ] 5.6 量角度上限：1.0 m 处从正对逐步倾斜到失效，记录 30°、45°、60° 各档命中率与失效角；判据为 ±45° 可识别
- [ ] 5.7 量距离上限：正对从 0.5 m 逐步后退到失效，记录失效距离；判据为 ≥2.0 m。不足则按 design D4 加大印刷尺寸而非再提分辨率
- [ ] 5.7b 量首次识别耗时：从入镜到首次输出世界位姿，判据 <1 s
- [ ] 5.8 量端到端延迟：从帧到达到世界位姿可用的耗时分布
- [ ] 5.9 量位姿精度：静止 marker 的世界位姿抖动与相对真值的偏差，判据位置 ≤5 cm、角度 ≤5°
- [ ] 5.10 调参轮：扫 `QuadDecimate` / `RefineEdges` / `QuadSigma`，记录选定值及其对命中率与延迟的影响
- [ ] 5.11 用帧自带的 `frame.pose` 替换 `Camera.main` 当前位姿快照，施加 `EnterpriseAPI.cs:155-156` 的 Z 翻转转换（位置 `(x, y, -z)`、旋转 `(x, y, -z, -w)`）
- [ ] 5.11b 在 `frame.pose` 基础上应用相机外参（实测平移约 `(-0.02055, 0.08227, -0.01658)`），并验证残余偏置；不得隐式遗留
- [ ] 5.11c 头部持续转动而 marker 静止时量世界位姿漂移，确认不随转速系统性增大——这是 5.11 是否真正生效的判据
- [ ] 5.12 汇总段一数字；任一判据未达成则把数字与原因写回 design 并停在此处，不启动段二

## 6. 段二：接入 Provider

- [ ] 6.1 `PicoMarkerProvider` 更换快照来源（`SetMarkerInfoCallback` → 当帧 AprilTag 检测结果），保留 `visibleIds` / `currentIds` 差集与公共契约
- [ ] 6.1b 为 `MarkerLost` 加 **1.0 s 时间滞回**（design D9）：连续缺席超时才触发，使其与 Quest 的 `TrackableRemoved` 表达同一件事。用时间量而非帧数，避免随 D7 的采样率旋钮漂移
- [ ] 6.1c 验证短暂遮挡（<1 s）不触发 `MarkerLost`，持续缺席（>1 s）触发且只触发一次
- [ ] 6.2 Registry 增加 `AprilTagId → 业务 ID` 映射；未注册 ID 记 registry miss 并保留实际 ID，不伪造身份
- [ ] 6.3 用 4.5 的实测中心距回填 `PlatformOffsetConfig.picoMarkerToTargetOffset`；`questMarkerToTargetOffset` 不动
- [ ] 6.4 确认 `MarkerStabilizer` 的既有阈值对逐帧测量的稳定化是否合适，如需调整则记录理由与新值
- [ ] 6.5 确认 Quest 侧 MRUK QR 路径未受影响

## 7. 段二验收

- [ ] 7.1 PICO 上对静态 ID 与动态 ID 各完成 10 轮独立识别；"独立"= 目标先完全离开视野、系统不再报告，再重新入镜
- [ ] 7.2 每轮覆盖静止、缓慢平移、缓慢旋转、短暂遮挡与重新入镜，记录实际行为
- [ ] 7.3 固定放置 A/B 两套夹具，在 Quest 与 PICO 分别记录并计算各端内部的 A→B 相对变换；两次测量之间夹具不得移动，时序记入日志
- [ ] 7.4 用相对位姿评估跨端对齐，判据位置差 ≤5 cm、角度差 ≤5°
- [ ] 7.5 生成脱敏代表日志，核对序号、计数、版本、夹具哈希与位姿变化量仍可审计
- [ ] 7.6 编写真机结果摘要，分别报告检测指标、位姿精度、跨端对齐与所用检测器参数
- [ ] 7.7 回填 design 的 Open Questions（行序、轴向、距离上限、ZXing 去留）；未关闭项保留阻塞原因

## 8. 收尾

- [x] 8.1 运行 EditMode 测试，确认 `PlanarPoseSolverTests` 与第 4a 节的检测器集成测试全绿（2026-08-24：54/54）
- [ ] 8.2 运行 `openspec validate pico-camera-fiducial-tracking`，确认 proposal、design、spec、tasks 与最终证据一致
