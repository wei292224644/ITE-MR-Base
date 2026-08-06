# Quest 真机 smoke 证据（2026-08-04）

## 结论

Quest 3 上的原生 QR 路径已完成一次 smoke：MRUK QR 能力、Scene 权限、诊断入口、Console 镜像、MarkerID 解析、有效 6DOF Pose 和 persistentDataPath JSONL 均有真机证据。因此 `tasks.md` 的 7.2 可以关闭。

这次 smoke 只证明 Quest Probe 架构路径可运行，不等同于 10 轮独立获取，也不构成跨 Quest/PICO 的最终可行性结论。一次性触发和重复扫描辅助定位仍是后续 change 的业务模式，本次没有实现。

## 构建

- 最终 APK：`Builds/MarkerProbe/Quest/MarkerProbe-Quest.apk`
- 构建时间：2026-08-04 14:58:54 +08:00
- 大小：263,633,061 bytes
- SHA-256：`8aea0e6c944c802afc5cea7247038978955ccdbb6a9109f1777f1168632fc606`
- Unity 构建结果：Succeeded，耗时 00:05:24.9162910
- 最终包包含 smoke 后修复的 Quest 样本分类字段；该字段修复通过 EditMode 测试，但没有要求操作者为此重复扫描。

## 原始证据位置

原始设备文件只保留在本机 `Builds/MarkerProbe/Logs/`，不直接入库：

- JSONL：`Builds/MarkerProbe/Logs/Quest-device-files-20260804-143104/files/MarkerProbe/20260804T063011.605Z-6a5122c51a58456dbeed864f51dcfebb.jsonl`
- 实时 logcat：`Builds/MarkerProbe/Logs/Quest-20260804-142811-logcat.log`
- 安装、启动与拉取记录：`Builds/MarkerProbe/Logs/Quest-20260804-142811-adb.log`

会话 ID：`20260804T063011.605Z-6a5122c51a58456dbeed864f51dcfebb`。

## 真机事实

- 设备：Oculus Quest 3；Android 14 / API 34；Unity 6000.4.4f1；OpenXR Input 正在运行，Tracking Origin 为 Floor。
- MRUK 运行时启动成功：Scene 权限为 true，World Lock 为 false。
- MRUK 创建 QR spatial context 成功；Anchor Tracker 配置中 `QRCodeTrackingEnabled=true`。
- 预检六项均为 true：MRUK 实例、QR 支持、Scene 权限、请求前/后配置以及预检时 Active 状态。
- 使用夹具 `static-0-a3`，期望 MarkerID 为 `0`；日志只观察到 MarkerID `0`。
- 26 条 Quest Trackable 观测均解析成功、Pose 校验成功；其中 `IsTracked=true` 23 条、false 3 条。
- 事件 sequence 从 1 到 31 连续；会话尾 `totalEventCount=31`、`droppedEventCount=0`、结束原因为 `OperatorStopped`。
- 事件构成：session started/ended 各 1、run started/ended 各 1、preflight 1、existing trackable 1、状态变化观测 25。
- 首个 tracked 样本距本轮开始约 1,947.48 ms。
- 状态变化观测间隔最小约 14.25 ms、中位约 501.35 ms、最大约 10,009.99 ms。MRUK 当前公开 API 提供的是状态变化等价观测，因此这些数据不能解释为底层相机或原生回调固定频率。
- RawPayload 原文默认关闭，只记录 UTF-8 长度和 SHA-256。

## smoke 后修复

原始 JSONL 中 MarkerID 与 expectedMarkerId 都为 `0`，但 `markerMatchesCurrentRun=false`。这是日志样本在补入当前 run 上下文之前执行分类造成的字段错误，不是 MRUK 扫描或 MarkerID 解析失败。

修复后由统一分类入口补入 runId、expectedMarkerId/expectedMarkerIds，并按 `not_tracked`、`invalid_pose`、`valid_id_mismatch`、`valid_exact_match` 等事实分类。EditMode `MRBase.Localization.Tests` 最终结果为 32/32 通过；最终 APK 已包含修复。

## 已知但不在本次处理的事实

- PICO managed package 在 Quest 启动时尝试加载 `com.psmart.aosoperation.SysActivity`，产生非致命 `ClassNotFoundException`；应用随后继续启动 MRUK 并完成扫描。按当前 scope 暂不修改第三方包，后续可单独收敛跨平台包初始化噪声。
- 夹具会话里的 ArUco 实测边长、QR→ArUco 实测中心距、平整度与安装说明为空，因此 8.1/8.1b 不满足，Pose 验收不能引用本轮打印尺寸。
- 本次目标在会话开始时已作为 existing Trackable 出现，没有真实 Removed/Reappeared 事件；因此不计入 8.2 的 10 轮独立获取，也不能关闭 8.3。
- PICO 端尚未完成，所以 7.4、9.x、10.x 继续保持未完成。
