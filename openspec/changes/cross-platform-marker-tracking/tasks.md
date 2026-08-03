## 1. 公共契约与配置

- [ ] 1.1 记录现有 `IMarkerTrackingProvider`、Quest/PICO Provider、Bootstrapper 与 `MarkerAnchorService` 的测试基线，确认本 change 不吸收业务内容创建职责
- [ ] 1.2 新增统一平台、服务状态、LostReason、诊断代码与 PICO 获取状态枚举，并为公开枚举写契约测试
- [ ] 1.3 新增不可变的 Tracked/Tracking、Lost、Diagnostic、StateChanged 事件数据类型，覆盖 MarkerID、RawPayload、世界 Pose、平台、时间戳和终止上下文
- [ ] 1.4 定义 `IMarkerTrackingService` 的 `Tracked`、`Tracking`、`Lost`、`Diagnostic`、`StateChanged` 事件以及幂等 Enable/Disable、PICO BeginQrScan 入口
- [ ] 1.5 定义初始化只读的 `IMarkerIdParser`，提供测试用解析器与一个不假定 UUID/URL/JSON 的默认透传/委托实现
- [ ] 1.6 新增 Tracking 配置模型：30 Hz 默认更新频率、0.5 秒默认 Lost 宽限、15 秒默认 PICO 配对超时、完整 `ArUcoToQrOffset` 和 PICO 并发策略
- [ ] 1.7 为配置添加边界校验，并确保更新频率和 Lost 宽限可运行时生效、Provider 与 Offset 只能在关闭状态配置

## 2. 平台无关追踪内核

- [ ] 2.1 定义内部 Provider 原始观察契约，区分能力状态、目标可见/不可见、Pose 样本、QR 扫描结果和不可恢复错误
- [ ] 2.2 实现 SDK 回调到 Unity 主线程的线程安全观察队列，确保公开事件不在锁内触发
- [ ] 2.3 实现服务代次与目标会话代次，关闭、Lost 或错误后丢弃旧的异步 QR/Provider 回调
- [ ] 2.4 实现按区分大小写 MarkerID 索引的活动目标表和 `Tracked → Tracking* → Lost` 状态机
- [ ] 2.5 实现首次样本同帧只发 Tracked、下一有效更新开始 Tracking 的事件顺序
- [ ] 2.6 实现基于 `Update` 与单调时间的 Tracking 调度器，静止目标也按配置频率持续派发且不绑定 `FixedUpdate`
- [ ] 2.7 实现不可见时立即停发 Tracking、0.5 秒默认宽限、宽限恢复延续会话和超时 `Lost(OutOfView)`
- [ ] 2.8 实现默认 Disabled、幂等 Enable/Disable、Initializing/Ready/Unavailable/Error 状态与手动重试
- [ ] 2.9 实现 Disable 时逐目标 `Lost(TrackingDisabled)`、运行时不可恢复错误时逐目标 `Lost(ProviderError)` 后进入 Error
- [ ] 2.10 隔离公开事件订阅者异常，确保一个 Hook 抛错不阻止其他订阅者或破坏内部状态
- [ ] 2.11 实现重复 MarkerID 的首个目标保留、冲突观察忽略与 DuplicateMarkerId 诊断，防止 Pose 在物理位置间跳动

## 3. 平台无关自动化测试

- [ ] 3.1 用可控时钟和 Fake Provider 测试首次 Tracked、持续 Tracking、静止持续派发及严格事件顺序
- [ ] 3.2 测试 Lost 宽限内恢复、超时 Lost、Lost 后旧代次样本丢弃及同一码新会话
- [ ] 3.3 测试默认关闭、重复 Enable/Disable、关闭活动目标、初始化失败和运行时 ProviderError 收敛
- [ ] 3.4 测试 30 Hz 默认调度、运行时降频、不同渲染帧率下的派发上限及宽限期内不重复旧 Pose
- [ ] 3.5 测试 MarkerID 精确区分大小写、非 UUID RawPayload、解析失败诊断和运行时无解析器替换入口
- [ ] 3.6 测试至少三个不同 MarkerID 的独立会话、单目标 Lost 隔离、重复 ID 冲突和订阅者异常隔离
- [ ] 3.7 测试所有公开 Hook 均由 Unity 主线程派发，并覆盖关闭与回调并发的竞态

## 4. Quest Provider

- [ ] 4.1 重构 `QuestMarkerProvider`，只接收 MRUK QRCode Trackable，保存活动 Trackable 引用和 RawPayload
- [ ] 4.2 在主线程持续读取活动 Trackable 的当前有效状态与 Transform，而不是只在 TrackableAdded 时复制一次 Pose
- [ ] 4.3 把 Added、持续 Pose、Removed/失效转换为统一原始观察，交由服务内核处理解析和 Lost 宽限
- [ ] 4.4 处理空 Payload、解析失败、同一 Payload 多 Trackable 和 Stop 后 MRUK 在途事件
- [ ] 4.5 添加 Quest Provider 适配测试，覆盖 QR 类型过滤、持续 Transform 更新、移除、重新 Added 和单位 Pose 校正

## 5. PICO 企业服务边界

- [ ] 5.1 定义由平台 Bootstrap/共享层拥有的 PICO 企业服务门面，暴露已绑定状态、QR 扫描与 Marker 回调能力
- [ ] 5.2 将企业服务 Init/Bind/Unbind 从 `PicoMarkerProvider` 移出，确保 Marker Enable/Disable 不调用全局 Unbind
- [ ] 5.3 在 PICO 企业服务未就绪、设备不支持、权限/授权缺失和 SDK 返回失败时映射为 Unavailable/Error 与结构化诊断
- [ ] 5.4 对没有 Marker 回调反注册 API 的路径实现逻辑停用与代次过滤，验证停用后在途回调不会进入公开生命周期
- [ ] 5.5 与 `mr-core-scene-cross-platform` 的平台 Bootstrap/程序集拆分任务对齐所有权，避免重复创建第二套企业服务或跨平台工厂

## 6. PICO QR→ArUco 获取与多目标

- [ ] 6.1 实现单一获取通道 `AwaitingQr → ScanningQr → AwaitingArUco`，Ready/AwaitingQr 时只发 QrScanRequired 而不自动扫码
- [ ] 6.2 实现外部 `BeginQrScan()`、同一时刻单请求约束、失败/取消/空内容 QrScanFailed 和无自动重试
- [ ] 6.3 把 QR RawPayload 交给初始化解析器；解析成功后保存 MarkerID/RawPayload，失败时只发 MarkerIdParseFailed 并回到 AwaitingQr
- [ ] 6.4 实现 ArUco `iMarkerId.ToString()` 精确匹配、PairingMismatch 诊断和继续等待原目标
- [ ] 6.5 实现 15 秒默认 AwaitingArUco 超时、PairingTimedOut、清除待配对 QR 与回到 AwaitingQr
- [ ] 6.6 将 PICO Marker 全量快照转换为每个活动 ID 的可见状态和最新 Pose，不让单目标缺失影响其他目标
- [ ] 6.7 配对成功后创建活动会话并立即释放获取通道，使外部可继续添加其他 MarkerID
- [ ] 6.8 实现 PICO Lost 后只清除该活动会话、不自动扫码；后续外部重新完成 QR→ArUco 时创建新会话
- [ ] 6.9 实现至少三个活动目标的独立 Tracking/Lost，并对平台明确容量错误发出结构化诊断而不设置 70、10 等业务上限
- [ ] 6.10 为 QR 回调、配对超时、Marker 快照和全局关闭之间的竞态添加代次测试

## 7. Pose 统一与组合码校准

- [ ] 7.1 实现 `Compose(arucoWorldPose, arucoToQrOffset)`，按 ArUco 局部位置与旋转偏移输出 QR 逻辑世界 Pose
- [ ] 7.2 保持 Quest QR Pose 为单位校正，并避免把基础 `ArUcoToQrOffset` 与业务 `PlatformOffsetConfig` 混用
- [ ] 7.3 添加 Pose 组合 EditMode 测试，覆盖平移、旋转、两者组合和坐标轴方向
- [ ] 7.4 添加追踪启用期间拒绝修改 Offset、关闭后可重新配置的测试
- [ ] 7.5 制作固定 QR 左/ArUco 右组合码校准记录模板，预留最终位置和旋转参数

## 8. PICO 扫码与 Marker 并发闸门

- [ ] 8.1 实现两种策略：已验证并发时继续活动 Tracking；未知/失败时启用安全暂停策略
- [ ] 8.2 实现安全暂停期间停止 Tracking Hook但冻结 Lost 计时，扫码结束后再恢复观察
- [ ] 8.3 实现恢复时不可见目标从恢复时刻开始新的 Lost 宽限，而不是立即 Lost
- [ ] 8.4 用 Fake PICO 企业服务测试并发路径、暂停路径、扫码失败恢复、多目标暂停和关闭竞态
- [ ] 8.5 **真机闸门**：在 PICO 4 Ultra Enterprise 上验证 `ScanQRCode` 与 `SetMarkerInfoCallback` 是否可并行，记录系统/SDK 版本和结果
- [ ] 8.6 根据 8.5 结果明确目标部署配置使用并发或安全暂停策略，并回填 design/proposal 中对应 `[ASSUMED]`

## 9. 现有下游与 Bootstrap 迁移

- [ ] 9.1 更新 `MarkerTrackingBootstrapper`，按平台创建 Provider 与统一服务，并在初始化时注入 MarkerID 解析器和配置
- [ ] 9.2 将 `MarkerAnchorService` 迁移为 Tracked/Tracking/Lost Hook 的下游消费者，同时保持现有稳定化、注册表解析和内容创建行为属于业务层
- [ ] 9.3 为旧 `MarkerResolved` 调用方提供短期适配器或一次性迁移，并在全部调用方切换后删除旧接口
- [ ] 9.4 更新程序集引用和平台 define 边界，确保平台无关服务不引用 MRUK/PICO 类型，Quest/PICO 包缺失时对应实现可被条件排除
- [ ] 9.5 更新 Demo/诊断入口，允许手动 Enable/Disable、PICO BeginQrScan，并显示 StateChanged 与 Diagnostic 结果但不引入业务判定
- [ ] 9.6 运行现有 `MarkerAnchorServiceTests`、`MarkerStabilizerTests` 及新增 EditMode 测试，修复迁移回归

## 10. 编译、真机与验收

- [ ] 10.1 分别验证 Editor/无平台实现、Quest define 和 PICO define 的程序集编译，确认平台无关代码不泄漏厂商 SDK 符号
- [ ] 10.2 修改脚本后等待 Unity 完成编译并检查 Console，确保零新增编译错误
- [ ] 10.3 在 Quest 目标设备验证权限/能力状态、QR Tracked、30 Hz 持续 Pose、短暂遮挡恢复、Lost 和同码重新识别新会话
- [ ] 10.4 在 PICO 4 Ultra Enterprise 记录系统版本、TOB 授权、企业服务状态和运行模式，验证前置条件失败时正确报告 Unavailable/Error
- [ ] 10.5 在 PICO 验证外部手动扫码、非 UUID RawPayload 解析、ID 匹配/不匹配、15 秒超时、扫码失败和 Lost 后手动重新配对
- [ ] 10.6 在 Quest 与 PICO 分别验证至少三个不同 MarkerID 的独立生命周期和单目标 Lost 隔离
- [ ] 10.7 测量最终固定版式并填写真实 `ArUcoToQrOffset`，记录组合码尺寸、QR/ArUco 朝向与测量方法
- [ ] 10.8 使用同一块静止组合码对比两端逻辑 Pose，确认位置差异不超过 5 cm、角度差异不超过 5°并保存结果
- [ ] 10.9 回填并消解系统版本、权限、PICO TOB 授权和 Offset 两项剩余 `[ASSUMED]`，若不满足则修订设计与部署说明
- [ ] 10.10 运行 `openspec validate cross-platform-marker-tracking` 并完成 Quest/PICO 回归清单后，确认所有 Hook 顺序、错误原因和状态转换与 spec 一致
