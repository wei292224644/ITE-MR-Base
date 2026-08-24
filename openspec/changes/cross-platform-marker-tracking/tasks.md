## 1. 测试夹具与证据基线

- [x] 1.1 从 PICO 官方 static/dynamic PDF 逐位确认 ID 0 与 ID 250 对应 OpenCV `DICT_4X4_1000` 码字，并记录来源文件 SHA-256
- [x] 1.2 实现可复现 Swift 生成器，输出 static 0、dynamic 250 的 A3 横版单页 PDF、双 A4 备用 PDF 和 300 DPI 预览
- [x] 1.3 将 QR 外框与 ArUco 外框设为 160 mm、QR 左/ArUco 右、中心距 210 mm，并记录 A3/A4 打印与安装方法
- [x] 1.4 用纯软件二维码解码器验证生成预览分别得到 `0` 和 `250`，记录 PDF 页面尺寸、页数和文件哈希
- [x] 1.5 将夹具、生成器和验证说明纳入 `docs/test-fixtures/cross-platform-marker-tracking/` 与 `Tools/MarkerFixtures/`

## 2. Probe 隔离入口与会话模型

- [x] 2.1 用 CodeGraph 复核现有 `IMarkerTrackingProvider`、Quest/PICO Provider、Bootstrapper、程序集与平台 define 的最新边界，记录本次最小接入点
- [x] 2.2 新增仅在开发/诊断构建启用的 Marker Probe 入口，确保默认关闭且不替换现有生产 Provider 或 `MarkerAnchorService`
- [x] 2.2b 在入口处强制「PICO Marker 回调单槽独占」：Probe 场景不挂 `MarkerTrackingBootstrapper`；启动时检测到生产 Provider 已运行则拒绝启动并报明原因，不静默覆盖注册（`setMarkerInfoCallback` 是 set 语义且无反注册 API）
- [x] 2.3 定义 Probe 会话、测试轮次、平台、状态、结束原因、夹具元数据和环境快照的数据模型
- [x] 2.4 定义最小 MarkerID 解析边界，本次默认支持规范十进制 `0`/`250`，同时保存 RawPayload 摘要和解析失败原因
- [x] 2.5 提供手动开始/结束会话、选择 static/dynamic 夹具、开始下一轮及显示当前状态/日志路径的诊断控制入口
- [x] 2.6 验证探针关闭、场景卸载和应用退出后不会继续消费平台回调或影响现有业务路径

## 3. 持久化 JSONL 与实时监控

- [x] 3.1 实现 `Application.persistentDataPath/MarkerProbe/<utc-session-id>.jsonl` 记录器，每行单个 JSON 对象并维护单调递增 sequence
- [x] 3.2 实现会话头：schema/session/file/platform、设备/系统/应用/Unity/SDK 版本、能力、权限、授权和夹具元数据（含实测 ArUco 边长与实测 QR→ArUco 中心距）
- [x] 3.2b 会话头额外记录 PICO Marker 回调注册参数：实际传入的 `trackingMode`、它是探测到的还是回退的、`cameraYOffset`、`SetMarkerInfoCallback` 返回值，以及探测时可见的 XRInputSubsystem 及其 TrackingOriginMode。这两个参数经 `MarkerInfoCallback` 的 `OriginHeight`/`YOffset` 直接改变每个样本的 posY，缺失则该会话 Pose 不可用于验收
- [x] 3.3 实现通用事件字段：runId、eventType、UTC/单调/原生时间、回调间隔、Unity 帧号、线程 ID、Probe 状态和错误上下文
- [x] 3.4 实现 Marker/Pose 字段：MarkerID、RawPayload 长度与 SHA-256、有效标志、原生 Pose、Unity Pose、XR Origin、候选 Offset 和 SDK 返回值
- [x] 3.5 实现周期 flush，并在错误、测试轮结束、应用暂停、会话结束时强制 flush；会话尾写入事件/丢弃计数、最大静默间隔和结束原因
- [x] 3.6 以 `[MarkerProbe]` 前缀把会话 ID、文件路径、关键状态、错误和节流后的 Pose 摘要镜像到 Unity Console，完整高频样本只写 JSONL
- [x] 3.7 默认禁止记录 RawPayload 原文；仅开发构建显式配置可开启，并在日志头和诊断 UI 中显示敏感数据警告
- [x] 3.8 为设备日志提取实现脱敏步骤：删除设备/账户/网络标识和业务原文，将世界 Pose 转为相对本轮首帧后再生成可入库代表日志

## 4. Quest 原生 QR Probe

- [x] 4.1 接入 MRUK QR Trackable 的 Added/Updated/Removed 或当前版本等价事件，只观察 QR 类型并保留 Trackable 实例身份
- [x] 4.2 逐次读取 RawPayload、当前 Trackable 状态和当前 Transform，记录真实事件时间与回调间隔，不重复首次 Pose 副本
- [x] 4.3 校验 Quest Pose 位置有限且旋转可归一化，并记录其已经是 QR 参考点 Unity World Pose，不应用 PICO Offset
- [x] 4.4 记录空 Payload、解析失败、无效 Pose、移除、重新出现、能力/权限失败和 SDK 异常，不推导生产 Lost/恢复语义
- [x] 4.5 验证 Probe Stop 或场景卸载后的迟到 MRUK 事件被当前会话代次隔离且不会写入已关闭会话
- [x] 4.6 为 Quest Probe 增加双 QR 诊断可视锚定：ID 0/250 同时各自显示跟随盒子与 QR 原文/MarkerID 标签，不受夹具选择过滤；盒子中心对齐 MRUK `PlaneRect` 中心并沿局部 `+Z` 抬高半边长，使底面紧贴 QR 平面；单个 Trackable 失效只隐藏自身，会话结束清空全部

## 5. PICO ArUco Probe 与外部身份映射

- [x] 5.1 由 Probe 自行 `InitEnterpriseService` + `BindEnterpriseService` 并记录二者结果、TOB 授权、设备支持和 Marker 回调注册返回值；Probe Stop **不**调用 `UnBindEnterpriseService`，只用会话代次吞掉后续回调。本 change 不建立共享企业服务门面
- [x] 5.2 将诊断流程改为 `Idle → TrackingRequested → MarkerObserved → RegistryResolved/RegistryMiss/RunEnded`
- [x] 5.3 新增版本化 MarkerRegistry，维护 `PicoArUcoId → QrId → LogicalMarkerId → businessObjectId` 映射，并记录文件哈希
- [x] 5.4 移除 Probe 对 `ScanQRCode` 的调用、扫码 watchdog、扫码取消和 QR→ArUco 配对状态；PICO 运行时不得启动系统扫码界面
- [x] 5.5 接收 Marker 全量回调并逐条记录 `iMarkerId`、`validFlag`、原始 Pose、原生时间戳、回调间隔和静默区间
- [x] 5.6 只把 Registry 命中的 `iMarkerId` 标为身份解析成功；完整记录未知 ID、重复条目和无效样本
- [x] 5.7 保存 PICO 原始 Pose、当前 XR Origin、Unity Pose 及任何候选空间转换/Offset 的输入输出，禁止只保留最终派生 Pose
- [x] 5.8 支持选择 static ID 0 与 dynamic ID 250 测试轮，并确保两类使用相同动作与日志字段
- [ ] 5.8b 实现双码采样轮：从同一份 Marker 快照同时读取 Registry 配置的 A(ID 0) 与 B(ID 250) 的 Pose；只有同一快照/同一帧内两码均有效的样本参与 A→B 计算，跨时刻拼配的样本丢弃并记录原因。Quest 侧同理，从同时持有的两个 Trackable 取同一帧样本
- [ ] 5.9 验证 Probe Stop、下一轮开始和应用暂停之间的迟到 Marker 回调不会串入错误 runId

## 6. Editor 与自动化验证

- [x] 6.1 测试 Registry 对 ArUco ID `0`、`250`、未知 ID、重复映射和版本哈希的确定结果
- [x] 6.2 测试 JSONL 每行可独立解析、sequence 严格递增、公共字段完整且事件顺序可重放
- [x] 6.3 测试默认日志不含 RawPayload 原文，显式开发配置会写入原文并标记 `rawPayloadCaptured=true`
- [x] 6.4 测试周期/强制 flush、异常结束和会话尾计数，确保已关闭会话不再接收事件
- [x] 6.5 用 Fake Quest 观察测试 Added/Updated/Removed、当前 Transform、无效 Pose和迟到事件隔离
- [x] 6.6 用 Fake PICO 能力测试 Marker 回调、Registry 命中/未命中、静默 watchdog、迟到回调及 Marker 无效样本
- [x] 6.7 测试 Pose 序列化完整保留原生、Unity、XR Origin 与 Offset 前后值，并能计算相对首帧和 A→B 相对变换
- [x] 6.8 测试 Console 镜像被节流而 JSONL 不丢完整样本，错误与会话 ID 可相互关联
- [x] 6.9 运行现有 Marker 相关测试，确认启用或移除 Probe 不改变 `MarkerAnchorService` 和现有 Provider 的生产行为

## 7. 分平台构建与预检

- [x] 7.1 验证 Editor/无设备环境编译，平台无关日志与模型不泄漏 MRUK/PICO SDK 类型
- [x] 7.2 构建 Quest 开发包，确认 QR 权限、MRUK 能力、诊断入口、Console 镜像和 persistentDataPath 日志文件可用（证据：`quest-smoke-20260804.md`）
- [ ] 7.3 构建 PICO 4 Ultra Enterprise 开发包，确认企业服务、TOB 授权、连续 Marker 回调、Registry、诊断入口和日志文件可用，并确认不会弹出系统 QR 扫码界面
- [ ] 7.4 在两端执行一轮 smoke test，拉取 JSONL 并验证版本、权限、夹具、Marker、Pose、线程和会话尾字段完整
- [ ] 7.5 对任何预检失败保存原始 SDK 返回值和环境快照；权限/授权受阻不得伪装成算法失败

## 8. Quest 真机矩阵

- [ ] 8.1 打印 A3 首选夹具并记录 PDF 哈希、打印机设置、ArUco 实测边长、纸面平整度和安装说明
- [ ] 8.1b **实测 QR 中心到 ArUco 中心的距离**（标称 210 mm，容差 ±1 mm）并把实测值记入日志。双 A4 必测——该距离由手工拼页决定，量 ArUco 边长推不出来；A3 每批抽测。超差则用实测值参与 Pose 验收或该夹具不进入验收
- [ ] 8.2 在 Quest 上对同一 QR 完成 10 轮独立获取，每轮确认 MarkerID、有限位置、可归一化旋转和闭合 JSONL。**「独立」= 目标先完全离开视野、平台报告不再追踪，再重新入镜重新建立追踪**；一直在视野里反复开始/结束不计入轮数，日志须留下离开与重新出现的事实
- [ ] 8.3 至少一轮记录 QR 静止、缓慢平移、缓慢旋转、短暂遮挡、移除和重新出现的真实 MRUK 事件
- [ ] 8.4 汇总 Quest 各轮首次获取耗时、回调间隔、最大静默区间、无效样本和失败原因

## 9. PICO 真机矩阵与设备闸门

> **本节按 design D12 关闭，结论 `not_feasible`。** `SFS_TRACKING_ENABLE_DYNAMIC_MARKER` 真机读回 `0`，
> 且该能力需先完成大空间扫描，与本 change 的非侵入式前提冲突。9.1–9.8 按原文不可执行，
> 不作为未完成工作遗留。PICO 的替代路线由 change `pico-camera-fiducial-tracking` 承接。
> Quest 半边（第 4、8 节）不受影响。


- [ ] 9.1 在 PICO 上对 static ID 0 完成 10 轮独立 ArUco 识别与 Registry 命中，每轮保存有效 6DOF Pose 和闭合 JSONL
- [ ] 9.2 在 PICO 上对 dynamic ID 250 完成 10 轮独立 ArUco 识别与 Registry 命中，每轮保存有效 6DOF Pose 和闭合 JSONL
- [ ] 9.3 对 static/dynamic 分别执行静止、缓慢平移、缓慢旋转、短暂遮挡和重新入镜，比较实际有效标志、Pose、回调节奏与静默表现
- [ ] 9.4 在等待目标 ID 时展示另一 ID，验证日志明确记录 actual ID、Registry miss 且不会伪造身份成功
- [ ] 9.5 记录无回调、watchdog 结束及下一轮恢复的实际行为；不启动 PICO 系统扫码界面
- [ ] 9.6 验证 Marker 回调持续活动时 Unity 业务交互不中断，并记录回调间隔/有效标志变化
- [ ] 9.7 通过已知方向的移动/旋转动作确认 PICO Marker Pose 的局部轴、符号和原点，并回填 210 mm 物理偏移的候选局部 Pose
- [ ] 9.8 汇总 PICO 两类 Marker 的 Registry 命中耗时、回调间隔、最大静默区间、无效样本、SDK 错误和权限/TOB 约束

## 10. Pose 验收、日志入库与结论

- [ ] 10.1 固定放置两套组合夹具 A(ID 0)/B(ID 250)，用 5.8b 的双码轮在 Quest 与 PICO 分别记录并计算各端内部的 A→B 相对变换，不直接比较跨设备原始世界坐标
- [ ] 10.1b 前提核验：A/B 在 Quest 测量与 PICO 测量之间**不得移动**（安装/测量/拆除时序记入日志）；每样本记 XR Origin，轮末确认整轮恒定且无非单位缩放，否则该轮作废
- [ ] 10.2 在 PICO 局部轴已确认后应用记录明确的候选 `ArUcoToQrOffset`（用 8.1b 的实测中心距，不用标称 210 mm），验证 Quest/PICO 相对位置差目标 ≤ 5 cm、角度差目标 ≤ 5°
- [ ] 10.2b 在结论摘要写明：A→B 对 XR Origin 的刚性变换不敏感，故 10.2 通过**不构成**对「追踪原点→世界坐标」转换的验证，后续 change 不得据此认为坐标管线已验证
- [ ] 10.3 若轴向未确认，保留原始数据并将 Pose 验收标记为 `blocked_by_axis_mapping`，不得用手调 Offset 掩盖问题
- [ ] 10.4 从 Quest/PICO 原始日志生成脱敏代表 JSONL，核对 sequence、计数、版本、夹具哈希、时序和 Pose 变化量仍可审计
- [ ] 10.5 编写真机结果摘要，分别报告 Quest、PICO static、PICO dynamic、并发、权限授权、回调节奏和 Pose 对齐证据
- [ ] 10.6 将结论归类为 `feasible`、`feasible_with_constraints` 或 `not_feasible`，说明对一次性触发与重复扫描定位后续业务的影响
- [ ] 10.7 用真机事实回填 probe/design 中四项 deferred observations；未关闭项必须保留日志关联和阻塞原因
- [ ] 10.8 运行全部相关自动化测试和 `openspec validate cross-platform-marker-tracking`，确认 proposal、design、spec、tasks 与最终证据一致
