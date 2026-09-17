# marker-sheets 打印夹具（thirdDemo，AprilTag ID 0 起递增）

现场铺设用：每个 tour 一份 A4 PDF，**第 1 页二维码，第 2 页 AprilTag**，两页贴同一个展位。

由 `Tools/MarkerFixtures/generate_marker_sheets.swift` 生成：

```
swift Tools/MarkerFixtures/generate_marker_sheets.swift docs/test-fixtures/marker-sheets \
  'wm0l5qcn_ibd,4kvhqwvp_12f,earyserh_i5x,azdugaax_xry,hkdaowxy_0hu' \
  '******{tour}******'
```

参数：

- 第 2 个：tourID 清单（逗号分隔），**清单里的位置就是 AprilTag ID**；也可以直接给数量。
- 第 3 个：QR 载荷模板。`{tour}` 换成 tourID，`{id}` 换成 AprilTag ID。

tourID 清单取自线上空间场景描述
`https://ite-spatial-config.uality.cn/thirdDemo.zip` → `thirdDemo.json` 的 `tours[].tourID`，
按文件顺序。5 个 tour 全部 `isEnabled: true`、`displayType: regionalTrigger`。
场景描述改了 tour 列表就按新清单重出。

## 对应表

| 文件 | QR 载荷 | AprilTag ID | tourID |
|---|---|---|---|
| `marker_id00_a4.pdf` | `******wm0l5qcn_ibd******` | 0 | `wm0l5qcn_ibd` |
| `marker_id01_a4.pdf` | `******4kvhqwvp_12f******` | 1 | `4kvhqwvp_12f` |
| `marker_id02_a4.pdf` | `******earyserh_i5x******` | 2 | `earyserh_i5x` |
| `marker_id03_a4.pdf` | `******azdugaax_xry******` | 3 | `azdugaax_xry` |
| `marker_id04_a4.pdf` | `******hkdaowxy_0hu******` | 4 | `hkdaowxy_0hu` |

## ⚠️ AprilTag 这半边在内容侧补字段之前走不通

线上 `thirdDemo.json` 的 tour 对象只有
`isEnabled / displayType / triggerVolume / transform / tourID`，**没有 `aprilTagID`**。

AprilTag 反查（`MarkerIdentity.TryFindByTag`）按 `IteSpaceScene.Tour.aprilTagID` 找 tour；
字段缺省即为 null，null 不参与反查。所以在内容方补上字段之前，PICO 扫到任何标都只会打一条
「标记解析出的 Tour 不在场景中」。要补的就是上表后两列：

```json
{ "tourID": "wm0l5qcn_ibd", "aprilTagID": 0, ... }
```

QR 那半边（Quest）不依赖这个字段：外壳里解出的就是 tourID，直接按 `tourID` 匹配。

## 标族是 AprilTag，不是 ArUco

当前检测器是 `AprilTagDetectorCore`，标族 `tagStandard41h12`（见
`Assets/Scripts/Localization/Native/PicoFiducialObservationSource.cs`）。
ArUco 路线已结论为 `not_feasible`，证据保留在
`docs/test-fixtures/cross-platform-marker-tracking/`，代码里没有解码路径 —— 印 ArUco 检不出。

## 与 `pico-camera-fiducial-tracking/` 的分工

那一份是 PICO 相机探针的**定版夹具**：固定 id 0/250、含 A3 横版、QR 模块矩阵写死以保证
跨机器逐字节可复现。它记录一次已归档的实测，不动。这一份面向现场：按 tour 出、只出 A4、
QR 载荷可换格式。

## 打印

**100% Actual Size，关掉 Fit / Scale。** 缩放了尺寸就不对，位姿全错且现场看不出来。

## 打印后必须实测

位姿求解吃的是**检测四边形**，不是图幅。`tagStandard41h12` 的 `width_at_border = 5` 而
`total_width = 9`，检测框只占图幅的 **5/9**：

| | 标称 |
|---|---|
| 标图幅面 | 160 mm |
| **检测四边形**（白框方块，外沿到外沿） | **88.9 mm** |
| QR 外框（含 4 模块静区） | 160 mm |

量到的检测四边形边长填进 `PicoFiducialObservationSource.tagSizeMeters`（单位米）。
**一律用实测值，不用标称值。**

## QR 载荷格式

`******{tourID}******`（六个星号包住 tourID），与包侧 `QrPayloadFormat.TryParseTourId`
的 `^\*{6}(.*?)\*{6}$` 对齐。载荷原文印在 QR 页上（`payload: ...`），现场扫出来的
`rawPayload` 与那一行对不上，就是解析链出了问题，不必回来翻生成命令。

## 生成器自检

每份出图后跑两道检查，任一不过直接失败：

1. **标未镜像** —— 从预览 PNG 把标采回来，与 `Tools/MarkerFixtures/tags/` 的官方 9×9 源图
   逐模块比。CG 的 y 轴朝上而位图第 0 行在顶部，少翻一次坐标系标就是上下镜像的；
   镜像不是旋转，apriltag 解不了，而纸面上肉眼完全看不出。代价是一次白打印加一轮白测。
2. **QR 能解回原载荷** —— 用 `CIDetector` 把刚编的码解一遍，与原载荷比。
   针对静区剥离剥过头或载荷编码出错，两者印出来都是「扫不出」，纸面上同样看不出。

标与 QR 在 PDF 里都是**逐模块填的矢量矩形**，不是嵌入位图。9×9 位图拉到 160 mm 等效约
1.4 DPI，`/Interpolate false` 只是个建议，打印机 RIP 可以不理 —— 一旦被平滑，糊掉的正是
角点精修要吃的那条边。

## 标图来源

`Tools/MarkerFixtures/tags/tag41_12_*.png` 取自 AprilRobotics/apriltag-imgs 的官方 9×9 位图。
id 0 与本仓库既有的那份已验证副本 SHA-256 一致（`ad5bc161…`），来源已确认。

## PDF SHA-256

```
59d3a8152caf8d0f61f0bce3a0b37a22c575e3f4f3397a45adb09462512c69e8  marker_id00_a4.pdf
3a097885c8d968ccb399df860d3018260cfb25c0fa92d6f19b0b560f5d772512  marker_id01_a4.pdf
831e22e0cf5386653c51c6ac1cfc870849f4127e69089eebc56f6bd93a7a3d2e  marker_id02_a4.pdf
5496059ce22c9f9179c3421176fda42e2d36deff6d04d536b14b14d3c4c09ee6  marker_id03_a4.pdf
3c05fe2b80e55a3e7ffa9a63f38b0103d9b1adb23879099be8b0178b5aa140c7  marker_id04_a4.pdf
```
