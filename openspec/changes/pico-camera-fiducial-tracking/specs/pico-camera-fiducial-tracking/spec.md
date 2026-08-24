## ADDED Requirements

### Requirement: 非侵入式 marker 检测

系统 SHALL 在 PICO 上仅通过企业相机流检测 fiducial marker，不得依赖大空间扫描，不得启动系统 QR 扫码界面，不得注册原生 ArUco marker 回调。

#### Scenario: 未执行大空间扫描时仍可识别

- **WHEN** 设备未执行过大空间扫描，且 marker 处于相机视野内
- **THEN** 系统报告该 marker 的 ID 与 6DoF 位姿

#### Scenario: 不触发系统扫码体验

- **WHEN** 检测在整个会话期间持续运行
- **THEN** 系统不进入 PICO 系统 QR 扫码界面，Unity 业务交互不被接管

### Requirement: 位姿来自真实角点与实测内参

系统 SHALL 使用检测器返回的四个真实角点求解位姿，MUST NOT 由少于四个真实角点外插补齐；求解 MUST 使用 `GetCameraParametersNewfor4U` 返回的实测 `fx`、`fy`、`cx`、`cy`，MUST NOT 假设 `fx == fy` 或 `cx, cy` 位于图像中心。

#### Scenario: 四角点进入求解器

- **WHEN** 检测器返回一个 detection
- **THEN** 其 `Corner1`–`Corner4` 与黑框四角模型点一并传入 `PlanarPoseSolver.TrySolve`，且传入的内参为当前分辨率下读取的实测值

#### Scenario: 拒绝退化输入

- **WHEN** 求解器判定角点共线或数量不足
- **THEN** 系统丢弃该样本并记录原因，MUST NOT 输出位姿

### Requirement: 检测不阻塞渲染线程

检测 SHALL 在非主线程执行，MUST NOT 从非主线程调度 Unity Job 或调用任何 Unity API。

#### Scenario: 检测运行在工作线程

- **WHEN** 一帧图像进入检测
- **THEN** 检测在工作线程完成，主线程只消费结果队列

#### Scenario: 工作线程调用安全性经真机验证

- **WHEN** 检测在真机上连续运行不少于一次完整度量轮次
- **THEN** 进程不因 JNI 或 Job System 的线程约束终止，且该验证结果被记录

### Requirement: 图像行序由真机事实确定

RGB32 相机缓冲区到灰度图的转换 SHALL 使用经真机确认的行序，并在代码中写明该事实来源。

#### Scenario: 行序错误可被检出

- **WHEN** 已知朝向的 marker 正对相机
- **THEN** 解出的位姿上方向与实际一致；若不一致，系统记录为行序未确认并阻塞位姿验收，MUST NOT 通过翻转符号掩盖

### Requirement: Provider 公共契约不变

`PicoMarkerProvider` SHALL 保持 `IMarkerTrackingProvider` 的既有契约与"全量快照 + 差集算丢失"语义，仅更换快照来源。

#### Scenario: 快照来源更换后事件语义不变

- **WHEN** 一个先前可见的 marker 离开视野
- **THEN** Provider 通过比对前后两次快照的 ID 集合触发 `MarkerLost`，事件签名与触发条件与更换来源前一致

#### Scenario: 新出现的 marker 触发解析

- **WHEN** 一个 Registry 中已注册的 AprilTag ID 首次出现在快照中
- **THEN** Provider 触发 `MarkerResolved`，携带映射后的业务 ID 与世界位姿

### Requirement: 检测器参数可配置且置信度可观测

检测器的 `QuadDecimate`、`QuadSigma`、`RefineEdges`、`DecodeSharpening`、`ThreadCount` SHALL 可在不改代码的前提下调整；每个 detection 的 `Hamming` 与 `DecisionMargin` SHALL 进入日志。

#### Scenario: 参数可调

- **WHEN** 操作者修改检测器参数
- **THEN** 下一轮检测使用新值，且该轮日志记录所用参数

#### Scenario: 误检与漏检可分离统计

- **WHEN** 一轮度量结束
- **THEN** 日志足以区分"未检出"与"检出但置信度低/ID 错误"，二者分别计数

### Requirement: 探针与生产共用检测核心

度量台架与生产 Provider SHALL 使用同一检测组件实例化路径，MUST NOT 各自实现一份检测逻辑。

#### Scenario: 度量数字来自生产路径

- **WHEN** 在探针场景中量得命中率、延迟与位姿误差
- **THEN** 这些数字由生产 Provider 所用的同一检测组件产生

### Requirement: 相机外参处理必须显式

相机到头部的外参 SHALL 或者被正确应用于位姿链，或者作为已知偏置被显式记录；MUST NOT 隐式遗留。

#### Scenario: 外参状态可审计

- **WHEN** 系统输出世界位姿
- **THEN** 日志表明外参是已应用还是作为已知偏置未应用，若未应用则记录其实测数值

### Requirement: 真机度量判据

段一 SHALL 在真机上量出命中率、角度上限、距离上限、端到端延迟与位姿误差，并以数值判定是否进入段二。

#### Scenario: 工作距离内的命中率

- **WHEN** marker 正对相机、距离在 0.5 m 至 2.0 m 之间、静止不少于 10 秒
- **THEN** 检测命中率不低于 90%

#### Scenario: 倾斜可识别

- **WHEN** marker 相对相机光轴倾斜 45 度、距离在 0.5 m 至 2.0 m 之间
- **THEN** 系统仍能检出并输出位姿

#### Scenario: 首次识别耗时

- **WHEN** marker 进入视野
- **THEN** 从入镜到首次输出世界位姿的耗时不超过 1 秒

#### Scenario: 位置误差

- **WHEN** marker 静止且检测持续输出
- **THEN** 世界位姿的位置误差不超过 5 cm，角度误差不超过 5 度

#### Scenario: 判据未达成时不推进

- **WHEN** 上述任一判据未达成
- **THEN** 实测数字与失败原因记回 design，段二不启动

### Requirement: 夹具几何与身份映射

打印夹具 SHALL 在原 ArUco 槽位绘制 `tagStandard41h12`，同页保留 QR；Registry SHALL 将 AprilTag ID 映射到业务 ID，Quest 的 QR 载荷映射不变。

#### Scenario: 夹具尺寸可追溯

- **WHEN** 一批夹具打印完成
- **THEN** 记录 PDF 哈希、打印设置、黑框实测边长与纸面平整度，位姿验收使用实测边长而非标称值

#### Scenario: 未注册 ID 不伪造身份

- **WHEN** 检出的 AprilTag ID 不在 Registry 中
- **THEN** 系统记录 registry miss 并保留实际 ID，MUST NOT 输出业务身份

### Requirement: 成像模型与取帧路径一致

系统 SHALL 使用 PICO 官方 CameraRendering 样例的 4U 取帧路径采集 RGB32 帧，内参 MUST 来自同一宽高的 `GetCameraParametersNewfor4U`。MUST NOT 调用 `OpenVSTCamera`、`AcquireVSTCameraFrameAntiDistortion` 或 VST 的 `GetCameraParameters()`。

#### Scenario: 取帧路径为官方 4U 样例

- **WHEN** 系统采集一帧用于检测
- **THEN** 该帧来自 `SetCameraFrameBufferfor4U` + `StartGetImageDatafor4U`，像素格式为 RGB32，所用内参与缓冲区宽高相同

#### Scenario: 禁止 VST 取流

- **WHEN** 设备为 PICO 4 Ultra
- **THEN** 系统不打开 VST 相机会话，不拉取去畸变帧

#### Scenario: 去畸变不得改走 VST

- **WHEN** 畸变导致位姿倾角超出判据
- **THEN** 系统只对四个角点施加自行标定的畸变校正后再求解，MUST NOT 为此切换到 VST 取流

### Requirement: 位姿与图像时间对齐

世界位姿 SHALL 由该帧自带的 `frame.pose` 先翻进 Unity 追踪系（位置 `(x,y,-z)`，旋转 `(x,y,-z,-w)`）再与 marker 在 Unity 相机坐标系下的位姿合成，MUST NOT 使用消费时刻的 `Camera.main` 位姿快照，MUST NOT 把 `GetCameraExtrinsicsfor4U` 乘进该合成。官方样例对 `FrameTarget` 的原样赋值不作为本合成的契约。

#### Scenario: 使用帧自带位姿

- **WHEN** 一帧检测出 marker
- **THEN** 世界位姿为 `PoseMath.Compose(ToUnityTrackingPose(frame.pose), markerInCamera)`

#### Scenario: 头部运动下位姿不漂移

- **WHEN** marker 静止而头部持续转动
- **THEN** 输出的世界位姿保持在 5 cm / 5 度判据内，MUST NOT 随头部转速增大而系统性偏移

### Requirement: 丢失事件跨平台语义一致

`MarkerLost` SHALL 表示 marker 确已不在，而非当帧未见；PICO 侧 MUST 在连续缺席超过 1.0 秒后才触发。

#### Scenario: 短暂遮挡不触发丢失

- **WHEN** marker 被遮挡少于 1.0 秒后重新可见
- **THEN** 系统不触发 `MarkerLost`

#### Scenario: 持续缺席触发丢失

- **WHEN** marker 连续缺席超过 1.0 秒
- **THEN** 系统触发一次 `MarkerLost`

#### Scenario: 滞回不随采样率漂移

- **WHEN** 采样率被调整
- **THEN** 触发阈值仍为 1.0 秒的时间量，MUST NOT 表现为固定帧数

### Requirement: 检测链在无设备时可验证

系统 SHALL 提供使用真实原生检测器的 EditMode 测试，覆盖 ID、角点顺序、位姿轴向与行序约定。

#### Scenario: 合成标图还原已知位姿

- **WHEN** 按已知 `markerToCamera` 将渲染出的标图做透视变形后送入检测器
- **THEN** 解出的 ID 与位姿同真值一致，角点顺序符合模型点定义

#### Scenario: 行序约定被固化为可执行事实

- **WHEN** 同一张合成图按两种行序各送入一次
- **THEN** 两次结果相差一个 Y 镜像，测试断言该关系，使真机上只需判定属于哪一种

#### Scenario: 非 macOS 平台跳过

- **WHEN** 测试在缺少该原生插件的平台上运行
- **THEN** 用例被跳过而非失败
