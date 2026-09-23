# ITE 扫码区域门禁（ite-scan-region-gate）—— 设计

> 状态：已评审；实现已提交（1201e94），本文件随后续补丁一起提交
> 日期：2026-09-23
> 范围：只改 ITE 包里的两个纯决策函数 `TourScanPolicy`、`ScanPromptPolicy`，以及它们的测试。宿主（`Assets/Scripts/IteHost`）、识别链路、区域进出逻辑、触发体积尺寸都不动。

## 1. 目标

定位过一次之后，只有人站在某个 Tour 的区域（触发体积）里，扫这个 Tour 的码才生效。只有「必须扫码」状态下才不看区域：冷启动、摘下后重新戴上、追踪原点重置。

## 2. 现状（读码所得）

扫码链路：标记源 → `MarkerTrackingSession`（1 秒丢失滞回）→ `IteMarkerBridge`（约 0.5 秒判稳）→ `IteRuntime.SubmitMarkerScan`（payload → tourId）→ `TourDirector.SubmitMarkerScan` → `TourScanPolicy.Decide`。

`TourScanPolicy.Decide`（`Packages/com.uality.ite-tour/Runtime/Core/TourScanPolicy.cs`）的现行规则：

1. 暂停（头显摘下）、markerId 为空、tourId 不在已装配的 Tour 里 → 忽略。
2. `ForcedScanPending`（「必须扫码」状态）→ 任何匹配的 Tour 都激活并锚定，不看区域，也不看展示类型。三处会进入这个状态：冷启动（`IteRuntime` 加载完成）、重新戴上（`TourDirector.SetHeadsetMounted(true)`）、追踪原点重置（`IteDeviceMarkerRig.HandleRecentered` → `RequireScan()`）。
3. `PendingTourIds`（相机当前所在触发体积对应的 Tour）**非空**且不含该码 → 忽略。**为空时不限制。**
4. 按展示类型：`normal` 未激活则激活，已激活则忽略；`regionalTrigger` 未激活则激活，已激活且还有二次锚定许可则重新锚定一次；`alwaysDisplayed` 忽略。

第 3 条和源工程一致（`ite-space-tour/Assets/Scripts/ITE/IteSpaceManagerScan.cs:75`）：**人在所有区域之外时，扫任何码都能激活并重新锚定。**

`ScanPromptPolicy.Decide` 的现行规则：有 Tour 在播 → 不提示；处于「必须扫码」状态，**或人在所有区域之外** → 提示「扫任意码」；区域里有 `regionalTrigger` → 不提示；区域里只有 `normal` → 提示扫这几个；其余 → 不提示。

## 3. 新规则

| 状态 | 扫码 | 扫码提示 |
|---|---|---|
| 「必须扫码」状态（冷启动 / 重新戴上 / 追踪原点重置） | 不看区域，扫到哪个已装配 Tour 的码就激活并锚定哪个（不变） | 「扫任意码」（不变） |
| 定位过之后，人在某些区域里 | 只认这些区域对应 Tour 的码，其余忽略（不变）；区域内按展示类型处理（不变） | 不变 |
| 定位过之后，人在所有区域之外 | **任何码都忽略**（原为不限制） | **不提示**（原为「扫任意码」） |

## 4. 决策

### D1 定位后，人在所有区域之外时扫码一律忽略

**选了什么**：`TourScanPolicy` 在「必须扫码」分支之后，要求 `PendingTourIds` 包含该码。集合为空或为 null（人在所有区域之外）→ 忽略。

**替代方案**：保留源工程的「区域外不限制」。否决：这正是本次要改的产品规则。

### D2 追踪原点重置同样不看区域

**选了什么**：三种进入「必须扫码」状态的情况一视同仁，都不看区域。`TourScanPolicy` 本身不需要区分，它们都落到同一个 `ForcedScanPending`。

**为什么**：原点重置意味着上一次锚定作废，触发体积在世界里的位置也跟着错了。如果还要求人站进体积，人可能永远站不进一个错位的盒子，导览就再也锚不回来。

**替代方案**：只豁免冷启动和重新戴上。否决：理由同上，会把人卡死在锚定作废的状态里。（用户已确认，2026-09-23。）

### D3 定位后人在所有区域之外时，不显示扫码提示

**选了什么**：`ScanPromptPolicy` 里，只有「必须扫码」状态才提示「扫任意码」；定位后 `PendingTourIds` 为空或为 null → `Hidden`。

**为什么**：区域外扫码已经不生效，还提示「扫任意码」就是让人去做无效操作。

**替代方案**：
- 新增「请进入区域」提示状态。否决：`ScanPrompt` 要加状态、宿主面板要加文案，属于新功能；本次只让提示和行为保持一致。
- 保持原样。否决：提示和行为矛盾。

（用户已确认，2026-09-23。）

### D4 触发体积尺寸这次不动

**现状**：`IteTourObject.CreateTourObject` 把 `BoxCollider.size` 设为描述尺寸的一半（源工程的既有行为，移植时原样保留）。

**选了什么**：这次不改。

**为什么**：D1 之后，这个尺寸直接决定「站在哪才能扫」，但两处行为变化混在一起，真机上出问题时分不清是哪一处引起的。尺寸也影响 `regionalTrigger` 的自动激活，是否改回描述尺寸，等现场实测后另行决定。（用户已确认，2026-09-23。）

### D5 定位偏了且未触发强制扫码时，靠重新佩戴恢复（待用户确认）

**后果**：旧行为下，人在所有区域之外扫码会重新锚定整个空间（所有 Tour 共享 AnchorRoot/TourRoot），相当于一个隐式的自我纠正入口。D1 去掉这条路径之后：如果第一次定位本身偏了（扫描角度太斜、距离太远，或发生了一次没有派发 `trackingOriginUpdated` 的重定位），又没有触发强制扫码，所有触发体积都会跟着错位，且此后没有提示（D3）。纠正的办法变成走进一个错位、还按 D4 减半的体积去扫码——如果偏差恰好让人站不进去，就没有别的路径了。D2 否决「只豁免冷启动和重新戴上」时用的理由（人可能永远站不进一个错位的盒子）同样适用于这里，但目前只有冷启动、重新戴上、追踪原点重置这三种进入强制扫码的路径被豁免，「第一次定位本身偏了」不在其中。

**选了什么**：接受这个后果。恢复办法是摘下头显再重新戴上——这会重新进入「必须扫码」状态，不看区域。

**替代方案**：给现场工作人员一个能手动触发 `RequireScan()` 的入口（例如一个隐藏手势或后台按钮）。否决（本次）：这是宿主新功能，超出本次范围；先用「重新戴上头显」这条已有路径顶着。

**状态**：待用户确认。

## 5. 影响面

- **宿主**：不改。`IteMarkerBridge`、`IteDeviceMarkerRig`、`HeadsetPresenceAdapter` 的行为不变。
- **`IteHmdPanel`**：黄色横幅「视角已重定位，请重新扫码」由 `_recenterPending` 控制（`Assets/Scripts/IteHost/IteHmdPanel.cs:56/132-140/202/230`），目前只在收到一次 `Visible` 提示时才会清掉（`HandleScanPromptChanged`）。这个耦合本身是既有问题——横幅理应跟着「必须扫码」状态走，而不是跟着提示的变化走——但 D3 去掉了它最常见的清除路径：导览播放中发生追踪原点重置时，`IteDeviceMarkerRig.HandleRecentered`（`Assets/Scripts/IteHost/IteDeviceMarkerRig.cs:218-235`）先调 `RequireScan()` 再调 `NotifyRecentered()`；旧规则下重扫成功、随后走出所有区域，提示会变回 `Visible([])` 顺带清掉横幅，新规则（D3）下提示保持 `Hidden`，横幅不会自动清除。只用 `regionalTrigger` 展示类型的导览，定位后提示永远不会再变 `Visible`，横幅会一直挂到下次重新戴上头显。
  这次不修：宿主侧的修法是把 `ForcedScanPending` 转发出来（`TourDirector` 已经暴露）经 `IteRuntime` 传给 `IteHmdPanel`，在 `Refresh` 里改成「`ForcedScanPending` 变为 false 时清 `_recenterPending`」（安全，因为 `RequireScan` 总是先于 `NotifyRecentered` 执行）。这是宿主改动，超出本次范围，留作后续 patch。
- **其余扫码提示的消费方**（`IteEditorHud`、`IteHostBootstrap` 的日志）：只是多了一种会收到 `Hidden` 的情况，接口不变，不需要改。
- **编辑器假扫码**（`IteEditorFakeScan`）：第一次扫码之后，也要先把相机移进目标 Tour 的区域，假扫才生效。这和真机行为一致，不需要改。
- **区域进出与自动重选**（`TourRegionPolicy`）：不变。

## 6. 测试

都是 EditMode 纯函数测试，放在 `Packages/com.uality.ite-tour/Tests/Editor/`。

`TourScanPolicyTests`：
- 新增：「必须扫码」状态下人在所有区域之外 → 激活（D2 的语义固定下来）。
- 新增：定位后人在所有区域之外 → 忽略（D1）。
- 新增：定位后 `PendingTourIds` 为 null → 忽略（D1）。
- 删除：原「列表为空不过滤」那条（与 D1 相反）。
- 修改：`normal` / `regionalTrigger` / `alwaysDisplayed` 和重复扫码那几条补上「人在 t1 区域里」的前提。它们测的是区域内的分支，不补就会因为 D1 这个别的原因变成忽略。

`ScanPromptPolicyTests`：
- 修改：原「不在任何区域 → 提示扫任意码」改成「定位后不在任何区域 → 不提示」（D3）。
- 新增：定位后 `PendingTourIds` 为 null → 不提示（D3）。
- 新增：「必须扫码」状态下人在所有区域之外 → 提示扫任意码。

回归：在打开的 Editor 里跑 `Uality.IteTour.Tests`、`MRBase.Ite.Host.Tests` 两个程序集。

## 7. 真机验证（不在本次代码范围内，出包时一并做）

- 定位后走出所有区域扫码 → 无反应、无提示；走进某个 Tour 的区域扫它的码 → 生效。
- 摘下后戴上，在区域外扫码 → 生效并重新锚定。
- 因为 D4 的尺寸减半，记录一下「站在画的区域边缘能不能扫」，作为 D4 后续决策的依据。
- 导览播放中触发一次重定位、重扫成功后，再走出所有触发体积：观察黄色「视角已重定位，请重新扫码」横幅是否一直挂着不消失（F1，`IteHmdPanel`，本次不修，见 §5）。
- 刻意让第一次扫码角度很斜或距离很远，使定位明显偏掉（不触发强制扫码的前提下）：确认此时无法自行纠正，重新戴上头显能恢复（D5，待用户确认）。

## 8. 当前代码状态

这份 spec 写之前，代码已经按本设计改过，并已提交（commit 1201e94）；提交信息写的是「D36」——那时指向本文件的 D1–D3，「D36」是已经删除的 openspec 文档里的编号，本文件继承并延续了那份设计。后续补丁（本文件所在这次提交）把代码、测试注释里残留的「design D36」改成「ite-scan-region-gate D1/D2/D3」，并按最终评审的发现补了一条重叠区域测试（F6，钉住「两个码都认」而不只是「认最后一个」）。
