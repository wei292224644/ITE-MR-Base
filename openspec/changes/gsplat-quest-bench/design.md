## Context

工程为 Unity 6000.4.4f1 + URP 17.4 + Linear 色彩空间，Android 走 Vulkan，同时装了 Meta XR SDK 205 与 PICO Integration SDK，目标设备 Quest 3 与 PICO 4 Ultra（均为 XR2 Gen 2 / Adreno 740）。

被测对象 `wuyize25/gsplat-unity` v1.4.0 的架构选择决定了本变更的几处约束：它把 splats 当作「带自定义 shader 的半透明 mesh」直接插进 transparent queue（PlayCanvas 思路），只额外插一个排序 pass —— 不像 `aras-p/UnityGaussianSplatting` 那样渲进独立 RT 再合成。这带来两个后果：能与半透明 mesh 正确穿插、移动端少一次全屏带宽；但 alpha 混合直接发生在最终 buffer 上，因此**要求项目色彩空间为 Gamma** 才与 3DGS 的训练语义一致。

3DGS 的 color/opacity 参数是在 gamma 域做 alpha 合成、与 gamma 编码的 PNG 比对、梯度下降拟合出来的。换到 linear 域混合，那组参数不再是最优解。这是一个**真实的架构分歧**，但它是下一个变更的题目，不是本变更的。

## Goals / Non-Goals

**Goals**

- 得到本项目实际内容在 Quest 3 / PICO 4 Ultra 上的 splat 数量天花板（可复现的数字，不是估算）
- 得到每个优化旋钮的**边际** Δ GPU ms，用来决定下一步该投资哪里
- 装置本身可整体删除，不在主工程留下痕迹

**Non-Goals**

- 不修色彩空间、不加独立 RT 模式、不做 LOD/视锥剔除、不接 SOG
- 不追求「让它跑得快」—— 本变更的产出是**数据**，不是性能
- 不把 3DGS 接进任何产品场景
- 不做自动化回归；这是一次性探针，不是 CI 指标

## Decisions

### D1：本轮接受 Linear 下的颜色错误，不做任何色彩空间处理

**选了什么**：项目保持 Linear，gsplat-unity 保持默认（不开 `Gamma To Linear`），画面偏色照单全收。

**为什么成立**：颜色错误改变的是每个像素**算出来的值**，不改变**参与运算的像素数量**和**混合层数**。而 GPU 成本 ≈ `Σ(splat 屏幕面积 × 混合层数)`。两者正交，测量有效。

**替代方案与否决理由**：
- *全项目切 Gamma*：为一个资产格式让整条管线（210 material、50 shader、4 个 VFX Graph、Bloom、lightmap）和 MR 合成层迁就；且 OpenXR + Gamma 在 GLES 下不支持、Vulkan 下少人验证，Quest 上有偏暗的历史报告。局部最优换全局劣化。**否决。**
- *开 `Gamma To Linear` 补偿*：该选项是「先转换后混合」，顺序与正确解（「先混合后转换」）相反，作者自述降质。它引入一个**与被测对象无关的变量**，污染基线。**否决。**
- *先实现独立 RT 模式再测*：把待验证的架构改动放进测量装置，测出来的就不是 upstream 的能力。**否决 —— 测量必须先于优化。**

### D2：测三个必须锁死的变量，否则数字全部作废

**选了什么**：sweep 启动前显式锁定并在 HUD 上持续显示三项：

| 变量 | 不锁的后果 |
|---|---|
| ASW / SpaceWarp | 掉到 36fps 但**观感流畅**，误判为通过 |
| 动态分辨率 / 动态 FFR | 系统偷偷降清晰度，档与档不可比 |
| 刷新率（72/90） | 帧预算差 20%，跨档不可比 |

**为什么是硬约束**：这三项都是**系统在你背后改变工作量**。任何一项浮动，「档 N 比档 N-1 快了 2ms」就无法归因。HUD 必须显示它们的实时值，而不只是启动时设一次 —— 系统可以在运行中改。

**替代方案**：只看 fps 不管这些。否决 —— 这正是 issue #10 那类数据点无法复现的原因。

### D3：主指标是 GPU 中位数毫秒，不是 FPS

**选了什么**：`FrameTimingManager.GetLatestTimings()` 取 GPU 时间，报**中位数**（配 1% low）。FPS 作为次要显示。

**为什么**：FPS 被刷新率钳制（跑得再快也是 72），且在 XR 上抖动剧烈无法读数。GPU ms 是连续量，能看出「离预算还有多远」和「省了多少」。中位数而非均值 —— 均值被偶发尖刺污染。

**替代方案与否决理由**：
- *厂商 API（`OVRPlugin.GetAppGPUTime` 等）*：更准，但 Quest 与 PICO 各一套，装置要分叉。跨平台一致性优先于绝对精度 —— 我们要的是**档间差值**，系统性偏差可以接受。**否决。**
- *均值 / 瞬时值*：见上。**否决。**

需要打开 Player Settings 的 Frame Timing Stats（当前 `enableFrameTimingStats: 0`）。

### D4：sweep 用累加序列，不用单变量序列

**选了什么**：每档在前一档基础上**再加一个**旋钮：

```
档0 baseline → +SPI → +MSAA off → +sort 1/30 → +SH0 → +downscale 0.25 → +viewScale 0.7
```

**为什么**：旋钮之间有交互（downscale 之后 MSAA 的成本已经变了）。累加式测的是**边际收益** —— 「在已有配置基础上，再开这个还能省多少」，这正是「下一步投资哪里」需要的答案。

**替代方案**：单变量式（每档只从 baseline 开一个旋钮）测的是**独立贡献**，归因更干净，但会高估总收益（各项相加远大于实际叠加效果）。**否决为默认**，但保留为可选模式 —— 若某档出现反直觉结果，需要单变量回归来定位。

### D5：bench 用独立 URP Asset + Renderer Data，不改既有渲染配置

**选了什么**：新建 bench 专属 URP Asset 与 Universal Renderer Data，`GsplatURPFeature` 只挂在后者上；通过 bench 专属 Quality Level / Build Profile 生效。

**为什么**：既有 `Build Profiles/Quest.asset`、`PICO.asset` 是产品配置。往它们的 Renderer Data 上挂一个评估期三方 Renderer Feature，等于让产品配置依赖一个可能被删掉的包，且任何人打包产品都会带上它。装置必须可整体删除。

**替代方案**：直接往现有 Renderer Data 加 Feature（改动最小）。**否决 —— 「照搬更快」不是理由，可删除性是结构要求。**

### D6：新建 bench HUD，不扩展既有 `DiagnosticsHud`

**选了什么**：`MRBase.GsplatBench` 独立 asmdef，内含自己的 HUD。既有 `Assets/Scripts/Diagnostics/DiagnosticsHud.cs` 一行不改。

**为什么**：`MRBase.Diagnostics` 引用 `MRBase.Core`，是主工程常驻模块。给它加 gsplat 段落 = 让主工程诊断模块依赖评估期三方包。方向反了 —— 应当是 bench 依赖世界，不是世界依赖 bench。

且两者指标不同源：`DiagnosticsHud` 报的是手部/输入/passthrough 状态与平滑 FPS；bench 要的是 GPU ms 中位数、百分位、旋钮矩阵。共用一个类会把两套无关关注点缠在一起。

**沿用**其形状（分段 `Append*` + `StringBuilder` + TMP target + `reportIntervalSeconds`/`logIntervalSeconds` 双间隔），保持工程内可读性一致。

**替代方案**：往 `DiagnosticsHud` 加一段（代码更少）。**否决，理由如上。**

### D7：HUD 刷新限频，且必须自证不污染测量

**选了什么**：HUD 文本刷新 ≤5Hz（`reportIntervalSeconds ≥ 0.2`），日志 ≤0.5Hz。sweep 采样期间 HUD 可整体关闭，采样前后各测一次带/不带 HUD 的 GPU ms 作为自证。

**为什么**：TMP 每帧 rebuild 在移动端是可测量的开销。测量装置污染被测量对象，是这类工具最常见的失败模式。

### D8：单一大 ply + Opacity Prune 切档，而非多个 ply 文件

**选了什么**：要求用户提供一份 ≥200 万的完整 ply，用 `OpacityPruneThreshold` 在导入期切出不同数量档位。

**为什么**：换文件会同时改变数量、空间分布、不透明度分布、屏幕覆盖 —— 四个变量一起动，测出来无法归因。同一份数据只改剪枝阈值，只有数量在变。

**已知偏差**：Opacity Prune 优先剪掉低不透明度 splat，因此高剪枝档的**平均混合层数也会下降** —— 数量与 overdraw 并非严格解耦。这个偏差要在报告里显式标注，不能当作纯数量曲线读。

**替代方案**：准备多份不同规模的独立扫描。**否决**（变量太多），但保留「环境 vs 物件」两份作为**内容类型**维度 —— 这一维度差 3–5 倍，不测就没有全貌。

### D9：SPI / Multi-pass 不是运行时旋钮，降级为构建期上下文

**发现**：立体渲染模式是 OpenXR 的**构建期**设置，`XRSettings.stereoRenderingMode` 只读。tasks 6.1 和 7.1 原本把它列为旋钮和 sweep 首档，做不到。

**选了什么**：从旋钮集合与 sweep 序列中移除；`BenchConditions` 每帧回读并记录到 HUD 与 CSV 的 `stereo` 列。要对比 SPI 与 Multi-pass，**必须出两个包**，各跑一轮 sweep，按 CSV 的 `stereo` 列配对比较。

**替代方案与否决理由**：
- *在 sweep 中途重启 XR loader 切换模式*：中途重初始化会让 GPU 频率、资源驻留、着色器变体全部回到冷态，档间不可比。**否决。**
- *干脆不测 SPI*：SPI 是估算里倍率最大的一项（~2×），不测等于放弃最重要的一个数。**否决。**

### D10：新增「renderer 副本数」旋钮，副本排方阵而非堆叠

**背景**：用户提供的三份 ply 实际 splat 数为 28.6 万 / 33.8 万 / 55.7 万（文件名 30w/50w/100w 与内容不符），最大值远低于 proposal 要求的 200 万。单靠这些资产测不到天花板。

**选了什么**：加一个副本数旋钮（1/2/4/8/16），副本在 XZ 平面排方阵，间距取资产包围盒的世界尺寸并夹到 [0.5m, 30m]。

**为什么排方阵而不是堆在原地**：堆叠测的是「同一块屏幕叠 N 层」，那是纯 overdraw 放大器；排方阵测的是「场景里有 N 倍 splat」，后者才对应「能渲多少高斯点」这个问题。夹间距是因为扫描件包围盒常被离群 splat 撑到上万米（Model_30w 的包围盒是 11464×26796×20599），照搬会把副本甩出视野。

**已知偏差**：目前包无视锥剔除，屏幕外的副本仍参与排序，所以副本数带来的成本不全是渲染成本。报告须标注。

### D11：bench 用最小 XR rig，不用产品 `XROrigin_Base`

**选了什么**：bench 场景自建 `XROrigin + Camera Offset + Main Camera(Camera/TrackedPoseDriver)`，不引用 `Assets/Assets/Prefabs/Rig/XROrigin_Base.prefab`。

**为什么**：产品 rig 带双手蒙皮网格、poke/near-far/teleport 交互器、affordance 状态机、locomotion 一整套，每帧开销会混进被测数字。手柄按钮与摇杆输入来自 XR input subsystem 的设备层，不需要场景里有 controller 对象，所以砍掉它们不影响旋钮操作。

**替代方案**：复用产品 rig（改动更小、更贴真实运行环境）。**否决 —— 测量装置的第一要务是不污染被测量对象。**

### D12：周期性日志落 `.log` 文件，不走 `Debug.Log`

**触发**：真机前的编辑器试跑暴露两个问题 —— HUD 文本无界增长（见下）以及 Console 被每 2 秒一份的完整报告刷屏。

**选了什么**：新增 `BenchLog`，周期性快照与逐档 CSV 记录一律 `File.AppendAllText` 到 `Application.persistentDataPath` 下带时间戳的 `.log`。Console 只保留就绪、sweep 起止、文件路径三类里程碑和错误。

**为什么**：编辑器里每条 `Debug.Log` 都要收集调用栈、进 Console 列表、触发窗口重绘。一个每隔几秒吐整份报告的装置，等于把自己的开销混进被测数字里 —— 这正是「装置不污染被测量对象」那条要求要防的事。文件追加没有这些副作用，且真机上取文件比翻 logcat 更可靠。

**替代方案与否决理由**：
- *保留 `Debug.Log` + 靠 tag 过滤*：过滤解决的是**阅读**问题，不解决**开销**问题。**否决。**
- *完全不进 Console*：那就没法在 logcat 里找到 `.log` 的路径。**折中：只有里程碑进 Console。**

### D13：漂移原因用定长槽位，且 XR 未激活时整体标注

**Bug**：初版 `MarkDrift` 每帧做 `DriftReason += "; " + reason`。编辑器平面模式下 `XRSettings.renderViewportScale` 回读 0 而锁定值为 1，于是**每帧都判漂移**，字符串无界增长，HUD 文本涨到数万字符，TMP 每次重排把帧率吃光 —— 装置把自己测的东西压垮了。

**选了什么**：
1. 每个漂移项占一个**定长枚举槽位**，重复越界只覆写自己的槽位，`DriftReason` 长度由构造保证有界；`ClearDrift` 清空全部槽位。
2. `XrActive == false` 时不再逐项判定（那些量此时读不到真值），改为整体标一条 `xr-inactive (flat mode; not a valid headset run)` —— 平面模式本来就不是有效的头显测量，让它在 HUD 与 CSV 里都可见比静默跳过诚实。
3. `BenchRig.BuildReport` 加 4000 字符硬上限兜底。

**验证**：编辑器内构造 `BenchConditions`，锁定后制造持续越界条件，连续 `Poll` 5000 次 —— `DriftReason` 恒为 48 字符，`ClearDrift` 后归零。

## Risks / Trade-offs

- **测出来的是 upstream v1.4.0 的能力，不是 3DGS 在该硬件上的上限。** 装置结论不应外推为「Quest 3 最多渲 X 万高斯点」。
- **`FrameTimingManager` 在 XR 上的 GPU 时间可能包含或不含 compositor 开销**，跨平台语义未必一致。缓解：只用档间差值，不用绝对值下结论；首次运行时与 OVR Metrics Tool 对照一次校准。
- **PICO 与 Quest 的 FFR / 刷新率默认策略不同**，可能让两台的 baseline 不可直接比。缓解：HUD 显示实时值，报告分设备记录，不做跨设备平均。
- **`Opacity Prune` 的数量-overdraw 耦合**（见 D8）会让数量曲线偏乐观。
- **gsplat-unity 无视锥剔除**：屏幕外的 `GsplatRenderer` 仍参与排序。若测试内容拆成多个 renderer，会引入与 splat 总数无关的额外成本。缓解：默认单 renderer；多 renderer 作为独立一档单测。

## Migration Plan

装置是纯增量的，无迁移。删除路径：移除 `Assets/Scripts/GsplatBench/`、`Assets/Scenes/GsplatBench.unity`、bench URP 资产、`gsplat-unity` 包依赖，以及（可选）把 `enableFrameTimingStats` 改回 0。主工程无任何引用指向它们。

## Open Questions

1. 测试 ply 的实际内容形态（环境 / 物件 / 两者）—— 决定 sweep 要跑几轮。待用户提供资产后确定。
2. 是否两台设备都跑。默认都跑；若只关心 Quest 可裁掉一半工作。
3. `FrameTimingManager` 与 OVR Metrics Tool 的读数若差异显著（>20%），是否值得为 Quest 单独接 `OVRPlugin.GetAppGPUTime` 做校准锚点 —— 留到校准步骤有结果后再判。
