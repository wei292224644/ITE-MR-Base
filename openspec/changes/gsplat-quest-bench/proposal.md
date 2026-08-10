## Why

要在 MR 工程里用 3D Gaussian Splatting，先得知道**在 Quest 3 / PICO 4 Ultra 上到底能渲染多少高斯点**。目前只有二手估算，来源互相矛盾且全部过时：

- `wuyize25/gsplat-unity` issue #10（2025-11-21）：50 万 splats 在 Quest 上 **20 fps**。但这个数据点早于该包**全部**移动端优化（Spark 打包 2026-01、SH 打包 2026-02、Downscale 2026-03、排序降频 2026-03、Opacity Prune 2026-07）。
- `ninjamode` fork 报「Quest 3 约 40 万稳 72fps」，行业普遍说法「<50 万」。
- issue #7 报 150 万「VR 无卡顿」，但未说明是 PCVR 还是 standalone。

更要命的是「多少万」本身是坏指标 —— 真实成本是 `Σ(splat 屏幕面积 × 混合层数)`，同样 100 万 splats，「看着一个物件」和「站在环境里」差 3–5 倍。没有针对**本项目实际内容**的实测，后续所有架构决策（是否重训 linear 资产、是否给渲染器加独立 RT 模式、是否要自研 LOD）都建立在猜测上。

本变更交付一套**一次性、可整体删除的实测装置**：设备内 HUD + 运行时旋钮 + 自动 sweep + 结构化日志，产出「每个优化旋钮对本项目内容的 Δ GPU ms」这张表。

本变更**只测量，不优化，不改渲染架构**。

## What Changes

- 新增 bench 场景与独立程序集 `MRBase.GsplatBench`，与主工程运行时解耦，可整体移除
- 引入 `wuyize25/gsplat-unity` v1.4.0 **上游原样**作为被测对象（不打补丁、不合任何未合并 PR）
- 设备内 HUD：GPU/CPU 帧时间中位数、FPS 与 1% low、splat 总数、全部旋钮当前值、XR 刷新率/渲染缩放/FFR 等级、显存与系统内存
- 运行时旋钮控制器：手柄按键实时切换 SPI、MSAA、SH degree、Downscale、排序间隔、渲染缩放，无需重编译
- 自动 sweep：按固定累加序列逐档采样，每档预热后取中位 GPU ms
- 结构化输出：CSV 落 `Application.persistentDataPath`，同内容打 logcat（固定 tag）
- 测量前置条件的显式锁定：关 ASW/SpaceWarp、固定刷新率、固定渲染缩放与 FFR 等级
- 打开 Player Settings 的 **Frame Timing Stats**（当前为 `enableFrameTimingStats: 0`）

## Capabilities

### New Capabilities

- `gsplat-benchmark-harness`：3DGS 渲染性能的设备内测量装置 —— 测量条件锁定、指标可读性、旋钮可控性、采样与产出契约

### Modified Capabilities

- （无）不修改任何既有能力。既有 `DiagnosticsHud` 不改动（理由见 design D6）

## Impact

- **代码**：新增 `Assets/Scripts/GsplatBench/`（独立 asmdef），不改 `MRBase.Diagnostics` / `MRBase.Core` / 任何既有渲染代码
- **场景**：新增 `Assets/Scenes/GsplatBench.unity`，独立可运行，不 additive 依赖 `MRCore`
- **渲染配置**：新增 bench 专属 URP Asset + Renderer Data（挂 `GsplatURPFeature`），**不污染** 既有 `Build Profiles/Quest.asset`、`PICO.asset` 走的渲染配置
- **项目设置**：`enableFrameTimingStats` 0 → 1（全局，影响主工程，代价可忽略）
- **依赖**：`gsplat-unity` v1.4.0（MIT）。仅 bench asmdef 引用它，主工程程序集不引用
- **资产**：需要用户提供测试用 `.ply`（要求见下方 Open Assumptions）
- **非目标**：色彩空间修正、独立 RT 模式、LOD、视锥剔除、SOG 支持、任何进入产品路径的 3DGS 集成

## Open Assumptions

- [DECIDED] 用户选择先走 OpenSpec change（B）而非直接实现（A）。本变更为该决定的产物。（用户确认 B）
- [DECIDED] 本轮**只测上游 v1.4.0 原样**，不引入 SOG（PR #40 未合并、Android 上 libwebp 可用性未验证），且 SOG 解码后落回同一 Spark 表示、对渲染性能零影响 —— 测它没有信息量。（研究结论）
- [DECIDED] 本轮**接受画面偏色**。项目为 Linear 色彩空间，gsplat-unity 在 Linear 下颜色不正确；但颜色不改变像素数与混合层数，对性能测量零影响。色彩空间语义的落点是**另一个变更**的议题。（design D1）
- [OPEN] 测试 ply 由用户提供。要求：≥200 万 splats、带完整 SH degree 3、未经剪枝压缩的原始 3DGS 输出；**最好两份** —— 一份「站进去的环境」、一份「看着的物件」，二者性能预算差 3–5 倍，单一类型测不出全貌。
- [OPEN] 是否需要在 PICO 4 Ultra 上同步跑一遍。两者同为 XR2 Gen 2 / Adreno 740，数字应当接近；差异若出现，多半来自 FFR/刷新率策略而非渲染本身。默认两台都跑，用户可裁。
- [OPEN] 验收门槛。本变更是测量装置，不设性能目标；验收 = sweep 能在真机跑完并产出完整 CSV。
