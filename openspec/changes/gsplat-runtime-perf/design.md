## Context

`gsplat-quest-bench` 是测量变更，它的 D1 明确把色彩空间和独立 RT 推给「下一个变更」。本变更就是那个变更。

前提事实（读 v1.4.0 源码 + 探查工程得到，非推测）：

| 事实 | 出处 |
|---|---|
| 工程色彩空间 Linear | `ProjectSettings.asset:51` `m_ActiveColorSpace: 1` |
| bench 场景开着 `GammaToLinear` | `GsplatBench.unity:869` |
| 逐 splat 转 linear 后再混合 | `Gsplat.shader` frag + `Blend One OneMinusSrcAlpha` |
| 作者本人把它描述为「before output」 | `Documentation~/Implementation Details.md:59` |
| 排序结果是 back-to-front | 同上 :40 |
| 每 instance 多个 quad，z 编码 intra-instance index | 同上 :8 |
| 实例数靠 CPU 回读算出 | `GsplatRendererImpl.cs:85-91` `OrderSizeBuffer.GetData` |
| bench 管线 MSAA 4x、不产深度图 | `Bench URP Asset.asset:28,22` |

数据集不动（200w 全留在显存），所有手段都作用在「每帧实际提交/着色的量」上。路线：**C（独立 RT + gamma 合成）→ 间接绘制 → A+B（compute 剔除 + view-data 预通道）→ D（自适应抽取闭环）→ E（前到后 + framebuffer fetch）留后手**。

## Goals / Non-Goals

**Goals**

- 修掉合成域错位：整条 over 链在 gamma 域完成，链尾一次转 linear
- 把 draw 提交点从 `MonoBehaviour.Update` 搬进 render pass —— 这是 A/B/D/E 全部的前置
- 每一步都能在设备上单独测量、单独回滚

**Non-Goals**

- 不改数据集、不做离线 LOD、不接 SOG
- 不改 BiRP / HDRP 路径的行为（保持逐字等价）
- 本变更不追求净加速；C1 预期是**负收益**，见 D004

## Decisions

### D001：fork + submodule 挂 `Packages/`，而不是 sibling + `file:` 路径

**选了什么**：`git@github.com:wei292224644/gsplat-unity.git` 作为 submodule 挂在 `Packages/wu.yize.gsplat`，manifest 移除原 git 依赖，包侧工作分支 `feature/quest-perf`。

**为什么成立**：本分支的产出是压测数字。一个 bench 数字不带包的版本信息就不是测量，是传闻。submodule 让项目 commit 锁定包 commit，两者被同一次 checkout 还原。

**替代方案与否决理由**：

- *sibling 目录 + `file:../../gsplat-unity`*：每天摩擦为零，但包版本在项目历史里完全不可见。**否决 —— 省下的是每天一点点摩擦，丢掉的是「这个数字是怎么来的」。**
- *裸 clone 进 `Packages/`*：嵌套 git 仓库，项目 git 既不跟踪也不忽略。**否决。**
- *继续用 `Library/PackageCache`*：只读缓存，reimport 即失。**不成立。**

### D002：draw 提交点搬进 render pass，用显式 `cmd.DrawMeshInstancedProcedural`

**选了什么**：`GsplatRendererImpl.Render` 拆成 `PrepareDraw` / `RecordDraw(cmd)` / `SubmitImmediate`。URP 在自己的 pass 里 `RecordDraw`；BiRP/HDRP 仍走 `SubmitImmediate` → `Graphics.RenderMeshPrimitives`。分流由 `GsplatSorter.DeferDraws` 决定，它是**当前管线类型的纯函数**。

**为什么成立**：`Graphics.RenderMeshPrimitives` 把 draw 交给 Unity 的 transparent 队列，项目侧既无法把它重定向到自己的 RT（C），也无法把实例数换成间接参数（A）。这一步是后面每个方案的共同前置，做一次。

**替代方案与否决理由**：

- *给 shader pass 打自定义 `LightMode` tag，用 `RendererList` 在自己的 pass 里筛出来*：改动小得多，且 splat 会自动从常规 transparent 队列消失。**否决 —— 它依赖「`Graphics.RenderMeshPrimitives` 提交的 draw 会被 `RendererList` 按 shader tag 捞到」这个隐式互操作。CLAUDE.md 的原则里，行为依赖隐式因素时重构优先于照搬。而且这条路给不了间接绘制。**
- *用「上一帧 render feature 跑过没有」推断是否延迟提交*：`Update` 早于管线钩子，只能读到上一帧的结论。**否决 —— 这正是「把一个决策藏在多个处理器的相互作用中」。**

**已知后果**：URP 下 `GsplatURPFeature` 从「可选（只管排序）」变成**必需**。缺失时 splat 直接不显示，而不是静默地画进错误的色彩空间。这是刻意的：响的失败比哑的失败便宜。

### D003：offscreen RT 定义为 gamma 域，转换只发生在链尾

**选了什么**：splat 画进独立 RT，`Blend One OneMinusSrcAlpha` 在 gamma 域完成整条 over 链；composite pass 做 unpremultiply → `GammaToLinearSpace` → 重新 premultiply → 合成回相机色。

**为什么成立**：3DGS 的 color/opacity 是在 gamma 域拟合的，`C = Σ cᵢαᵢΠ(1-αⱼ)`。正确解是 `γ⁻¹(Σ cᵢwᵢ)`，现状是 `Σ γ⁻¹(cᵢ)wᵢ`。γ⁻¹ = x^2.2 是凸函数，由 Jensen `Σf(cᵢ)wᵢ ≥ f(Σcᵢwᵢ)` —— 现状**系统性偏亮、对比过冲**，相邻层颜色差越大偏得越多。这不是参数没调好：`GammaToLinear` 开是数学错，关是把 sRGB 数值当 linear 用，**两个档位都不对**。共享的 linear 主 RT 上物理上放不下链尾的那次转换。

**连带的语义变更**：`GammaToLinear` 从「输出前转一次」改为「源色是 gamma 域」。offscreen 路径下 flag 为假时改做 `LinearToGammaSpace`（进入目标域）。非 offscreen 路径逐字保持原行为，由全局 `_GsplatOffscreen` 分流。

**替代方案与否决理由**：

- *全项目切 Gamma 色彩空间*：`gsplat-quest-bench` D1 已否决，理由未变。
- *逐 splat 补偿系数*：误差在合成算子里，不在每个 splat 的标量上，补不出来。**否决。**

**已知残留**：`GammaToLinearSpace` 用的是 UnityCG 的快速多项式而非精确 sRGB 曲线。composite 里**内联同一条多项式**而不是调 SRP core 的 `SRGBToLinear`，否则两端曲线不一致会自己引入偏色。

### D004：C 拆成 C1（全分辨率，只修正确性）和 C2（降分辨率，取性能）

**选了什么**：C1 的 RT 就是相机分辨率，只改合成域。C2 再把分辨率降下去。

**为什么成立**：CLAUDE.md 的「迁移与修 bug 保持可分离」。C1 + C2 一起上如果设备上掉帧或颜色不对，无法归因到「RT 重定向 / gamma 数学 / 分辨率+深度改法」三者中的哪一个。拆开每步单独可测、单独可回滚。

**诚实的预期**：**C1 是负收益。** 它净增：一张全分辨率 fp16 RT + 一次全屏 composite + 一个 depth prepass + 每片元一次深度采样。C1 的产出是「颜色对了」和「地基铺好了」，不是帧率。收益全在 C2 及之后。

### D005：offscreen 不挂深度附件，改在片元里采 `_CameraDepthTexture` 比较

**选了什么**：offscreen 分配为 `msaaSamples = 1`，splat 片元采样深度图自己做遮挡判断（镜像 ZTest LEqual，含 reversed-Z 分支）。pass 用 `ConfigureInput(ScriptableRenderPassInput.Depth)` 让 URP 自己产深度图，不依赖管线资产的开关。

**为什么被迫**：bench 管线 MSAA 4x。附件的 sample count 必须一致，所以要么给 offscreen 也配 4× MSAA 的 fp16，要么不挂附件。前者在 Quest 上把 tile 预算直接翻倍。

**顺带的发现**：splat 今天画进 4× MSAA 的相机色，**这笔钱完全白花** —— MSAA 只对硬几何边有效，而 splat 的边是 alpha 渐隐。搬到非 MSAA 的 RT 是质量中性的省。

**代价**：MSAA 开着时 URP 会插一个 depth prepass。bench 场景几乎没有不透明几何，接近免费；真实 MR 场景是实打实的一笔。**如果 prepass 在 bench 里显形，先考虑关 MSAA —— 对 splat 主导的场景那本来就该关。**

**回报**：因为是采样而非附件，offscreen 与相机分辨率彻底解耦，C2 变成只改一个 scale。

### D006：offscreen 用 fp16 而不是 8-bit

**选了什么**：`R16G16B16A16_SFloat`。

**为什么成立**：composite 要除掉 premultiplied alpha，把量化误差按 `1/alpha` 放大。8-bit 下 alpha 取到一步（1/255）时，颜色的一步误差被放大到满量程，每个 splat 的淡边都会长噪点。今天没这个问题，是因为今天从不做 unpremultiply。

**代价**：C1 阶段带宽翻倍。C2 降到半分辨率后，fp16 半分辨率 ≈ 8-bit 全分辨率。**这笔钱是 C2 付的。**

**暂不做**：不给格式开旋钮。等 C2 有了分辨率 scale 再一起开，那时才有可 sweep 的组合。

### D007：offscreen 缩放为相机分辨率的 0.5，并为 splat 绘制覆盖 `_ScreenParams`

**选了什么**：`GsplatSettings.OffscreenScale`（0.25–1，默认 0.5）决定 offscreen 的边长。pass 在 splat 绘制期间把 `_ScreenParams` 覆盖为 offscreen 尺寸，绘制结束还原。

**为什么必须覆盖**：splat 是**按目标像素定尺寸**的。`InitCorner` 用 `_ScreenParams.x * projMat00` 求 focal、用 `min(_ScreenParams.xy)` 限核大小、用 `proj.ww / _ScreenParams.xy` 把偏移换回裁剪空间；`GsplatOccluded` 也用它把 `SV_Position` 归一化。留在相机值上，splat 会按一个这张 RT 并不具备的分辨率排布。

**深度纹理的坐标桥**：`_CameraDepthTexture` 仍是相机分辨率，且作为 RTHandle 可能比在用区域更大。归一化坐标跨接两种分辨率，再乘 pass 传入的 `_GsplatDepthUvScale`（= `RTHandles.rtHandleProperties.rtHandleScale`）落进在用区域。**显式传值而不是假设它等于 1。**

**替代方案与否决理由**：

- *不覆盖 `_ScreenParams`，让 splat 按相机像素排布再缩放*：splat 会画得比目标能表达的更细，等于先算后扔。**否决。**
- *把 offscreen 尺寸单独发一个全局，不动 `_ScreenParams`*：要在 `InitCorner` 的四处引用逐个替换，且和上游分叉更深。覆盖再还原的作用域更小、更好审阅。**否决。**

### D008：删掉尺寸剔除，渲染器不做「画哪些高斯点」的决定

**选了什么**：移除 `InitCorner` 里 upstream 的 `if (l1 < 2.0 && l2 < 2.0) return false;`。同时把 composite 的丢弃阈值从 `a < 1/255` 收紧到 `a <= 0`。保留视锥剔除与相机背后剔除。

**为什么成立**：

1. **职责**：哪些高斯点存在，是资源的决定 —— 离线剪枝、LOD、重训。渲染器擅自丢，等于把一个资源问题藏进 shader 里，而且藏在一个没人会去读的早退分支里。
2. **它污染测量**（决定性的一条）：D007 覆盖 `_ScreenParams` 之后，这个阈值变成以 offscreen 像素计。于是 `OffscreenScale` 一动，**被画的高斯点集合跟着动** —— 一个旋钮同时改 fill 和点数，测出来的 Δ 无法归因到任何一个。这直接违反 `gsplat-quest-bench` D2「系统在你背后改变工作量」的禁令，而这次那个系统就是我们自己。

composite 的 `a < 1/255` 同理：它的理由是「8-bit 下低于一步无法改变目标」，但 bench 管线 `m_SupportsHDR: 1`，相机色不是 8-bit，前提不成立。而 premultiplied 下 `rgb <= a`，`rgb/a` 天然有界，fp16 里也不需要下限保护。

**代价**：投影到亚像素的高斯点现在会走完顶点段并生成退化四边形。fill 接近零（光栅化不出片元），但顶点与 binning 的成本要付。**这笔成本是显式的、可测的，而且应该由资源侧的剪枝来消除，不是由渲染器偷偷吃掉。**

**尚未处理，待定**：upstream 还有两处基于 alpha 而非尺寸的丢弃 —— `Gsplat.shader` 的 `alpha < 1/255 discard`，以及 `ClipCorner` 按 alpha 收缩四边形。两者都是「贡献低于可见阈值」而不是「按尺寸取舍」，且 `ClipCorner` 一旦移除，每个 splat 的四边形都会变成全尺寸，fill 成本大涨。归入同一原则与否，留待决定。

## Risks

| 风险 | 现状 |
|---|---|
| ASW 在两次实测里都报 `half-rate x1.70` | **未解决，且它使任何跨条件比较失效**（见 `gsplat-quest-bench` D2）。取受控数字前必须先关掉 |
| C2 的半分辨率对边缘质量的影响 | 未评估。composite 走 bilinear 上采样，`OffscreenScale` 可回到 1 直接对比 |
| 删掉尺寸剔除后亚像素高斯点的顶点/binning 成本 | 未测。D008 明确接受它作为显式成本，由资源侧剪枝消除 |
| 遮挡测试从未被真正验证 | bench 场景没有挡在 splat 前面的不透明几何，D005 那条路径在真机上只是没报错，不等于对 |

## Verified

| 项 | 结论 |
|---|---|
| `cmd.DrawMeshInstancedProcedural` 的立体展开 | **通过**。设备为 `SinglePassMultiview`，眼睛索引走 `gl_ViewID_OVR` 内建而非 instance ID，双眼均正常渲染 |
| offscreen + composite 确实在跑 | **通过（逻辑必然）**。`DeferDraws=true` 时 `SubmitImmediate` 永不被调用，splat 上屏的唯一通路就是 offscreen pass 的 `RecordDraws` + composite |
| 颜色 | 观感正常，无瑕疵 |
| `GsplatURPFeature` 装配 | 已加入 Performant / Balanced renderer config，`m_RendererFeatureMap` 由 `ValidateRendererFeatures` 重算 |

## 尚无有效数据

改前/改后两份设备日志的快照均为自由走动采集，视角与朝向不同，且 sweep 从未运行（全程 `sweep (idle)`）。改后聚在 7.5–8.2ms、改前聚在 10–17ms，**但这不是受控对比，不构成结论**。有效的 Δ 需要：关闭 ASW，跑 bench 自带的 sweep，并重建一次改前的包作为 baseline。
