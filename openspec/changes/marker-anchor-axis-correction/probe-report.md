# Probe Report: marker-anchor-axis-correction（**未完成，已暂停**）

> Generated: 2026-09-20
> Summary: 1 question asked, 0 decisions, 调查结论已落地；用户说"这个问题后面再处理"，转去做
> `ite-scene-layout-convention`。本文件保存已查实的证据，避免下次重新推一遍。

## 问题陈述（用户）

扫码（二维码 / ArUco）之后建出来的场景，轴向不是按真实 JSON 场景数据来的；怀疑与左右手坐标系有关。
要求对比原工程（`~/Desktop/unity/ite-space-tour`、`ite-space-tour-quest`）与本工程的做法。

## 已查实的事实

### 1. 原工程：标记系与内容锚点之间**没有任何换算**

- 位姿求解：`ite-space-tour-quest/Assets/Samples/QRCodeTracking/Scripts/QrCodeScannerManager.cs:133-140`
  —— `up = 角0→角1`（印刷上边）、`normal = -Cross(up, right)`、`poseRot = LookRotation(normal, up)`。
  即 **X = 印刷左、Y = 印刷上、Z = 出纸面**。
- 该位姿经 `AnchorObject`（仅抖动平滑，`AnchorObject.cs`）原样转发，再原样进
  `tour.ChangeTourObjectTransform(qrCodeSource.position, qrCodeSource.rotation)`
  （`IteSpaceManagerScan.cs:94,159`）。换算为 **identity**。

### 2. 本工程：多了一个固定旋转

- `IteTourObject.ChangeTourObjectTransform`（`Packages/com.uality.ite-tour/Runtime/Core/IteTourObject.cs:134-146`）
  与原工程逐行等价：`TourRoot.local = 被扫 tour 局部矩阵的逆`、`AnchorRoot.local = 标记世界位姿`。
- 唯一差异在 `IteRuntime.SubmitMarkerScan:273` 的 `MarkerFrame.ToContentAnchorPose`
  = 右乘 `Quaternion.Euler(270, 180, 0)`（`MarkerFrame.cs:24`，design D32）。
  它把锚点旋成 **X = 印刷右、Y = 出纸面、Z = 印刷上**。
- D32 目标轴向的依据是"ITE 编辑器摆内容用 X=右、Y=垂直纸面、Z=印刷下（右手系）"
  —— 一条目视/口头确认的约定，**不是从场景 JSON 读出来的**。

### 3. 真实场景 JSON 是 Y-up 的楼层坐标系（关键证据）

缓存路径：`~/Library/Application Support/响堂山/响堂山/IteSpaceScene_thirdDemo/thirdDemo.json`

| tourID | 平移 | 旋转 | displayType | aprilTagID |
|---|---|---|---|---|
| wm0l5qcn_ibd | (0,0,0) | 单位 | regionalTrigger | **null** |
| 4kvhqwvp_12f | (0,0,2) | 绕 Y 45° | normal | **null** |
| earyserh_i5x | (0,0,4) | 单位 | regionalTrigger | **null** |
| azdugaax_xry | (0,0,6) | 单位 | regionalTrigger | **null** |
| hkdaowxy_0hu | (0,0,8) | 绕 Y 90° | regionalTrigger | **null** |

- **沿 Z 每 2 米排，不是沿 X**（与 design.md:170 的"现场沿 Z 轴每 2 米一个"一致；
  用户印象中的"横向沿 X 依次排列"与这份数据不符）。
- **所有 Y = 0、所有旋转只绕 Y** → 这是普通的 Y-up 楼层坐标系，
  **不是**以某张码的印刷面为基准的坐标系。
- 所有 `triggerVolume` 都是 4×4×4、pos 全零，而 tour 间距只有 2 米 → 触发体积必然大面积重叠。
- **所有 `aprilTagID` 都是 null** → PICO 侧的 tag 反查在这份数据上没有任何绑定可用（独立问题，另记）。

### 4. 由此得到的核心判断（待真机验证）

JSON 内容系的 Y 是**世界上方**；而 D32 把锚点 Y 旋成**出纸面**（`MarkerFrameTests` 里明写
"Y 应垂直纸面朝外"）。两者只有在**码平放**时才相容（平放时出纸面 = 上）；
码立着贴墙时锚点 Y 变成水平，整套空间被放倒 90°。原工程的 identity 正好相反：码立着对、平放错。

D32 的真机发现（design.md:288）记的就是"码平放时内容躺倒"——那次是拿平放的码验的。

**所以这不是"identity 还是 Euler(270,180,0)"的二选一：两者都在猜一个 JSON 里根本没有的信息
——码相对 tour 原点是怎么贴的。** JSON 只给了 tour 在空间里的位姿，没给这个 tour 的码印在哪个面、朝哪边。
按 D30 的语义，"贴纸与锚点的物理差异"本来就该落在 `PlatformOffsetConfig`，而 D32 把一个
内容约定的旋转推进了包里。

## 唯一已问、尚未回答的问题

现场那五个码物理上怎么贴的：
(a) 码是**立着**（贴墙/立牌面朝观众）还是**平放**（地面/台面朝上）？
(b) 站在码前看，JSON 的 +Z（下一个 tour 的方向，每 2 米）物理上指向观众的**右手边**，还是**前方/身后**（沿进深）？

AI 当时的推测（未被证实）：码立着、+Z 指向观众右手边。依据是 4 米体积配 2 米间距沿进深排会大面积重叠。
用户答复："这个问题后面在处理。"

## Open assumptions [NEEDS CLARIFICATION]

- [ ] `[ASSUMED]` 上表的 `thirdDemo.json` 是现场实际使用的那份场景数据（来自本机 Editor 运行时缓存，未与线上比对）。
- [ ] `[ASSUMED]` 两端标记位姿契约（X=印刷左、Y=印刷上、Z=出纸面）仍如 D32 所述成立；本次未重新上机实测。
- [ ] `[ASSUMED]` `IteSpaceScene.Tour` 不做 `FlipRotY` 而 `Entity` 做的那处 180° 不一致
      （`CoordinateConversionTests` 锁定的既有行为）与本问题无关——未验证。
- [ ] `[ASSUMED]` ITE 场景 JSON 里不存在"码相对 tour 原点的位姿"字段；仅凭 `thirdDemo.json` 的字段集判断，
      未查 ITE 导出格式文档。

## Suggested next step

- [ ] 回答上面 (a)(b) 两问后继续 probe；在那之前不要动 `MarkerFrame`。
