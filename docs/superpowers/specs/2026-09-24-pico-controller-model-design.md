# PICO 手柄模型错位：换用 SDK 自带手柄模型

> 2026-09-24。bug 修复，用户选择方案 A、直接改；本文只记决策。
> 问题来源：`docs/handoff/2026-09-24-pico-controller-pose-mismatch.md`。

## 问题与根因

在 PICO 上，虚拟手柄（XRI 通用模型）比真手柄朝手腕方向、往下偏了约一个手柄头的长度（真机截图，
2026-09-24）。射线起点落在真手柄机身中段。朝向基本对得上。和扫码偏移不是同一个根因（见
`2026-09-24-pico-camera-extrinsic-design.md`）。

- rig 里 `Left/Right Controller` 的 `TrackedPoseDriver` 按 `pointerPosition → devicePosition` 的顺序取值，
  优先取 aim 位姿。XRI 模型挂在 `(0, 0, −0.05)`、绕 Y 转 180°，这组偏移是按 OpenXR aim 位姿
  （原点在手柄前端）调的。
- PICO 走 `PXR_Loader`，手柄被注册成 `PXR_Controller` 布局（`PXR_Loader.cs:62`）。这个布局**没有
  `pointerPosition`**，所以 XRI 的 `Vector3FallbackComposite` 会退到 `devicePosition`，它的原点在机身中段。
- 两个模型在手柄坐标系里的包围盒：
  - SDK 的 `PICO 4U R` 模型挂在 device 位姿上、偏移为 0，中心在 (0, −0.9, −0.9) cm；
  - XRI 模型中心在 (0, −2.9, −7.7) cm。
  - 所以在 PICO 上，XRI 模型比真手柄靠后约 7 cm、靠下约 2 cm，和截图对得上。

## 决策

**D1：PICO 上换成 SDK 自带的手柄模型，Quest 保持不变。** SDK 的 `Resources/Prefabs/Left|RightControllerModel`
带有 `PXR_ControllerLoader`，会按实际连接的手柄型号（4U / 4 / Neo3 / G3）加载模型，原点正对
`devicePosition`，手柄断开和重连也由它处理。
- 否决「PICO 上把 XRI 模型往前挪 7 cm、往上挪 2 cm」：这组数是从一张截图和包围盒量出来的，每种手柄型号都不一样，
  外形也还是 Quest 手柄。
- 否决「在 PICO 上合成出一个 aim 位姿」：PXR 原生布局没有这个数据，合成只能靠写死的常量。

**D2：换装点是组件加序列化引用，平台判断留在 `PlatformRuntime`。**
- `PlatformControllerModel` 挂在 rig（`XROrigin_Base.prefab`）的 Left/Right Controller 上，序列化字段是
  `leftHand` 和 `defaultVisual`。
- 它在 `Awake` 里向 `PlatformRuntime.LoadControllerModel` 取模型：返回 null 时什么都不做；返回模型时，
  隐藏默认模型，在本节点下以原点对齐挂上平台模型。
- 否决「在 `PlatformRuntime` 里按名字扫场景」：rig 由项目自己维护，用序列化引用可以明确指定目标，
  改名也不会悄悄失效。

**D3：出错时报 Error，不静默失败。**
- PICO 上找不到 SDK 模型资源时，`PlatformRuntime` 报 `LogError`，手柄沿用 XRI 模型。
- `defaultVisual` 没有指定时，`PlatformControllerModel` 报 `LogError`，两个模型会叠在一起，一眼就能看出来。

## 不在范围

- 射线：起点仍在 `devicePosition`，也就是机身中段，和 PICO 系统自己的射线一致。
- `Teleport Interactor` 的本地偏移 `(0, −0.02, −0.035)`：同样是按 aim 位姿调的，要看到实际问题再处理。

## 验证

- EditMode：`MRBase.Build.Editor.Tests`（`LayoutConventionTests`）6/6 通过。组件只有一处分支，没有单独写测试。
- 真机（待打包）：手柄拿在眼前 30–40 cm，截图，PICO 手柄模型应该和真手柄重合。另外检查左右手、
  手柄断开再连上的情况。
