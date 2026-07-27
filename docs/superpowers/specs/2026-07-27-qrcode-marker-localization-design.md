# 二维码/标记扫描定位方案设计(Quest + PICO)

日期:2026-07-27
项目:MR_Base(Unity MR Template,面向 Quest 与 PICO 全平台)

## 1. 背景与目标

项目要做一套跨 Quest、PICO 两个平台的 MR 应用,核心需求是:**用户举起设备扫描现场贴的一个标记,应用把虚拟内容(信息面板/3D物体等)锚定显示在该标记对应的真实世界位置上**。

## 2. 范围与非目标

- 只做"扫描后锚定内容到真实位置",不做跨会话持久化(每次进入 App 重新扫码即可,不用平台空间锚点做长期持久化)。
- 不做多设备/多用户共享坐标系对齐。
- 目标设备:Quest 3 / 3S,PICO 4 Ultra / PICO 4 Enterprise(已确认拿到 PICO 企业开发授权)。

## 3. 总体架构

调研结论(见第 8 节决策记录):两端都用**平台原生标记识别 API**,不自研摄像头图像处理管线。

```
业务层  MarkerAnchorService (平台无关)
                    │  收到 (anchorId, rawPose) 事件
                    │  查 AnchorDefinition 表 → 应用平台 offset → 实例化/更新内容
                    │
抽象层  IMarkerTrackingProvider
        StartTracking() / OnMarkerResolved(anchorId, pose) / OnMarkerLost(anchorId)
                    │
        ┌───────────┴───────────┐
平台层  QuestMarkerProvider      PicoMarkerProvider
        包 MRUK QR事件           包 Enterprise SDK Marker Tracking 回调
                    │                       │
原生SDK  Meta XR Core SDK + MRUK       PICO Unity Integration SDK(Enterprise能力)
        (QR Code Detection)            (ArUco Marker Tracking, LBE API)
```

编译期用 Build Profile + 自定义 Scripting Define(`MRBASE_QUEST` / `MRBASE_PICO`)隔离两端平台代码,`IMarkerTrackingProvider` 两个实现分别包在 `#if` 里,运行时按当前平台选择对应 provider,业务层不感知平台差异。

## 4. 关键组件

- **`AnchorDefinition`**(配置表,ScriptableObject):
  - `anchorId`:业务自定义的锚点标识,字符串。
  - `questPayload`:Quest 端 QR 码文本内容(即 anchorId 本身或映射值)。
  - `picoMarkerId`:PICO 端 ArUco 数字 ID(0-9,受限于 PICO Demo 现状"最多同时追踪 10 个 ArUco 码"这一已知限制,详见第 8 节)。
  - `contentPrefab`:命中后要实例化/挂载的虚拟内容。

- **`QuestMarkerProvider`**:订阅 `MRUKTrackable.TrackableAdded` / `TrackableRemoved`,过滤 `TrackableType == OVRAnchor.TrackableType.QRCode`,用 `MarkerPayloadString` 反查 `AnchorDefinition.questPayload`,原始 pose 取自 trackable 的 `Transform`。

- **`PicoMarkerProvider`**:注册 `SetMarkerInfoCallback`,回调里拿 ArUco 数字 ID,反查 `AnchorDefinition.picoMarkerId`,原始 pose 来自回调结构体。

- **`MarkerAnchorService`**:唯一对业务代码暴露的入口。
  - 收到 `(anchorId, rawPose)` 后按 `anchorId` 去重(已激活的锚点忽略重复触发)。
  - 计算 `contentPose = rawPose * platformOffset`(见第 5 节),实例化/更新 `contentPrefab`。
  - 内容跟随设备自身 inside-out tracking 维持世界位置,不因标记暂时脱离视野而隐藏(范围声明里已确认不需要持久化,重进 App 重新扫即可)。

## 5. 物理标记物料规范(解决双码对齐问题)

**问题**:Quest 用 QR、PICO 用 ArUco,是两种不同的视觉图案。若各自单独贴一张纸,人工张贴精度只有厘米级,会导致两台设备算出的内容位置对不上。

**方案**:QR 与 ArUco 印在**同一张刚性面板**上,两者之间的相对坐标由**印刷设计稿**决定,不依赖现场测量或人工对齐。

- 设计一套统一面板模板:同一版式,QR 区域与 ArUco 区域的相对坐标固定、已知(印刷精度亚毫米级,远高于人工贴纸精度)。两个图案允许尺寸不同(以各自识别距离要求为准),只要在同一刚性平面上、相对坐标已知即可。
- 现场把整块面板当**一个刚体**张贴一次;面板整体贴歪不影响两台设备之间的位置一致性(两个码同时歪,相对关系不变),只影响面板相对真实目标点的绝对精度。
- 软件里 `platformOffset` 是**全项目级别的两个常量**(`QuestMarkerToTargetOffset`、`PicoMarkerToTargetOffset`),由面板设计稿坐标一次性算出,不随点位变化、不需要逐点位现场标定。
- 退化方案(面板放不下两个码的极端场景):退回逐点位现场标定,用测距工具量出两张独立贴纸的相对偏移并录入配置表,比统一面板方案繁琐,仅作兜底。

## 6. 数据流

```
用户举起设备对准面板 → 平台原生识别(数帧内)→ provider 收到 (rawId, rawPose)
→ 反查 AnchorDefinition
   → 命中:MarkerAnchorService 计算 contentPose,实例化内容,标记 anchorId 已激活
   → 未命中:记录 warning 日志(现场码印错/表未更新),不做用户可见的错误提示
→ 内容依赖设备自身 SLAM 维持世界位置,标记脱离视野不影响已生成内容
```

## 7. 错误处理

- **权限缺失**(Quest Spatial Data Permission / PICO 企业能力未授权):启动时探测,引导用户去系统设置开启,而非静默失败。
- **识别到的 ID 查无 AnchorDefinition**:记录日志,可选调试面板列出"已识别但未匹配"的原始 ID,便于现场排查印刷/配置错误。
- **同一 anchorId 重复触发**:`MarkerAnchorService` 按状态位去重,忽略重复事件。
- **运行时平台探测失败**(部署配置错误,比如构建时混入了错误的 Scripting Define):`Start()` 直接抛出明确异常终止,不静默无行为——这种错误应在测试阶段暴露。

## 8. 测试计划

- **编辑器模式**:实现 `MockMarkerProvider`,用 UI/键盘模拟"识别到某 anchorId",验证 `MarkerAnchorService` 的查表、去重、实例化逻辑,不依赖真机。
- **真机 QA**(Quest、PICO 分别测):
  - 识别成功率、pose 精度(卷尺/激光测距核对误差)。
  - 两台设备扫同一块面板,内容落点是否一致(核心验收标准)。
  - 内容是否随头显走位漂移。
  - 面板脱离视野后内容是否按预期保持(不隐藏)。
  - 权限关闭时是否走提示流程而非崩溃。

## 9. 依赖与项目集成清单

- **Meta XR Core SDK(含 MRUK)**:调研时最新版本 v205.0(2026-07-22)。**⚠️ 实施前必须重新查询官方最新版本,不要直接套用此版本号**——该 SDK 更新频繁,以 https://developers.meta.com/horizon/downloads/package/meta-xr-core-sdk/ 当时页面为准。
- **PICO Unity Integration SDK(Enterprise)**:调研时最新版本 v3.4.0(2026-02-27)。**⚠️ 同样要求实施前重新查询** https://github.com/Pico-Developer/PICO-Unity-Integration-SDK/releases 确认当前最新版。
- Quest 端:OVRManager 开 Scene Support(Required)+ Anchor Support,场景加 Camera Rig + Passthrough Layer 两个 Building Block,MRUK Tracker Configuration 勾选 QR Code Tracking。
- PICO 端:按 Enterprise SDK 文档注册 Marker Tracking 权限与 `SetMarkerInfoCallback`。
- 两套 Build Profile(Quest / PICO),各自 XR Plug-in Management 只启用对应 loader,自定义 Scripting Define 隔离平台专属代码。
- 现有 `Packages/manifest.json` 里的 `com.unity.xr.meta-openxr`、`com.unity.xr.androidxr-openxr` 继续保留(标准 AR Foundation 子系统用途),新增上述两个原生 SDK 不冲突。

## 10. 已知限制与待验证项

- PICO 官方 ArUco Marker Tracking Demo 显示"最多同时追踪 10 个不同 ArUco 码",这限制了 `AnchorDefinition.picoMarkerId` 的可用取值范围,如果点位数量超过 10 个需要向 PICO 官方确认是否可扩展,或分区域复用 ID(同一时间只在同一物理空间出现的点位可复用同一批 ID)。
- Meta MRUK QR 检测文档提到"QR 码需要相对大且离设备近"才能可靠识别,PICO ArUco 的最佳识别尺寸/距离未经实测确认,面板设计需要现场实测两者的可靠识别距离,可能导致面板尺寸比预想更大。
- 两个原生 SDK 均更新频繁(Meta 205.x、PICO 3.4.x),API 细节(类名、回调签名)可能随版本变化,实施阶段需以当时最新官方文档为准,本文档中列出的类名/方法名(`MRUKTrackable`、`OVRAnchor.TrackableType.QRCode`、`SetMarkerInfoCallback` 等)以调研时公开资料为准,实施前应重新核实。
- 面板物料的耐候性(室内/室外、是否反光、是否褪色影响长期识别率)未纳入本设计范围,如现场环境特殊需额外评估。

## 11. 决策记录(供后续复查)

调研过程中评估过三条路线,记录否决理由避免重复踩坑:

1. **AR Foundation ARTrackedImageManager(两端通用)**:已否决。两端均未实现 `XRImageTrackingSubsystem`——Meta 专门另起炉灶做 MRUK QR 而非复用该子系统;PICO 官方论坛明确"Image Tracking via AR Foundation is not supported"。
2. **两端统一自研摄像头管线(ZXing.Net 解码 QR + 自写位姿解算,基于各自裸摄像头 API)**:已否决。Quest 侧有现成参考实现(`xrdevrob/QuestCameraKit` 的 QR Code Tracking 示例,基于 Passthrough Camera API),风险低;但 **PICO 侧没有任何已知的现成实现或公开验证**,`PXR_CameraImage` 裸帧访问虽然企业授权下可用,但帧格式、内参可用性、识别稳定性均未经验证,风险不可控,故整体否决,回退到两端原生方案。
3. **两端统一 ArUco(自写 Quest 端 ArUco 识别)**:已否决。Quest 端自写 ArUco 需要引入 OpenCVForUnity(付费)或 OpenCvSharp(免费但需自行处理 IL2CPP/arm64 native 插件打包),工程复杂度高于自写 QR 方案,且同样存在"PICO 侧统一自研"的风险,故一并否决。

最终采用:两端原生 API(Quest MRUK QR / PICO ArUco LBE API)+ 统一物理面板解决对齐问题(第 5 节)。
