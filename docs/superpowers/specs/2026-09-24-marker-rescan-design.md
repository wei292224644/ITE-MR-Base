# 标记重扫：底层扫描能力分层，ITE 只按回调决定激活 / 定位 / 忽略

> 日期：2026-09-24
> 范围：`Assets/Scripts/Localization`（标记层）、`Assets/Scripts/IteHost`（宿主适配）、`Packages/com.uality.ite-tour`（ITE 包）
> 决策编号在代码注释里写作 `marker-rescan Dn`。

## 1. 目标

1. 一直盯着码看不会重复扫码。只有**连续一段时间认不出这张码（移开视线，全局 3 秒）后再次认出**，才算一次新的扫描。两个平台行为一致。
2. 「扫到之后怎么处理」完全由 ITE 决定：激活、定位、忽略。**当前 Tour 正在播放时扫它的码 = 定位**，不分展示类型、不限次数。
3. 分层：底层扫描能力拆成职责单一的模块、通过事件对外。ITE 只响应回调，不认识底层；底层也不认识 ITE。

## 2. 背景与证据

- `4524f97` 修了 PICO 判稳慢的问题：桥接改为把真实的观测间隔喂给防抖。修完后在 PICO 真机上，从持续认出码到扫码生效约 0.5 秒（修复前要 10～24 秒）。
- 修好后暴露出一个问题（PICO 真机日志，2026-09-24 16:21）：一直盯着 tag 看，约 4 秒后自动提交一次 `Reanchor`，16:21:29.3 和 16:21:45.6 各一次。原因是 PICO 解出的位姿有抖动，偶尔超过防抖阈值（5 cm / 6°）；`MarkerStabilizer` 把这当成「码移动了」，重新判稳后又提交一次，于是用掉了 regionalTrigger 唯一的二次锚定，内容轻微跳一下。
- 现有结构里，「防止误触发重扫」由两套机制各管一半：
  - 防抖的「位姿移动后再次提交」（`MarkerStabilizer.Feed` 里的 `HasFiredStableEvent = false`）；
  - ITE 的「每次激活只允许一次二次锚定」（源工程语义 design D14：`IteTourObject._canAnchor`、`TourDescriptor.SecondAnchorAvailable`、`ScanDecision.ConsumesSecondAnchor`）。
  两者都不表达「人有意重扫」这件事。

## 3. 架构

```
底层扫描能力（Localization，不认识 ITE）
  ① 观测源 IMarkerObservationSource（按平台） → Poll：本帧原始识别结果（码 + 位姿）
  ② 在场判定 MarkerTrackingSession          → 事件：MarkerObserved、MarkerLost
                                              连续 lostAfterSeconds（全局 3 秒）没认出才发 MarkerLost
  ③ 判稳 MarkerStabilizer                   → 事件：Stabilized
                                              每次出现只发一次；位姿抖动不再重发
  ④ 放行 MarkerStabilizer.ResetAll          → 把在场的码当作新的一次出现，重新走判稳

宿主适配（IteHost，唯一同时认识两边的层）
  IteMarkerBridge：MarkerObserved → ③ Feed（真实观测间隔）；MarkerLost → ③ Reset
                   Stabilized → 加平台偏移 → ITE.SubmitMarkerScan
                   Rearm() → ④
  IteHostBootstrap：ITE.OnScanPromptChanged 变为 Visible → bridge.Rearm()
                    持有全局配置 MarkerStabilizerProfile，把 lostAfterSeconds 交给输入层建会话

ITE（只响应回调，不认识底层）
  SubmitMarkerScan(kind, payload, pose) → TourScanPolicy 决定：激活 / 定位 / 忽略
  OnScanPromptChanged：本来就有的公开事件，ITE 用它表达「我在等扫码」
```

**依赖方向**：ITE 不引用标记层，标记层不引用 ITE；宿主适配层同时订阅两边的事件，把它们接起来。

**判定的归属**：
- 「这是不是一次有意的扫描」由底层回答：连续一段时间没认出之后，再次认出并判稳。
- 「这次扫描是第一次还是第二次、要怎么处理」由 ITE 回答：同一张码被再次认出，在 ITE 看来可能是第一次扫描（例如刚戴上、正在等待扫码），也可能是二次扫描（例如这个 Tour 正在播放），只有 ITE 知道当前处于哪种情况。

## 4. 决策

### D1 判稳：每次出现只提交一次

**选了什么**：`MarkerStabilizer` 对同一张码，从出现开始，位姿稳定满 `stableSeconds` 就提交一次 `Stabilized`；之后不管位姿怎么变，都不再提交，直到这张码被 `Reset`（丢失，或被放行）。第一次提交前，位姿一移动，稳定计时照旧清零。

**为什么**：码是固定贴在场地里的，持续观测期间的「位姿移动」只会来自识别噪声或追踪漂移，不代表人有意重扫。PICO 位姿抖动 2～5°，按现有规则约 4 秒就会误触发一次重扫（§2）。

**替代方案**：调大 PICO 的角度阈值来压住误触发。否决：抖动带宽取决于距离、光照和角度，调阈值只能降低概率；而且「移动后重发」这条语义本身就不对。

### D2 丢失时长就是重扫门槛：全局一个值，默认 3 秒

**选了什么**：
- `MarkerTrackingSession` 的 `lostAfterSeconds` 同时是「移开视线多久后才能重扫」的门槛。计时从最后一次认出开始，中途只要再认出一次就重新计时。
- 这个值挪到 `MarkerStabilizerProfile` 资产（`Assets/Settings/ITE/MarkerStabilizerProfile.asset`）的顶层字段 `lostAfterSeconds`，默认 3 秒，两个平台共用。
- `IteHostBootstrap` 对外提供这个值；真机输入层 `IteDeviceMarkerRig` 和编辑器假扫码 `IteEditorFakeScan` 建会话时都从宿主取，删掉它们各自的 `lostAfterSeconds` 序列化字段。
- 没接配置资产时，退回默认 3 秒（与 `IteMarkerBridge.DefaultSettings` 的做法一致）。

**为什么**：「看不见多久算这张码走了」本来就是在场判定的职责；再另设一个「重扫时长」，会出现两个互相矛盾的「丢失」。把值放进宿主持有的配置资产，是为了真机和编辑器只有一个来源。

**替代方案**：
- 在桥接里另算一个重扫门槛，会话的丢失判定仍保持 1 秒。否决：两个概念会打架，1 秒时会话已经发出丢失、桥接已经重置了判稳。
- 两个输入组件各自保留序列化字段。否决：同一个值要在两处保持一致，这样不算「全局」。

**范围外**：`MarkerHookTestRig`（标记探针）自己建的会话，丢失时长不变。

### D3 放行：ITE 在等扫码时，视野里的码不需要先移开视线

**选了什么**：
- 标记层：`MarkerStabilizer.ResetAll()` 清掉所有码的判稳状态。
- 桥接：`IteMarkerBridge.Rearm()` 对两个平台的判稳器各调一次 `ResetAll()`，平滑和稳定计时都从头开始。
- 宿主：`IteHostBootstrap` 收到 `OnScanPromptChanged`、提示状态为 `Visible` 时，调用 `Rearm()`。

**为什么**：ITE 发出扫码提示的场合，正好是需要「第一次扫描」的时候：
- 冷启动、戴上、重定位、宿主要求之后的「扫任意码」；
- 当前 Tour 是 normal、还没播放时的「扫 X 的码」。

没有放行的话，这些时候如果码一直在视野里（例如摘下不到 3 秒就戴回来，或者戴上后连续几次重定位作废了刚扫成功的定位，见 Quest 真机 2026-09-24 12:49:00），就必须先移开 3 秒才能扫上。有提示 = 第一次扫描 = 放行；没提示 = 重扫 = 必须先移开视线。

另外，应用暂停期间会话既不派发、也不累计缺席时长，所以休眠醒来后码不会被判为丢失。戴上后 ITE 发出扫码提示时放行，正好接住这种情况。

**替代方案**：
- 底层自己判断「这是第一次扫」。否决：底层不知道 ITE 的状态（§3「判定的归属」）。
- 监听 ITE 的状态变化（进入等待扫码）来放行。否决：漏掉「当前 Tour 是 normal、等扫它的码」这种情况，扫码提示覆盖了全部需要第一次扫描的场合。

### D4 ITE：当前 Tour 正在播放时扫它的码 = 定位，不分类型、不限次数

**选了什么**：`TourScanPolicy` 在已定位状态下，对当前 Tour 的码：

| 当前 Tour | 在播 | 决定 |
|---|---|---|
| regionalTrigger / normal | 是 | **定位**（`Reanchor`），每次都生效 |
| normal | 否 | 激活（不变） |
| regionalTrigger | 否 | 激活（不变；正常流程里 regionalTrigger 一当上当前 Tour 就在播） |
| alwaysDisplayed | — | 忽略（不变，它不会是当前 Tour，见 ite-current-tour I5） |

等待扫码状态下的规则不变：扫任意码都激活；扫到 alwaysDisplayed 只定位（ite-current-tour D10）。扫其他 Tour 的码仍然忽略（ite-current-tour D6）。定位照旧打开锚定结算窗口（ite-current-tour D9）。

**为什么**：二次扫描的语义就是「重新定位」，与展示类型无关。防止误触发已经由底层的 D1、D2 负责，ITE 不需要再限制次数。

**取代**：源工程语义 design D14 中的「regionalTrigger 每次激活只允许一次二次锚定」，以及「normal 在播时扫它的码一律忽略」。

**替代方案**：保留「每次激活一次」的限制。否决（用户选择，2026-09-24）：它和底层的门槛做的是同一件事，还会让用户第二次有意重扫时没有反应。

### D5 删除 ITE 里的二次锚定许可状态

**选了什么**：删除以下内容：
- `TourDescriptor.SecondAnchorAvailable`；
- `ScanDecision.ConsumesSecondAnchor`、`GuideEffect.ConsumesSecondAnchor`；
- `IteTourObject._canAnchor`、`CanSecondAnchor()`、`SecondAnchored()`；
- `TourDirector.Reanchor` 的 `consumesSecondAnchor` 参数，以及 `Descriptors()` 里对许可状态的填充。

`TourScanPolicy` 及相关注释里围绕 D14 的「是否消耗许可」论证一并删除。

**为什么**：许可状态只为 D4 取代的那条规则服务。留着不用，就是一套没人读的状态，下一个读代码的人会以为它还有作用。

### D6 同帧多张码只提交第一张（不变）

`IteMarkerBridge` 的 design D20 保持不变：同一帧里有多张码判稳时，只提交第一张。落选的码怎么处理见 D7。

### D7 同帧落选的码重新判稳，之后单独提交

**选了什么**：同一帧里多张码判稳时，仍只提交第一张（D6）；落选的码由桥接对它调用 `Reset`，重新走一遍判稳窗口，之后在更晚的帧单独提交。

**为什么**：D1 之后每张码每次出现只提交一次，落选如果直接丢弃，就要移开视线 3 秒才能再扫。而 D3 放行会让视野里所有码的判稳同时从头开始，它们大概率在同一帧判稳，落选成了常态。例如：已定位后当前 Tour 是 normal、提示「扫 X」，视野里同时有 X 的码和一张 alwaysDisplayed 的码；放行后若先提交的是 alwaysDisplayed（被 ITE 忽略），X 就扫不上。

延后提交不会重新引出 D20 要防的事（后到的码把先到者刚激活的 Tour 停掉）：已定位后 ITE 只认当前 Tour 的码（ite-current-tour D6），后到的码不会换掉当前 Tour。

**替代方案**：
- 落选直接丢弃（D20 的原做法）。否决：见上。
- 落选的码排队，下一帧直接提交、不重新判稳。否决：多一份排队状态；重新判稳只多等一个稳定窗口（0.4～0.5 秒）。

## 5. 对外接口变化

| 接口 | 变化 |
|---|---|
| `MarkerStabilizer` | 位姿移动不再触发重新提交（D1）；新增 `ResetAll()`（D3） |
| `MarkerStabilizerProfile` | 新增顶层字段 `lostAfterSeconds`，默认 3（D2） |
| `IteMarkerBridge` | 新增 `Rearm()`（D3）；同帧落选的码重新判稳后再提交（D7） |
| `IteHostBootstrap` | 对外提供丢失时长；扫码提示变为可见时调用 `Rearm()`（D2、D3） |
| `IteDeviceMarkerRig`、`IteEditorFakeScan` | 删除 `lostAfterSeconds` 序列化字段，改从宿主读取（D2） |
| `TourScanPolicy.Decide` | 签名不变；当前 Tour 在播时扫它的码一律返回 `Reanchor`（D4） |
| `TourDescriptor`、`ScanDecision`、`GuideEffect`、`IteTourObject`、`TourDirector` | 删除二次锚定许可相关的成员（D5） |

ITE 对外的 API（`IteRuntime`）不变。

## 6. 行为变化

1. 一直盯着码看，只扫一次；PICO 不会再在约 4 秒后自动重新定位（D1）。
2. 要重扫同一张码，必须先连续 3 秒认不出它（D2）。
3. 在需要第一次扫描的场合（有扫码提示时），码一直在视野里也能直接扫上（D3）。
4. 当前 Tour 正在播放时，每次有意重扫它的码都会重新定位；normal 也一样（D4）。
5. 丢失判定从 1 秒变为 3 秒：码离开视野 1～3 秒内又回来，算同一次出现（D2）。
6. 视野里同时有多张码时，它们依次提交，间隔一个稳定窗口；ITE 分别决定每一张怎么处理（D7）。

## 7. 测试

EditMode：

- `MarkerStabilizerTests`（`MRBase.Localization.Tests`）：
  - 把 `Feed_FiresOnce_ThenRefiresOnlyAfterMovingAgain` 改成「提交一次后位姿移动再稳定，也不再提交」（D1）；
  - `Reset` 之后再次提交；
  - `ResetAll` 之后，所有码都会再次提交（D3）。
- `IteMarkerBridgeTests`（`MRBase.Ite.Host.Tests`）：
  - 持续可见、位姿抖动超过阈值：只转发一次（D1）；
  - 看不见 2 秒（小于 3 秒）后再看见：不转发；看不见超过 3 秒后再看见：转发第二次（D2，会话用 3 秒丢失时长）；
  - 持续可见时调用 `Rearm()`：再转发一次（D3）；
  - 两张码同帧判稳：同一帧只转发一张，落选的那张在之后的帧转发（D6、D7；改写 `TwoMarkersStabilizingInSameTick_OnlyFirstIsForwarded`）。
- `TourScanPolicyTests`、`TourGuideTests`（`Uality.IteTour.Tests`）：
  - normal 在播时扫它的码 → `Reanchor`；
  - regionalTrigger 在播时连续多次扫它的码，每次都是 `Reanchor`；
  - 删除与二次锚定许可有关的用例和字段（D4、D5）。

宿主接线（`IteHostBootstrap` 在扫码提示可见时调用 `Rearm()`、从配置资产读丢失时长）属于 MonoBehaviour 接线，EditMode 测不了，放到真机验证。

回归：在已打开的 Editor 里跑 `Uality.IteTour.Tests`、`MRBase.Ite.Host.Tests`、`MRBase.Localization.Tests`，全部通过。

## 8. 真机验证（用户说「打包」才出包；Quest 和 PICO 各测一遍）

1. 扫码生效后一直盯着码看 10 秒：日志里只有一次「扫码」，没有自动 `Reanchor`。
2. 移开视线约 2 秒再看回来：没有新的「扫码」。
3. 移开视线 3 秒以上再看回来：出现「扫码 → Reanchor」，内容重新定位。regionalTrigger 和 normal 各试一次，各连续试两次。
4. 摘下后 3 秒内就戴回来、码一直在视野里：不用移开视线就能扫上（放行生效）。
5. 休眠后戴上：直接能扫上。
6. 当前 Tour 是 normal、还没播放时，盯着它的码：直接激活。

## 9. 不在范围内

- PICO 相机出帧慢（每秒约 6 帧，刚启动时约 3 帧），决定了「开始看到码」的下限。
- `MarkerHookTestRig` 探针的丢失时长。
- ITE 的当前 Tour、区域、状态机规则（ite-current-tour D1–D14）。

## 10. 与既有文档的关系

- `docs/superpowers/specs/2026-09-23-ite-current-tour-design.md`：D6（已定位后只认当前 Tour 的码）、D9（定位打开结算窗口）、D10（等待扫码时扫到 alwaysDisplayed 只定位）不变。它的 §7 行为变化和 `TourScanPolicy` 注释里沿用的 design D14 二次锚定语义，由本文 D4、D5 取代。
- `IteMarkerBridge` 的 design D5（桥接只做稳定、偏移、透传）、D22（稳定窗口按时间算）不变；D20（同帧只认第一张）不变，但落选的码不再丢弃，由本文 D7 规定。
