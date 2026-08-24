# pico-camera-fiducial-tracking 打印夹具

由 `Tools/MarkerFixtures/generate_pico_qr_apriltag_fixtures.swift` 生成：

```
swift Tools/MarkerFixtures/generate_pico_qr_apriltag_fixtures.swift docs/test-fixtures/pico-camera-fiducial-tracking
```

## 打印

- **A3 横版为首选**，双 A4 为备用（备用版的 QR→AprilTag 中心距由手工拼页决定，必须实测）。
- **100% Actual Size，关掉 Fit / Scale。** 缩放了尺寸就不对，位姿全错且现场看不出来。

## 打印后必须实测

位姿求解吃的是**检测四边形**，不是图幅。tagStandard41h12 的 `width_at_border = 5` 而
`total_width = 9`，检测框只占图幅的 **5/9**：

| | 标称 |
|---|---|
| 标图幅面 | 160 mm |
| **检测四边形**（白框方块，外沿到外沿） | **88.9 mm** |
| QR 外框（含 4 模块静区） | 160 mm |
| QR 中心 → AprilTag 中心 | 210 mm |

量到的检测四边形边长填进探针的 `tagSizeMeters`（单位米）。**一律用实测值，不用标称值。**

## 标图来源

`Tools/MarkerFixtures/tags/tag41_12_*.png` 取自 AprilRobotics/apriltag-imgs 的官方 9×9 位图，
已逐模块比对过原生库 `apriltag_to_image` 的输出。

标在 PDF 里是**逐模块填的矢量矩形**，不是嵌入位图。9x9 的位图拉到 160 mm 等效约 1.4 DPI，
`/Interpolate false` 只是个建议，阅读器和打印机 RIP 可以不理——一旦被平滑，糊掉的正是角点
精修要吃的那条边。矢量在任何打印分辨率下边缘都精确。

生成器每次运行都会从预览 PNG 把标采回来与源图逐模块比对，不一致直接失败。这道守卫针对的是
上下镜像：镜像的码 apriltag 解不出，而纸面上肉眼完全看不出来，代价是一次白打印加一轮白测。
守卫本身已用「把行序反过来」反向验证过会失败，输出也用不经 CoreGraphics 的独立 PNG 解码器
核对过。

## PDF SHA-256

```
3f7826f73a5a6a90056166354f3069cd96b1be2c19e4e2ff60f6c4fa2788d46a  pico_qr_apriltag_static_id0_a3_landscape.pdf
864a853415715510ae6fa4fb5c147614ff5fdf859d0b28496438d341c85473a7  pico_qr_apriltag_static_id0_a4.pdf
d9dd1e1d134c3382fb3b80f2e782cac9a398c6703d68c333c335c16671833444  pico_qr_apriltag_dynamic_id250_a3_landscape.pdf
c1e0a189b7e051972cdce86f48832730f57ac611e4b3a2144b6b6054c627e03c  pico_qr_apriltag_dynamic_id250_a4.pdf
```

`docs/test-fixtures/cross-platform-marker-tracking/` 下的 ArUco 夹具属于已结论为
`not_feasible` 的 PICO 原生路线，作为证据保留，不再用于本 change。
