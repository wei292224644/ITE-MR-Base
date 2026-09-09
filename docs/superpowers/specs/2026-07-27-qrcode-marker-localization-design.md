# 二维码/标记扫描定位方案设计(Quest + PICO)

> **过时（2026-09-09）**：本文是 2026-07-27 的原设计（Quest MRUK QR + PICO ArUco + `IMarkerTrackingProvider` + `AnchorRegistry`）。PICO 原生 ArUco 已被真机证伪，生产链已下线。现行契约见 `openspec/specs/unified-marker-tracking-contract/spec.md`。

日期:2026-07-27
项目:MR_Base(Unity MR Template,面向 Quest 与 PICO 全平台)

## 1. 背景与目标

项目要做一套跨 Quest、PICO 两个平台的 MR 应用,核心需求是:**用户举起设备扫描现场贴的一个标记,应用把虚拟内容(信息面板/3D物体等)锚定显示在该标记对应的真实世界位置上**。

## 2. 范围与非目标

- 只做"扫描后锚定内容到真实位置",不做跨会话持久化(每次进入 App 重新扫码即可,不用平台空间锚点做长期持久化)。
- 不做多设备/多用户共享坐标系对齐。
- 目标设备:Quest 3 / 3S,PICO 4 Ultra / PICO 4 Enterprise(已确认拿到 PICO 企业开发授权)。

## 3. 总体架构

调研结论(见第 11 节决策记录):两端都用**平台原生标记识别 API**,不自研摄像头图像处理管线。

架构参考了同项目组 `ite-space-tour` 项目(`Assets/Scripts/ITE`、`Assets/Scripts/AnchorObject.cs` 等)已经跑通的一套模式——扫描稳定化、网络拉取配置表+动态下载内容、"同一个类多个实例"的实体装配——不重新发明,直接照该项目验证过的形状改造:

```
业务层  MarkerAnchorService (平台无关)
                    │  收到 provider 原始 (rawId, rawPose) 事件
                    ▼
        MarkerStabilizer(位置/角度抖动阈值 + 连续N帧稳定才确认,借鉴 AnchorObject.cs)
                    │  稳定后吐出 (rawId, stablePose)
                    ▼
        AnchorRegistry.TryResolve(rawId) → AnchorEntityData
                    │  (表数据来自网络,见下方 AnchorRegistry/IAnchorDataSource)
                    ▼
        实例化/装配 AnchorEntity(MonoBehaviour,同一个类多个实例,async Create() 自我装配+下载内容)
                    │
抽象层  IMarkerTrackingProvider
        StartTracking() / OnMarkerResolved(rawId, pose) / OnMarkerLost(rawId)
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

- **`AnchorEntityData`**(纯数据 DTO,Newtonsoft 反序列化自网络 JSON,不是 ScriptableObject):
  - `anchorId`:业务自定义的锚点标识,字符串。
  - `questPayload`:Quest 端 QR 码文本内容。
  - `picoMarkerId`:PICO 端 ArUco 数字 ID(0-9,受限于 PICO Demo"最多同时追踪 10 个 ArUco 码",详见第 10 节)。
  - `contentUrl` / `contentVersion`:命中后要下载展示的内容地址与版本号。版本号用于本地缓存比对(参照 `ite-space-tour` 的 `IteSpaceManagerAssets.cs`:先打版本查询接口比对本地 `PlayerPrefs` 记录的版本,不一致才重新下载),避免每次扫描都重新下载同一份内容。

- **`IAnchorDataSource`**(网络数据源抽象):`Task<List<AnchorEntityData>> FetchAsync()`。当前还没有真实后端,先实现 `LocalJsonAnchorDataSource`(读本地 JSON 文件模拟接口返回值)让流程跑通;以后有真实接口了实现 `HttpAnchorDataSource`(照抄 `ite-space-tour` 的 `FetchUtils.FetchJsonAsync<T>` 模式)替换即可,业务层不用改。

- **`AnchorRegistry`**:运行时对象(不是 ScriptableObject 资产),启动时调用 `LoadAsync(IAnchorDataSource)` 从网络拉表填充,`TryResolve(rawId, out AnchorEntityData)` 查表逻辑同旧设计不变(按 `questPayload` 或 `picoMarkerId.ToString()` 匹配)。

- **`AnchorEntity`**(MonoBehaviour,同一个类多个实例,对应"一个 class 对应一个扫描实体"的设计要求):照搬 `ite-space-tour` 的 `IteTourObject` 模式——不是数据行,是真正的运行时对象,靠 `async Task Create(AnchorEntityData data, Pose pose)` 自我装配:检查本地缓存版本、按需下载内容、下载完实例化展示、把自己摆到给定 pose。内容摆位靠 Transform 父子关系(挂在一个定位到 pose 的锚点物体下),不手写矩阵合成。

- **`MarkerStabilizer`**(新增,借鉴 `AnchorObject.cs` 的稳定化逻辑,平台无关的纯 C# 类):原生 API 吐出来的 pose 是逐帧抖动的,不能直接拿来用。对每个 `rawId` 维护一份"当前平滑目标 pose + 连续稳定帧计数",用位置/角度阈值判断是否还在抖动,达到阈值帧数(如 30 帧)才真正判定为"稳定",此时才吐出 `Stabilized(rawId, pose)` 事件供后续查表/实例化使用。纯逻辑类,可脱离设备单元测试(喂一串模拟 pose 序列,断言第几帧触发)。

- **`QuestMarkerProvider`**:订阅 `MRUKTrackable.TrackableAdded` / `TrackableRemoved`,过滤 `TrackableType == OVRAnchor.TrackableType.QRCode`,把 `MarkerPayloadString` 当 rawId、trackable 的 `Transform` 当原始 pose,原样上抛(不在这层做稳定化或查表,保持 provider"只做平台 I/O"的单一职责)。

- **`PicoMarkerProvider`**:注册 `SetMarkerInfoCallback`,回调里把 ArUco 数字 ID 转字符串当 rawId,原始 pose 来自回调结构体,同样原样上抛。

- **`MarkerAnchorService`**:唯一对业务代码暴露的入口,串起 `MarkerStabilizer → AnchorRegistry.TryResolve → AnchorEntity.Create`。
  - 收到 provider 的 `(rawId, rawPose)` 先喂给 `MarkerStabilizer`,只有稳定事件才继续走下一步。
  - 按 `rawId` 去重(已激活的锚点忽略重复触发)。
  - `AnchorRegistry.TryResolve` 未命中:记录日志,不做处理。
  - 命中:实例化 `AnchorEntity`,调用 `Create(data, stablePose * platformOffset)`(平台 offset 见第 5 节)。
  - 内容跟随设备自身 inside-out tracking 维持世界位置,不因标记暂时脱离视野而隐藏(范围声明里已确认不需要持久化,重进 App 重新扫即可)。

- **工具类复用**:`ite-space-tour` 项目里的 `FileUtils`(下载/解压/缓存/glTF加载)、`FetchUtils`(JSON网络请求)、`StaticInstance<T>`(单例基类)三个通用工具类已经过生产验证且与业务无耦合,直接移植进 MR_Base(改命名空间即可),不重新造轮子。

## 5. 物理标记物料规范(解决双码对齐问题)

**问题**:Quest 用 QR、PICO 用 ArUco,是两种不同的视觉图案。若各自单独贴一张纸,人工张贴精度只有厘米级,会导致两台设备算出的内容位置对不上。

**方案**:QR 与 ArUco 印在**同一张刚性面板**上,两者之间的相对坐标由**印刷设计稿**决定,不依赖现场测量或人工对齐。

- 设计一套统一面板模板:同一版式,QR 区域与 ArUco 区域的相对坐标固定、已知(印刷精度亚毫米级,远高于人工贴纸精度)。两个图案允许尺寸不同(以各自识别距离要求为准),只要在同一刚性平面上、相对坐标已知即可。
- 现场把整块面板当**一个刚体**张贴一次;面板整体贴歪不影响两台设备之间的位置一致性(两个码同时歪,相对关系不变),只影响面板相对真实目标点的绝对精度。
- 软件里 `platformOffset` 是**全项目级别的两个常量**(`QuestMarkerToTargetOffset`、`PicoMarkerToTargetOffset`),由面板设计稿坐标一次性算出,不随点位变化、不需要逐点位现场标定。
- 退化方案(面板放不下两个码的极端场景):退回逐点位现场标定,用测距工具量出两张独立贴纸的相对偏移并录入配置表,比统一面板方案繁琐,仅作兜底。

## 6. 数据流

```
App 启动 → AnchorRegistry.LoadAsync(IAnchorDataSource) 从网络拉取锚点配置表(缓存到本地)
用户举起设备对准面板 → 平台原生识别(逐帧,有抖动)→ provider 收到 (rawId, rawPose)
→ MarkerStabilizer 消抖(位置/角度阈值 + 连续N帧稳定)
   → 未稳定:继续喂下一帧,不触发任何业务逻辑
   → 稳定:吐出 (rawId, stablePose)
      → AnchorRegistry.TryResolve(rawId)
         → 命中:MarkerAnchorService 按 rawId 去重后,实例化 AnchorEntity,调用
           Create(data, stablePose * platformOffset)——内部按需下载内容(比对本地缓存版本号)
         → 未命中:记录 warning 日志(现场码印错/表未更新),不做用户可见的错误提示
→ 内容依赖设备自身 SLAM 维持世界位置,标记脱离视野不影响已生成内容
```

## 7. 错误处理

- **权限缺失**(Quest Spatial Data Permission / PICO 企业能力未授权):启动时探测,引导用户去系统设置开启,而非静默失败。
- **识别到的 ID 查无 AnchorRegistry 记录**:记录日志,可选调试面板列出"已识别但未匹配"的原始 ID,便于现场排查印刷/配置错误。
- **同一 rawId 重复触发**:`MarkerAnchorService` 按状态位去重,忽略重复事件。
- **运行时平台探测失败**(部署配置错误,比如构建时混入了错误的 Scripting Define):`Start()` 直接抛出明确异常终止,不静默无行为——这种错误应在测试阶段暴露。
- **`AnchorRegistry.LoadAsync` 网络拉表失败**(启动时无网络/接口报错):记录错误日志,保留上一次成功拉取并缓存到本地的表(若有);完全没有本地缓存可用时,给用户明确的"暂时无法加载锚点数据"提示,而不是让扫描功能在无提示的情况下什么都不做。
- **`AnchorEntity.Create` 内容下载失败**(单个锚点的内容资源下载失败,不影响其他锚点):记录 warning 日志,该锚点本次不展示内容,不阻塞其他锚点的正常识别与展示。

## 8. 测试计划

- **编辑器模式**:
  - `MockMarkerProvider` 模拟"识别到某 rawId",验证 `MarkerAnchorService` 串起 `MarkerStabilizer → AnchorRegistry.TryResolve → AnchorEntity.Create` 整条链路的查表、去重、实例化逻辑,不依赖真机。
  - `MarkerStabilizer` 单独用一串模拟 pose 序列做纯逻辑单元测试(断言第几帧触发稳定事件、抖动范围内不触发)。
  - `LocalJsonAnchorDataSource` 单独测试:给定本地 JSON 文件,断言反序列化出的 `AnchorEntityData` 字段正确。
- **真机 QA**(Quest、PICO 分别测):
  - 识别成功率、pose 精度(卷尺/激光测距核对误差)。
  - 两台设备扫同一块面板,内容落点是否一致(核心验收标准)。
  - 内容是否随头显走位漂移。
  - 面板脱离视野后内容是否按预期保持(不隐藏)。
  - 权限关闭时是否走提示流程而非崩溃。
  - 断网/弱网环境下 `AnchorRegistry` 拉表失败、`AnchorEntity` 内容下载失败的降级表现是否符合第 7 节预期。

## 9. 依赖与项目集成清单

- **Meta XR Core SDK(含 MRUK)**:调研时最新版本 v205.0(2026-07-22)。**⚠️ 实施前必须重新查询官方最新版本,不要直接套用此版本号**——该 SDK 更新频繁,以 https://developers.meta.com/horizon/downloads/package/meta-xr-core-sdk/ 当时页面为准。
- **PICO Unity Integration SDK(Enterprise)**:调研时最新版本 v3.4.0(2026-02-27)。**⚠️ 同样要求实施前重新查询** https://github.com/Pico-Developer/PICO-Unity-Integration-SDK/releases 确认当前最新版。
- Quest 端:OVRManager 开 Scene Support(Required)+ Anchor Support,场景加 Camera Rig + Passthrough Layer 两个 Building Block,MRUK Tracker Configuration 勾选 QR Code Tracking。
- PICO 端:按 Enterprise SDK 文档注册 Marker Tracking 权限与 `SetMarkerInfoCallback`。
- 两套 Build Profile(Quest / PICO),各自 XR Plug-in Management 只启用对应 loader,自定义 Scripting Define 隔离平台专属代码。
- 现有 `Packages/manifest.json` 里的 `com.unity.xr.meta-openxr`、`com.unity.xr.androidxr-openxr` 继续保留(标准 AR Foundation 子系统用途),新增上述两个原生 SDK 不冲突。
- `com.unity.nuget.newtonsoft-json` 项目里已有,`AnchorEntityData`/`IAnchorDataSource` 的 JSON 反序列化直接用。
- 从 `ite-space-tour` 项目移植 `FileUtils`、`FetchUtils`、`StaticInstance<T>` 三个通用工具类(命名空间调整为 MR_Base 自己的,逻辑不变)。若后续内容类型需要下载 glTF 模型,还需按该项目的先例引入 `Unity.SharpZipLib`(zip 解压)与 `GLTFast`(glb 加载)——本设计范围内暂不强制引入,先用最简单的内容类型(如图片/文字)跑通 `AnchorEntity.Create` 的下载-缓存-展示流程,模型类内容作为后续扩展,不阻塞主线。

## 10. 已知限制与待验证项

- PICO 官方 ArUco Marker Tracking Demo 显示"最多同时追踪 10 个不同 ArUco 码",这限制了 `AnchorDefinition.picoMarkerId` 的可用取值范围,如果点位数量超过 10 个需要向 PICO 官方确认是否可扩展,或分区域复用 ID(同一时间只在同一物理空间出现的点位可复用同一批 ID)。
- Meta MRUK QR 检测文档提到"QR 码需要相对大且离设备近"才能可靠识别,PICO ArUco 的最佳识别尺寸/距离未经实测确认,面板设计需要现场实测两者的可靠识别距离,可能导致面板尺寸比预想更大。
- 两个原生 SDK 均更新频繁(Meta 205.x、PICO 3.4.x),API 细节(类名、回调签名)可能随版本变化,实施阶段需以当时最新官方文档为准,本文档中列出的类名/方法名(`MRUKTrackable`、`OVRAnchor.TrackableType.QRCode`、`SetMarkerInfoCallback` 等)以调研时公开资料为准,实施前应重新核实。
- 面板物料的耐候性(室内/室外、是否反光、是否褪色影响长期识别率)未纳入本设计范围,如现场环境特殊需额外评估。
- `MarkerStabilizer` 的位置/角度阈值与稳定帧数是从 `ite-space-tour` 的 `AnchorObject.cs` 借鉴的经验值(阈值 0.05m/1°,30帧),该项目跑在 Quest 上;PICO 端的抖动特性未经实测,这组参数可能需要针对 PICO 单独调优,不能假设两端用同一组数值就一定合适。
- `AnchorEntity.Create` 的内容下载目前只设计了"单一内容地址+版本号"这种最简单的数据形状,尚未确定具体支持的内容类型(图片/模型/视频),留到实施阶段按 Task 优先级决定先做哪种,不影响整体数据流设计。

## 11. 决策记录(供后续复查)

调研过程中评估过三条路线,记录否决理由避免重复踩坑:

1. **AR Foundation ARTrackedImageManager(两端通用)**:已否决。两端均未实现 `XRImageTrackingSubsystem`——Meta 专门另起炉灶做 MRUK QR 而非复用该子系统;PICO 官方论坛明确"Image Tracking via AR Foundation is not supported"。
2. **两端统一自研摄像头管线(ZXing.Net 解码 QR + 自写位姿解算,基于各自裸摄像头 API)**:已否决。Quest 侧有现成参考实现(`xrdevrob/QuestCameraKit` 的 QR Code Tracking 示例,基于 Passthrough Camera API),风险低;但 **PICO 侧没有任何已知的现成实现或公开验证**,`PXR_CameraImage` 裸帧访问虽然企业授权下可用,但帧格式、内参可用性、识别稳定性均未经验证,风险不可控,故整体否决,回退到两端原生方案。
3. **两端统一 ArUco(自写 Quest 端 ArUco 识别)**:已否决。Quest 端自写 ArUco 需要引入 OpenCVForUnity(付费)或 OpenCvSharp(免费但需自行处理 IL2CPP/arm64 native 插件打包),工程复杂度高于自写 QR 方案,且同样存在"PICO 侧统一自研"的风险,故一并否决。
4. **锚点配置表用 ScriptableObject 在 Editor 里手工维护(第一版设计)**:已否决,改为网络拉表。用户明确要求"表数据必须来自外部网络,不写死在场景里",且"扫描实体"要能动态生成而不是编辑器里预先摆好的固定数据行。改造后参考了同项目组 `ite-space-tour` 已跑通的模式(见第 3、4 节),没有从零设计。
5. **锚点内容用固定本地 Prefab 引用(第一版设计)**:已否决,改为运行时动态下载内容。同样是用户明确要求;`ite-space-tour` 的 `IteTourObject`/`FileUtils` 已经有一套"版本号比对+按需下载+本地缓存"的验证过的实现,直接照该形状改造,内容具体类型(模型/图片/视频)留到实施阶段按优先级决定,暂不需要 `ite-space-tour` 那套完整的 Trigger/Element/Action 组件编排引擎(用户明确表示这部分是后续需求,本设计不做)。

最终采用:两端原生 API(Quest MRUK QR / PICO ArUco LBE API)+ 统一物理面板解决对齐问题(第 5 节)+ 参考 `ite-space-tour` 的扫描稳定化/网络拉表/动态内容下载模式(第 3、4 节)。
