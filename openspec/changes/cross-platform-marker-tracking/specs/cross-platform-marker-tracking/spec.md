## ADDED Requirements

### Requirement: 隔离的架构可行性探针
系统 SHALL 提供仅用于开发与真机诊断的跨平台 Marker Probe。探针 MUST 与现有业务对象创建、稳定化和生产 Marker 生命周期隔离，MUST NOT 要求迁移现有业务调用方。

#### Scenario: 探针未启用
- **WHEN** 应用未显式启用 Marker Probe
- **THEN** 探针不启动 Quest QR 或 PICO 企业扫描能力
- **THEN** 探针不改变现有 Marker Provider 和 `MarkerAnchorService` 的行为

#### Scenario: 探针启用
- **WHEN** 开发人员从独立诊断入口启用 Marker Probe
- **THEN** 探针只执行平台原生观察、结构化日志和测试状态显示
- **THEN** 探针不创建业务 GameObject、不查询业务注册表，也不执行一次性触发或重复定位业务

#### Scenario: PICO Marker 回调单槽独占
- **WHEN** 探针准备在 PICO 上注册 Marker 回调，而生产 Marker Provider 已经在运行
- **THEN** 探针拒绝启动并报告冲突原因
- **THEN** 探针不覆盖已有注册，因为 `setMarkerInfoCallback` 是单槽 set 语义且没有反注册 API

#### Scenario: 探针停止不解绑企业服务
- **WHEN** 探针会话结束或被停止
- **THEN** 探针不调用 `UnBindEnterpriseService`
- **THEN** 探针用会话代次丢弃后续到达的 Marker 与 QR 回调

### Requirement: MarkerID 与 QR 原文分离记录
探针 SHALL 将 QR 解码原文视为不透明 RawPayload，并独立记录解析后的 MarkerID。基础探针 MUST NOT 假定 RawPayload 是 UUID、URL 或 JSON；本次标准夹具的默认解析规则 SHALL 把规范十进制原文 `"0"` 和 `"250"` 分别解析为同值 MarkerID。

#### Scenario: 标准夹具解析成功
- **WHEN** 探针扫描标准夹具 QR 原文 `"0"` 或 `"250"`
- **THEN** 探针分别得到 MarkerID `"0"` 或 `"250"`
- **THEN** 探针在日志中分别保存 RawPayload 摘要和 MarkerID

#### Scenario: 非标准内容无法解析
- **WHEN** 当前探针解析边界无法从 QR 原文产生 MarkerID
- **THEN** 探针记录解析失败、失败原因和 RawPayload 摘要
- **THEN** 探针不把该原文伪造成有效 MarkerID

### Requirement: Quest 原生 QR 证据采集
Quest Probe SHALL 使用 MRUK QR Trackable 取得 QR RawPayload、Trackable 状态和当前 6DOF Transform，并逐次记录原生事件及 Pose。探针 MUST 记录真实回调节奏，不得用固定模拟频率代替平台事实。

#### Scenario: Quest QR 首次出现
- **WHEN** MRUK 报告一个可解析的 QR Trackable
- **THEN** 探针记录 RawPayload 摘要、MarkerID、Trackable 身份、状态和当前 Unity World Pose
- **THEN** Pose 的位置分量均有限且旋转四元数可归一化

#### Scenario: Quest QR 持续观察
- **WHEN** 同一 QR Trackable 持续存在、移动、旋转或状态变化
- **THEN** 探针记录每次原生更新的单调时间、回调间隔、线程、状态和最新 Transform
- **THEN** 探针不把首次 Pose 副本重复写成新的原生样本

#### Scenario: Quest QR 移除或重新识别
- **WHEN** MRUK 移除 QR Trackable，或之后重新报告同一 RawPayload
- **THEN** 探针按实际顺序记录移除和重新出现事实
- **THEN** 探针不在本 change 中推导生产级 Lost 宽限或自动恢复语义

### Requirement: PICO 顺序 QR 到 ArUco 配对证据
PICO Probe SHALL 由操作者显式启动 QR 扫描，解析 MarkerID，再等待 `MarkerInfo.iMarkerId.ToString()` 与 MarkerID 精确相等的有效 ArUco 样本。稳定的顺序路径 SHALL 是本 change 的基础架构判定路径。

#### Scenario: PICO 成功配对
- **WHEN** QR 解析得到 MarkerID，且后续有效 ArUco 的整数 ID 与其精确相等
- **THEN** 探针记录 QR 结果、匹配 ArUco 的原始数据、Pose 和配对耗时
- **THEN** 当前测试轮标记为成功配对

#### Scenario: PICO ID 不匹配
- **WHEN** 等待目标 ID 时收到不同 `iMarkerId` 的 ArUco 样本
- **THEN** 探针记录期望 ID、实际 ID、Pose、有效标志和时间
- **THEN** 探针不把不匹配样本标记为配对成功

#### Scenario: PICO 扫码取消、空结果或无回调
- **WHEN** QR 扫描被取消、返回空内容或在测试 watchdog 内没有回调
- **THEN** 探针记录可观察到的 SDK 行为和当前测试轮结束原因
- **THEN** watchdog 只终止诊断轮次，不形成生产超时或自动重试契约

#### Scenario: PICO 下一轮由操作者启动
- **WHEN** 一轮 PICO 测试成功或失败后需要再次测试
- **THEN** 探针等待操作者显式开始下一轮
- **THEN** 探针不自动弹出扫码界面或无限重试

### Requirement: PICO 静态与动态 Marker 分别实测
PICO Probe MUST 分别使用 `DICT_4X4_1000` 静态 ID 0 和动态 ID 250 夹具采集真机证据。系统 MUST NOT 根据 static/dynamic 文件名预设回调或移动语义。

#### Scenario: 静态 ID 0 测试
- **WHEN** 操作者使用 `QR 0 + ArUco 0` 夹具执行静止、平移、旋转、遮挡和重新入镜动作
- **THEN** 探针记录 PICO 对 ID 0 实际返回的有效标志、Pose、回调间隔和静默区间

#### Scenario: 动态 ID 250 测试
- **WHEN** 操作者使用 `QR 250 + ArUco 250` 夹具执行相同动作
- **THEN** 探针记录 PICO 对 ID 250 实际返回的有效标志、Pose、回调间隔和静默区间

#### Scenario: 比较两类 Marker
- **WHEN** 静态和动态测试均结束
- **THEN** 结论依据日志比较两类 Marker 的实际行为
- **THEN** 结论不把资源目录命名当作运行时证据

### Requirement: 完整持久化 JSONL
每次 Probe 会话 SHALL 在 `Application.persistentDataPath/MarkerProbe/` 创建独立 JSONL 文件。每个事件 MUST 是单行完整 JSON，并按单调递增 sequence 保持写入顺序。

#### Scenario: 会话开始
- **WHEN** Probe 创建新测试会话
- **THEN** 首批日志记录 schemaVersion、sessionId、文件路径、平台、设备/系统/应用/Unity/SDK 版本、能力、权限和授权状态
- **THEN** 日志记录所用夹具格式、fixtureId、文件 SHA-256、实测 ArUco 边长、实测 QR→ArUco 中心距和平整度/安装说明

#### Scenario: 原生回调事件
- **WHEN** Quest 或 PICO 产生扫描、状态或 Marker 回调
- **THEN** 对应 JSONL 行包含 sequence、eventType、platform、runId、UTC 时间、单调时间、原生时间、回调间隔、Unity 帧号、线程 ID 和 Probe 状态
- **THEN** 可用时同时包含 MarkerID、有效标志、原生 Pose、Unity Pose、XR Origin、候选 Offset、SDK 返回值和错误详情

#### Scenario: 会话正常或异常结束
- **WHEN** 会话结束、应用暂停或发生不可恢复错误
- **THEN** 记录器强制 flush
- **THEN** 会话尾记录事件计数、丢弃计数、最大静默间隔和结束原因

#### Scenario: 日志可重放时序
- **WHEN** 测试人员离线读取完整 JSONL
- **THEN** 可按 sequence 还原每轮扫码、解析、配对、Pose 样本、错误和结束的先后关系

### Requirement: 实时监控不替代完整日志
探针 SHALL 使用统一 `[MarkerProbe]` 前缀把关键状态和节流后的 Pose 摘要镜像到 Unity Console，使测试可通过 Editor Console 或 `adb logcat` 实时监控。完整高频样本 MUST 以 JSONL 为准。

#### Scenario: 实时查看测试
- **WHEN** Probe 会话正在运行
- **THEN** Console 显示会话 ID、日志路径、状态变化、配对结果、错误和节流后的 Pose 摘要
- **THEN** 测试人员可将 Console 事件关联到同 sessionId 的 JSONL

#### Scenario: Console 受到截断或轮转
- **WHEN** 设备系统丢弃、截断或轮转 Console 日志
- **THEN** JSONL 仍保留完整有序样本
- **THEN** 验收不把 Console 输出当作唯一证据

### Requirement: RawPayload 与入库日志隐私
设备 JSONL 默认 MUST 只记录 RawPayload 的 UTF-8 字节长度和 SHA-256。只有开发构建显式启用时才可记录原文，且日志头 MUST 标明是否启用。提交仓库的代表日志 MUST 脱敏。

#### Scenario: 默认记录 QR
- **WHEN** Probe 使用默认日志配置扫描 QR
- **THEN** JSONL 包含 RawPayload 长度和 SHA-256
- **THEN** JSONL 不包含 RawPayload 原文

#### Scenario: 开发构建显式记录原文
- **WHEN** 开发人员在开发构建中显式启用 RawPayload 原文记录
- **THEN** 日志头记录 `rawPayloadCaptured=true`
- **THEN** 测试界面明确显示当前日志可能包含敏感数据

#### Scenario: 代表日志入库
- **WHEN** 从设备日志提取代表样本提交仓库
- **THEN** 样本删除设备序列号、账户、网络标识和业务 QR 原文
- **THEN** 世界 Pose 转为相对本轮首个有效样本，同时保留时序、变化量、版本、状态和错误码

### Requirement: 版本化打印夹具
本 change SHALL 提供 A3 横版首选夹具和双 A4 备用夹具。两种格式 MUST 使用相同的码、尺寸、朝向和中心关系，并 MUST 提供可复现生成器、预览、打印说明和文件哈希。

#### Scenario: A3 首选夹具
- **WHEN** 测试人员打印 A3 横版 PDF
- **THEN** 页面尺寸为 420 × 297 mm，QR 在左、ArUco 在右
- **THEN** QR 外框和 ArUco 外框均为 160 mm，两码朝向一致、垂直偏移为 0、中心距为 210 mm

#### Scenario: 双 A4 备用夹具
- **WHEN** 测试人员无法使用 A3 而打印双 A4 PDF
- **THEN** QR 与 ArUco 分别位于两张 210 × 297 mm 竖版页面
- **THEN** 两页上下对齐、内边贴合后复现 A3 的 210 mm 中心距
- **THEN** 该中心距由人工拼接决定，因此必须实测，不能从页面尺寸推定

#### Scenario: 打印比例检查
- **WHEN** 夹具准备进入 Pose 测试
- **THEN** 打印使用 100% / Actual Size 且关闭 Fit/Scale
- **THEN** ArUco 外黑框实测为 160 mm，容差 ±1 mm
- **THEN** 不满足尺寸或平整度要求的夹具不进入 Pose 验收

#### Scenario: 中心距实测
- **WHEN** 夹具准备进入 Pose 测试
- **THEN** 实测 QR 外框中心到 ArUco 外框中心的距离为 210 mm，容差 ±1 mm
- **THEN** 双 A4 夹具必测该距离；A3 夹具在每批打印中至少抽测一次
- **THEN** 会话日志记录该实测值本身，而不是记录标称 210 mm
- **THEN** 若实测值超出容差，Pose 验收使用实测值而不是标称值，或该夹具不进入 Pose 验收

#### Scenario: QR 可机器解码
- **WHEN** 生成静态与动态夹具预览
- **THEN** 软件解码器分别得到 `"0"` 和 `"250"`
- **THEN** 夹具说明记录验证方式与输出哈希

### Requirement: Pose 事实与坐标转换可审计
Probe MUST 保留平台原始 Pose、Unity Pose 和所有派生转换参数，使 PICO 坐标轴或 XR Origin 错误可从日志审计。系统 MUST NOT 在真机轴向未确认前把 210 mm 物理偏移硬编码成平台局部向量。

#### Scenario: Quest Pose 记录
- **WHEN** Quest 报告 QR Trackable Transform
- **THEN** 探针将其记录为 QR 参考点的 Unity World Pose
- **THEN** 日志说明未额外应用 PICO 式 ArUco Offset

#### Scenario: PICO Pose 记录
- **WHEN** PICO 报告 ArUco MarkerInfo
- **THEN** 日志保留原始位置、旋转、有效标志、XR Origin 和转换后的 Unity Pose
- **THEN** 若应用候选 ArUcoToQrOffset，日志同时保存应用前后 Pose 和 Offset 值

#### Scenario: Marker 回调注册参数
- **WHEN** 探针调用 `SetMarkerInfoCallback` 注册 PICO Marker 回调
- **THEN** 会话头记录实际传入的 `trackingMode`、该值是探测到的还是回退的、`cameraYOffset` 和 SDK 返回值
- **THEN** 会话头记录探测时可见的 XRInputSubsystem 及其各自的 TrackingOriginMode
- **THEN** 这些字段被视为 Pose 的隐式输入，缺失时该会话的 Pose 数据不可用于 Pose 验收

#### Scenario: PICO 坐标轴尚未确认
- **WHEN** 210 mm 物理向量无法可靠映射到 PICO Marker 局部坐标
- **THEN** Probe 保留原始样本并把 Pose 对齐结果标记为 `blocked_by_axis_mapping`
- **THEN** Probe 不通过手调 Offset 隐藏坐标问题

#### Scenario: 跨设备 Pose 比较
- **WHEN** Quest 与 PICO 分别观察固定放置的组合码 A 和 B
- **THEN** 验收比较每个平台内部计算的 A→B 相对变换
- **THEN** 验收不直接相减两台设备的原始世界坐标

#### Scenario: 双码同时采样
- **WHEN** 探针执行 A→B 相对变换测量轮
- **THEN** 探针同时记录 A 与 B 各自的 MarkerID、有效标志和 Pose
- **THEN** 只有同一份快照或同一帧内两码均有效的样本用于计算 A→B
- **THEN** 跨时刻拼配的样本被丢弃并记录原因

#### Scenario: 双码轮的夹具与 XR Origin 前提
- **WHEN** 执行并比较两端的 A→B 测量
- **THEN** A/B 夹具在 Quest 测量与 PICO 测量之间保持不动，安装与测量时序记入日志
- **THEN** 每个样本记录当前 XR Origin，轮末检查其在整轮内是否恒定
- **THEN** 若 XR Origin 在轮内变动或带非单位缩放，该轮作废

#### Scenario: A→B 验收的覆盖边界
- **WHEN** A→B 相对变换达到位置与角度目标
- **THEN** 结论摘要写明该结果对追踪原点到世界坐标的转换不敏感，因此不构成对该转换的验证
- **THEN** 后续 change 不得据此认为坐标管线已验证

### Requirement: 重复真机验收矩阵
Probe 实现 MUST 完成 Quest、PICO static 和 PICO dynamic 的重复真机矩阵，并保存每轮日志。单次偶然成功 MUST NOT 作为架构可行证据。

#### Scenario: Quest 重复获取
- **WHEN** 在 Quest 上执行同一 QR 的 10 轮独立获取
- **THEN** 每轮均得到正确 MarkerID、有限位置和可归一化旋转
- **THEN** 每轮均产生字段完整且会话尾计数闭合的 JSONL

#### Scenario: 一轮「独立获取」的定义
- **WHEN** 判断某一轮是否算独立获取
- **THEN** 该轮要求目标先完全离开视野、平台报告不再追踪，再重新入镜并重新建立追踪
- **THEN** 目标始终留在视野中时反复开始/结束测试轮不计入独立获取轮数
- **THEN** 日志记录该轮的离开与重新出现事实，使轮次可事后核验

#### Scenario: PICO 静态重复配对
- **WHEN** 在 PICO 上对 static ID 0 执行 10 轮独立 QR→ArUco 获取
- **THEN** 每轮 QR MarkerID 与最终匹配 ArUco ID 均为 `0`
- **THEN** 每轮保存有效 6DOF Pose、配对耗时和完整 JSONL

#### Scenario: PICO 动态重复配对
- **WHEN** 在 PICO 上对 dynamic ID 250 执行 10 轮独立 QR→ArUco 获取
- **THEN** 每轮 QR MarkerID 与最终匹配 ArUco ID 均为 `250`
- **THEN** 每轮保存有效 6DOF Pose、配对耗时和完整 JSONL

#### Scenario: 相对 Pose 目标
- **WHEN** 两个平台均完成固定 A/B 夹具的相对变换测量且 PICO 轴向已确认
- **THEN** Quest 与 PICO 的 A→B 位置差目标不超过 5 cm
- **THEN** Quest 与 PICO 的 A→B 角度差目标不超过 5°

### Requirement: 真机未知量只记录事实
Probe SHALL 观测但 MUST NOT 预设 PICO QR/Marker 并发、PICO Pose 轴向、设备权限/TOB 授权和 Marker 回调节奏。每项结论 MUST 关联系统、SDK、应用版本和原始日志。

#### Scenario: PICO 并发观测
- **WHEN** PICO 已有 Marker 回调时启动一次 QR 扫描
- **THEN** 探针记录扫描期间 Marker 回调是否继续、间隔与有效标志如何变化，以及扫描结束后是否恢复
- **THEN** 探针不预先实现生产暂停、并发开关或自动恢复策略

#### Scenario: 权限或授权前置条件不足
- **WHEN** Quest 权限、PICO TOB 授权或平台能力不可用
- **THEN** 探针记录具体环境、能力状态、SDK 返回值和失败阶段
- **THEN** 结论把该轮标记为环境受阻，而不是伪造扫描失败或成功

#### Scenario: Marker 回调节奏
- **WHEN** PICO Marker 回调持续、间歇或静默
- **THEN** 日志保存所有回调间隔、最大静默区间、有效标志变化和会话计数
- **THEN** 本 change 不根据猜测设置生产停滞阈值

### Requirement: 可行性结论有明确证据等级
最终报告 SHALL 将结果归类为 `feasible`、`feasible_with_constraints` 或 `not_feasible`，并引用对应环境记录、夹具哈希、测试轮次和代表日志。

#### Scenario: 判定 feasible
- **WHEN** Quest 与 PICO 的重复顺序路径全部稳定完成、PICO 两类 Marker 均成功配对、日志完整且 Pose 目标通过
- **THEN** 报告将基础扫描架构判定为 `feasible`

#### Scenario: 判定 feasible_with_constraints
- **WHEN** 顺序身份与 Pose 路径稳定，但并发、特定系统版本、权限授权或 Pose 映射存在已记录限制
- **THEN** 报告将架构判定为 `feasible_with_constraints`
- **THEN** 报告列出限制对一次性触发和重复扫描定位业务的影响

#### Scenario: 判定 not_feasible
- **WHEN** 任一目标平台的原生顺序路径无法重复取得正确身份与有效 Pose，且日志证明原因不是权限、授权、打印或操作条件
- **THEN** 报告将该路径判定为 `not_feasible` 并引用失败证据

#### Scenario: PICO 并发失败但顺序路径稳定
- **WHEN** PICO QR 扫描与 Marker 回调不能并行，但顺序 QR→ArUco 路径满足重复验收
- **THEN** 报告不因并发失败单独判定基础架构不可行
- **THEN** 报告把并发限制留给后续重复扫描业务设计
