# 高斯 LoD：`.gsd` 格式、`gsd-build` 与运行时选点（gsplat-lod）—— 设计

> 状态：待评审
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
  features = ["ply", "spz", "tiny_lod", "bhatt_lod"] }`。
- 参数：`--source`、`--max-sh 0..3`（默认保留源阶数）、`--quick`。输出节点数、叶子数、层数、耗时。
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
  2. **与源大小脱钩**：`Model_100w.gsd` 与 `Model_200w.gsd` 在同一 N 下 GPU 时间相差 ≤ ±5%。
  3. 同时记录 `Model_200w.ply` 全量作对照，量化 LoD 相对全量的收益倍数。
  4. 每帧实画数 ≤ N；主线程除绑定外不等待遍历 job。
- **局限（写入结果）**：Mac GPU 与 Adreno 同为 tile-based，但算力与带宽差一个量级、非立体渲染、带 Editor 开销。
  Editor 数字只证明**相对**收益与脱钩；72fps 的绝对判断属真机阶段（§10）。

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

### D12 — 预算 N 放 `GsplatSettings`（全局）；v1 最多 1 个激活的 LoD renderer，超出响亮报错

**为什么**：预算是整帧共享的资源，不属于某个 renderer。多个 LoD renderer 各持一份预算会悄悄叠加超支。
**升级路径**：Spark 的多树联合遍历（单堆、多实例根节点）。

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
| 合并节点的 fill | 粗节点足迹更大，贴近视点时 fill 未必下降。§8.2 判据 1 的 1.2 系数即为此留；fill 的正解在 B |
| LoD 父子硬切换的观感 | 未评估。Spark 以 base 1.5–1.75 缓解；需用户在 Editor/头显里看 |
| `FrameTimingManager` 在 Mac/Metal 上是否有值 | 未验；为 0 即中止（D17） |
| bench 在无头显 Editor 下能否跑 | `BenchRig` 依赖 XR 手柄与 XR display subsystem，非 XR 路径需新补，属本轮工作量 |
| 内存 | 210 万节点：GPU 32B + SH3 40B ≈ 150MB，CPU 遍历表 ≈ 46MB。Quest 3 8GB 共享内存可容纳，但需在真机阶段确认 |
