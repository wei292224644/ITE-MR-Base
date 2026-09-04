## Why

`IMarkerTrackingProvider.MarkerResolved(string, Pose)` 这个契约让平台不对称漏进了业务层：同一个 `string` 在 Quest 上是 QR 原文、在 PICO 上是整数 ID 的十进制串，于是 `AnchorRegistry.cs:18` 只能用 `||` 双条件去猜身份——一个决策被藏在了两个处理器的相互作用里。

更硬的问题是节奏。Quest 只在 `TrackableAdded` 时复制一次 Pose（`QuestMarkerProvider.cs:35`），PICO 则持续推快照。而下游 `MarkerStabilizer` 用 `stableFrameThreshold = 30` **帧**判稳（`MarkerStabilizer.cs:18,60`）——在 PICO 的 6 Hz 采样下是 5 秒，在 Quest 上则**永远达不到**，`Stabilized` 不会触发。这不是推测。MRUK 包源码（`Library/PackageCache/com.meta.xr.mrutilitykit@2a23a4eea58d/Core/Scripts/MRUK.Trackers.cs`）证实：`:162` / `:174` 只有 `TrackableAdded` 与 `TrackableRemoved` 两个 `UnityEvent`，没有 `TrackableUpdated`；`:327-344` 对已存在的 key 显式去重后直接 `return`，故每个 trackable 一生只触发一次 Added；`:346-350` 的位姿更新只改组件字段、不发事件。结论是 **Quest 的「标记 → 内容」生产链路从未在真机跑通过**——不是位姿不准，是整条链死掉。

业务层要写"扫到标就出内容"这么一句话，现在得同时知道自己跑在哪个平台、拿到的 string 是什么语义、事件会来一次还是每帧来。

## What Changes

- **BREAKING** `IMarkerTrackingProvider` 替换为 `IMarkerObservationSource`：平台实现只提供 `Poll()` 纯查询与 `Open()`/`Close()`，**不再自己发事件**。平台差异全部收敛在 `Poll()` 内。
- 新增 `MarkerTrackingSession`（纯 C#，平台无关）：唯一发事件的地方，统一承担 Poll 节流、身份收敛、Lost 时间滞回、暂停/恢复。
- **BREAKING** 事件载荷从裸 `string` 换成分层的 `MarkerIdentity`（`RawPayload` / `NativeId` / `LogicalId`）。`AnchorRegistry` 的 `||` 双条件随之删除，改为按 `LogicalId` 单条件匹配。
- **BREAKING** Quest 侧从"Added 时读一次 Transform"改为持有活动 `MRUKTrackable`、每次 `Poll()` 读最新 Transform/IsTracked。这是**行为改变**，Quest 已取得真机证据的路径需重测。
- **BREAKING** `MarkerStabilizer` 的稳定阈值从帧数改为秒（`stableFrameThreshold: int` → `stableDurationSeconds: float`）。
- `MarkerLost` 统一走 1.0 s 时间滞回，两端同一套判定；短暂遮挡不再被当成"离开"。
- 新增 `Pause()` / `Resume()`，与 `Open()` / `Close()` 分离。现在想暂停扫描只能调 `StopTracking()`，而它在 PICO 上会 `UnBindEnterpriseService()` 解绑整个企业服务（`PicoMarkerProvider.cs:69`）。
- 把 `IMarkerIdParser` 从探针提升到生产。该接口的类注释（`MarkerIdParser.cs:41-44`）本就写明 "future URL, JSON, or lookup rules belong behind the same interface in a later production change"——本 change 即是。

## Capabilities

### New Capabilities

- `unified-marker-tracking-contract`: 跨平台标记追踪的公共契约。规定观测源的纯查询边界、身份分层与收敛、统一派发节奏、Lost 滞回判定、暂停/恢复语义，使业务层在 Quest 与 PICO 上订阅同一套事件并得到同一种语义。

### Modified Capabilities

（无。`openspec/specs/` 当前为空，尚无已归档主 spec 需要 delta。）

## Impact

**公共契约**
- `Assets/Scripts/Localization/IMarkerTrackingProvider.cs` — 替换
- 新增 `MarkerTrackingSession.cs`、`MarkerObservation.cs`、`MarkerIdentity.cs`

**平台实现**
- `Native/QuestMarkerProvider.cs`、`Native/PicoMarkerProvider.cs`、`MockMarkerProvider.cs`

**消费方与装配**
- `MarkerAnchorService.cs`、`IteHost/MarkerSourceAdapter.cs`（ITE 导览包）、`Native/MarkerTrackingBootstrapper.cs`、`AnchorRegistry.cs`、`MarkerStabilizer.cs`、`DemoMarkerTrigger.cs`

**测试**
- 改：`MarkerStabilizerTests.cs`、`MarkerAnchorServiceTests.cs`、`MockMarkerProviderTests.cs`、`Ite/MarkerSourceAdapterTests.cs`
- 增：`MarkerTrackingSessionTests.cs`

**PICO 数据源切换**
- `PicoMarkerProvider.cs:79` 今天仍从企业 TOB 的 `SetMarkerInfoCallback` 取快照；AprilTag 检测只存在于探针 `Native/Probe/PicoQrCameraProbe.cs`。本 change 要在同一文件上同时完成换源（TOB → AprilTag）与换契约（事件 → `Poll()`），需拆成两步提交。

**已知前置缺口**
- `MarkerTrackingBootstrapper.cs:6` 默认引用的 `anchor_registry.json` 在仓库中不存在（无 `StreamingAssets/`），生产链路接通后 registry 为空。
