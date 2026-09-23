# Handoff: ITE 区域触发的两个真机问题（PICO 实测）

> 后续：两个问题的修复见 spec `docs/superpowers/specs/2026-09-23-ite-current-tour-design.md` 与计划 `docs/superpowers/plans/2026-09-23-ite-current-tour.md`。

> Generated: 2026-09-23 14:40 CST
> Next session focus: 记录并排查 PICO 实测暴露的两个区域触发问题——①扫码锚定后被区域重选立刻切走；②同一触发体积的进入/离开事件成对重复

## Goal

让「扫码区域门禁」（定位后必须站在某 Tour 的触发体积内扫码才生效）在真机上可靠：区域集合要准确反映相机当前在哪些体积里，扫哪个码就稳定显示哪个 Tour。

## Current state

- **扫码区域门禁已落地**：实现在 `1201e94`，补充测试与设计文档在 `f28df0c`。规则、决策 D1–D5 见 spec `docs/superpowers/specs/2026-09-23-ite-scan-region-gate-design.md`，执行计划见 `docs/superpowers/plans/2026-09-23-ite-scan-region-gate.md`。
- **未提交**：给真机测试加的诊断日志（只加日志，不改逻辑），涉及 `Packages/com.uality.ite-tour/Runtime/Core/TourDirector.cs`（扫码判定及当时状态和位姿、区域进出、区域重选、摘戴头显、要求重扫）、`Assets/Scripts/IteHost/IteHmdPanel.cs`（重定位黄条的显示和清除）、`Assets/Scripts/IteHost/IteHostBootstrap.cs`（扫码提示带 TourIds）。EditMode 测试通过：`Uality.IteTour.Tests` 276/276，`MRBase.Ite.Host.Tests` 30/30。是否提交由用户决定。
- **PICO 包**：`Builds/Pico/MR_Base.apk` 已装到设备（包名 `com.uality.xiangtangshan`），包含上述日志。
- **空间场景包**：用户今天重新上传了 `DazuRockCarvings.zip`，给 11 个 Tour 绑定了 `aprilTagID` 0–10。起初 CDN 一直返回 9/20 的旧包，app 按 ETag 判定命中缓存，于是扫码无效。用户刷新 CDN 后，14:35 已下载到新包，现在 tag 0 扫码可以激活 `ujf5bo31_frb`。

## 问题 1：扫码锚定成功后，区域重选立刻切到别的 Tour

**现象**（PICO，14:36:31 这一次扫码）：

```
14:36:31.129 [ITE] 扫码 ujf5bo31_frb → Activate（强制扫码=True 在播=无 所在区域=[ujf5bo31_frb]） 位姿 pos=(-0.245,-0.342,0.178) rot=(0.3,0.5,0.1)
14:36:31.161 [ITE] 区域 Exit ujf5bo31_frb → 所在区域=[]，帧末重选（候选=[]）
14:36:31.161 [ITE] 区域 Enter hncxtzfe_p4d → 所在区域=[hncxtzfe_p4d]，帧末重选（候选=[hncxtzfe_p4d]）
14:36:31.191 [ITE] 区域重选：激活 hncxtzfe_p4d（沿用现有锚定）
14:36:34.208 [ITE] 区域 Exit hncxtzfe_p4d → 停用；14:36:34.602 Enter ujf5bo31_frb → 重选激活 ujf5bo31_frb
```

扫的是 tag 0（`ujf5bo31_frb`），32ms 后内容被换成 tag 1（`hncxtzfe_p4d`），之后人在两个区域的边界附近走动时来回切换。

**机理（已读码确认）**：锚定会移动 AnchorRoot/TourRoot，所有触发体积随之挪到真实位置。如果相机此时不在被扫 Tour 的体积里，`TourRegionPolicy.Decide` 会把在播 Tour 的 Exit 判为需要重选，候选是所在区域里的 `regionalTrigger`（场景里 11 个 Tour 全是 `regionalTrigger`）。这是源工程就有的行为，不是 D1 引入的。

**可能的放大因素**：
- 触发体积只有描述尺寸的一半（`IteTourObject.CreateTourObject` 里的 `/ 2`，spec D4 已定为暂不改）。
- 码贴的位置可能不在自己 Tour 的体积里，而是靠近相邻 Tour 的体积。

**待决（产品问题，需要用户拍板）**：扫码激活的 Tour 在锚定后应不应该立刻被区域重选覆盖？可选方向（未评估）：
- 扫码激活后给一段宽限期或加迟滞；
- 以扫码为准，直到人明确离开该 Tour 的体积；
- 先实测 D4 的尺寸，再决定。

## 问题 2：同一触发体积的进入/离开事件成对重复

**现象**：每个体积的 Enter 和 Exit 都各出现两次，而且两次的时间不同。例：

```
qtcljiro_zrx  Enter 14:36:36.104、14:36:37.148；Exit 14:36:37.503、14:36:39.826
hncxtzfe_p4d  Exit  14:36:22.423、14:36:24.127
ujf5bo31_frb  Enter 两次同在 14:36:21.831（加载完成时）
```

**危害**：`TourRegionPolicy.UpdatePending` 在 Enter 时去重添加，但在**第一次** Exit 就把该 Tour 移出区域集合。以上面为例，37.5 到 39.8 这 2 秒多里，实际还有一个碰撞体在 `qtcljiro_zrx` 的体积里，区域集合里却已经没有它。按 D1（定位后只认所在区域的码），这段时间扫这个码会被错误地忽略，区域重选也可能误判。

**根因未查**。线索与假设：
- 两次事件的时间不同，说明是两个位置或尺寸不同的碰撞体在分别进出，不是同一帧重复派发。
- `IteDeviceMarkerRig`（`Assets/Scripts/IteHost/IteDeviceMarkerRig.cs:145-160`）只在相机**没有** Collider 时才挂一个 0.15m 的 SphereCollider 加 Rigidbody。要查相机本身或子物体上是否已经有别的碰撞体。
- `IteTourObject.NotifyVolumeTransition` 用 `SceneRoles.IsCamera(_camera, other.transform)` 过滤。要查它是否把相机的子物体、父物体或 XR rig 上的其他碰撞体（手、控制器、CharacterController 等）也算作相机。
- 也可能是体积侧重复：一个 Tour 有两个体积对象或两个 `TourVolumeTrigger`。查 Tour 预制体。
- 编辑器验收场景（`IteEditorHarness.prefab` 的 Camera 上有胶囊 trigger + kinematic 刚体）有没有同样的重复，可以在 Editor 里先复现。

**可能的修法方向（未定）**：按碰撞体计数（Enter +1、Exit -1，归零才算离开），或只认一个指定的相机碰撞体。前者是区域策略的语义变化，要走 spec。

## Key decisions（不要重议）

- 扫码区域门禁的规则和 D1–D5：见 `docs/superpowers/specs/2026-09-23-ite-scan-region-gate-design.md`。D5（定位偏了只能靠重新佩戴恢复）标着「待用户确认」。
- `openspec/` 已被用户删除，不要读，也不要恢复。设计文档写在 `docs/superpowers/specs/`，计划写在 `docs/superpowers/plans/`。
- 改变运行时行为或规则的需求，先写 spec、再写 plan，两步都经用户审过才动代码。修上面两个问题都属于这一类。
- 不主动打包或安装；用户说「打包」才做，而且只做一轮。

## Open questions / blockers

- 问题 1 的产品语义（见上）需要用户决定。
- 问题 2 的根因需要调查。
- 上一轮留下、尚未处理的：
  - `IteHmdPanel` 的重定位黄条可能一直挂着（spec §5，宿主侧后续补丁）；
  - D5 待确认；
  - 未提交的诊断日志要不要提交。
- 运维：以后每次重新上传场景包都要刷新 CDN（`ite-spatial-config.uality.cn` 的缓存会返回旧 ETag），或者调短 `.zip` 的缓存时间。

## Relevant artifacts

- `docs/superpowers/specs/2026-09-23-ite-scan-region-gate-design.md` — 区域门禁的规则、决策 D1–D5、真机检查清单
- `Packages/com.uality.ite-tour/Runtime/Core/TourRegionPolicy.cs` — 区域集合更新（`UpdatePending`）与重选决策，问题 1 和问题 2 都落在这里
- `Packages/com.uality.ite-tour/Runtime/Core/TourDirector.cs` — `SubmitVolumeTransition` / `FlushRegionTransitions`（帧末重选），以及新加的日志
- `Packages/com.uality.ite-tour/Runtime/Core/IteTourObject.cs` — `NotifyVolumeTransition`（相机过滤）、`CreateTourObject`（体积尺寸减半）
- `Packages/com.uality.ite-tour/Runtime/Core/TourVolumeTrigger.cs` — 物理回调入口
- `Assets/Scripts/IteHost/IteDeviceMarkerRig.cs:145-160` — 真机相机碰撞体与刚体的挂载
- `Packages/com.uality.ite-tour/Tests/Editor/TourRegionPolicyTests.cs` — 区域策略现有测试
- PICO 抓日志：`/Applications/Unity/Hub/Editor/6000.4.4f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb logcat -v time -s Unity`。logcat 缓冲区只有 256KB，会被 `PicoFiducialObservationSource` 的日志刷满，要从 app 启动时就持续写入文件，不能事后用 `-d` 读。

## Suggested next steps

- [ ] 问题 2：在 Editor（`IteTourSpace` 验收场景）或真机上确认两个碰撞体分别是什么。打印 `other.name` 和 `other.GetInstanceID()` 即可，这一步不改逻辑。
- [ ] 问题 2：根因确认后，写 spec（区域集合的语义：按碰撞体计数，或只认指定碰撞体），再写 plan，都经用户审过再改。
- [ ] 问题 1：先和用户确认「扫码激活 vs 区域重选」的优先级，再写 spec。可以先实测码的位置和体积（含减半尺寸）的关系，作为 D4 的依据。
- [ ] 让用户决定未提交的诊断日志要不要提交。

## Suggested skills

- `superpowers:systematic-debugging` — 查问题 2 的根因（先取证，再下结论）。
- `superpowers:brainstorming` — 问题 1 和问题 2 的修复都改变区域语义，走 architectural 路径：先写 spec 到 `docs/superpowers/specs/`，用户审过后再用 `superpowers:writing-plans` 写计划。
- `unity-cli` — 所有 Editor 操作（跑测试、在编辑器里复现）都走 `unity` CLI，连用户已经打开的 Editor。
- 不要用 `/opsx:*` 系列：`openspec/` 已删除，这个仓库不再用 OpenSpec 流程。
