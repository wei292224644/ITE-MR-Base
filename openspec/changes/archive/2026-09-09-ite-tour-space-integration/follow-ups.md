# 后续 change 清单

本 change 编辑器 Play 验收已通过。下面是查实但未修、或明确划出本 change 边界的事项。

## 下载与缓存（tasks 8.2）

- **`DownloadHandlerBuffer` 全量入内存**：`ZipContentDownloader.DownloadAndExtractAsync` 用 `UnityWebRequest.Get` + `DownloadHandlerBuffer`，`4kvhqwvp_12f` zip 约 151 MB，再 `File.WriteAllBytes` 一份，峰值约 300 MB。编辑器可承受，真机存疑。改流式落盘是独立 change。
- **空间场景包无版本校验**：`FetchSpaceSceneAsync` 在 `networkAvailable=true` 时每次冷启动都重下 `thirdDemo.zip`（358 KB）。与 tour 包的 `TourVersionCache` 不一致，`IteContentPipeline.cs` 已有 TODO。

## 验收边界（tasks 8.3）

`ComponentRegistry` 11 个组件类型中，thirdDemo 覆盖 7 个。下列 4 个本 change **未验证**，等实际内容用到时再验，MUST NOT 造合成数据：

- `VideoPlane`
- `PrimitiveModelRender`
- `ApproximateTrigger`（包内近距离判定本就未实现）
- `PlayAudioAction`

## 真机接入（tasks 8.4 / design D9）

单开 change，形状约束：

- 接真实观测源（`QuestObservationSource` / `PicoFiducialObservationSource`），会话仍由外部注入 `IteHostBootstrap.AttachMarkerSession`
- 相机归属：产品场景的 XR 相机替换 Editor Rig
- `IteTourSpace` 的产品版本进 `EditorBuildSettings` 与 `BuildScript`
- 290 MB 内容在设备上的落盘与首启策略
- 编辑器 Standalone 着色器变体 ≠ Android + XR 单通道立体；黑模/粉模/单眼可见只在设备上暴露

## Play 验收中记下、本次不改的行为

- Tour 触发体积的 `BoxCollider.isTrigger = false`；相机胶囊是 trigger，因此 `OnTriggerEnter` 仍能打到 `TourVolumeTrigger`。
- 把 kinematic 相机瞬移进体积常常**不会**派发 `OnTriggerEnter`。本次验收靠禁用/再启用胶囊来补一次进入。走位控制器连续移动不受影响。
- 扫码会移动 `AnchorRoot`，整个空间一起锚定。验收时不能再用 space json 的原始世界坐标去找体积。
- 假扫码 `feedSeconds=0.35` 足够让 `Enable()` 完成后再收到同一观测，从而在同一次投喂里走出 `Reanchor consumesSecondAnchor`。这正是 7.7 的路径，不是多余激活。
