# 高斯 LoD：`.gsd` 格式、`gsd-build` 与运行时选点（gsplat-lod）—— 设计

> 状态：已验收（2026-09-30，Editor；以判据 2 为准，见 D23）
> 日期：2026-09-28
> 范围：子项目 **A**（LoD）。B（填充：E-B 早停 + √5σ）、C（地板：间接绘制）各自另起 spec。
> 本轮验收只在 **Editor（Play 模式）** 里做，真机阶段见 §10。

## 1. 目标

让一个 **150 万高斯的源模型**能在 Quest 3/3S 与 PICO 4 Ultra（XR2 Gen 2）上渲染，每帧只画按视角选出的 ≤ N 个高斯，
使每帧开销由预算 N 决定，而不是由源模型大小决定。

- "60fps" 在头显上落到 **72Hz = 13.9ms/帧**（两台设备没有原生 60Hz 档）。PICO 4 Enterprise（XR2 Gen 1）能跑即可，不承诺帧率。
- **A 单独到不了 72fps**：实测 fill 占八成（§2），LoD 只限点数、不限 fill。A 的承诺是"开销与源大小脱钩"，72fps 是 A+B+C 的合计目标。
- LoD 策略与模型形态无关（房间扫描、单个物体一视同仁），判据只有投影到屏幕上的尺寸。

**不在范围内**：流式/分页加载、多模型联合遍历、cutouts 与 LoD 同用、连续 LoD（父子插值）、SH 聚类压缩、
下载与缓存（调用方的事）、离线剪枝、BiRP/HDRP 的验证。

## 2. 现状与参照（读码 + 实测所得，非推测）

### 2.1 本工程渲染器（`Packages/wu.yize.gsplat`，fork 的 submodule）

已有：GPU 基数排序（`DeviceRadixSort`）、隔帧排序、gamma 域半分辨率 offscreen + composite、前到后 under 算子、
Spark 16B 打包格式（`GsplatAssetSpark`）。每帧要画的高斯集合就是 `OrderBuffer`：`InitOrder` compute 追加 →
`ExtractOrderSize` 用 `GetData` **同步回读**数量 → `ComputeDepth` → 基数排序 → 绘制。

2026-08-11 Quest 3 实测（Model_30w = 286,358 splats，旧 openspec `gsplat-runtime-perf` 存档）：

| 档 | GPU 中位 |
|---|---:|
| 无损画质档（MSAA off、SH3、sort 1/30、全分辨率） | 27.8ms |
| 推荐档（quad ×0.75、viewport 0.7、offscreen 0.5） | 7.74ms |

分解：无损档 **fill ≈ 22–24ms（八成）**，地板 ≈ 4–6ms。150 万是 28.6 万的 5.2 倍，全量绘制无路可走。

### 2.2 Spark（sparkjsdev/spark @ `9672638`，MIT）

- **每帧预算 N**，Quest 默认 **50 万**（`SparkRenderer.defaultSplatTarget()`），文档原话 "Quest 3: 1 million splats or less"。
- **LoD 树 = 每个高斯一个节点**。建树两法：`tiny-lod`（体素逐层合并，base 1.5）、`bhatt-lod`（逐层内 3×3×3 邻格找
  Bhattacharyya 相似度最高者两两贪心合并，base 1.75，离线推荐）。合并按 `opacity × 椭球面积` 加权；合并后不透明度 >1，
  编码为 D∈(1,5]，衰减函数 `1 − (1 − e^{−z²/2})^{exp((D²−1)/e)}`，quad 放大 `0.7·(D−1)` σ。
- **选点**（`lod_tree.rs::traverse_lod_trees`）：按 `pixel_scale = size / 径向距离 × foveate` 的最大堆，
  取最大者展开；小于一像素或展开会超 N 即停。O(N log N)，与总量无关，worker 里异步跑，父子硬切换。
- foveate 默认：前方 90° 内 1.0，120° 处 0.4，身后 0.2。
- RAD 格式存中心坐标默认用 F32（源码注释："Use F32 for now to be safe"）。

### 2.3 LCC-Unity-SDK（xgrids，v2.2.10，读 API 手册 + 反编译结构，未搬代码）

- VR 默认**每帧渲染预算 55 万**（`c_maxVRRenderSplat = 550000`），GPU buffer 上限 200 万。
- LoD 是**按空间块**（`MetaNode`：包围盒 + 子节点 + splat 文件），按到包围盒的距离算 SSE 选块，再叠预算。
- Quest/PICO 上排序用 GPU `DeviceRadixSort`。

两家独立方案都收敛在 **XR2 Gen 2 每帧 50–55 万 + LoD**。

## 3. 架构总览

```
离线   .ply/.spz ──gsd-build(Rust)──▶ model.gsd
                                        │
Unity  ScriptedImporter ─┐              │
       运行时 byte[] ────┴─▶ GsdReader ─▶ GsplatLodAsset
                                        │
绑定   节点+SH ─▶ GPU (GsplatResourceLod)
       Nodes 段 ─Burst 推导─▶ CPU 遍历表
                                        │
每帧   相机动了且上一轮完成 ─▶ 遍历 job（worker）
       完成 ─▶ OrderBuffer.SetData(≤N 索引)，RemainingCount = N'（CPU 已知）
       ─▶ ComputeDepth(N') ─▶ 基数排序(N') ─▶ 绘制（LOD shader 变体）
```

LoD 只替换"集合"这一段，排序与绘制主体不动。

## 4. `.gsd` 格式

小端；各段 16B 对齐，可直接上传 GraphicsBuffer；不压缩。

```
Header   magic "GSD\0" · version u32 = 1 · nodeCount u32 · leafCount u32      // 16B
         shDegree u8 (0..3) · 3B 填充 · boundsMin f32×3 · boundsMax f32×3 · 4B 填充   // 共 48B
Nodes    2×uint4 × nodeCount              // 32B ExtSplat，叶子 + 内部节点，层序
SH       SH1 uint2 × nodeCount            // shDegree ≥ 1
         SH2 uint4 × nodeCount            // shDegree ≥ 2
         SH3 uint4 × nodeCount            // shDegree ≥ 3
Tree     childStart u32 × nodeCount
         childCount u16 × nodeCount
```

**Nodes（32B，Spark ExtSplats 布局）**：

```
uint4 #0  center.x f32 · center.y f32 · center.z f32 · packHalf2x16(alpha, 0)
uint4 #1  packHalf2x16(r, g) · packHalf2x16(b, ln sx) · packHalf2x16(ln sy, ln sz) · quat(oct 10+10, angle 12)
```

alpha ≤ 1 为普通不透明度；alpha > 1 即合并节点的 D（≤ 5）。坐标系为 **Unity RUF**。

**SH**：与现有 `GsplatAssetSpark` 的打包逐位相同（SH1 sint7，SH2 sint8，SH3 sint6）。

**不变式**（写入端自检、读取端校验，共用一套规则）：

1. 根节点下标 0；
2. `childCount > 0` 时 `childStart > 自身下标` 且 `childStart + childCount ≤ nodeCount`；
3. 所有子区间互不重叠，除根外每个节点恰被一个父节点覆盖；
4. `childCount == 0` 的节点数 = `leafCount`；
5. 各段长度与 `nodeCount`、`shDegree` 推出的长度一致，文件无截断、无多余。

## 5. `gsd-build` CLI

位置：gsplat 仓库 `Tools~/gsd-build/`（Rust；`~` 使 Unity 不导入）。`target/` 进 `.gitignore`。

```
[复用 spark-lib]  MultiDecoder            读 .ply/.spz/.splat/.ksplat
[复用 spark-lib]  bhatt_lod / tiny_lod    默认 bhatt（base 1.75），--quick → tiny（base 1.5）
[复用 spark-lib]  chunk_tree              重排：根在 0，子节点连续
[自写]            坐标系转换 + .gsd 编码  --source（默认 RUB）→ RUF，含 SH 奇偶翻转；与 HLSL 解码逐位一致
[自写]            不变式自检              §4 规则，不过不写文件
```

- 依赖：`spark-lib = { git = "https://github.com/sparkjsdev/spark", rev = "9672638", default-features = false,
  features = ["gsplat", "ply", "spz", "antisplat", "ksplat", "tiny_lod", "bhatt_lod"] }`。`antisplat`/`ksplat` 就是
  `.splat`/`.ksplat` 的解码器（锁定的 rev 里都有、都不引入额外依赖）；此前的特性表漏了这两项，`MultiDecoder` 那行声称的
  `.splat`/`.ksplat` 实际解不了，现已补上。`.splat` 有 `cargo test`（`antisplat_input_decodes`）覆盖，`.ksplat` 只依赖
  spark-lib 自身的解码器，本仓库未单测。
- 参数：`--source`、`--max-sh 0..3`（默认保留源阶数）、`--quick`。输出一行统计：输入数、丢弃数、叶子数、节点数、层数、SH 阶、耗时、文件大小，
  如 `338003 leaves, 450113 nodes, 25 levels, SH 3, LoD 4.0s, 33.5 MB`（`Model_50w`）。
- 坐标转换规则照 `GsplatUtils.AxisSigns` 及其 SH 带内奇偶规则。

## 6. 运行时：加载、遍历、调度

**加载**：`GsdReader.Read(ReadOnlySpan<byte>) → GsplatLodAsset`，ScriptedImporter（`.gsd`）与运行时字节共用。
违反 §4 任一条即抛出指明条目的异常，不产出半个资产。

**绑定**：节点与 SH 按现有 `UploadData` 模式上传；Burst job 从 Nodes 段推导 CPU 遍历表
（SoA NativeArray：`float3 center`、`float size = 2·max(scale)·max(1, D)`、`uint childStart`、`ushort childCount`）。

**遍历 job**（Burst `IJob`，单线程，worker 上跑）：

- 输入：中心眼在模型空间的位置与前向、预算 N、像素阈值、foveate 参数。
- 算法：照移 `traverse_lod_trees`（单实例），最大堆为自写 Burst 二叉堆。
- 像素阈值 = `2·tan(fovY/2) / (眼图高度 × OffscreenScale)`。
- foveate 默认同 Spark：`coneFov0 = 90°`、`coneFov = 120°`（均为全角）、`coneFoveate = 0.4`、`behindFoveate = 0.2`。

**调度**：

- 每帧：上一轮 `IsCompleted` 且相机位姿（模型空间位置、前向）与上一轮发起时**不完全相等** → 发起下一轮。
  不设阈值：头显里头总在动，等于连续跑；静止视点则不重复跑。主线程只查不等。
- 完成：`OrderBuffer.SetData(indices, 0, 0, n)`，`RemainingCount = n`，**不回读**。
- 新集合上传那一帧**强制重排**，不受排序间隔影响。
- 绑定时同步跑第一轮，首帧即有画面。
- `OnDisable` / 销毁：先 `Complete` 在飞 job，再释放 NativeArray 与 buffer。

## 7. 与排序、shader 的对接

- **资源**：新增 `GsplatResourceLod`（Nodes 两个 uint4 buffer + SH buffer）。
- **深度**：LOD 深度 kernel 读 `_OrderBuffer[i]` → 节点 f32 中心；**dispatch 规模按 `RemainingCount`**。
- **容量**：`OrderBuffer` 与排序辅助 buffer 按预算 N 分配；预算改变走 `RecreateResources`。
- **所有权**：LoD 资产的 `OrderBuffer` 只由 LoD 上传写入；上传时置 `Initialized = true`；`InitPayload` 对 LoD 资源断言不可达。
- **全局排序**：`CanRenderGlobally` 对 LoD 资源回退逐 renderer，警告文案改为如实说明资源类型。
- **Shader**：`Gsplat.shader` 的 `multi_compile UNCOMPRESSED SPARK` 增加 `LOD`；新增 `GsplatLod.hlsl`（ExtSplat 解码，
  移植自 Spark `splatDefines.glsl`）；SH 解码从 `GsplatSpark.hlsl` 拆到共享的 `GsplatSparkSH.hlsl`。
  D > 1 时顶点端 quad 从 √8σ 扩到 `(√8 + 0.7·(D−1))` σ 并同步 `A` 的归一化，片元端换 Spark 衰减；D ≤ 1 保持 `exp(-4A)`。
  `_ScaleFactor`、视锥剔除、`ClipCorner` 不变。
- **许可**：gsplat 包 `Third Party Notices.md` 追加 Spark（World Labs，MIT）条目，覆盖移植的解码与遍历算法。

## 8. 测试与验收（本轮：Editor）

### 8.1 自动化测试

| 层 | 内容 | 位置 |
|---|---|---|
| Rust 单测 | 不变式检查器；ExtSplat/SH 编解码往返；坐标翻转手算样例 | `cargo test` |
| 跨语言金样 | CLI 从 64 点合成 PLY 产出 fixture `.gsd` + 期望值 JSON；EditMode 测试**在 GPU 上**用 `GsplatLod.hlsl` 的 compute 解码回读逐项比对 | fixture 提交进 gsplat 仓库 |
| 解析器 | 截断、magic/版本错、`childStart ≤ 自身`、区间重叠、孤儿节点、leafCount 不符 —— 各报对应错误 | EditMode |
| 遍历 | ① 输出 ≤ N；② 输出是合法切面（每个叶子恰被输出中一个祖先或自身覆盖）；③ 相机越远输出越少；④ N 无限且阈值 0 时输出 = 全部叶子 | EditMode，合成树 + Burst job |

gsplat 包新建 `Tests/Editor`（asmdef），`Packages/manifest.json` 的 `testables` 加入 `wu.yize.gsplat`。

### 8.2 Editor 性能对比（回答"是否真的提升帧率"）

- **场所**：用户打开着的 Editor，GsplatBench 场景，**Play 模式**，Mac / Metal。Game 视图固定 2064×2208（Quest 3 单眼），单目渲染。
- **GPU 时间**：`FrameTimingManager`（bench 的 `BenchGpuSource.FrameTiming`）。开跑前 60 帧 `gpuFrameTime` 若恒为 0，
  **中止并报错**，不静默换成墙钟时间。
- **bench 改动**：可选模型增加 `.gsd`；新增旋钮 LoD 预算 {30, 40, 50, 60, 80} 万；三个固定视点（全景 / 中距 / 贴近表面，
  贴近是 fill 最坏情况）。CSV 额外记录每帧实画数、遍历 job 耗时、遍历延迟（帧）。
  bench 现在全由 XR 手柄驱动（`BenchRig` 的 `<XRController>` 绑定），且启动时等待 XR display subsystem：
  Editor 无头显下需补一条非 XR 的启动路径，sweep 由 `unity command eval` 调 bench 的公开入口触发。
- **判据**（同旋钮、同视点）：
  1. **LoD 有效**：`GPU(Model_200w.gsd @ N=50万) ≤ 1.2 × GPU(Model_50w.ply 全量)`。1.2 留给合并节点更大的足迹。
     **（前提已被 §8.3 否定，降为记录项，不作门槛，见 D23）**
  2. **与源大小脱钩**：`Model_100w.gsd` 与 `Model_200w.gsd` 在同一 N 下 GPU 时间相差 ≤ ±5%。
  3. 同时记录 `Model_200w.ply` 全量作对照，量化 LoD 相对全量的收益倍数。
  4. 每帧实画数 ≤ N；主线程除绑定外不等待遍历 job。
- **局限（写入结果）**：Mac GPU 与 Adreno 同为 tile-based，但算力与带宽差一个量级、非立体渲染、带 Editor 开销。
  Editor 数字只证明**相对**收益与脱钩；72fps 的绝对判断属真机阶段（§10）。

### 8.3 结果（2026-09-28，Editor / Mac Metal）

**环境**：`Mac14,3 / Apple M2 / Metal`，Unity `6000.4.4f1`，Game 视图 `2064×2208`（Quest 3 单眼，`SetCustomRenderingResolution` 生效）。
三个固定视点（世界坐标，沿用 Task 13 写入场景的位姿）：

| 视点 | position | euler |
|---|---|---|
| Overview | (1.56, 1.20, 3.06) | (12, 225, 0) |
| Mid | (0.60, 1.10, 2.10) | (8, 225, 0) |
| Close | (0.32, 1.00, 1.82) | (3, 225, 0) |

CSV：`docs/superpowers/specs/data/2026-09-28-gsplat-lod-editor.csv`，24 行数据（控制台打印的"40 rows"是文件总行数，含 16 行 `#` 头注释 + 1 行列头 + 24 行数据 = 41 行，不是数据行数）。sweep 全程未出现新增 console error、未触发 Error Pause 暂停（对比开跑前的错误基线 seq 16172，跑完后最新错误仍是 16172）。CSV 头注释里的 `# asset,Model_30w` / `# asset_splat_count,286358` 是 bench rig 启动时挂着的初始资产，**不是** sweep 所测的资产；每行测的是哪个资产以数据行的 `asset` / `asset_is_lod` 列为准。

**行有效性（Ruling 9）**：Editor 下每一行 `valid` 列都是 `invalid`——`BenchConditions` 给非 XR（flat mode）运行统一打上 `drift = "xr-inactive (flat mode; not a valid headset run)"`，这是设计如此（D17 让本轮就在 Editor 里验收），不是异常。判据改用"editor-valid"：`gpu_median_ms` 是数字、`samples > 0`，且把这条固定 drift 原因（以及残留的 `"; "` 分隔符）去掉后 `drift` 为空——其余任何 drift 原因，或没有 GPU 计时，仍会被排除。按此规则，24 行全部 editor-valid，判据 1/2/4 用这 24 行计算，没有行因此被跳过。

**Task 12 建树统计（原文照抄）**：

```
Model_50w.ply -> Model_50w.gsd: 338003 input, 0 empty dropped, 338003 leaves, 450113 nodes, SH 3, LoD 3.8s, 33.5 MB (wall 4.6s)
Model_100w.ply -> Model_100w.gsd: 556529 input, 0 empty dropped, 556529 leaves, 728449 nodes, SH 3, LoD 8.6s, 54.2 MB (wall 10.3s)
Model_200w.ply -> Model_200w.gsd: 880282 input, 0 empty dropped, 880282 leaves, 1134668 nodes, SH 3, LoD 31.7s, 84.4 MB (wall 33.9s)
```

**模型规模说明**：文件名夸大了 splat 数——"200w" 实际只有 0.88M splats（880282 leaves），本轮判据没有直接测到 spec 的 1.5M 目标规模。判据 2（与源大小脱钩）是唯一间接支持"往 1.5M 外推"的证据，但见下文，它在 80 万预算档已经不成立。

**Burst / Jobs 设置（Ruling 10，原样记录，未改动）**：`Unity.Burst.BurstCompiler.IsEnabled = True`，`EnableBurstSafetyChecks = True`，`Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = True`。`lod_traversal_ms` 是"带这些设置的 Editor 数字"，不是真机估计：18 行 LoD 行里 `lod_traversal_ms` 落在 **51.4–237.5 ms**，`lod_latency_frames` 落在 **0–25 帧**——量级与 Task 13 冒烟测试的 45–51 ms / 最多 28 帧一致，且随预算变大而显著变长（80 万预算档遍历普遍超过 140 ms）。这两项都不进 GPU 时间，只影响 LoD 更新的滞后。**Overview 各行的 lat = 0 不是测出来的**：sweep 每换一个资产/预算就先把相机放到 Overview 再换绑，绑定时同步跑的那一轮 `RunNow`（D10）用的已经是 Overview 的视角，它把 `LastLatencyFrames` 置 0；相机此后不动，不会再发起异步遍历。异步延迟只在 Mid/Close（换视点后的那一轮）上测到，即 7–25 帧。

**逐视点结果表**（GPU 时间为 `gpu_median_ms`，trav 为 `lod_traversal_ms`，lat 为 `lod_latency_frames`）：

*Overview*

| 资产 | GPU ms | drawn | trav ms | lat |
|---|---|---|---|---|
| Model_50w.ply 全量 | 11.56 | 338003 | – | – |
| Model_200w.ply 全量 | 25.42 | 880282 | – | – |
| Model_100w.gsd @30w | 11.43 | 300000 | 67.06 | 0 |
| Model_200w.gsd @30w | 10.67 | 299994 | 56.66 | 0 |
| Model_100w.gsd @50w | 16.12 | 500000 | 122.83 | 0 |
| Model_200w.gsd @50w | 16.52 | 499996 | 137.29 | 0 |
| Model_100w.gsd @80w | 16.89 | 549658 | 123.40 | 0 |
| Model_200w.gsd @80w | 23.32 | 799998 | 183.99 | 0 |

*Mid*

| 资产 | GPU ms | drawn | trav ms | lat |
|---|---|---|---|---|
| Model_50w.ply 全量 | 15.21 | 338003 | – | – |
| Model_200w.ply 全量 | 26.42 | 880282 | – | – |
| Model_100w.gsd @30w | 20.84 | 299998 | 59.05 | 11 |
| Model_200w.gsd @30w | 21.61 | 299998 | 51.36 | 8 |
| Model_100w.gsd @50w | 23.44 | 500000 | 106.14 | 15 |
| Model_200w.gsd @50w | 24.07 | 499999 | 94.80 | 25 |
| Model_100w.gsd @80w | 23.99 | 556480 | 146.97 | 16 |
| Model_200w.gsd @80w | 27.91 | 799998 | 237.47 | 21 |

*Close*

| 资产 | GPU ms | drawn | trav ms | lat |
|---|---|---|---|---|
| Model_50w.ply 全量 | 22.11 | 338003 | – | – |
| Model_200w.ply 全量 | 29.52 | 880282 | – | – |
| Model_100w.gsd @30w | 29.26 | 299999 | 58.88 | 7 |
| Model_200w.gsd @30w | 29.64 | 299999 | 55.91 | 7 |
| Model_100w.gsd @50w | 30.73 | 499995 | 122.35 | 12 |
| Model_200w.gsd @50w | 29.76 | 499995 | 108.17 | 10 |
| Model_100w.gsd @80w | 30.76 | 556122 | 142.21 | 15 |
| Model_200w.gsd @80w | 33.86 | 799993 | 186.12 | 19 |

**判据 1（LoD 有效，`200w.gsd@50w ≤ 1.2 × 50w.ply 全量`）：未通过，三个视点都超标。**

- Overview：16.52 / 11.56 = **1.43×**
- Mid：24.07 / 15.21 = **1.58×**
- Close：29.76 / 22.11 = **1.35×**

原因判断：预算 50 万（500000）在三个视点的 `drawn_splats` 都逼近或打满预算上限（499995–500000），比对照组 `Model_50w.ply` 的 338003 个原生 splat 多出约 48%——但"多画了图元"只解释得了 Overview。用**等图元数**的切片把"多画图元"这个变量控制掉：取 `Model_200w.gsd @30万`（`drawn_splats` ≈30 万，比 338003 **更少**）与 `Model_50w.ply` 全量比：

| 视点 | 200w.gsd@30万 ms | 50w.ply 全量 ms | 比值 | ns/drawn（200w.gsd@30万） | ns/drawn（200w.ply 全量） |
|---|---|---|---|---|---|
| Overview | 10.67 | 11.56 | **0.92×（通过）** | 35.6 | 28.9 |
| Mid | 21.61 | 15.21 | **1.42×（未通过）** | 72.0 | 30.0 |
| Close | 29.64 | 22.11 | **1.34×（未通过）** | 98.8 | 33.5 |

（ns/drawn = `gpu_median_ms × 1e6 / drawn_splats`，即每画一个图元的平均 GPU 时间；"200w.ply 全量"的 ns/drawn 用 `Model_200w.ply` 全量的 `gpu_median_ms`/`drawn_splats` 算，作为"未合并的原生 splat"基准。）

这张表说明：图元更少（30 万 < 33.8 万）时，Overview 反而比 `50w.ply` 全量更快（0.92×，通过）——Overview 的判据 1 失败是**数量驱动**：它本来不需要画到 50 万个节点，是预算档位把它推高的。Mid 和 Close 在图元更少时依然更慢（1.42×、1.34×），每图元耗时也高得多（Close 处 98.8 vs 33.5 ns/drawn，约 2.9 倍；30 万个 LoD 节点的 29.64 ms 已接近画全部 88 万个原生 splat 的 29.52 ms）。但这**不是**合并节点变宽造成的，数据不支持那个解释：

- **D > 1 的节点极少，也不宽。** 直接读三棵树的 Nodes 段（§4：节点 word0.w 低 16 位是 half 的 alpha/D）：D > 1 的节点只占全树的 **1.18% / 0.86% / 0.59%**（50w / 100w / 200w；200w 是 1,134,668 个节点里的 6,666 个，占其 254,386 个内部节点的 2.62%），最大 D 分别为 3.176 / 3.098 / 3.156，对应 quad 放宽 k = (√8 + 0.7·(D−1))/√8 ≤ 1.54。内部节点绝大多数是 alpha ≤ 1 的普通高斯，根本不走放宽路径。
- **数据支持的是"LoD 保留了源模型的屏幕覆盖与 overdraw"。** Close 处 `200w.gsd@30万 / 50w.ply` = 29.64 / 22.11 = **1.34**，恰好等于 `200w.ply / 50w.ply` = 29.52 / 22.11 = **1.335**：30 万个节点的切面画出的 fill 与 88 万个原生 splat 相同。合并节点按 opacity × 面积加权拟合子节点，足迹覆盖子节点的并集（机制是推断，证据是上面这组比值；"B 的第一个实验"第 3 条直接量）——LoD 保住了 200w 重建的外观，也就保住了它的屏幕覆盖和每像素叠层数；`Model_50w.ply` 是另一份更稀疏的重建，overdraw 本来就少。在 fill 受限的视点，少画图元不等于少着色像素，ns/drawn 随图元数下降而上升，只是同一份 fill 摊到了更少的图元上。
- **LoD 路径另有一份尚未归因的逐图元开销。** Close @80万 的 LoD 画 799,993 个图元用 33.86 ms，比画 880,282 个原生 splat 的 `200w.ply` 全量（29.52 ms）**慢 15%**——图元更少、fill 按上一条应当相同，却更慢。这份开销出在 LoD 路径自身，本轮没有拆分（候选，均未验证：32B ExtSplat 节点的逐顶点读取与解码、LoD 深度 kernel 同样读 32B 节点）。

结论：判据 1 **记为未通过**。Overview 是**数量驱动**（预算档位偏松，调低默认预算能直接改善）；Mid/Close 是 **fill 受限 + LoD 路径逐图元开销**：LoD 切面保留了源重建的屏幕覆盖，在 fill 受限的视点，它按构造就追不上一份更稀疏的重建（`50w.ply`），在此之上还有约 15% 的逐图元开销待归因。调低预算从未让 GPU 时间变差（`200w.gsd`：Mid 30/50/80 万 = 21.61 / 24.07 / 27.91 ms，Close 29.64 / 29.76 / 33.86 ms；`100w.gsd` 与所有视点同样单调），只是在 fill 受限的视点收益很小（`200w.gsd` Close 30 万与 50 万只差 0.4%）。fill 的正解在子项目 B（E-B 早停 + quad 收紧到 √5σ）；合并节点 k 放宽落在 0.3 px² 低通之前这件事（D21）只影响 ≤ 1.2% 的亚像素节点、且改正它只会让这些节点更宽，**不是** fill 的杠杆。

**B 的第一个实验**（先把上面三条拆干净，再动 fill）：

1. Close 视点让 LoD 画出全部叶子（预算 ≥ 叶子数、像素阈值 0，切面即全部叶子，与 `200w.ply` 画的是同一组高斯），与 `200w.ply` 全量对比：差值就是 LoD 路径的逐图元开销，与 fill 无关。
2. 把 D 夹到 1（D > 1 节点按普通高斯画）重测：预期与现状几乎无差（D > 1 节点 ≤ 1.2%），确认合并节点放宽不是 fill 的来源。
3. 在 Close @30万 的切面里分别统计内部节点与叶子的屏幕面积（quad 面积之和、覆盖像素数），量化 fill 由谁贡献。

**判据 2（与源大小脱钩，同预算下 `100w.gsd` 与 `200w.gsd` 相差 ≤ ±5%）：部分通过，随预算增大而失败。**

| 预算 | Overview | Mid | Close |
|---|---|---|---|
| 30 万 | 6.6%（未通过） | 3.7%（通过） | 1.3%（通过） |
| 50 万 | 2.5%（通过） | 2.7%（通过） | 3.2%（通过） |
| 80 万 | 38.0%（未通过） | 16.4%（未通过） | 10.1%（未通过） |

只有 50 万预算档三个视点全部通过；30 万档 Overview 略超（6.6% vs 5%）；80 万档三个视点全部大幅超标。原因判断：`Model_100w.gsd` 全树只有 728449 个节点、556529 个叶子，80 万（800000）预算已经超出它的总节点数——三个视点在 80 万档的 `drawn_splats` 都停在 549658–556480（约等于它的叶子数上限），预算根本没被用满；而 `Model_200w.gsd`（1134668 节点）在同一档能继续扩到接近 800000。两者的差距因此不是"同预算下谁需要更多细节"，而是**预算超出了较小源模型的物理容量**，属于本轮判据设计没有预留的边界情况，不是脱钩假设本身失效。30 万 / 50 万档（都在两棵树的容量之内）绝大多数通过，能支持"脱钩"的结论；80 万档的失败提示：往 1.5M splat 外推时，判据 2 只在预算不超过最小候选源树容量时成立。

**判据 3（相对 `Model_200w.ply` 全量的收益倍数，仅记录不设门槛）**：跨度很大，从最快 Overview @30万 的 **2.38×** 到最慢 Close @80万 的 **0.87×**（此时 LoD 反而比全量 `.ply` 更慢）。收益随距离拉远、预算调低而变好，随贴近视点、预算调高而转为负收益——与判据 1 的等图元数切片一致：Overview 是数量驱动（远处本来就不需要那么多节点，收益随预算走）；Mid/Close 受 fill 限制，LoD 切面保留了源模型的屏幕覆盖，调低预算省下的只是逐图元的那部分（`200w.gsd` Close 30 万与 50 万只差 0.4%），调高预算则把 LoD 路径的逐图元开销叠上去——Close @80万 比 `200w.ply` 全量慢 15%（33.86 vs 29.52 ms，图元 80 万对 88 万）。预算越高，切面里被细化成子节点的合并节点越多，而不是越依赖合并节点。

**判据 4（每帧实画数 ≤ N）：通过。** 18 行 LoD 数据（2 资产 × 3 预算 × 3 视点）里 `drawn_splats` 无一超过对应 `lod_budget`（差额 0–250494，全部 ≤ 预算）。判据的后半条"主线程除绑定外不等待遍历 job"本轮**没有测量**：sweep 不记录主线程等待时间。它由构造保证——`GsplatLodSelector.TryComplete` 只在 job `IsCompleted` 之后才 `Complete`，`TrySchedule` 不阻塞，只有绑定时的 `RunNow` 同步（`GsplatLodSelectorTests` 覆盖）。trav/lat 两列与 GPU 时间分列记录，不占 GPU 时间。

**画面等价（Task 13 结论，本任务不重跑，原文引用）**：

| 对 | 文件 | 结论 |
|---|---|---|
| v0/v1/v2，预算 80 万 | `eq-ply-v0/v1/v2.png` / `eq-gsd-v0/v1/v2.png` | 形状、颜色、朝向、位置、尺度一致，无毛刺、无镜像；平均绝对差 0.41–0.46/255，差值 >32 的像素 0–1 个 |
| v0，预算 30 万 | `eq-ply-v0-b300k.png` / `eq-gsd-v0-b300k.png` | 与 80 万那一对**逐字节相同**（30 万在 Overview 距离没有真正卡住预算） |
| 约 6 m 远景，预算 30 万（Ruling 3 额外验证，未写入场景） | `eq-ply-far6m-b300k.png` / `eq-gsd-far6m-b300k.png` | `.gsd` 只画 66,427 个节点（绝大多数是合并节点）；目视无明显变薄/变透明，只是略软；**全图总亮度 −4.5%**（4.80→4.58），剪影覆盖面积基本不变（−0.5%）；判断为合并节点的积分不透明度存在约 4–5% 的真实损失，肉眼不可见，不属于失败清单（毛刺/偏色/镜像/位置尺度错）里的大错，作为已知偏差跟踪到子项目 B 的最终画质评审 |

注：上表"合并节点"指树的内部节点；按判据 1 下的 D 分布，内部节点绝大多数是 alpha ≤ 1 的普通高斯，D > 1 的只占 2.6–4.7%。那 4–5% 的亮度损失本轮没有归因到具体节点类型。

截图目录：`/private/tmp/claude-501/-Users-wwj-Desktop-unity-MR-Base/340e9260-a0c9-4c4b-960e-bb96788bc51b/scratchpad/`（文件名如上表）。

**局限**：Mac GPU 与 Adreno 同为 tile-based，但算力带宽差一个量级、非立体渲染、带 Editor 开销；Jobs Debugger 在本次测量中是开着的，`lod_traversal_ms`/`lod_latency_frames` 不能代表真机。本节数字只是**相对**证据——判据 1 在本轮预算/模型组合下未通过，判据 2 仅在预算不超过最小候选源树容量时成立；72fps 的绝对判断留给真机阶段（§10）。**调低默认预算只能改善判据 1 里数量驱动的部分（Overview 这类远视点）**；在 Mid/Close 这类 fill 受限的视点，调低预算不会变差，但收益很小——LoD 切面保留了源模型的屏幕覆盖，这部分要靠子项目 B（E-B 早停、quad 收到 √5σ），不是调参数能绕开的。另有约 15% 的 LoD 路径逐图元开销（Close @80万 对 `200w.ply` 全量）尚未归因，由"B 的第一个实验"第 1 条拆分。

## 9. 编号决策

### D1 — 逐高斯 LoD 树 + CPU 优先队列遍历，而非分块 HLOD 或 GPU 阈值切

**选了什么**：Spark 式树，每个高斯一个节点，Burst job 做预算约束的优先队列遍历。
**为什么**：帧耗时有上界的前提是"预算守得住"，只有优先队列切能在单高斯粒度保证它。
**否决**：*LCC 式分块 HLOD* —— 预算粒度粗到块，不同级相邻块有密度跳变，其主要优点（流式）已判定不需要。
*GPU 阈值切* —— 点数随视角浮动，不保证预算；要保证就得二分阈值 + 回读计数，把同步或多帧延迟引回来；且每帧扫全部节点。

### D2 — 自定义 `.gsd`，不直接用 Spark `.rad`

**为什么**：`.rad` 带分页、分块与多种可选编码，都不需要；其规范随 Spark 版本演进。自定义格式把契约压到最小且归我们管。
建树算法与数据编码仍复用 Spark。

### D3 — 中心 f32（32B ExtSplat），不用 16B + AABB 内 u16 量化

**为什么**：扫描件包围盒常被离群点撑到公里级，u16 量化精度随之崩塌，正确性依赖输入干净，是"碰巧能跑"。
f32 对任何输入成立；且 f16 alpha 可直接存 D。
**代价**：每节点多 16B（约 210 万节点 ≈ +34MB 显存）；每帧读取增量约 0.6GB/s，相对 fill 可忽略。

### D4 — 文件只存不可推导的数据

遍历用的中心与尺寸在加载时从 Nodes 段推导，不另存。两份就可能不一致，一份不会。

### D5 — v1 不压缩

Unity 资产路径出包时有 Unity 自己的压缩；运行时字节路径由调用方决定封装。需要时升版本号。

### D6 — 坐标系转换在 CLI，`.gsd` 固定为 RUF

**为什么**：坐标系是格式契约的一部分，只在一处转换；reader 只解析不计算。
转换放在**编码时**（建树之后）：翻转在连续意义上与合并可交换，但建树用的离散网格对齐会随之改变，
两棵树都合法却不相同；编码时转换，树与 Spark 对原始文件建出的一致，可直接对照。
**否决**：*文件头记源坐标系、reader 加载时转换* —— reader 承担计算，契约里多一个自由度。

### D7 — CLI 用 Rust，放 gsplat 仓库 `Tools~/`，`spark-lib` 锁 rev

**为什么**：写入端与 `GsdReader` 同仓同版本，契约只有一份；锁 rev 保证同一输入可复现（同 gsplat 走 submodule 的理由）。
**否决**：*Unity 编辑器工具（C# 重写建树）* —— 离不开 Unity，也进不了服务端管线；*放 MR_Base `Tools/`* —— 格式契约被拆到两个仓库。

### D8 — 默认 bhatt-lod，`--quick` 保留 tiny-lod

Spark 推荐离线用 bhatt；建树是离线的，慢可以接受。tiny 保留为画质对照。

### D9 — LoD 接入点是 `OrderBuffer`，数量由 CPU 给出

**为什么**：`OrderBuffer` 本来就是"本帧画哪些"的集合，排序与绘制无须知道 LoD 的存在。
数量是遍历的产物，天然在 CPU 上，不需要现路径那次 `GetData` 同步回读。

### D10 — 遍历异步、主线程不等；仅绑定时同步一次

**为什么**：遍历 O(N log N)，在 Quest CPU 上可能跨 1–3 帧；阻塞主线程就是掉帧。代价是转头后细节晚几帧补上，
Spark 在 Quest 上以同一代价运行。绑定时同步一次，避免首帧空白。

### D11 — 新集合上传当帧强制重排

**为什么**：`SetData` 后 `OrderBuffer` 是未排序的；若仍受排序间隔约束，会用乱序直接绘制。此规则显式化，不依赖间隔恰好为 1。

### D12 — 预算 N 放 `GsplatSettings`（全局）；v1 最多 1 个激活的 LoD renderer，超出报 Warning

**为什么**：预算是整帧共享的资源，不属于某个 renderer。多个 LoD renderer 各持一份预算会悄悄叠加超支。
**升级路径**：Spark 的多树联合遍历（单堆、多实例根节点）。
被拒不是永久的：名额空出后被拒的 renderer 自动绑定，见 D20。
**日志级别（2026-09-30 用户拍板，由 Error 降为 Warning）**：`MRSceneDirector` 每次在两个带 `.gsd` 的场景之间切换都会经过一次拒绝，
随后按 D20 自愈——这是本工程正常流程的必经态，报 Error 会让每次切场景都亮红。替代方案：(a) 保持 Error；(b) 包里识别"正在切场景"再抑制。
否决 (a)：正常流程报红，淹没真正的错误；否决 (b)：包不该知道宿主的切场景方式（`IteHost` 边界）。真正的误用（同一场景两个 `.gsd`）
表现为第二个模型始终不显示，加一条 Warning 已足够定位。

### D13 — `.gsd` 资产 v1 不支持 cutouts，挂上即响亮报错

**为什么**：支持它需要在 LoD 集合之后再做一次 GPU 过滤，过滤后的数量又得回读，正好把 D9 省掉的同步加回来。

### D14 — `OrderBuffer` 由 LoD 独占写入，`InitPayload` 对 LoD 不可达

**为什么**：现有 `DispatchSort` 在 `!Initialized` 时写恒等序列，会冲掉 LoD 索引。这是隐式耦合，用断言显式化。

### D15 — shader 加 `LOD` 变体；SH 解码抽到共享 include

**为什么**：ExtSplat 解码与 D 衰减是新的数据语义，应作为独立变体；SH 打包与 SPARK 逐位相同，
拆成一处共享，避免两份位操作各自漂移。

### D16 — 像素阈值用 offscreen 分辨率

**为什么**：splat 实际画进 offscreen RT。按相机分辨率算会选出 offscreen 画不出来的亚像素细节，白花预算。

### D17 — 先在 Editor 做相对性能验证，再上真机

**为什么**：用户决定先回答"是否真的提升帧率"，且不出包。复用 bench 的 sweep/CSV/`FrameTiming` 数据源。
走 Play 模式而不是 EditMode 单测：EditMode 测试循环不推进真实帧，`FrameTimingManager` 拿不到数据。
GPU 时间为 0 时中止，不换墙钟时间 —— 墙钟测的是另一件事。

### D18 — 只在 URP 上验证

绘制与排序路径三条管线共用，但本工程只用 URP；BiRP/HDRP 保证编译通过即可。

### D19 — 本分支给非 LoD `GsplatRenderer` 带来的行为变化

**选了什么**：

1. 清空 `GsplatAsset`（置 null，或资产被销毁）现在当帧就释放该绑定的 GPU 资源（与 LoD driver）。此前 `Update` 先把
   `m_prevAsset` 置 null 再比较，null 与 null 相等，于是不释放，资源一直占到 `OnDisable` 或下一次绑定。
2. 每次换绑（包括 `ReloadAsset`，即每次重新导入）`ReleaseGsplatAsset` 把 `RemainingCount` 清零、`Initialized` 置 false：
   换绑后的第一次排序一定先做一次恒等填充（`InitPayload`）。此前同数量、无 cutout 的换绑沿用上一个绑定排好的顺序缓冲。
3. 同时清掉 cutout 的"未变"缓存（`m_cutoutsData`、`m_prevSplatCount`），并重置刷新计划（`ForceRefresh`）：换绑后的下一帧
   一定重算 cutouts 并排序。

**替代方案**：换绑沿用上一个绑定的 `RemainingCount` / `Initialized` / cutout 缓存与刷新计划（原状）。

**为什么否决**：LoD 进来之后原状不再安全。被 D12 拒绝、或还没发布切面的 LoD 绑定会拿上一个绑定的数量去画一个大小不同的缓冲
（越界读）；LoD → 非 LoD 的同容量换绑会跳过恒等填充，直接画旧切面的节点下标。可一旦换绑清零了数量，"cutout 未变、数量未变"
的缓存就会跳过重算，带 cutout 的非 LoD renderer 每次重新导入后都画不出来，直到某个 cutout 移动；在 `CutoutsEveryNSorts`
下，刷新计划还会再拖 N 次排序——所以缓存与计划必须和数量一起清。代价只是换绑那一帧多一次恒等填充和一次 cutout 计算，
换绑本就罕见。`GsplatLodRendererTests` 的 `ReloadingAPlainAssetWithACutoutKeepsDrawingIt`（两种排序模式）与
`SwitchingFromLodToAPlainAssetIdentityFillsItsOrder` 钉住这两条。

### D20 — D12 的拒绝在名额空出后自动恢复

**选了什么**：被 D12 拒绝的 LoD 绑定记下"被拒"；此后每帧 `UpdateLod` 一旦看到 `GsplatLodDriver.LiveCount == 0` 就自动绑定，
不需要重新赋值资产。Warning（D12）按"被拒的绑定"计：每次被拒报一次；重试只在名额空出时发生，不会再次被拒，所以不会每帧报；恢复之后
若同一 renderer 的新绑定又被拒，再报一次。

**替代方案**：(a) 维持一次性拒绝：被拒的 renderer 在重新绑定之前永远不画；(b) 每帧重试、每帧报错。

**为什么否决**：(a) `MRSceneDirector` 切场景是"先加载新场景、设为激活、再卸载旧场景"（`CLAUDE.md`），新场景里的 `.gsd`
在旧场景的 LoD renderer 还活着时绑定、被拒，旧场景卸载后它也永远不画——在本工程唯一的场景切换方式下必然触发。
(b) 每帧报错刷屏，淹没其它错误；被拒是一个事件，不是每帧的新情况。

### D21 — 合并节点的 quad 放宽发生在 `InitCorner` 的 0.3 px² 低通之前、随 `_ScaleFactor` 缩放：已知偏离 Spark，推迟到 B

**选了什么**：保持现状。`GsplatLod.hlsl` 先把 k = `LodExtentScale(D)` 乘到 scale 上再算协方差，所以放宽落在 `InitCorner`
的 0.3 px² 低通（模糊）**之前**，整个 quad 之后还会被 `_ScaleFactor` 缩放。Spark 则在模糊**之后**、按模糊后的 σ 放宽
`maxStdDev + 0.7·(D−1)`。后果：亚像素的 D > 1 节点（≤ 1.2% 的树，§8.3）剖面比 Spark 窄 16–23%；`_ScaleFactor` 与 D 剖面
相互作用——`_ScaleFactor = 0.75` 时 D = 5 节点在截断边缘的 alpha ≈ 0.50（剖面被硬切）。

**替代方案**：(a) `InitCorner` 之后把 `corner.offset` 乘 k、uv 不变（Ruling 3 的原选项）；(b) 在 `InitCorner` 里加一个 extent
乘子，同时作用于视锥剔除与 clamp；并按 Spark 的方式处理 `_ScaleFactor` 与 D 剖面。

**为什么否决（推迟）**：(a) 让剖面与 Spark 一致，但不完整——`InitCorner` 的剔除与 clamp 仍按未放宽的 extent 算。正确做法是
(b)，而它改的正是子项目 B 为 √5σ 重写的那段 `InitCorner`；现在改这段共享代码，会让 §8.3 的基线作废。它也不是 fill 的杠杆：
改正只会让这 ≤ 1.2% 的节点变宽。连同 `_ScaleFactor`/D 剖面的相互作用一起交给 B。剖面本身（`LodMergedAlpha`）已由
`GsdDecodeTests.MergedNodeProfileMeetsThePlainGaussianAndFadesTowardItsEdge` 在 GPU 上钉住。

### D22 — LoD 选点只用 `Camera.main`（已知局限）

**选了什么**：`GsplatRenderer` 每帧用 `Camera.main` 的位姿做选点；找不到就报错一次并停止更新切面（已发布的切面继续画，
还没有切面就什么都不画），找回相机后恢复、再丢失时再报。

**替代方案**：(a) 为渲染它的每个相机各选一次（Game、Scene 视图、多相机）；(b) 在 renderer 上显式指定选点相机。

**为什么否决**：(a) 预算属于整帧（D12），每个相机各选一次就是多份预算、多份遍历；(b) 本工程运行时只有 MRCore rig 的一个主相机，
v1 不需要。已知后果：编辑模式下单独（additive）打开一个不含 MRCore rig 的内容场景时没有 MainCamera，LoD 资产什么都不画；
Scene 视图显示的是 Game 相机选出的切面（离 Game 相机远、离 Scene 相机近的地方会偏粗）。升级路径即 (b)，或编辑模式下改用
SceneView 相机。

### D23 — A 的验收以判据 2（与源大小脱钩）为准；判据 1 降为记录项

**选了什么**（2026-09-30 用户拍板）：A 在 Editor 下以判据 2、判据 4 为门槛，判定**通过**。判据 2 在 50 万预算档三个视点全部
≤ ±5%。判据 1 保留为记录项（1.43 / 1.58 / 1.35×，未通过），不作门槛。

**替代方案**：(a) 按判据 1 判 A 未通过；(b) A 暂不验收，等 B 完成后按判据 1 一起重测。

**为什么否决**：判据 1 的前提是"GPU 时间由图元数决定，1.2 倍留给合并节点的足迹"，§8.3 否定了它。Overview 失败是对照组
点数不对等：`Model_50w` 实为 338003 个 splat，按等图元数切片比是 0.92×，通过。Mid/Close 是 fill 受限：LoD 按构造保留源重建的
屏幕覆盖（Close 下 1.34 ≈ 两个源模型之比 1.335），拿另一份更稀疏的重建当门槛，等于要求 LoD 改变画面。A 的承诺（§1）本就是
"开销与源大小脱钩"，fill 属于 B。(b) 把两件独立的事绑在同一个门槛上，B 的收益会被判据 1 对照组的偏差污染。

**遗留**：判据 2 在 80 万档不成立（较小源树的容量不够，属边界情况）；约 15% 的 LoD 路径逐图元开销尚未归因，由 B 的
第一个实验第 1 条拆分；72fps 的判断仍属真机阶段（§10）。

## 10. 后续阶段（不在本轮，但不丢）

- **真机验收**：恢复 GsplatBench 的 Queue 构建入口（Quest/PICO，`f730e04` 收紧掉的那个，建议长期保留作性能回归量具）；
  关 ASW 后跑 sweep；PICO 的 GPU 时间来源待定（先试 `FrameTimingManager`，不行找 PXR 接口，都没有则降级看帧率与显示间隔）。
  每次出包等用户说"打包"。
- **子项目 B**：E-B 早停（framebuffer fetch）+ quad 收到 √5σ。
- **子项目 C**：间接绘制，去掉非 LoD 路径 `ExtractOrderSize` 的同步回读。

## 11. 风险与开放项

| 项 | 现状 |
|---|---|
| Quest CPU 上遍历耗时 | 未测。Editor 只给 Mac 数字；超过 3 帧再考虑 Spark 的增量遍历（`dynamic_traverse_lod_trees`） |
| 合并节点的 fill | 实测（§8.3）：LoD 切面保留源模型的屏幕覆盖与 overdraw，fill 受限视点上少画图元不减 fill；D > 1 节点只占全树 0.6–1.2%，不是来源。fill 的正解在 B；LoD 路径约 15% 的逐图元开销待拆分 |
| LoD 父子硬切换的观感 | 未评估。Spark 以 base 1.5–1.75 缓解；需用户在 Editor/头显里看 |
| `FrameTimingManager` 在 Mac/Metal 上是否有值 | 未验；为 0 即中止（D17） |
| bench 在无头显 Editor 下能否跑 | `BenchRig` 依赖 XR 手柄与 XR display subsystem，非 XR 路径需新补，属本轮工作量 |
| 内存 | 每节点三份常驻：GPU 32B 节点 + 40B SH3 = 72B；`GsplatLodAsset` 上传后仍常驻的托管副本（`Nodes`/`PackedSH1–3`/`ChildStart`/`ChildCount`）78B；CPU 遍历表 22B。210 万节点 ≈ GPU 150MB + 托管 164MB + 遍历表 46MB ≈ **360MB**，约为只算 GPU 与遍历表（≈196MB）的 2 倍；绑定时还有一份 32B/节点的临时 `TempJob` 拷贝（≈67MB）。实测的 `Model_200w`（113 万节点）≈ 82 + 88 + 25 ≈ 195MB。Quest 3 8GB 是 CPU/GPU 共享内存，三份都算在里面；可容纳，但需在真机阶段确认 |
