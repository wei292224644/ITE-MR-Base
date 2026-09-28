# 高斯 LoD（gsplat-lod）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 150 万高斯的源模型按视角每帧只画 ≤N 个节点，并在 Editor（Play 模式）里用 bench 量出这件事对 GPU 时间的真实影响。

**Architecture:** 离线 Rust CLI `gsd-build` 复用 Spark 的 `spark-lib` 建 LoD 树，写出自定义 `.gsd`（32B ExtSplat 节点 + 与现有 SPARK 同构的 SH + 子节点表）。Unity 侧 `GsdReader` 解析并校验，`GsplatLodAsset` 上传全部节点；每帧由 Burst job 在 worker 上做预算约束的优先队列遍历，结果直接写进渲染器的 `OrderBuffer`，现有深度 compute、基数排序、绘制原样复用，shader 只加一个 `LOD` 变体。

**Tech Stack:** Rust 1.94（`spark-lib` @ Spark `967263804e637776e94395e61ab2f6cb6a04663c`，`glam` 0.30，`half` 2.6），Unity 6000.4.4f1 + URP 17.4，Burst 1.8.29，Unity.Mathematics，Unity Test Framework 1.6（EditMode），HLSL（Metal / Vulkan）。

**Spec:** `docs/superpowers/specs/2026-09-28-gsplat-lod-design.md`（D1–D18）。执行前两份一起读。

## Global Constraints

- 目标设备 XR2 Gen 2（Quest 3/3S、PICO 4 Ultra），72Hz = 13.9ms/帧；**本轮只在 Editor 验收，不出包、不装机**（用户记忆：没说"打包"就不构建）。
- `.gsd`：magic `"GSD\0"`，version `1`，小端，头 48B，各段 16B 对齐，不压缩，坐标系 Unity RUF。
- 节点 32B（Spark ExtSplat）：`uint4 #0 = center.x f32 · center.y f32 · center.z f32 · packHalf2x16(alpha, 0)`；`uint4 #1 = packHalf2x16(r, g) · packHalf2x16(b, ln sx) · packHalf2x16(ln sy, ln sz) · quat(oct 10+10, angle 12)`。alpha ≤ 1 为不透明度，> 1 为合并节点 D（≤ 5）。
- SH 与现有 `GsplatAssetSpark` 打包逐位相同：SH1 sint7（2 words）、SH2 sint8（4 words）、SH3 sint6（4 words）。
- 不变式：①根在 0；②`childCount>0` ⇒ `childStart > 自身` 且 `childStart+childCount ≤ nodeCount`；③子区间不重叠，除根外恰有一个父；④`childCount==0` 的个数 = `leafCount`；⑤段长与 `nodeCount`/`shDegree` 推出的完全一致，不截断、无多余。
- `spark-lib` 依赖：git rev `967263804e637776e94395e61ab2f6cb6a04663c`，`default-features = false`，features `["gsplat", "ply", "spz", "tiny_lod", "bhatt_lod"]`。
- 建树默认 bhatt（base 1.75），`--quick` → tiny（base 1.5），`--source` 默认 RUB，`--max-sh 0..3`。
- 预算 `GsplatSettings.LodSplatBudget` 默认 500000；foveate 默认 `coneFov0 = 90°`、`coneFov = 120°`（全角）、`coneFoveate = 0.4`、`behindFoveate = 0.2`。
- 最多 1 个激活的 LoD renderer（D12），`.gsd` 不支持 cutouts（D13），两者都**响亮报错**。只在 URP 上验证（D18）。
- Unity 操作一律走 `unity` CLI 连用户**已打开**的 Editor（`.claude/CLAUDE.md`），不用 MCP，不开第二个 Unity 进程。
- 提交：gsplat 子模块在新分支 `feature/gsd-lod`（从 `feature/quest-perf` 切出）上提交；MR_Base 在 `master` 上提交，**只 `git add` 明确路径**——工作区里有用户未提交的 PICO 改动，绝不 `git add -A` / `git commit -a`。**不 push**。
- 注释语言：gsplat 包内英文（随包现有风格）；`Assets/Scripts/GsplatBench` 内中文（随 bench 现有风格）。新 C# 文件头照包内惯例写 `// Copyright (c) 2026 wwj` + `// SPDX-License-Identifier: MIT`。

### 共用操作流程（各任务引用）

`R=/Users/wwj/Desktop/unity/MR_Base`

**§U 刷新并编译**（新增/修改 Unity 侧文件后）：

```bash
unity command eval --project-path $R 'UnityEditor.AssetDatabase.Refresh(); return "refreshed";'
unity command editor_status --project-path $R   # 反复执行，直到 "compiling":false 且 "domainReloadInProgress":false
unity command console --project-path $R --level error --tail 30   # 必须没有编译错误
```

CLI 在域重载中断线时：先等 20 秒再 `editor_status`；仍连不上就查真实 Editor 日志（先 `lsof -c Unity | grep Editor.*log` 确认是 `Editor.log` 还是 `Editor-prev.log`），找 `Failed to start Pipeline Server: No available ports`。**确认是绑端口失败才**请用户切到 Unity 按 `Cmd+R`。

**§T 跑测试**（先确认没有未保存的场景，否则 Test Runner 的保存提示会卡住 CLI）：

```bash
unity command eval --project-path $R 'return UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty.ToString();'
# 输出 True 就停下，请用户保存或放弃改动，不要替用户决定
unity command run_tests --project-path $R --mode editor --filter Gsplat.Editor.Tests --filter_type assembly --timeout 600
```

单个测试类：把 `--filter` 换成类名、`--filter_type testName`。

## Review Focus

以下输入是 spec 隐含、但单看功能描述容易漏测的，最可能咬人；每条都已在所属任务里加了测试：

1. **运行时改预算**（bench 旋钮）：`OrderBuffer` 与排序 buffer 必须按新预算重建，不能沿用旧容量或越界写。→ Task 11 `ChangingTheBudgetRebindsWithTheNewCapacity`。
2. **模型带非单位变换**（缩放/旋转的 renderer）：遍历在模型空间里做，均匀缩放 s 倍同时相机远 s 倍必须选出同一刀。→ Task 11 `UniformScaleIsInvisibleToTheSelection`。
3. **相机正好落在节点中心**：距离为 0 时不能出 NaN/Inf，也不能越预算。→ Task 9 `CameraOnANodeCentreStaysFinite`。
4. **恶意/损坏文件头声明超大 nodeCount**：必须在分配内存前按"截断"拒绝。→ Task 6 `RejectsHugeNodeCountBeforeAllocating`。
5. **遍历 job 在飞时 renderer 被禁用/销毁**（additive 卸场景）：先 `Complete` 再释放，不泄漏 NativeArray、不触发 job 安全报错。→ Task 10 `DisposeWithATraversalInFlightIsSafe`、Task 11 `DestroyingTheRendererReleasesItsDriver`。

---

## 文件结构

**gsplat 子模块（`Packages/wu.yize.gsplat/`，分支 `feature/gsd-lod`）**

| 文件 | 职责 |
|---|---|
| `Tools~/gsd-build/Cargo.toml`、`.gitignore`、`Cargo.lock` | CLI crate；`~` 让 Unity 不导入 |
| `Tools~/gsd-build/src/lib.rs` | 模块出口 |
| `Tools~/gsd-build/src/encode.rs` | f16 对、四元数 1010R12、ExtSplat、SH 位编解码 |
| `Tools~/gsd-build/src/frame.rs` | 源坐标系 → RUF（中心、四元数、SH 符号） |
| `Tools~/gsd-build/src/invariants.rs` | 树不变式 ①–④ |
| `Tools~/gsd-build/src/format.rs` | 容器布局、写、读（含 ⑤ 与调用 ①–④） |
| `Tools~/gsd-build/src/pipeline.rs` | 解码 → 过滤 → 建树 → 重排 → 编码 |
| `Tools~/gsd-build/src/main.rs` | 参数解析与输出 |
| `Tools~/gsd-build/tests/common/mod.rs` | 合成 PLY |
| `Tools~/gsd-build/tests/pipeline.rs` | 端到端 |
| `Tools~/gsd-build/tests/fixture.rs` | 生成/守护 Unity 金样 |
| `Tests/Editor/Fixtures/tiny.gsd`、`tiny.expected.json` | 跨语言金样 |
| `Tests/Editor/Gsplat.Editor.Tests.asmdef` | 包测试程序集 |
| `Tests/Editor/GsdFixture.cs` | 金样路径与 JSON 模型 |
| `Tests/Editor/GsdReaderTests.cs` | 解析与不变式 |
| `Tests/Editor/GsdDecodeProbe.compute`、`GsdDecodeTests.cs` | GPU 解码 vs Rust 解码；shader 变体可编 |
| `Tests/Editor/GsdImporterTests.cs` | 导入器 |
| `Tests/Editor/LodTestTrees.cs`、`GsplatLodTraversalTests.cs`、`GsplatLodSelectorTests.cs` | 遍历与调度 |
| `Tests/Editor/GsplatLodRendererTests.cs` | renderer 集成 |
| `Runtime/Lod/GsdReader.cs` | 唯一解析器 + `GsdData` + `GsdFormatException` |
| `Runtime/Lod/GsplatLodAsset.cs` | LoD 资产（上传、深度 kernel 派发） |
| `Runtime/Lod/GsplatLodTree.cs` | 绑定时推导的 CPU 遍历表 |
| `Runtime/Lod/GsplatLodTraversal.cs` | `GsplatLodView`、`GsplatLodScratch`、Burst 遍历 job |
| `Runtime/Lod/GsplatLodSelector.cs` | 一次一个在飞遍历的调度 |
| `Runtime/Lod/GsplatLodDriver.cs` | 视图构造 + 上传进 `OrderBuffer` + 单实例守卫 |
| `Runtime/Shaders/GsplatSparkSH.hlsl` | 从 `GsplatSpark.hlsl` 拆出的 SH 解码（SPARK 与 LOD 共用） |
| `Runtime/Shaders/GsplatLodDecode.hlsl` | 无依赖的 ExtSplat 解码 |
| `Runtime/Shaders/GsplatLod.hlsl` | LOD 变体的 `InitSplatData` |
| `Runtime/Shaders/CalcDepthLod.compute` | 选中集合的深度 |
| `Runtime/Materials/GsplatLod.mat`、`GsplatLod.asset` | LOD 材质与 `GsplatMaterial` |
| `Editor/GsdImporter.cs` | `.gsd` ScriptedImporter |
| 修改：`Runtime/GsplatAsset.cs`、`GsplatAssetSpark.cs`、`GsplatAssetUncompressed.cs`、`GsplatResource.cs`、`GsplatRenderer.cs`、`GsplatRendererImpl.cs`、`GsplatSorter.cs`、`GsplatSettings.cs`、`SRP/GsplatURPFeature.cs`、`Shaders/Gsplat.shader`、`Shaders/GsplatSpark.hlsl`、`Editor/GsplatImporter.cs`、`Runtime/Gsplat.asmdef`、`package.json`、`Third Party Notices.md` | |

**MR_Base（`master`）**

| 文件 | 职责 |
|---|---|
| `Packages/manifest.json` | `testables` 加 `wu.yize.gsplat` |
| `Assets/Gsplat/Settings/Resources/GsplatSettings.asset` | 设置升级（LoD 字段 + Lod 材质槽） |
| `.gitignore` | 忽略 `Assets/Assets/3dgs/*.gsd` |
| `Assets/Assets/3dgs/Model_{50,100,200}w.gsd.meta` | 稳定 GUID |
| `Assets/Scripts/GsplatBench/BenchKnobs.cs`、`BenchRig.cs`、`BenchSweep.cs` | LoD 预算旋钮、视点、LoD 对比 sweep |
| `Assets/Scenes/GsplatBench.unity` | 视点对象、.gsd 资产登记 |
| `docs/superpowers/specs/2026-09-28-gsplat-lod-design.md`、`docs/superpowers/specs/data/2026-09-28-gsplat-lod-editor.csv` | 结果 |
| `Packages/wu.yize.gsplat`（子模块指针） | 里程碑处更新 |

---

## Task 1: Rust crate 骨架与位编解码（`encode.rs`）

**Files:**
- Create: `Packages/wu.yize.gsplat/Tools~/gsd-build/Cargo.toml`
- Create: `Packages/wu.yize.gsplat/Tools~/gsd-build/.gitignore`
- Create: `Packages/wu.yize.gsplat/Tools~/gsd-build/src/lib.rs`
- Create: `Packages/wu.yize.gsplat/Tools~/gsd-build/src/encode.rs`

**Interfaces:**
- Produces: `gsd_build::encode::{pack_half2x16(lo: f32, hi: f32) -> u32, unpack_half2x16(u32) -> (f32, f32), encode_quat_oct1010r12(Quat) -> u32, decode_quat_oct1010r12(u32) -> Quat, ExtSplat { center: Vec3, alpha: f32, rgb: Vec3, ln_scales: Vec3, quat: Quat }, pack_ext_splat(&ExtSplat) -> [u32; 8], unpack_ext_splat(&[u32; 8]) -> ExtSplat, pack_sh1(&[f32; 9]) -> [u32; 2], pack_sh2(&[f32; 15]) -> [u32; 4], pack_sh3(&[f32; 21]) -> [u32; 4], unpack_sh1(&[u32; 2]) -> [f32; 9], unpack_sh2(&[u32; 4]) -> [f32; 15], unpack_sh3(&[u32; 4]) -> [f32; 21]}`。四元数一律是实四元数 `(x, y, z, w)`，w = cos(θ/2)。SH 数组按"系数主序、通道次序"：`values[k*3 + c]`。

- [ ] **Step 1: 在子模块切分支**

```bash
git -C /Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat status --short   # 必须为空
git -C /Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat switch -c feature/gsd-lod
```

- [ ] **Step 2: 写 crate 清单与忽略文件**

`Tools~/gsd-build/Cargo.toml`：

```toml
[package]
name = "gsd-build"
version = "0.1.0"
edition = "2021"
license = "MIT"
description = "Builds .gsd LoD trees for the Gsplat Unity renderer"

[dependencies]
spark-lib = { git = "https://github.com/sparkjsdev/spark", rev = "967263804e637776e94395e61ab2f6cb6a04663c", default-features = false, features = ["gsplat", "ply", "spz", "tiny_lod", "bhatt_lod"] }
glam = "0.30.8"
half = "2.6.0"
anyhow = "1.0.98"
serde_json = "1.0.145"

[profile.release]
opt-level = 3
```

`Tools~/gsd-build/.gitignore`：

```
/target
```

`Tools~/gsd-build/src/lib.rs`（本任务只有 encode，后续任务逐个追加）：

```rust
pub mod encode;
```

- [ ] **Step 3: 写 `encode.rs`，测试先行**

`Tools~/gsd-build/src/encode.rs`：

```rust
//! Bit-level codecs for the `.gsd` node payload.
//!
//! Every encoder here has a decoder twin, and `Runtime/Shaders/GsplatLodDecode.hlsl` mirrors the
//! decoders on the GPU. `tests/fixture.rs` writes the decoded values next to an encoded fixture so
//! the Unity side checks its HLSL against them: the two languages share one contract.
//!
//! The quaternion codec and the 32-byte ExtSplat layout are ported from Spark
//! (`src/shaders/splatDefines.glsl`, World Labs Technologies, MIT).

use glam::{Quat, Vec3};
use half::f16;
use std::f32::consts::PI;

pub fn pack_half2x16(lo: f32, hi: f32) -> u32 {
    u32::from(f16::from_f32(lo).to_bits()) | (u32::from(f16::from_f32(hi).to_bits()) << 16)
}

pub fn unpack_half2x16(packed: u32) -> (f32, f32) {
    (
        f16::from_bits((packed & 0xffff) as u16).to_f32(),
        f16::from_bits((packed >> 16) as u16).to_f32(),
    )
}

/// Spark `encodeQuatOctXy1010R12`. `q` is a real quaternion (x, y, z, w), w = cos(θ/2).
pub fn encode_quat_oct1010r12(q: Quat) -> u32 {
    let mut q = q.normalize();
    if q.w < 0.0 {
        q = -q;
    }
    let half_theta = q.w.clamp(-1.0, 1.0).acos();
    let theta = 2.0 * half_theta;
    let s = half_theta.sin();
    let axis = if s.abs() < 1e-6 { Vec3::X } else { Vec3::new(q.x, q.y, q.z) / s };
    let sum = axis.x.abs() + axis.y.abs() + axis.z.abs();
    let mut px = axis.x / sum;
    let mut py = axis.y / sum;
    if axis.z < 0.0 {
        let old_px = px;
        px = (1.0 - py.abs()) * if px >= 0.0 { 1.0 } else { -1.0 };
        py = (1.0 - old_px.abs()) * if py >= 0.0 { 1.0 } else { -1.0 };
    }
    let quant_u = ((px * 0.5 + 0.5) * 1023.0).round().clamp(0.0, 1023.0) as u32;
    let quant_v = ((py * 0.5 + 0.5) * 1023.0).round().clamp(0.0, 1023.0) as u32;
    let angle = ((theta / PI) * 4095.0).round().clamp(0.0, 4095.0) as u32;
    (angle << 20) | (quant_v << 10) | quant_u
}

/// Spark `decodeQuatOctXy1010R12`. Returns a real quaternion (x, y, z, w).
pub fn decode_quat_oct1010r12(encoded: u32) -> Quat {
    let fx = (encoded & 0x3ff) as f32 / 1023.0 * 2.0 - 1.0;
    let fy = ((encoded >> 10) & 0x3ff) as f32 / 1023.0 * 2.0 - 1.0;
    let mut axis = Vec3::new(fx, fy, 1.0 - fx.abs() - fy.abs());
    let t = (-axis.z).max(0.0);
    axis.x += if axis.x >= 0.0 { -t } else { t };
    axis.y += if axis.y >= 0.0 { -t } else { t };
    let axis = axis.normalize();
    let theta = (encoded >> 20) as f32 / 4095.0 * PI;
    let (s, c) = (theta * 0.5).sin_cos();
    Quat::from_xyzw(axis.x * s, axis.y * s, axis.z * s, c)
}

/// One LoD tree node as laid out in the file: eight little-endian u32 words.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct ExtSplat {
    pub center: Vec3,
    /// Linear opacity in 0..=1 for plain splats; the merged-node D in (1, 5] for LoD interiors.
    pub alpha: f32,
    pub rgb: Vec3,
    pub ln_scales: Vec3,
    /// Real quaternion (x, y, z, w).
    pub quat: Quat,
}

pub fn pack_ext_splat(s: &ExtSplat) -> [u32; 8] {
    [
        s.center.x.to_bits(),
        s.center.y.to_bits(),
        s.center.z.to_bits(),
        pack_half2x16(s.alpha, 0.0),
        pack_half2x16(s.rgb.x, s.rgb.y),
        pack_half2x16(s.rgb.z, s.ln_scales.x),
        pack_half2x16(s.ln_scales.y, s.ln_scales.z),
        encode_quat_oct1010r12(s.quat),
    ]
}

pub fn unpack_ext_splat(w: &[u32; 8]) -> ExtSplat {
    let (alpha, _) = unpack_half2x16(w[3]);
    let (r, g) = unpack_half2x16(w[4]);
    let (b, ln_x) = unpack_half2x16(w[5]);
    let (ln_y, ln_z) = unpack_half2x16(w[6]);
    ExtSplat {
        center: Vec3::new(f32::from_bits(w[0]), f32::from_bits(w[1]), f32::from_bits(w[2])),
        alpha,
        rgb: Vec3::new(r, g, b),
        ln_scales: Vec3::new(ln_x, ln_y, ln_z),
        quat: decode_quat_oct1010r12(w[7]),
    }
}

/// Packs `values` as consecutive `bits`-wide two's-complement fields, LSB first, a field allowed
/// to straddle a word boundary: the layout `GsplatAssetSpark.PackSH1/2/3` writes and
/// `GsplatSparkSH.hlsl` reads.
fn pack_signed_bits(values: &[f32], scale: f32, bits: u32, out: &mut [u32]) {
    out.fill(0);
    let mask = (1u32 << bits) - 1;
    for (i, &v) in values.iter().enumerate() {
        let raw = ((v * scale).clamp(-scale, scale).round() as i32 as u32) & mask;
        let bit = i as u32 * bits;
        let (word, offset) = ((bit / 32) as usize, bit % 32);
        out[word] |= raw << offset;
        if offset + bits > 32 {
            out[word + 1] |= raw >> (32 - offset);
        }
    }
}

fn unpack_signed_bits(words: &[u32], count: usize, scale: f32, bits: u32) -> Vec<f32> {
    let mask = (1u64 << bits) - 1;
    (0..count)
        .map(|i| {
            let bit = i as u32 * bits;
            let (word, offset) = ((bit / 32) as usize, bit % 32);
            let mut raw = u64::from(words[word]) >> offset;
            if offset + bits > 32 {
                raw |= u64::from(words[word + 1]) << (32 - offset);
            }
            let raw = (raw & mask) as u32;
            let signed = ((raw << (32 - bits)) as i32) >> (32 - bits);
            signed as f32 / scale
        })
        .collect()
}

pub fn pack_sh1(sh: &[f32; 9]) -> [u32; 2] {
    let mut out = [0; 2];
    pack_signed_bits(sh, 63.0, 7, &mut out);
    out
}

pub fn pack_sh2(sh: &[f32; 15]) -> [u32; 4] {
    let mut out = [0; 4];
    pack_signed_bits(sh, 127.0, 8, &mut out);
    out
}

pub fn pack_sh3(sh: &[f32; 21]) -> [u32; 4] {
    let mut out = [0; 4];
    pack_signed_bits(sh, 31.0, 6, &mut out);
    out
}

pub fn unpack_sh1(w: &[u32; 2]) -> [f32; 9] {
    unpack_signed_bits(w, 9, 63.0, 7).try_into().unwrap()
}

pub fn unpack_sh2(w: &[u32; 4]) -> [f32; 15] {
    unpack_signed_bits(w, 15, 127.0, 8).try_into().unwrap()
}

pub fn unpack_sh3(w: &[u32; 4]) -> [f32; 21] {
    unpack_signed_bits(w, 21, 31.0, 6).try_into().unwrap()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn same_rotation(a: Quat, b: Quat) -> bool {
        a.dot(b).abs() > 1.0 - 1e-4
    }

    #[test]
    fn half_pair_round_trips() {
        assert_eq!(unpack_half2x16(pack_half2x16(1.5, -0.25)), (1.5, -0.25));
    }

    #[test]
    fn identity_quat_round_trips() {
        let q = decode_quat_oct1010r12(encode_quat_oct1010r12(Quat::IDENTITY));
        assert!(same_rotation(q, Quat::IDENTITY), "{q:?}");
    }

    #[test]
    fn sample_quats_round_trip_within_quantisation() {
        let samples = [
            Quat::from_rotation_y(1.2),
            Quat::from_rotation_x(-2.9),
            Quat::from_axis_angle(Vec3::new(1.0, -2.0, 0.5).normalize(), 0.7),
            Quat::from_axis_angle(Vec3::new(-0.3, 0.2, -0.9).normalize(), 3.1),
            Quat::from_xyzw(0.1, 0.2, 0.3, -0.9).normalize(),
        ];
        for q in samples {
            let d = decode_quat_oct1010r12(encode_quat_oct1010r12(q));
            assert!(same_rotation(d, q), "{q:?} decoded as {d:?}");
        }
    }

    #[test]
    fn ext_splat_round_trips() {
        let s = ExtSplat {
            center: Vec3::new(1234.5678, -0.001, 3.0e4),
            alpha: 3.5,
            rgb: Vec3::new(0.25, 0.5, -0.75),
            ln_scales: Vec3::new(-3.0, -2.5, -1.25),
            quat: Quat::IDENTITY,
        };
        let d = unpack_ext_splat(&pack_ext_splat(&s));
        assert_eq!(d.center, s.center, "centers are carried bit-for-bit as f32");
        assert_eq!((d.alpha, d.rgb, d.ln_scales), (s.alpha, s.rgb, s.ln_scales));
        assert!(same_rotation(d.quat, s.quat));
    }

    #[test]
    fn sh_bands_round_trip_on_their_grid() {
        let sh1: [f32; 9] = std::array::from_fn(|i| (i as f32 - 4.0) / 63.0);
        let sh2: [f32; 15] = std::array::from_fn(|i| (i as f32 * 9.0 - 60.0) / 127.0);
        let sh3: [f32; 21] = std::array::from_fn(|i| (i as f32 * 3.0 - 30.0) / 31.0);
        assert_eq!(unpack_sh1(&pack_sh1(&sh1)), sh1);
        assert_eq!(unpack_sh2(&pack_sh2(&sh2)), sh2);
        assert_eq!(unpack_sh3(&pack_sh3(&sh3)), sh3);
    }

    #[test]
    fn sh_values_beyond_unit_range_clamp() {
        assert_eq!(unpack_sh1(&pack_sh1(&[2.0; 9])), [1.0; 9]);
        assert_eq!(unpack_sh3(&pack_sh3(&[-7.0; 21])), [-1.0; 21]);
    }
}
```

- [ ] **Step 4: 跑测试，确认通过**

```bash
cd "/Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat/Tools~/gsd-build" && cargo test --lib encode
```

Expected：6 个测试 PASS（首次会下载并编译 spark-lib 依赖）。若 `sample_quats_round_trip_within_quantisation` 失败，说明移植与 GLSL 不一致——对照 spec §2.2 引用的 `splatDefines.glsl` 逐行核对，不要放宽容差。

- [ ] **Step 5: 提交（子模块）**

```bash
cd /Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat
git add "Tools~/gsd-build/Cargo.toml" "Tools~/gsd-build/Cargo.lock" "Tools~/gsd-build/.gitignore" "Tools~/gsd-build/src/lib.rs" "Tools~/gsd-build/src/encode.rs"
git commit -m "$(cat <<'EOF'
feat(gsd-build): bit-level codecs for the .gsd node payload

Quaternion 1010R12 and the 32-byte ExtSplat layout ported from Spark;
SH packing matches GsplatAssetSpark (sint7/sint8/sint6).

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: 源坐标系转换（`frame.rs`）

**Files:**
- Create: `Packages/wu.yize.gsplat/Tools~/gsd-build/src/frame.rs`
- Modify: `Packages/wu.yize.gsplat/Tools~/gsd-build/src/lib.rs`

**Interfaces:**
- Consumes: 无。
- Produces: `gsd_build::frame::{SourceFrame (Ldb|Rdb|Lub|Rub|Ldf|Rdf|Luf|Ruf, FromStr 不区分大小写), axis_signs(SourceFrame) -> Vec3, convert_center(SourceFrame, Vec3) -> Vec3, convert_quat(SourceFrame, Quat) -> Quat, sh_sign(SourceFrame, band: usize, k: usize) -> f32, convert_sh(SourceFrame, band: usize, values: &mut [f32])}`。规则与 C# `GsplatUtils.AxisSigns`/`ShSign` 及 `GsplatAssetSpark.LoadFromPlyStream` 的四元数符号规则相同。

- [ ] **Step 1: 写 `frame.rs`（含测试）**

```rust
//! Source-frame conversion into Unity's RUF frame: a port of `GsplatUtils.AxisSigns`/`ShSign` and
//! of the quaternion sign rule in `GsplatAssetSpark.LoadFromPlyStream`, so a `.gsd` lands in the
//! same frame the `.ply` importer produces.

use glam::{Quat, Vec3};
use std::str::FromStr;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SourceFrame {
    Ldb,
    Rdb,
    Lub,
    Rub,
    Ldf,
    Rdf,
    Luf,
    Ruf,
}

impl FromStr for SourceFrame {
    type Err = String;

    fn from_str(s: &str) -> Result<Self, Self::Err> {
        use SourceFrame::*;
        Ok(match s.to_ascii_uppercase().as_str() {
            "LDB" => Ldb,
            "RDB" => Rdb,
            "LUB" => Lub,
            "RUB" => Rub,
            "LDF" => Ldf,
            "RDF" => Rdf,
            "LUF" => Luf,
            "RUF" => Ruf,
            _ => return Err(format!("unknown source frame '{s}' (expected LDB RDB LUB RUB LDF RDF LUF RUF)")),
        })
    }
}

/// −1 on each axis the source frame points opposite to Unity (Left, Down, Back).
pub fn axis_signs(frame: SourceFrame) -> Vec3 {
    use SourceFrame::*;
    let left = matches!(frame, Ldb | Lub | Ldf | Luf);
    let down = matches!(frame, Ldb | Rdb | Ldf | Rdf);
    let back = matches!(frame, Ldb | Rdb | Lub | Rub);
    let sign = |flip: bool| if flip { -1.0 } else { 1.0 };
    Vec3::new(sign(left), sign(down), sign(back))
}

pub fn convert_center(frame: SourceFrame, center: Vec3) -> Vec3 {
    center * axis_signs(frame)
}

/// Each imaginary component takes the product of the other two axis signs, as the C# importer
/// does (`rotXSign = posYSign * posZSign`, …); the real part is untouched.
pub fn convert_quat(frame: SourceFrame, q: Quat) -> Quat {
    let s = axis_signs(frame);
    Quat::from_xyzw(q.x * s.y * s.z, q.y * s.x * s.z, q.z * s.x * s.y, q.w)
}

/// Sign for SH band `band`, band-local coefficient `k` (0..2·band+1).
pub fn sh_sign(frame: SourceFrame, band: usize, k: usize) -> f32 {
    let s = axis_signs(frame);
    let mut sign = 1.0;
    if s.x < 0.0 {
        sign *= sh_sign_x(band, k);
    }
    if s.y < 0.0 {
        sign *= if k < band { -1.0 } else { 1.0 };
    }
    if s.z < 0.0 {
        sign *= if k & 1 == 1 { -1.0 } else { 1.0 };
    }
    sign
}

// Real SH under an X flip (φ → π−φ): m > 0 → (−1)^(k−l), m = 0 → 1, m < 0 → (−1)^(l−k+1).
fn sh_sign_x(l: usize, k: usize) -> f32 {
    if k == l {
        return 1.0;
    }
    let power = if k > l { k - l } else { l - k + 1 };
    if power & 1 == 1 { -1.0 } else { 1.0 }
}

/// Applies `sh_sign` to one band stored coefficient-major, channel-minor (`values[k*3 + c]`).
pub fn convert_sh(frame: SourceFrame, band: usize, values: &mut [f32]) {
    for k in 0..(2 * band + 1) {
        let sign = sh_sign(frame, band, k);
        for c in 0..3 {
            values[k * 3 + c] *= sign;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_case_insensitively_and_rejects_unknown_frames() {
        assert_eq!("rub".parse::<SourceFrame>(), Ok(SourceFrame::Rub));
        assert!("xyz".parse::<SourceFrame>().is_err());
    }

    #[test]
    fn rub_flips_z_only() {
        assert_eq!(convert_center(SourceFrame::Rub, Vec3::new(1.0, 2.0, 3.0)), Vec3::new(1.0, 2.0, -3.0));
        assert_eq!(axis_signs(SourceFrame::Ruf), Vec3::ONE);
    }

    #[test]
    fn rub_quat_negates_x_and_y() {
        let q = convert_quat(SourceFrame::Rub, Quat::from_xyzw(0.1, 0.2, 0.3, 0.9));
        assert_eq!(q, Quat::from_xyzw(-0.1, -0.2, 0.3, 0.9));
    }

    #[test]
    fn rub_negates_odd_coefficients_of_every_band() {
        let signs = |band| (0..2 * band + 1).map(|k| sh_sign(SourceFrame::Rub, band, k)).collect::<Vec<_>>();
        assert_eq!(signs(1), [1.0, -1.0, 1.0]);
        assert_eq!(signs(2), [1.0, -1.0, 1.0, -1.0, 1.0]);
    }

    #[test]
    fn full_inversion_negates_odd_bands_only() {
        // LDB flips all three axes: point inversion. Real SH of degree l pick up (−1)^l.
        for (band, expected) in [(1, -1.0), (2, 1.0), (3, -1.0)] {
            for k in 0..2 * band + 1 {
                assert_eq!(sh_sign(SourceFrame::Ldb, band, k), expected, "band {band} k {k}");
            }
        }
    }

    #[test]
    fn ruf_leaves_sh_untouched() {
        let mut v: Vec<f32> = (0..21).map(|i| i as f32).collect();
        let before = v.clone();
        convert_sh(SourceFrame::Ruf, 3, &mut v);
        assert_eq!(v, before);
    }
}
```

`lib.rs` 追加一行：

```rust
pub mod frame;
```

- [ ] **Step 2: 跑测试**

```bash
cd "/Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat/Tools~/gsd-build" && cargo test --lib frame
```

Expected：6 个测试 PASS。`full_inversion_negates_odd_bands_only` 是这组符号规则的物理检验（点反演下 l 阶实球谐乘 (−1)^l），它失败就说明移植错了，改实现不改测试。

- [ ] **Step 3: 提交（子模块）**

```bash
cd /Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat
git add "Tools~/gsd-build/src/frame.rs" "Tools~/gsd-build/src/lib.rs"
git commit -m "$(cat <<'EOF'
feat(gsd-build): convert source frames to Unity RUF

Port of GsplatUtils.AxisSigns/ShSign and the importer's quaternion sign
rule, so .gsd and .ply imports land in the same frame.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: 容器格式与树不变式（`format.rs`、`invariants.rs`）

**Files:**
- Create: `Packages/wu.yize.gsplat/Tools~/gsd-build/src/invariants.rs`
- Create: `Packages/wu.yize.gsplat/Tools~/gsd-build/src/format.rs`
- Modify: `Packages/wu.yize.gsplat/Tools~/gsd-build/src/lib.rs`

**Interfaces:**
- Produces:
  - `gsd_build::invariants::{check_tree(&[u32], &[u16], leaf_count: u32) -> Result<(), TreeError>, TreeError}`；`TreeError` 的 `Display` 文案与 Task 6 的 C# 文案**逐字相同**。
  - `gsd_build::format::{MAGIC, VERSION, HEADER_SIZE, GsdFile { leaf_count: u32, sh_degree: u8, bounds_min: [f32; 3], bounds_max: [f32; 3], nodes: Vec<[u32; 8]>, sh1: Vec<[u32; 2]>, sh2: Vec<[u32; 4]>, sh3: Vec<[u32; 4]>, child_start: Vec<u32>, child_count: Vec<u16> }, Layout, layout(node_count: u32, sh_degree: u8) -> Layout, write(&GsdFile) -> Vec<u8>, read(&[u8]) -> Result<GsdFile, FormatError>, FormatError}`。

- [ ] **Step 1: 写 `invariants.rs`（含测试）**

```rust
//! The `.gsd` tree invariants ① – ④ (spec §4). `GsdReader.CheckTree` in C# enforces the same list
//! with the same wording, so a file rejected on one side is rejected on the other.

use std::fmt;

#[derive(Debug, PartialEq, Eq)]
pub enum TreeError {
    Empty,
    ChildStartNotAfterParent { node: u32, child_start: u32 },
    ChildRangeOutOfBounds { node: u32, end: u64, node_count: u32 },
    ClaimedTwice { child: u32, second_parent: u32 },
    Orphan { node: u32 },
    LeafCountMismatch { declared: u32, actual: u32 },
}

impl fmt::Display for TreeError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        use TreeError::*;
        match self {
            Empty => write!(f, "gsd: empty (nodeCount 0)"),
            ChildStartNotAfterParent { node, child_start } => write!(f, "gsd invariant 2: node {node} childStart {child_start} is not after its parent"),
            ChildRangeOutOfBounds { node, end, node_count } => write!(f, "gsd invariant 2: node {node} child range ends at {end}, past nodeCount {node_count}"),
            ClaimedTwice { child, second_parent } => write!(f, "gsd invariant 3: node {child} is claimed by more than one parent (second: node {second_parent})"),
            Orphan { node } => write!(f, "gsd invariant 3: node {node} has no parent"),
            LeafCountMismatch { declared, actual } => write!(f, "gsd invariant 4: leafCount declares {declared} but the tree has {actual} leaves"),
        }
    }
}

impl std::error::Error for TreeError {}

pub fn check_tree(child_start: &[u32], child_count: &[u16], leaf_count: u32) -> Result<(), TreeError> {
    use TreeError::*;
    let n = child_start.len();
    assert_eq!(n, child_count.len(), "child_start and child_count must have the same length");
    if n == 0 {
        return Err(Empty);
    }
    let mut has_parent = vec![false; n];
    has_parent[0] = true; // ①: node 0 is the root
    let mut leaves = 0u32;
    for i in 0..n {
        let count = u64::from(child_count[i]);
        if count == 0 {
            leaves += 1;
            continue;
        }
        let start = child_start[i];
        if start as usize <= i {
            return Err(ChildStartNotAfterParent { node: i as u32, child_start: start });
        }
        let end = u64::from(start) + count;
        if end > n as u64 {
            return Err(ChildRangeOutOfBounds { node: i as u32, end, node_count: n as u32 });
        }
        for c in start as usize..end as usize {
            if has_parent[c] {
                return Err(ClaimedTwice { child: c as u32, second_parent: i as u32 });
            }
            has_parent[c] = true;
        }
    }
    if let Some(orphan) = has_parent.iter().position(|&p| !p) {
        return Err(Orphan { node: orphan as u32 });
    }
    if leaves != leaf_count {
        return Err(LeafCountMismatch { declared: leaf_count, actual: leaves });
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use TreeError::*;

    // root 0 → [1, 2]; node 1 → [3, 4]; leaves 2, 3, 4.
    const START: [u32; 5] = [1, 3, 0, 0, 0];
    const COUNT: [u16; 5] = [2, 2, 0, 0, 0];

    #[test]
    fn accepts_a_well_formed_tree() {
        assert_eq!(check_tree(&START, &COUNT, 3), Ok(()));
    }

    #[test]
    fn rejects_each_invariant() {
        assert_eq!(check_tree(&[], &[], 0), Err(Empty));
        assert_eq!(check_tree(&[1, 1, 0, 0, 0], &COUNT, 3), Err(ChildStartNotAfterParent { node: 1, child_start: 1 }));
        assert_eq!(check_tree(&[1, 4, 0, 0, 0], &COUNT, 3), Err(ChildRangeOutOfBounds { node: 1, end: 6, node_count: 5 }));
        assert_eq!(check_tree(&[1, 2, 0, 0, 0], &COUNT, 3), Err(ClaimedTwice { child: 2, second_parent: 1 }));
        assert_eq!(check_tree(&[1, 3, 0, 0, 0], &[1, 2, 0, 0, 0], 3), Err(Orphan { node: 2 }));
        assert_eq!(check_tree(&START, &COUNT, 4), Err(LeafCountMismatch { declared: 4, actual: 3 }));
    }
}
```

- [ ] **Step 2: 写 `format.rs`（含测试）**

```rust
//! `.gsd` container (spec §4): a 48-byte header, then fixed sections, each starting 16-byte
//! aligned so it can be uploaded to a GraphicsBuffer as is. `GsdReader.cs` computes the same layout.

use crate::invariants::{check_tree, TreeError};
use std::fmt;

pub const MAGIC: [u8; 4] = *b"GSD\0";
pub const VERSION: u32 = 1;
pub const HEADER_SIZE: usize = 48;

#[derive(Clone, Debug, PartialEq)]
pub struct GsdFile {
    pub leaf_count: u32,
    pub sh_degree: u8,
    pub bounds_min: [f32; 3],
    pub bounds_max: [f32; 3],
    pub nodes: Vec<[u32; 8]>,
    pub sh1: Vec<[u32; 2]>,
    pub sh2: Vec<[u32; 4]>,
    pub sh3: Vec<[u32; 4]>,
    pub child_start: Vec<u32>,
    pub child_count: Vec<u16>,
}

#[derive(Debug, PartialEq)]
pub enum FormatError {
    BadMagic,
    UnsupportedVersion(u32),
    ShDegreeOutOfRange(u8),
    Truncated { expected: u64, actual: u64 },
    TrailingBytes { expected: u64, actual: u64 },
    Tree(TreeError),
}

impl fmt::Display for FormatError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        use FormatError::*;
        match self {
            BadMagic => write!(f, "gsd: bad magic"),
            UnsupportedVersion(v) => write!(f, "gsd: unsupported version {v}"),
            ShDegreeOutOfRange(d) => write!(f, "gsd: sh degree {d} out of range 0..3"),
            Truncated { expected, actual } => write!(f, "gsd: truncated (expected {expected} bytes, got {actual})"),
            TrailingBytes { expected, actual } => write!(f, "gsd: trailing bytes (expected {expected} bytes, got {actual})"),
            Tree(e) => e.fmt(f),
        }
    }
}

impl std::error::Error for FormatError {}

impl From<TreeError> for FormatError {
    fn from(e: TreeError) -> Self {
        FormatError::Tree(e)
    }
}

const fn align16(n: u64) -> u64 {
    (n + 15) & !15
}

/// Byte offset of every section. An absent SH band has offset 0.
#[derive(Debug, PartialEq, Eq)]
pub struct Layout {
    pub nodes: u64,
    pub sh1: u64,
    pub sh2: u64,
    pub sh3: u64,
    pub child_start: u64,
    pub child_count: u64,
    pub total: u64,
}

pub fn layout(node_count: u32, sh_degree: u8) -> Layout {
    let n = u64::from(node_count);
    let mut pos = HEADER_SIZE as u64;
    let nodes = pos;
    pos += n * 32;
    let mut sh = [0u64; 3];
    for (band, bytes_per_node) in [8u64, 16, 16].into_iter().enumerate() {
        if usize::from(sh_degree) > band {
            pos = align16(pos);
            sh[band] = pos;
            pos += n * bytes_per_node;
        }
    }
    pos = align16(pos);
    let child_start = pos;
    pos += n * 4;
    pos = align16(pos);
    let child_count = pos;
    pos += n * 2;
    Layout { nodes, sh1: sh[0], sh2: sh[1], sh3: sh[2], child_start, child_count, total: align16(pos) }
}

pub fn write(file: &GsdFile) -> Vec<u8> {
    let n = file.nodes.len() as u32;
    let l = layout(n, file.sh_degree);
    let mut out = vec![0u8; l.total as usize];
    out[0..4].copy_from_slice(&MAGIC);
    put_u32(&mut out, 4, VERSION);
    put_u32(&mut out, 8, n);
    put_u32(&mut out, 12, file.leaf_count);
    out[16] = file.sh_degree;
    for a in 0..3 {
        put_u32(&mut out, 20 + a * 4, file.bounds_min[a].to_bits());
        put_u32(&mut out, 32 + a * 4, file.bounds_max[a].to_bits());
    }
    put_words(&mut out, l.nodes, file.nodes.iter().flatten());
    if file.sh_degree >= 1 {
        put_words(&mut out, l.sh1, file.sh1.iter().flatten());
    }
    if file.sh_degree >= 2 {
        put_words(&mut out, l.sh2, file.sh2.iter().flatten());
    }
    if file.sh_degree >= 3 {
        put_words(&mut out, l.sh3, file.sh3.iter().flatten());
    }
    put_words(&mut out, l.child_start, file.child_start.iter());
    for (i, c) in file.child_count.iter().enumerate() {
        let at = l.child_count as usize + i * 2;
        out[at..at + 2].copy_from_slice(&c.to_le_bytes());
    }
    out
}

pub fn read(bytes: &[u8]) -> Result<GsdFile, FormatError> {
    use FormatError::*;
    let actual = bytes.len() as u64;
    if bytes.len() < HEADER_SIZE {
        return Err(Truncated { expected: HEADER_SIZE as u64, actual });
    }
    if bytes[0..4] != MAGIC {
        return Err(BadMagic);
    }
    let version = get_u32(bytes, 4);
    if version != VERSION {
        return Err(UnsupportedVersion(version));
    }
    let n = get_u32(bytes, 8);
    let leaf_count = get_u32(bytes, 12);
    let sh_degree = bytes[16];
    if sh_degree > 3 {
        return Err(ShDegreeOutOfRange(sh_degree));
    }
    let l = layout(n, sh_degree);
    if actual < l.total {
        return Err(Truncated { expected: l.total, actual });
    }
    if actual > l.total {
        return Err(TrailingBytes { expected: l.total, actual });
    }
    let n = n as usize;
    let words = |at: u64, i: usize, w: usize| -> Vec<u32> { (0..w).map(|j| get_u32(bytes, at as usize + (i * w + j) * 4)).collect() };
    let file = GsdFile {
        leaf_count,
        sh_degree,
        bounds_min: std::array::from_fn(|a| f32::from_bits(get_u32(bytes, 20 + a * 4))),
        bounds_max: std::array::from_fn(|a| f32::from_bits(get_u32(bytes, 32 + a * 4))),
        nodes: (0..n).map(|i| words(l.nodes, i, 8).try_into().unwrap()).collect(),
        sh1: if sh_degree >= 1 { (0..n).map(|i| words(l.sh1, i, 2).try_into().unwrap()).collect() } else { Vec::new() },
        sh2: if sh_degree >= 2 { (0..n).map(|i| words(l.sh2, i, 4).try_into().unwrap()).collect() } else { Vec::new() },
        sh3: if sh_degree >= 3 { (0..n).map(|i| words(l.sh3, i, 4).try_into().unwrap()).collect() } else { Vec::new() },
        child_start: (0..n).map(|i| get_u32(bytes, l.child_start as usize + i * 4)).collect(),
        child_count: (0..n)
            .map(|i| {
                let at = l.child_count as usize + i * 2;
                u16::from_le_bytes([bytes[at], bytes[at + 1]])
            })
            .collect(),
    };
    check_tree(&file.child_start, &file.child_count, file.leaf_count)?;
    Ok(file)
}

fn put_u32(out: &mut [u8], at: usize, v: u32) {
    out[at..at + 4].copy_from_slice(&v.to_le_bytes());
}

fn put_words<'a>(out: &mut [u8], at: u64, words: impl Iterator<Item = &'a u32>) {
    for (i, w) in words.enumerate() {
        put_u32(out, at as usize + i * 4, *w);
    }
}

fn get_u32(bytes: &[u8], at: usize) -> u32 {
    u32::from_le_bytes(bytes[at..at + 4].try_into().unwrap())
}

#[cfg(test)]
mod tests {
    use super::*;

    /// root 0 → [1, 2], SH degree 1: an odd node count so the SH1 section ends unaligned.
    fn three_nodes() -> GsdFile {
        GsdFile {
            leaf_count: 2,
            sh_degree: 1,
            bounds_min: [-1.0, -2.0, -3.0],
            bounds_max: [1.0, 2.0, 3.0],
            nodes: vec![[1, 2, 3, 4, 5, 6, 7, 8], [9; 8], [10; 8]],
            sh1: vec![[11, 12], [13, 14], [15, 16]],
            sh2: Vec::new(),
            sh3: Vec::new(),
            child_start: vec![1, 0, 0],
            child_count: vec![2, 0, 0],
        }
    }

    #[test]
    fn layout_aligns_every_section() {
        assert_eq!(
            layout(3, 1),
            Layout { nodes: 48, sh1: 144, sh2: 0, sh3: 0, child_start: 176, child_count: 192, total: 208 }
        );
    }

    #[test]
    fn write_then_read_round_trips() {
        let file = three_nodes();
        assert_eq!(read(&write(&file)), Ok(file));
    }

    #[test]
    fn read_rejects_malformed_containers() {
        let good = write(&three_nodes());
        let mut bad = good.clone();
        bad[0] = b'X';
        assert_eq!(read(&bad), Err(FormatError::BadMagic));
        let mut bad = good.clone();
        bad[4] = 2;
        assert_eq!(read(&bad), Err(FormatError::UnsupportedVersion(2)));
        let mut bad = good.clone();
        bad[16] = 4;
        assert_eq!(read(&bad), Err(FormatError::ShDegreeOutOfRange(4)));
        assert_eq!(read(&good[..good.len() - 1]), Err(FormatError::Truncated { expected: 208, actual: 207 }));
        let mut bad = good.clone();
        bad.extend_from_slice(&[0; 16]);
        assert_eq!(read(&bad), Err(FormatError::TrailingBytes { expected: 208, actual: 224 }));
    }

    #[test]
    fn read_rejects_a_broken_tree() {
        let mut file = three_nodes();
        file.child_start[0] = 0;
        assert!(matches!(read(&write(&file)), Err(FormatError::Tree(TreeError::ChildStartNotAfterParent { .. }))));
    }
}
```

`lib.rs` 追加：

```rust
pub mod format;
pub mod invariants;
```

- [ ] **Step 3: 跑测试**

```bash
cd "/Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat/Tools~/gsd-build" && cargo test --lib
```

Expected：Task 1–3 全部测试 PASS（`encode` 6、`frame` 6、`invariants` 2、`format` 4）。

- [ ] **Step 4: 提交（子模块）**

```bash
cd /Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat
git add "Tools~/gsd-build/src/invariants.rs" "Tools~/gsd-build/src/format.rs" "Tools~/gsd-build/src/lib.rs"
git commit -m "$(cat <<'EOF'
feat(gsd-build): .gsd container layout, reader and tree invariants

48-byte header, 16-byte aligned sections, invariants 1-5 checked on read
with the same wording the C# reader will use.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: 建树流水线与 CLI（`pipeline.rs`、`main.rs`）

**Files:**
- Create: `Packages/wu.yize.gsplat/Tools~/gsd-build/src/pipeline.rs`
- Create: `Packages/wu.yize.gsplat/Tools~/gsd-build/src/main.rs`
- Create: `Packages/wu.yize.gsplat/Tools~/gsd-build/tests/common/mod.rs`
- Create: `Packages/wu.yize.gsplat/Tools~/gsd-build/tests/pipeline.rs`
- Modify: `Packages/wu.yize.gsplat/Tools~/gsd-build/src/lib.rs`
- Modify: `Packages/wu.yize.gsplat/Third Party Notices.md`

**Interfaces:**
- Consumes: Task 1–3 的全部导出。
- Produces: `gsd_build::pipeline::{BuildOptions { source: SourceFrame, max_sh: Option<u8>, quick: bool }, BuildStats { input_splats, dropped_empty, leaves, nodes: usize, sh_degree: u8, lod_seconds: f64 }, build(bytes: &[u8], file_name: &str, &BuildOptions) -> anyhow::Result<(GsdFile, BuildStats)>}`；可执行文件 `gsd-build <input> [-o out.gsd] [--source RUB] [--max-sh 0..3] [--quick]`。

- [ ] **Step 1: 写合成 PLY 辅助**

`tests/common/mod.rs`：

```rust
//! Synthetic inputs for the integration tests.

#![allow(dead_code)]

use glam::{Quat, Vec3};

/// A binary little-endian PLY in the standard 3DGS layout: a `side`³ grid, 0.1 apart, SH degree 3.
/// Returns the bytes and the source-frame centres in file order.
pub fn synthetic_ply(side: usize) -> (Vec<u8>, Vec<[f32; 3]>) {
    let n = side * side * side;
    let mut header = format!("ply\nformat binary_little_endian 1.0\nelement vertex {n}\n");
    for p in ["x", "y", "z", "f_dc_0", "f_dc_1", "f_dc_2"] {
        header += &format!("property float {p}\n");
    }
    for i in 0..45 {
        header += &format!("property float f_rest_{i}\n");
    }
    for p in ["opacity", "scale_0", "scale_1", "scale_2", "rot_0", "rot_1", "rot_2", "rot_3"] {
        header += &format!("property float {p}\n");
    }
    header += "end_header\n";

    let mut bytes = header.into_bytes();
    let mut centers = Vec::with_capacity(n);
    for i in 0..n {
        let c = [(i % side) as f32 * 0.1, ((i / side) % side) as f32 * 0.1, (i / (side * side)) as f32 * 0.1];
        centers.push(c);
        let t = i as f32 / n as f32;
        let q = Quat::from_axis_angle(Vec3::new(1.0, t, -0.5).normalize(), t * 3.0);
        let mut values = vec![c[0], c[1], c[2], t - 0.5, 0.5 - t, 0.25];
        values.extend((0..45).map(|k| ((i * 7 + k * 13) % 17) as f32 / 17.0 - 0.5));
        values.extend([2.0, 0.03f32.ln(), 0.02f32.ln(), (0.01 + 0.01 * t).ln(), q.w, q.x, q.y, q.z]);
        for v in values {
            bytes.extend_from_slice(&v.to_le_bytes());
        }
    }
    (bytes, centers)
}

/// Overwrites the first vertex's x with NaN.
pub fn poison_first_x(bytes: &mut [u8]) {
    let body = bytes.windows(11).position(|w| w == b"end_header\n").unwrap() + 11;
    bytes[body..body + 4].copy_from_slice(&f32::NAN.to_le_bytes());
}
```

- [ ] **Step 2: 写失败的端到端测试**

`tests/pipeline.rs`：

```rust
mod common;

use gsd_build::format;
use gsd_build::frame::SourceFrame;
use gsd_build::pipeline::{build, BuildOptions};

fn options(source: SourceFrame) -> BuildOptions {
    BuildOptions { source, max_sh: None, quick: false }
}

#[test]
fn builds_a_valid_tree_over_every_input_splat() {
    let (ply, _) = common::synthetic_ply(4);
    let (file, stats) = build(&ply, "synthetic.ply", &options(SourceFrame::Ruf)).unwrap();
    assert_eq!((stats.leaves, file.leaf_count), (64, 64));
    assert!(file.nodes.len() > 64, "the LoD tree must add interior nodes");
    assert!(file.child_count[0] > 0, "node 0 is the root");
    assert_eq!(file.sh_degree, 3);
    assert_eq!(format::read(&format::write(&file)), Ok(file));
}

#[test]
fn leaves_land_in_the_unity_frame() {
    let (ply, centers) = common::synthetic_ply(4);
    let (file, _) = build(&ply, "synthetic.ply", &options(SourceFrame::Rub)).unwrap();
    let key = |c: &[f32; 3]| c.map(f32::to_bits);
    let mut leaves: Vec<[f32; 3]> = file
        .nodes
        .iter()
        .zip(&file.child_count)
        .filter(|(_, &count)| count == 0)
        .map(|(w, _)| [f32::from_bits(w[0]), f32::from_bits(w[1]), f32::from_bits(w[2])])
        .collect();
    let mut expected: Vec<[f32; 3]> = centers.iter().map(|c| [c[0], c[1], -c[2]]).collect();
    leaves.sort_by_key(key);
    expected.sort_by_key(key);
    assert_eq!(leaves, expected);
}

#[test]
fn every_node_carries_an_encodable_alpha() {
    let (ply, _) = common::synthetic_ply(4);
    let (file, _) = build(&ply, "synthetic.ply", &options(SourceFrame::Ruf)).unwrap();
    for (i, w) in file.nodes.iter().enumerate() {
        let alpha = gsd_build::encode::unpack_ext_splat(w).alpha;
        assert!(alpha > 0.0 && alpha <= 5.0, "node {i} alpha {alpha}");
    }
}

#[test]
fn max_sh_clamps_the_degree() {
    let (ply, _) = common::synthetic_ply(3);
    let (file, _) = build(&ply, "synthetic.ply", &BuildOptions { max_sh: Some(1), ..options(SourceFrame::Ruf) }).unwrap();
    assert_eq!(file.sh_degree, 1);
    assert!(file.sh2.is_empty() && file.sh3.is_empty());
}

#[test]
fn quick_method_also_yields_a_valid_tree() {
    let (ply, _) = common::synthetic_ply(3);
    let (file, _) = build(&ply, "synthetic.ply", &BuildOptions { quick: true, ..options(SourceFrame::Ruf) }).unwrap();
    assert_eq!(file.leaf_count, 27);
}

#[test]
fn non_finite_input_is_refused() {
    let (mut ply, _) = common::synthetic_ply(3);
    common::poison_first_x(&mut ply);
    let err = build(&ply, "synthetic.ply", &options(SourceFrame::Ruf)).unwrap_err();
    assert!(format!("{err:#}").contains("non-finite"), "{err:#}");
}
```

- [ ] **Step 3: 跑测试确认失败**

```bash
cd "/Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat/Tools~/gsd-build" && cargo test --test pipeline
```

Expected：编译失败，`unresolved import gsd_build::pipeline`。

- [ ] **Step 4: 写 `pipeline.rs`**

```rust
//! ply/spz → LoD tree → `.gsd` (spec §5). Decoding, LoD construction and the tree reorder come
//! from Spark's `spark-lib`; this module owns validation, the frame conversion and the encoding.

use anyhow::{bail, Context, Result};
use glam::{Quat, Vec3};
use spark_lib::decoder::{ChunkReceiver, MultiDecoder};
use spark_lib::gsplat::GsplatArray;
use spark_lib::tsplat::{Tsplat, TsplatArray};

use crate::encode::{pack_ext_splat, pack_sh1, pack_sh2, pack_sh3, ExtSplat};
use crate::format::GsdFile;
use crate::frame::{convert_center, convert_quat, convert_sh, SourceFrame};
use crate::invariants::check_tree;

/// Spark's `--quality` preset (bhatt-lod).
pub const QUALITY_LOD_BASE: f32 = 1.75;
/// Spark's `--quick` preset (tiny-lod).
pub const QUICK_LOD_BASE: f32 = 1.5;

#[derive(Clone, Copy, Debug)]
pub struct BuildOptions {
    pub source: SourceFrame,
    pub max_sh: Option<u8>,
    pub quick: bool,
}

#[derive(Clone, Debug, Default)]
pub struct BuildStats {
    pub input_splats: usize,
    pub dropped_empty: usize,
    pub leaves: usize,
    pub nodes: usize,
    pub sh_degree: u8,
    pub lod_seconds: f64,
}

pub fn build(bytes: &[u8], file_name: &str, options: &BuildOptions) -> Result<(GsdFile, BuildStats)> {
    let mut splats = decode(bytes, file_name)?;
    let mut stats = BuildStats { input_splats: splats.len(), ..Default::default() };
    reject_non_finite(&splats)?;
    // Same filter as Spark's build-lod: zero opacity, zero scale or a zero quaternion cannot be
    // merged meaningfully, and log(0) scales would poison the encoding.
    splats.retain(|s| s.opacity() > 0.0 && s.max_scale() > 0.0 && s.quaternion().length() > 0.0);
    stats.dropped_empty = stats.input_splats - splats.len();
    if splats.len() == 0 {
        bail!("{file_name}: no splats left after dropping empty ones");
    }
    if let Some(max_sh) = options.max_sh {
        splats.clamp_sh_degree(usize::from(max_sh));
    }
    stats.leaves = splats.len();

    let started = std::time::Instant::now();
    if options.quick {
        spark_lib::tiny_lod::compute_lod_tree(&mut splats, QUICK_LOD_BASE, false, |_| {});
    } else {
        spark_lib::bhatt_lod::compute_lod_tree(&mut splats, QUALITY_LOD_BASE, |_| {});
    }
    spark_lib::chunk_tree::chunk_tree(&mut splats, 0, |_| {});
    stats.lod_seconds = started.elapsed().as_secs_f64();

    let file = gather(&splats, options.source, stats.leaves as u32)?;
    stats.nodes = file.nodes.len();
    stats.sh_degree = file.sh_degree;
    Ok((file, stats))
}

fn decode(bytes: &[u8], file_name: &str) -> Result<GsplatArray> {
    let mut decoder = MultiDecoder::new(GsplatArray::new(), None, Some(file_name));
    decoder.push(bytes).with_context(|| format!("decoding {file_name}"))?;
    decoder.finish().with_context(|| format!("decoding {file_name}"))?;
    Ok(decoder.into_splats())
}

fn reject_non_finite(splats: &GsplatArray) -> Result<()> {
    let bad = (0..splats.len())
        .filter(|&i| {
            let s = splats.get(i);
            !(s.center().is_finite() && s.scales().is_finite() && s.quaternion().is_finite() && s.opacity().is_finite() && s.rgb().is_finite())
        })
        .count();
    if bad > 0 {
        bail!("{bad} splats have non-finite attributes; refusing to build a tree over them");
    }
    Ok(())
}

/// Encodes the reordered tree. The frame conversion happens here, after the tree is built, so the
/// tree is the one Spark would build from the same file (spec D6).
fn gather(splats: &GsplatArray, source: SourceFrame, leaf_count: u32) -> Result<GsdFile> {
    let n = splats.len();
    let sh_degree = TsplatArray::max_sh_degree(splats) as u8;
    let mut file = GsdFile {
        leaf_count,
        sh_degree,
        bounds_min: [f32::MAX; 3],
        bounds_max: [f32::MIN; 3],
        nodes: Vec::with_capacity(n),
        sh1: Vec::new(),
        sh2: Vec::new(),
        sh3: Vec::new(),
        child_start: Vec::with_capacity(n),
        child_count: Vec::with_capacity(n),
    };
    for i in 0..n {
        let g = &splats.splats[i];
        let s = splats.get(i);
        let center = convert_center(source, Vec3::from(s.center()));
        let opacity = s.opacity();
        let (count, start) = splats.get_child_count_start(i);
        let count = u16::try_from(count).with_context(|| format!("node {i} has {count} children; the format stores at most 65535"))?;
        file.nodes.push(pack_ext_splat(&ExtSplat {
            center,
            alpha: if opacity > 1.0 { s.lod_opacity().min(5.0) } else { opacity },
            rgb: Vec3::from(s.rgb()),
            ln_scales: Vec3::new(g.ln_scales[0].to_f32(), g.ln_scales[1].to_f32(), g.ln_scales[2].to_f32()),
            quat: convert_quat(source, Quat::from_array(g.quaternion.map(|v| v.to_f32()))),
        }));
        if sh_degree >= 1 {
            let mut v = TsplatArray::get_sh1(splats, i);
            convert_sh(source, 1, &mut v);
            file.sh1.push(pack_sh1(&v));
        }
        if sh_degree >= 2 {
            let mut v = TsplatArray::get_sh2(splats, i);
            convert_sh(source, 2, &mut v);
            file.sh2.push(pack_sh2(&v));
        }
        if sh_degree >= 3 {
            let mut v = TsplatArray::get_sh3(splats, i);
            convert_sh(source, 3, &mut v);
            file.sh3.push(pack_sh3(&v));
        }
        file.child_start.push(if count == 0 { 0 } else { start as u32 });
        file.child_count.push(count);
        if count == 0 {
            for a in 0..3 {
                file.bounds_min[a] = file.bounds_min[a].min(center[a]);
                file.bounds_max[a] = file.bounds_max[a].max(center[a]);
            }
        }
    }
    check_tree(&file.child_start, &file.child_count, leaf_count)?;
    Ok(file)
}
```

`lib.rs` 追加：

```rust
pub mod pipeline;
```

- [ ] **Step 5: 写 `main.rs`**

```rust
use anyhow::{anyhow, bail, Context, Result};
use gsd_build::format;
use gsd_build::frame::SourceFrame;
use gsd_build::pipeline::{build, BuildOptions};
use std::path::PathBuf;
use std::process::ExitCode;

const USAGE: &str = "usage: gsd-build <input.ply|.spz|.splat|.ksplat> [-o <output.gsd>] [--source RUB] [--max-sh 0..3] [--quick]";

fn main() -> ExitCode {
    match run(std::env::args().skip(1).collect()) {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            eprintln!("gsd-build: {e:#}");
            ExitCode::FAILURE
        }
    }
}

fn run(args: Vec<String>) -> Result<()> {
    let mut input: Option<PathBuf> = None;
    let mut output: Option<PathBuf> = None;
    let mut options = BuildOptions { source: SourceFrame::Rub, max_sh: None, quick: false };
    let mut it = args.into_iter();
    while let Some(arg) = it.next() {
        match arg.as_str() {
            "-o" => output = Some(it.next().ok_or_else(|| anyhow!("-o needs a path\n{USAGE}"))?.into()),
            "--source" => {
                let frame = it.next().ok_or_else(|| anyhow!("--source needs a frame\n{USAGE}"))?;
                options.source = frame.parse().map_err(|e: String| anyhow!(e))?;
            }
            "--max-sh" => {
                let degree: u8 = it.next().ok_or_else(|| anyhow!("--max-sh needs 0..3\n{USAGE}"))?.parse()?;
                if degree > 3 {
                    bail!("--max-sh must be 0..3");
                }
                options.max_sh = Some(degree);
            }
            "--quick" => options.quick = true,
            "-h" | "--help" => {
                println!("{USAGE}");
                return Ok(());
            }
            other if other.starts_with('-') => bail!("unknown option {other}\n{USAGE}"),
            other => {
                if input.replace(other.into()).is_some() {
                    bail!("exactly one input file\n{USAGE}");
                }
            }
        }
    }
    let input = input.ok_or_else(|| anyhow!(USAGE))?;
    let output = output.unwrap_or_else(|| input.with_extension("gsd"));
    let bytes = std::fs::read(&input).with_context(|| format!("reading {}", input.display()))?;
    let name = input.file_name().and_then(|n| n.to_str()).unwrap_or("input");

    let (file, stats) = build(&bytes, name, &options)?;
    let encoded = format::write(&file);
    // The writer's own output must pass the reader's checks before it reaches disk.
    format::read(&encoded)?;
    std::fs::write(&output, &encoded).with_context(|| format!("writing {}", output.display()))?;

    println!(
        "{} -> {}: {} input, {} empty dropped, {} leaves, {} nodes, SH {}, LoD {:.1}s, {:.1} MB",
        input.display(),
        output.display(),
        stats.input_splats,
        stats.dropped_empty,
        stats.leaves,
        stats.nodes,
        stats.sh_degree,
        stats.lod_seconds,
        encoded.len() as f64 / 1_048_576.0
    );
    Ok(())
}
```

- [ ] **Step 6: 跑全部 Rust 测试**

```bash
cd "/Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat/Tools~/gsd-build" && cargo test
```

Expected：`--lib` 18 个 + `pipeline` 6 个 PASS。若 `builds_a_valid_tree_over_every_input_splat` 报不变式错误（比如根不在 0），说明 `chunk_tree(&mut splats, 0, ..)` 的前提不成立——先读 spark-lib `chunk_tree.rs` 的 `chunk_tree_size` 与 bhatt 的末尾，定位根的下标，**不要绕过 `check_tree`**。

- [ ] **Step 7: 追加 Spark 许可到 Third Party Notices**

在 `Packages/wu.yize.gsplat/Third Party Notices.md` 末尾追加（格式与现有 ZstdSharp 条目一致）：

````markdown

## Spark

`Tools~/gsd-build` depends on Spark's `spark-lib` crate for splat decoding and LoD tree
construction. The ExtSplat node layout, its quaternion codec (`Runtime/Shaders/GsplatLodDecode.hlsl`)
and the LoD traversal (`Runtime/Lod/GsplatLodTraversal.cs`) are ported from Spark.

- Project: https://github.com/sparkjsdev/spark
- Revision: 967263804e637776e94395e61ab2f6cb6a04663c
- License: MIT

```
The MIT License

Copyright © 2025 WORLD LABS TECHNOLOGIES, INC.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```
````

- [ ] **Step 8: 提交（子模块）**

```bash
cd /Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat
git add "Tools~/gsd-build/src/pipeline.rs" "Tools~/gsd-build/src/main.rs" "Tools~/gsd-build/src/lib.rs" "Tools~/gsd-build/tests/common/mod.rs" "Tools~/gsd-build/tests/pipeline.rs" "Tools~/gsd-build/Cargo.lock" "Third Party Notices.md"
git commit -m "$(cat <<'EOF'
feat(gsd-build): build .gsd LoD trees from ply/spz

Decode, bhatt/tiny LoD and chunk reorder from spark-lib; frame
conversion and encoding at gather time; the writer's output is re-read
through the reader's checks before it is written.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: 跨语言金样（`tests/fixture.rs` → `Tests/Editor/Fixtures/`）

**Files:**
- Create: `Packages/wu.yize.gsplat/Tools~/gsd-build/tests/fixture.rs`
- Create（生成）: `Packages/wu.yize.gsplat/Tests/Editor/Fixtures/tiny.gsd`、`tiny.expected.json`

**Interfaces:**
- Consumes: `encode::{ExtSplat, pack_*, unpack_*}`、`format::{GsdFile, write}`。
- Produces: 金样 `tiny.gsd`（8 节点、5 叶、SH3：root 0 → [1,2]；1 → [3,4,5]；2 → [6,7]）与 `tiny.expected.json`：`{ nodeCount, leafCount, shDegree, nodes: [{ center[3], alpha, rgb[3], scale[3], quat[4](x,y,z,w), sh[45], childStart, childCount }] }`。期望值是 **Rust 解码器**对写出字节的解码结果；`scale = exp(ln_scale)`。

金样**手工构造、不经 spark-lib 建树**：它守的是"字节 ↔ 解码"契约，不应受建树算法的细节影响。

- [ ] **Step 1: 写 `tests/fixture.rs`**

```rust
use glam::{Quat, Vec3};
use gsd_build::encode::{self, ExtSplat};
use gsd_build::format::{self, GsdFile};
use serde_json::json;
use std::path::PathBuf;

fn fixture_dir() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../Tests/Editor/Fixtures")
}

/// root 0 → [1, 2]; node 1 → [3, 4, 5]; node 2 → [6, 7]: 8 nodes, 5 leaves, SH degree 3.
/// Interiors carry D > 1, one coordinate sits near 1000 (f32 centres, D3), and the rotation angle
/// sweeps −3..3 rad so the codec meets negative-w quaternions.
fn tiny() -> GsdFile {
    let child_count: [u16; 8] = [2, 3, 2, 0, 0, 0, 0, 0];
    let child_start: [u32; 8] = [1, 3, 6, 0, 0, 0, 0, 0];
    let mut file = GsdFile {
        leaf_count: 5,
        sh_degree: 3,
        bounds_min: [f32::MAX; 3],
        bounds_max: [f32::MIN; 3],
        nodes: Vec::new(),
        sh1: Vec::new(),
        sh2: Vec::new(),
        sh3: Vec::new(),
        child_start: child_start.to_vec(),
        child_count: child_count.to_vec(),
    };
    for i in 0..8 {
        let t = i as f32 / 7.0;
        let center = Vec3::new(1.5 * t - 0.3, -2.0 + t, 1000.0 + 3.25 * t);
        file.nodes.push(encode::pack_ext_splat(&ExtSplat {
            center,
            alpha: if child_count[i] > 0 { 1.25 + 3.5 * t } else { 0.1 + 0.8 * t },
            rgb: Vec3::new(t, 1.0 - t, -0.2 + 1.4 * t),
            ln_scales: Vec3::new(-4.0 + t, -3.0 - t, -2.5),
            quat: Quat::from_axis_angle(Vec3::new(0.3, -1.0, 0.2 + t).normalize(), -3.0 + 6.0 * t),
        }));
        let sh = |k: usize, len: f32| ((i * 5 + k * 3) % 13) as f32 / 13.0 * len - 0.5 * len;
        file.sh1.push(encode::pack_sh1(&std::array::from_fn(|k| sh(k, 1.0))));
        file.sh2.push(encode::pack_sh2(&std::array::from_fn(|k| sh(k, 0.8))));
        file.sh3.push(encode::pack_sh3(&std::array::from_fn(|k| sh(k, 0.6))));
        if child_count[i] == 0 {
            for a in 0..3 {
                file.bounds_min[a] = file.bounds_min[a].min(center[a]);
                file.bounds_max[a] = file.bounds_max[a].max(center[a]);
            }
        }
    }
    file
}

fn expected_json(file: &GsdFile) -> serde_json::Value {
    let nodes: Vec<_> = (0..file.nodes.len())
        .map(|i| {
            let s = encode::unpack_ext_splat(&file.nodes[i]);
            let mut sh = Vec::new();
            sh.extend(encode::unpack_sh1(&file.sh1[i]));
            sh.extend(encode::unpack_sh2(&file.sh2[i]));
            sh.extend(encode::unpack_sh3(&file.sh3[i]));
            json!({
                "center": s.center.to_array(),
                "alpha": s.alpha,
                "rgb": s.rgb.to_array(),
                "scale": s.ln_scales.to_array().map(f32::exp),
                "quat": s.quat.to_array(),
                "sh": sh,
                "childStart": file.child_start[i],
                "childCount": file.child_count[i],
            })
        })
        .collect();
    json!({ "nodeCount": file.nodes.len(), "leafCount": file.leaf_count, "shDegree": file.sh_degree, "nodes": nodes })
}

/// Regenerates the Unity-side fixture: `cargo test --test fixture -- --ignored`.
#[test]
#[ignore]
fn regenerate_fixture() {
    let file = tiny();
    std::fs::create_dir_all(fixture_dir()).unwrap();
    std::fs::write(fixture_dir().join("tiny.gsd"), format::write(&file)).unwrap();
    let json = serde_json::to_string_pretty(&expected_json(&file)).unwrap();
    std::fs::write(fixture_dir().join("tiny.expected.json"), json + "\n").unwrap();
}

/// The committed fixture must be what the current encoder writes; a stale one would have the Unity
/// test checking HLSL against a contract that no longer exists.
#[test]
fn committed_fixture_is_current() {
    let committed = std::fs::read(fixture_dir().join("tiny.gsd")).expect("fixture missing: run `cargo test --test fixture -- --ignored`");
    assert_eq!(committed, format::write(&tiny()), "fixture is stale: run `cargo test --test fixture -- --ignored`");
}

#[test]
fn fixture_passes_the_reader() {
    assert_eq!(format::read(&format::write(&tiny())), Ok(tiny()));
}
```

- [ ] **Step 2: 确认守护测试先失败**

```bash
cd "/Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat/Tools~/gsd-build" && cargo test --test fixture
```

Expected：`committed_fixture_is_current` FAIL（fixture missing），`fixture_passes_the_reader` PASS。

- [ ] **Step 3: 生成金样并复跑**

```bash
cd "/Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat/Tools~/gsd-build" && cargo test --test fixture -- --ignored && cargo test --test fixture
ls -l ../../Tests/Editor/Fixtures/
```

Expected：两个文件生成；第二次 3 个测试全 PASS；`tiny.gsd` 恰为 672 字节（头 48 + 节点 256 + SH1 64 + SH2 128 + SH3 128 + childStart 32 + childCount 16，均已 16B 对齐）。

- [ ] **Step 4: 提交（子模块）。Unity 生成的 `.meta` 留到 Task 6 一起提交**

```bash
cd /Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat
git add "Tools~/gsd-build/tests/fixture.rs" Tests/Editor/Fixtures/tiny.gsd Tests/Editor/Fixtures/tiny.expected.json
git commit -m "$(cat <<'EOF'
test(gsd-build): cross-language fixture for the .gsd decode contract

Hand-built 8-node tree (D > 1 interiors, far coordinates, negative-w
quats, SH3) plus the Rust decoder's view of it, for the Unity GPU test.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: 包测试程序集与 `GsdReader`

**Files:**
- Create: `Packages/wu.yize.gsplat/Tests/Editor/Gsplat.Editor.Tests.asmdef`
- Create: `Packages/wu.yize.gsplat/Tests/Editor/GsdFixture.cs`
- Create: `Packages/wu.yize.gsplat/Tests/Editor/GsdReaderTests.cs`
- Create: `Packages/wu.yize.gsplat/Runtime/Lod/GsdReader.cs`
- Modify: `Packages/manifest.json`（MR_Base）

**Interfaces:**
- Produces（命名空间 `Gsplat`）：
  - `public sealed class GsdData { uint NodeCount; uint LeafCount; byte SHDegree; float3 BoundsMin; float3 BoundsMax; uint4[] Nodes /*2 per node*/; uint[] SH1 /*2/node*/; uint[] SH2 /*4/node*/; uint[] SH3 /*4/node*/; uint[] ChildStart; ushort[] ChildCount; }`（公有字段）
  - `public sealed class GsdFormatException : Exception`
  - `public static class GsdReader { const uint Version = 1; const int HeaderSize = 48; static GsdData Read(ReadOnlySpan<byte>); static void CheckTree(uint[] childStart, ushort[] childCount, uint leafCount); }`
  - 测试辅助（命名空间 `Gsplat.Tests`）：`static class GsdFixture { const string Dir; const string GsdPath; static byte[] Bytes(); static Expected LoadExpected(); class Expected { uint nodeCount; uint leafCount; int shDegree; Node[] nodes; } class Node { float[] center; float alpha; float[] rgb; float[] scale; float[] quat; float[] sh; uint childStart; int childCount; } }`

- [ ] **Step 1: 测试程序集与 testables**

`Tests/Editor/Gsplat.Editor.Tests.asmdef`：

```json
{
    "name": "Gsplat.Editor.Tests",
    "rootNamespace": "",
    "references": [
        "Gsplat",
        "Gsplat.Editor",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "Unity.Mathematics"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`Packages/manifest.json`（MR_Base）的 `testables` 改为：

```json
  "testables": [
    "com.uality.ite-tour",
    "wu.yize.gsplat"
  ]
```

- [ ] **Step 2: 写金样辅助与失败的测试**

`Tests/Editor/GsdFixture.cs`：

```csharp
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using UnityEngine;

namespace Gsplat.Tests
{
    /// <summary>
    /// The fixture written by <c>Tools~/gsd-build/tests/fixture.rs</c>: an 8-node .gsd and the Rust
    /// decoder's view of it. Regenerate both there, never by hand.
    /// </summary>
    static class GsdFixture
    {
        public const string Dir = "Packages/wu.yize.gsplat/Tests/Editor/Fixtures/";
        public const string GsdPath = Dir + "tiny.gsd";

        public static byte[] Bytes() => File.ReadAllBytes(Path.GetFullPath(GsdPath));

        public static Expected LoadExpected() =>
            JsonUtility.FromJson<Expected>(File.ReadAllText(Path.GetFullPath(Dir + "tiny.expected.json")));

        [Serializable]
        public class Expected
        {
            public uint nodeCount;
            public uint leafCount;
            public int shDegree;
            public Node[] nodes;
        }

        [Serializable]
        public class Node
        {
            public float[] center;
            public float alpha;
            public float[] rgb;
            public float[] scale;
            public float[] quat; // real quaternion (x, y, z, w)
            public float[] sh;   // 45 floats: band 1, 2, 3, coefficient-major, channel-minor
            public uint childStart;
            public int childCount;
        }
    }
}
```

`Tests/Editor/GsdReaderTests.cs`：

```csharp
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;

namespace Gsplat.Tests
{
    public class GsdReaderTests
    {
        // root 0 → [1, 2]; node 1 → [3, 4]; leaves 2, 3, 4.
        static readonly uint[] k_start = { 1, 3, 0, 0, 0 };
        static readonly ushort[] k_count = { 2, 2, 0, 0, 0 };

        [Test]
        public void ReadsTheFixture()
        {
            var expected = GsdFixture.LoadExpected();
            var data = GsdReader.Read(GsdFixture.Bytes());
            Assert.AreEqual(expected.nodeCount, data.NodeCount);
            Assert.AreEqual(expected.leafCount, data.LeafCount);
            Assert.AreEqual(expected.shDegree, data.SHDegree);
            Assert.AreEqual(data.NodeCount * 2, data.Nodes.Length);
            Assert.AreEqual(data.NodeCount * 2, data.SH1.Length);
            Assert.AreEqual(data.NodeCount * 4, data.SH3.Length);
            for (var i = 0; i < expected.nodes.Length; ++i)
            {
                Assert.AreEqual(expected.nodes[i].childStart, data.ChildStart[i], $"childStart[{i}]");
                Assert.AreEqual(expected.nodes[i].childCount, data.ChildCount[i], $"childCount[{i}]");
            }
        }

        [Test] public void RejectsBadMagic() => ExpectError(b => b[0] = (byte)'X', "bad magic");
        [Test] public void RejectsUnsupportedVersion() => ExpectError(b => b[4] = 2, "unsupported version 2");
        [Test] public void RejectsShDegreeOutOfRange() => ExpectError(b => b[16] = 4, "sh degree 4");
        [Test] public void RejectsLeafCountMismatch() => ExpectError(b => b[12] += 1, "invariant 4");

        [Test]
        public void RejectsHugeNodeCountBeforeAllocating() =>
            ExpectError(b => { b[8] = 0xff; b[9] = 0xff; b[10] = 0xff; b[11] = 0x7f; }, "truncated");

        [Test]
        public void RejectsTruncatedFile()
        {
            var bytes = GsdFixture.Bytes();
            Assert.That(() => GsdReader.Read(bytes.AsSpan(0, bytes.Length - 1)),
                Throws.TypeOf<GsdFormatException>().With.Message.Contains("truncated"));
        }

        [Test]
        public void RejectsTrailingBytes()
        {
            var bytes = GsdFixture.Bytes();
            Array.Resize(ref bytes, bytes.Length + 16);
            Assert.That(() => GsdReader.Read(bytes),
                Throws.TypeOf<GsdFormatException>().With.Message.Contains("trailing bytes"));
        }

        [Test] public void AcceptsAWellFormedTree() => Assert.DoesNotThrow(() => GsdReader.CheckTree(k_start, k_count, 3));
        [Test] public void RejectsAnEmptyTree() => ExpectTree(Array.Empty<uint>(), Array.Empty<ushort>(), 0, "empty");
        [Test] public void RejectsChildStartNotAfterParent() => ExpectTree(new uint[] { 1, 1, 0, 0, 0 }, k_count, 3, "invariant 2");
        [Test] public void RejectsAChildRangePastTheEnd() => ExpectTree(new uint[] { 1, 4, 0, 0, 0 }, k_count, 3, "invariant 2");
        [Test] public void RejectsANodeClaimedTwice() => ExpectTree(new uint[] { 1, 2, 0, 0, 0 }, k_count, 3, "claimed by more than one parent");
        [Test] public void RejectsAnOrphan() => ExpectTree(new uint[] { 1, 3, 0, 0, 0 }, new ushort[] { 1, 2, 0, 0, 0 }, 3, "has no parent");

        static void ExpectError(Action<byte[]> corrupt, string message)
        {
            var bytes = GsdFixture.Bytes();
            corrupt(bytes);
            Assert.That(() => GsdReader.Read(bytes), Throws.TypeOf<GsdFormatException>().With.Message.Contains(message));
        }

        static void ExpectTree(uint[] start, ushort[] count, uint leaves, string message) =>
            Assert.That(() => GsdReader.CheckTree(start, count, leaves),
                Throws.TypeOf<GsdFormatException>().With.Message.Contains(message));
    }
}
```

- [ ] **Step 3: 刷新并确认测试因缺类型而编译失败**

执行 §U。Expected：console 报 `GsdReader`/`GsdFormatException` 不存在（CS0103/CS0246）。这证明测试程序集被编进来了。

- [ ] **Step 4: 写 `Runtime/Lod/GsdReader.cs`**

```csharp
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Gsplat
{
    /// <summary>
    /// Contents of a <c>.gsd</c> file, laid out exactly as in the file so each array uploads to a
    /// GraphicsBuffer unchanged. Only <see cref="GsdReader.Read"/> produces one.
    /// </summary>
    public sealed class GsdData
    {
        public uint NodeCount;
        public uint LeafCount;
        public byte SHDegree;
        public float3 BoundsMin;
        public float3 BoundsMax;
        /// <summary>Two <c>uint4</c> per node: the 32-byte ExtSplat layout.</summary>
        public uint4[] Nodes;
        public uint[] SH1; // 2 words per node when SHDegree >= 1, else empty
        public uint[] SH2; // 4 words per node when SHDegree >= 2, else empty
        public uint[] SH3; // 4 words per node when SHDegree >= 3, else empty
        public uint[] ChildStart;
        public ushort[] ChildCount;
    }

    public sealed class GsdFormatException : Exception
    {
        public GsdFormatException(string message) : base(message) { }
    }

    /// <summary>
    /// The one parser for <c>.gsd</c> files (spec §4), shared by the importer and runtime byte
    /// loading. Runtime bytes cross a trust boundary, so every section length and every tree
    /// invariant is checked before anything is returned. Messages match <c>gsd-build</c>'s reader.
    /// </summary>
    public static class GsdReader
    {
        public const uint Version = 1;
        public const int HeaderSize = 48;

        public static GsdData Read(ReadOnlySpan<byte> bytes)
        {
            if (!BitConverter.IsLittleEndian)
                throw new GsdFormatException("gsd: big-endian hosts are not supported");
            if (bytes.Length < HeaderSize)
                throw new GsdFormatException($"gsd: truncated (expected {HeaderSize} bytes, got {bytes.Length})");
            if (bytes[0] != (byte)'G' || bytes[1] != (byte)'S' || bytes[2] != (byte)'D' || bytes[3] != 0)
                throw new GsdFormatException("gsd: bad magic");
            var version = U32(bytes, 4);
            if (version != Version)
                throw new GsdFormatException($"gsd: unsupported version {version}");
            var nodeCount = U32(bytes, 8);
            var leafCount = U32(bytes, 12);
            var shDegree = bytes[16];
            if (shDegree > 3)
                throw new GsdFormatException($"gsd: sh degree {shDegree} out of range 0..3");

            // Lengths are computed in long and checked against the actual size before any array is
            // allocated, so a header claiming billions of nodes fails here instead of in the allocator.
            var layout = Layout.For(nodeCount, shDegree);
            if (bytes.Length < layout.Total)
                throw new GsdFormatException($"gsd: truncated (expected {layout.Total} bytes, got {bytes.Length})");
            if (bytes.Length > layout.Total)
                throw new GsdFormatException($"gsd: trailing bytes (expected {layout.Total} bytes, got {bytes.Length})");

            var n = (int)nodeCount;
            var data = new GsdData
            {
                NodeCount = nodeCount,
                LeafCount = leafCount,
                SHDegree = shDegree,
                BoundsMin = new float3(F32(bytes, 20), F32(bytes, 24), F32(bytes, 28)),
                BoundsMax = new float3(F32(bytes, 32), F32(bytes, 36), F32(bytes, 40)),
                Nodes = Slice<uint4>(bytes, layout.Nodes, n * 2),
                SH1 = shDegree >= 1 ? Slice<uint>(bytes, layout.SH1, n * 2) : Array.Empty<uint>(),
                SH2 = shDegree >= 2 ? Slice<uint>(bytes, layout.SH2, n * 4) : Array.Empty<uint>(),
                SH3 = shDegree >= 3 ? Slice<uint>(bytes, layout.SH3, n * 4) : Array.Empty<uint>(),
                ChildStart = Slice<uint>(bytes, layout.ChildStart, n),
                ChildCount = Slice<ushort>(bytes, layout.ChildCount, n),
            };
            CheckTree(data.ChildStart, data.ChildCount, leafCount);
            return data;
        }

        /// <summary>Invariants ① – ④ of spec §4.</summary>
        public static void CheckTree(uint[] childStart, ushort[] childCount, uint leafCount)
        {
            var n = childStart.Length;
            if (childCount.Length != n)
                throw new ArgumentException("childStart and childCount must have the same length");
            if (n == 0)
                throw new GsdFormatException("gsd: empty (nodeCount 0)");

            var hasParent = new bool[n];
            hasParent[0] = true; // ①: node 0 is the root
            uint leaves = 0;
            for (var i = 0; i < n; ++i)
            {
                var count = childCount[i];
                if (count == 0)
                {
                    ++leaves;
                    continue;
                }

                var start = childStart[i];
                if (start <= (uint)i)
                    throw new GsdFormatException($"gsd invariant 2: node {i} childStart {start} is not after its parent");
                var end = (long)start + count;
                if (end > n)
                    throw new GsdFormatException($"gsd invariant 2: node {i} child range ends at {end}, past nodeCount {n}");
                for (var c = (int)start; c < end; ++c)
                {
                    if (hasParent[c])
                        throw new GsdFormatException(
                            $"gsd invariant 3: node {c} is claimed by more than one parent (second: node {i})");
                    hasParent[c] = true;
                }
            }

            var orphan = Array.IndexOf(hasParent, false);
            if (orphan >= 0)
                throw new GsdFormatException($"gsd invariant 3: node {orphan} has no parent");
            if (leaves != leafCount)
                throw new GsdFormatException(
                    $"gsd invariant 4: leafCount declares {leafCount} but the tree has {leaves} leaves");
        }

        static T[] Slice<T>(ReadOnlySpan<byte> bytes, long offset, int count) where T : struct =>
            MemoryMarshal.Cast<byte, T>(bytes.Slice((int)offset, count * Marshal.SizeOf<T>())).ToArray();

        static uint U32(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(at, 4));

        static float F32(ReadOnlySpan<byte> bytes, int at) =>
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(at, 4)));

        /// <summary>Section offsets; mirrors <c>layout()</c> in <c>gsd-build/src/format.rs</c>.</summary>
        readonly struct Layout
        {
            public readonly long Nodes, SH1, SH2, SH3, ChildStart, ChildCount, Total;

            Layout(long nodes, long sh1, long sh2, long sh3, long childStart, long childCount, long total)
            {
                Nodes = nodes; SH1 = sh1; SH2 = sh2; SH3 = sh3;
                ChildStart = childStart; ChildCount = childCount; Total = total;
            }

            static long Align16(long n) => (n + 15) & ~15L;

            public static Layout For(uint nodeCount, byte shDegree)
            {
                long n = nodeCount, pos = HeaderSize;
                var nodes = pos;
                pos += n * 32;
                long sh1 = 0, sh2 = 0, sh3 = 0;
                if (shDegree >= 1) { pos = Align16(pos); sh1 = pos; pos += n * 8; }
                if (shDegree >= 2) { pos = Align16(pos); sh2 = pos; pos += n * 16; }
                if (shDegree >= 3) { pos = Align16(pos); sh3 = pos; pos += n * 16; }
                pos = Align16(pos);
                var childStart = pos;
                pos += n * 4;
                pos = Align16(pos);
                var childCount = pos;
                pos += n * 2;
                return new Layout(nodes, sh1, sh2, sh3, childStart, childCount, Align16(pos));
            }
        }
    }
}
```

- [ ] **Step 5: 刷新、跑测试**

执行 §U，再按 §T 跑 `--filter GsdReaderTests --filter_type testName`。Expected：14 个测试 PASS。

- [ ] **Step 6: 提交（子模块 + MR_Base）**

```bash
cd /Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat
git add Tests Runtime/Lod
git status --short   # 只应看到 Tests/ 与 Runtime/Lod/ 下的新文件（含 .meta）
git commit -m "$(cat <<'EOF'
feat(lod): GsdReader, the single .gsd parser, with its test assembly

Checks every section length before allocating and every tree invariant
before returning; messages match gsd-build's reader.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
cd /Users/wwj/Desktop/unity/MR_Base
git add Packages/manifest.json
git commit -m "$(cat <<'EOF'
chore(gsplat): run the gsplat package tests in the Test Runner

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Shader——SH 拆分、ExtSplat 解码、`LOD` 变体、深度 kernel

**Files:**
- Create: `Packages/wu.yize.gsplat/Runtime/Shaders/GsplatSparkSH.hlsl`（从 `GsplatSpark.hlsl` 移出）
- Modify: `Packages/wu.yize.gsplat/Runtime/Shaders/GsplatSpark.hlsl`
- Create: `Packages/wu.yize.gsplat/Runtime/Shaders/GsplatLodDecode.hlsl`
- Create: `Packages/wu.yize.gsplat/Runtime/Shaders/GsplatLod.hlsl`
- Create: `Packages/wu.yize.gsplat/Runtime/Shaders/CalcDepthLod.compute`
- Modify: `Packages/wu.yize.gsplat/Runtime/Shaders/Gsplat.shader`
- Create: `Packages/wu.yize.gsplat/Tests/Editor/GsdDecodeProbe.compute`
- Create: `Packages/wu.yize.gsplat/Tests/Editor/GsdDecodeTests.cs`

**Interfaces:**
- Consumes: Task 5 金样、Task 6 `GsdReader`/`GsdFixture`。
- Produces（HLSL）：`GsplatLodDecode.hlsl` 提供 `struct LodSplat { float3 center; float alpha; float3 rgb; float3 scale; float4 quat; }`、`LodSplat UnpackLodSplat(uint4 w0, uint4 w1)`、`float4 DecodeQuatOctXy1010R12(uint)`（返回实四元数 xyzw）、`float4 LodQuatToShaderOrder(float4 q)`（→ `q.wxyz`，即现有 `CalcCovariance` 需要的实部在前的顺序）、`float LodExtentScale(float alphaOrD)`。节点缓冲名 `_LodNodesBuffer`（`StructuredBuffer<uint4>`，节点 i 在 `[2i]`、`[2i+1]`）。SH 缓冲沿用 `_PackedSH1Buffer`/`_PackedSH2Buffer`/`_PackedSH3Buffer`。材质关键字 `LOD`。深度 kernel `CalcDepthLod`（kernel 0）。

- [ ] **Step 1: 写失败的 GPU 解码测试与变体编译测试**

`Tests/Editor/GsdDecodeProbe.compute`：

```hlsl
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

// Test-only probe: decodes .gsd nodes on the GPU through the runtime's own includes, so
// GsdDecodeTests can compare the result with the Rust decoder's output in the fixture JSON.
#pragma kernel Decode

#define SH_BANDS_3
#define SH_COEFFS 15
#include "Packages/wu.yize.gsplat/Runtime/Shaders/GsplatLodDecode.hlsl"
#include "Packages/wu.yize.gsplat/Runtime/Shaders/GsplatSparkSH.hlsl"

StructuredBuffer<uint4> _LodNodesBuffer;
RWStructuredBuffer<float> _Out;
uint _Count;

// center 3 · alpha 1 · rgb 3 · scale 3 · quat 4 · shader-order quat 4 · sh 45
#define STRIDE 63

void Put3(uint at, float3 v) { _Out[at] = v.x; _Out[at + 1] = v.y; _Out[at + 2] = v.z; }
void Put4(uint at, float4 v) { Put3(at, v.xyz); _Out[at + 3] = v.w; }

[numthreads(64, 1, 1)]
void Decode(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= _Count)
        return;
    LodSplat s = UnpackLodSplat(_LodNodesBuffer[id.x * 2], _LodNodesBuffer[id.x * 2 + 1]);
    uint o = id.x * STRIDE;
    Put3(o, s.center);
    _Out[o + 3] = s.alpha;
    Put3(o + 4, s.rgb);
    Put3(o + 7, s.scale);
    Put4(o + 10, s.quat);
    Put4(o + 14, LodQuatToShaderOrder(s.quat));
    float3 sh[SH_COEFFS];
    InitSH(id.x, sh);
    for (uint k = 0; k < SH_COEFFS; ++k)
        Put3(o + 18 + k * 3, sh[k]);
}
```

`Tests/Editor/GsdDecodeTests.cs`：

```csharp
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace Gsplat.Tests
{
    public class GsdDecodeTests
    {
        const int k_stride = 63;

        /// <summary>
        /// Rust encodes, Rust decodes into the JSON, HLSL decodes the same bytes: the two decoders
        /// must agree (spec §8.1). Centres, alpha, colour and SH are exact arithmetic on both sides;
        /// scale and rotation go through exp/sin/cos, which GPUs are allowed to approximate.
        /// </summary>
        [Test]
        public void GpuDecodeMatchesTheRustDecoder()
        {
            var data = GsdReader.Read(GsdFixture.Bytes());
            var expected = GsdFixture.LoadExpected();
            var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/wu.yize.gsplat/Tests/Editor/GsdDecodeProbe.compute");
            Assert.IsNotNull(cs, "probe compute shader missing");
            var n = (int)data.NodeCount;

            using var nodes = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n * 2, 16);
            using var sh1 = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 8);
            using var sh2 = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 16);
            using var sh3 = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 16);
            using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n * k_stride, 4);
            nodes.SetData(data.Nodes);
            sh1.SetData(data.SH1);
            sh2.SetData(data.SH2);
            sh3.SetData(data.SH3);

            var kernel = cs.FindKernel("Decode");
            cs.SetBuffer(kernel, "_LodNodesBuffer", nodes);
            cs.SetBuffer(kernel, "_PackedSH1Buffer", sh1);
            cs.SetBuffer(kernel, "_PackedSH2Buffer", sh2);
            cs.SetBuffer(kernel, "_PackedSH3Buffer", sh3);
            cs.SetBuffer(kernel, "_Out", output);
            cs.SetInt("_Count", n);
            cs.Dispatch(kernel, (n + 63) / 64, 1, 1);
            var got = new float[n * k_stride];
            output.GetData(got);

            for (var i = 0; i < n; ++i)
            {
                var e = expected.nodes[i];
                var o = i * k_stride;
                Close(e.center, got, o, 0f, $"node {i} center");
                Close(new[] { e.alpha }, got, o + 3, 1e-6f, $"node {i} alpha");
                Close(e.rgb, got, o + 4, 1e-6f, $"node {i} rgb");
                Close(e.scale, got, o + 7, 1e-4f * e.scale.Max(), $"node {i} scale");
                Close(e.quat, got, o + 10, 1e-4f, $"node {i} quat (x, y, z, w)");
                Close(new[] { e.quat[3], e.quat[0], e.quat[1], e.quat[2] }, got, o + 14, 1e-4f,
                    $"node {i} quat in shader order (w, x, y, z)");
                Close(e.sh, got, o + 18, 1e-6f, $"node {i} sh");
            }
        }

        /// <summary>The LOD variant must compile for the headset target as well as the Editor; SPARK guards the SH split.</summary>
        [TestCase("SPARK")]
        [TestCase("LOD")]
        public void VariantCompilesForQuestAndEditor(string encoding)
        {
            var shader = Shader.Find("Gsplat/Standard");
            var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
            var targets = new[]
            {
                (ShaderCompilerPlatform.Vulkan, BuildTarget.Android),
                (ShaderCompilerPlatform.Metal, BuildTarget.StandaloneOSX),
            };
            foreach (var (platform, target) in targets)
            foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
            {
                var info = pass.CompileVariant(stage, new[] { encoding, "SH_BANDS_3" }, platform, target);
                Assert.IsTrue(info.Success, $"{encoding} {stage} {platform}:\n" +
                                            string.Join("\n", info.Messages.Select(m => $"{m.severity}: {m.message} (line {m.line})")));
            }
        }

        static void Close(float[] expected, float[] got, int offset, float tolerance, string what)
        {
            for (var k = 0; k < expected.Length; ++k)
                Assert.AreEqual(expected[k], got[offset + k], tolerance, $"{what}[{k}]");
        }
    }
}
```

执行 §U。Expected：probe 编译报错（找不到 `GsplatLodDecode.hlsl` / `GsplatSparkSH.hlsl`），测试尚无法通过。

- [ ] **Step 2: 把 SH 解码从 `GsplatSpark.hlsl` 挪到 `GsplatSparkSH.hlsl`（原样移动，不改一个字节）**

```bash
cd /Users/wwj/Desktop/unity/MR_Base && python3 - <<'EOF'
from pathlib import Path
spark = Path("Packages/wu.yize.gsplat/Runtime/Shaders/GsplatSpark.hlsl")
src = spark.read_bytes().decode("utf-8")
start = src.index("#ifndef SH_BANDS_0")
guard_end = src.rindex("#endif")                       # closes GSPLAT_SPARK_INCLUDED
end = src.rindex("#endif", 0, guard_end) + len("#endif")  # closes the SH block
block = src[start:end]
assert "void InitSH(" in block and "UnpackSplat" not in block, "SH block boundaries moved; stop and inspect"
header = (
    "// Copyright (c) 2026 Arthur Aillet, Yize Wu\n"
    "// SPDX-License-Identifier: MIT\n\n"
    "// Packed SH decode shared by the SPARK and LOD variants: both store SH in the same\n"
    "// sint7 / sint8 / sint6 layout, so these bit operations exist exactly once.\n"
    "#ifndef GSPLAT_SPARK_SH_INCLUDED\n#define GSPLAT_SPARK_SH_INCLUDED\n\n"
)
Path("Packages/wu.yize.gsplat/Runtime/Shaders/GsplatSparkSH.hlsl").write_bytes((header + block + "\n\n#endif\n").encode("utf-8"))
spark.write_bytes((src[:start] + '#include "GsplatSparkSH.hlsl"' + src[end:]).encode("utf-8"))
EOF
grep -n "GsplatSparkSH\|InitSH" Packages/wu.yize.gsplat/Runtime/Shaders/GsplatSpark.hlsl
```

Expected：`GsplatSpark.hlsl` 里只剩一行 `#include "GsplatSparkSH.hlsl"`，不再有 `InitSH` 定义。`GsplatSparkGlobal.hlsl` 通过 include `GsplatSpark.hlsl` 间接拿到它，不用改。

- [ ] **Step 3: 写 `GsplatLodDecode.hlsl`**

```hlsl
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

// Decoders for the 32-byte ExtSplat node layout of .gsd files. Ported from Spark's
// splatDefines.glsl (World Labs Technologies, MIT). The encoder is Tools~/gsd-build/src/encode.rs;
// Tests/Editor/GsdDecodeTests pins the two together. No dependencies, so a compute shader can
// include it on its own.
#ifndef GSPLAT_LOD_DECODE_INCLUDED
#define GSPLAT_LOD_DECODE_INCLUDED

#define GSPLAT_LOD_PI 3.14159265358979323846

// Returns a real quaternion (x, y, z, w), w = cos(θ/2).
float4 DecodeQuatOctXy1010R12(uint encoded)
{
    float2 f = float2(float(encoded & 0x3FFu), float((encoded >> 10u) & 0x3FFu)) / 1023.0 * 2.0 - 1.0;
    float3 axis = float3(f.xy, 1.0 - abs(f.x) - abs(f.y));
    float t = max(-axis.z, 0.0);
    axis.x += (axis.x >= 0.0) ? -t : t;
    axis.y += (axis.y >= 0.0) ? -t : t;
    axis = normalize(axis);
    float theta = (float(encoded >> 20u) / 4095.0) * GSPLAT_LOD_PI;
    float s, c;
    sincos(theta * 0.5, s, c);
    return float4(axis * s, c);
}

struct LodSplat
{
    float3 center;
    float alpha; // opacity in 0..1, or the merged-node D in (1, 5]
    float3 rgb;
    float3 scale;
    float4 quat; // real quaternion (x, y, z, w)
};

LodSplat UnpackLodSplat(uint4 w0, uint4 w1)
{
    LodSplat s;
    s.center = asfloat(w0.xyz);
    s.alpha = f16tof32(w0.w & 0xFFFFu);
    s.rgb = float3(f16tof32(w1.x & 0xFFFFu), f16tof32(w1.x >> 16u), f16tof32(w1.y & 0xFFFFu));
    s.scale = exp(float3(f16tof32(w1.y >> 16u), f16tof32(w1.z & 0xFFFFu), f16tof32(w1.z >> 16u)));
    s.quat = DecodeQuatOctXy1010R12(w1.w);
    return s;
}

// The shared splat path (QuatToMat3 / CalcCovariance in Gsplat.hlsl) takes the real part first:
// the PLY rot_0..3 order the importers store. Spark's codec yields (x, y, z, w).
float4 LodQuatToShaderOrder(float4 q)
{
    return q.wxyz;
}

// A merged node's opacity profile reaches further than a plain splat's (Spark:
// maxStdDev + 0.7·(D − 1)). Returns how much to widen the √8σ quad for a node.
float LodExtentScale(float alphaOrD)
{
    return alphaOrD > 1.0 ? (sqrt(8.0) + 0.7 * (alphaOrD - 1.0)) / sqrt(8.0) : 1.0;
}

#endif
```

- [ ] **Step 4: 写 `GsplatLod.hlsl` 与 `CalcDepthLod.compute`**

`GsplatLod.hlsl`：

```hlsl
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

#ifndef GSPLAT_LOD_INCLUDED
#define GSPLAT_LOD_INCLUDED

#include "Gsplat.hlsl"
#include "GsplatLodDecode.hlsl"
#include "GsplatSparkSH.hlsl"

// Two uint4 per node (ExtSplat). source.id is a node index chosen by the LoD selection.
StructuredBuffer<uint4> _LodNodesBuffer;

bool InitSplatData(SplatSource source, float4x4 modelView, out SplatCenter center, out SplatCorner corner,
                   out float4 color)
{
    LodSplat s = UnpackLodSplat(_LodNodesBuffer[source.id * 2], _LodNodesBuffer[source.id * 2 + 1]);
    color = float4(s.rgb, s.alpha);
    if (!InitCenter(modelView, s.center, center))
        return false;
    // Widening the scale widens the covariance, so InitCorner's frustum cull already sees the
    // larger quad of a merged node. The fragment maps uv back into the node's own σ (Gsplat.shader).
    SplatCovariance cov = CalcCovariance(LodQuatToShaderOrder(s.quat), s.scale * LodExtentScale(s.alpha));
    if (!InitCorner(source, cov, center, corner))
        return false;
    return true;
}

#endif
```

`CalcDepthLod.compute`：

```hlsl
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

#define GROUP_SIZE 1024

#pragma kernel CalcDepthLod

// Depth of the selected cut only: _SplatCount is the cut size, _OrderBuffer holds node indices.
uint _SplatCount;
float4x4 _MatrixMV;
StructuredBuffer<uint4> _LodNodesBuffer;
StructuredBuffer<uint> _OrderBuffer;
RWStructuredBuffer<float> _DepthBuffer;

[numthreads(GROUP_SIZE, 1, 1)]
void CalcDepthLod(uint3 id : SV_DispatchThreadID)
{
    uint idx = id.x;
    if (idx >= _SplatCount)
        return;
    float3 pos = asfloat(_LodNodesBuffer[_OrderBuffer[idx] * 2].xyz);
    _DepthBuffer[idx] = mul(_MatrixMV, float4(pos, 1)).z;
}
```

- [ ] **Step 5: `Gsplat.shader` 加 `LOD` 变体**

把

```hlsl
            #pragma multi_compile UNCOMPRESSED SPARK
```

改为

```hlsl
            #pragma multi_compile UNCOMPRESSED SPARK LOD
```

在 `#ifdef SPARK ... #endif` 之后加：

```hlsl
            #ifdef LOD
            #include "GsplatLod.hlsl"
            #endif
```

片元里把

```hlsl
                float falloff = -exp((maxUV - _ScaleFactor * 1.16) * 25 * _ScaleFactor);
                float alpha = (exp(-A * 4.0) + falloff) * i.color.a;
```

替换为

```hlsl
                float falloff = -exp((maxUV - _ScaleFactor * 1.16) * 25 * _ScaleFactor);
                float alpha;
                #ifdef LOD
                if (i.color.a > 1.0)
                {
                    // Merged LoD node (spec D15): colour.a carries D. The quad was widened by k in
                    // GsplatLod.hlsl, so uv = 1 sits at √8·k of the node's σ; z² is in those σ units.
                    // Spark's profile: 1 − (1 − e^{−z²/2})^{exp((D²−1)/e)}.
                    float k = LodExtentScale(i.color.a);
                    float z2 = 8.0 * k * k * A;
                    float power = exp((i.color.a * i.color.a - 1.0) / 2.718281828459045);
                    alpha = 1.0 - pow(max(1.0 - exp(-0.5 * z2), 0.0), power) + falloff;
                }
                else
                #endif
                {
                    alpha = (exp(-A * 4.0) + falloff) * i.color.a;
                }
```

`ClipCorner(corner, color.w)` 不动：D > 1 时它算出的裁剪系数 ≥ 1.17，被 `min(1, …)` 截成 1，不缩四边形。

- [ ] **Step 6: 刷新、跑测试**

执行 §U，再按 §T 跑 `--filter GsdDecodeTests --filter_type testName`。Expected：`GpuDecodeMatchesTheRustDecoder` 与两个 `VariantCompilesForQuestAndEditor` PASS。

若四元数一项对不上而其它都对：先核对 Rust 与 HLSL 解码公式，**不要放宽容差**。若 `VariantCompilesForQuestAndEditor("SPARK")` 失败：说明 SH 拆分改变了 SPARK 变体的编译结果，回 Step 2 核对移动的边界。

- [ ] **Step 7: 提交（子模块）**

```bash
cd /Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat
git add Runtime/Shaders/GsplatSparkSH.hlsl Runtime/Shaders/GsplatSparkSH.hlsl.meta Runtime/Shaders/GsplatSpark.hlsl \
  Runtime/Shaders/GsplatLodDecode.hlsl Runtime/Shaders/GsplatLodDecode.hlsl.meta \
  Runtime/Shaders/GsplatLod.hlsl Runtime/Shaders/GsplatLod.hlsl.meta \
  Runtime/Shaders/CalcDepthLod.compute Runtime/Shaders/CalcDepthLod.compute.meta Runtime/Shaders/Gsplat.shader \
  Tests/Editor/GsdDecodeProbe.compute Tests/Editor/GsdDecodeProbe.compute.meta Tests/Editor/GsdDecodeTests.cs Tests/Editor/GsdDecodeTests.cs.meta
git commit -m "$(cat <<'EOF'
feat(lod): LOD shader variant, ExtSplat decode and cut depth kernel

SH decode moves unchanged into GsplatSparkSH.hlsl, shared by SPARK and
LOD. Merged nodes widen their quad by the D-dependent extent and use
Spark's opacity profile. A GPU probe checks HLSL decode against Rust.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: `GsplatLodAsset`、资源、材质、设置、`.gsd` 导入器

**Files:**
- Create: `Packages/wu.yize.gsplat/Runtime/Lod/GsplatLodAsset.cs`
- Create: `Packages/wu.yize.gsplat/Editor/GsdImporter.cs`
- Create: `Packages/wu.yize.gsplat/Tests/Editor/GsdImporterTests.cs`
- Create（Editor 生成）: `Packages/wu.yize.gsplat/Runtime/Materials/GsplatLod.mat`、`GsplatLod.asset`
- Modify: `Packages/wu.yize.gsplat/Runtime/GsplatAsset.cs`、`GsplatAssetSpark.cs`、`GsplatAssetUncompressed.cs`、`GsplatResource.cs`、`GsplatRendererImpl.cs:65-66`、`GsplatSettings.cs`、`SRP/GsplatURPFeature.cs:67-70,126`、`Editor/GsplatImporter.cs`
- Modify（Editor 升级）: `Assets/Gsplat/Settings/Resources/GsplatSettings.asset`（MR_Base）

**Interfaces:**
- Consumes: `GsdReader.Read`、`GsdData`、`GsdFormatException`；shader 关键字 `LOD`、`CalcDepthLod.compute`。
- Produces:
  - `CompressionMode.Lod`（枚举第 3 个值，材质槽下标 2）。
  - `GsplatAsset.ComputeDepth(CommandBuffer, Matrix4x4, ISorterResource, GsplatResource, uint activeCount)`（新增最后一个参数；SPARK/Uncompressed 忽略它，行为不变）。
  - `public class GsplatLodAsset : GsplatAsset { uint LeafCount; uint4[] Nodes; uint[] PackedSH1, PackedSH2, PackedSH3; uint[] ChildStart; ushort[] ChildCount; void LoadFromGsd(ReadOnlySpan<byte>); }`，`SplatCount` = 节点总数。
  - `public class GsplatResourceLod : GsplatResource { GraphicsBuffer NodesBuffer /*uint4 × 2N*/, PackedSH1Buffer, PackedSH2Buffer, PackedSH3Buffer; }`
  - `GsplatSettings`：`uint LodSplatBudget`、`float LodConeFov0, LodConeFov, LodConeFoveate, LodBehindFoveate`、`float EffectiveOffscreenScale { get; }`。
  - `[ScriptedImporter(1, "gsd")] GsdImporter`，主对象名 = 文件名去扩展名。

- [ ] **Step 1: 写失败的导入测试**

`Tests/Editor/GsdImporterTests.cs`：

```csharp
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Gsplat.Tests
{
    public class GsdImporterTests
    {
        [Test]
        public void ImportsTheFixtureAsALodAsset()
        {
            var expected = GsdFixture.LoadExpected();
            var asset = AssetDatabase.LoadAssetAtPath<GsplatLodAsset>(GsdFixture.GsdPath);
            Assert.IsNotNull(asset, "the .gsd importer did not produce a GsplatLodAsset");
            Assert.AreEqual(expected.nodeCount, asset.SplatCount, "SplatCount counts every node");
            Assert.AreEqual(expected.leafCount, asset.LeafCount);
            Assert.AreEqual(expected.shDegree, asset.SHBands);
            Assert.AreEqual(CompressionMode.Lod, asset.Compression);
            Assert.AreEqual(asset.SplatCount * 2, asset.Nodes.Length);
        }

        [Test]
        public void SettingsProvideTheLodMaterial()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GsplatLodAsset>(GsdFixture.GsdPath);
            Assert.IsNotNull(asset.GsplatMaterial, "GsplatSettings has no material for CompressionMode.Lod");
            Assert.IsNotNull(asset.GsplatMaterial.CalcDepthShader, "Lod material has no depth kernel");
            Assert.IsTrue(asset.Materials[0].IsKeywordEnabled("LOD"), "Lod material must enable the LOD keyword");
        }

        [Test]
        public void LoadFromGsdRejectsCorruptBytes()
        {
            var asset = ScriptableObject.CreateInstance<GsplatLodAsset>();
            try
            {
                var bytes = GsdFixture.Bytes();
                bytes[0] = (byte)'X';
                Assert.Throws<GsdFormatException>(() => asset.LoadFromGsd(bytes));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
```

- [ ] **Step 2: 在 Editor 里建 LOD 材质与 `GsplatMaterial`**

```bash
unity command eval --project-path $R '
var shader = UnityEngine.Shader.Find("Gsplat/Standard");
var mat = new UnityEngine.Material(shader) { name = "GsplatLod" };
mat.DisableKeyword("UNCOMPRESSED"); mat.DisableKeyword("SPARK"); mat.EnableKeyword("LOD");
UnityEditor.AssetDatabase.CreateAsset(mat, "Packages/wu.yize.gsplat/Runtime/Materials/GsplatLod.mat");
var gm = UnityEngine.ScriptableObject.CreateInstance<Gsplat.GsplatMaterial>();
gm.DefaultMaterial = mat;
gm.CalcDepthShader = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.ComputeShader>("Packages/wu.yize.gsplat/Runtime/Shaders/CalcDepthLod.compute");
UnityEditor.AssetDatabase.CreateAsset(gm, "Packages/wu.yize.gsplat/Runtime/Materials/GsplatLod.asset");
UnityEditor.AssetDatabase.SaveAssets();
return mat.IsKeywordEnabled("LOD") + " " + (gm.CalcDepthShader != null);'
```

Expected：`True True`。

- [ ] **Step 3: 枚举与 `ComputeDepth` 签名（`GsplatAsset.cs`）**

`CompressionMode` 改为：

```csharp
    public enum CompressionMode
    {
        Uncompressed,
        Spark,
        /// <summary>A LoD tree from a .gsd file (see <see cref="GsplatLodAsset"/>). Not an import option for .ply/.spz.</summary>
        Lod
    }
```

抽象方法改为：

```csharp
        /// <param name="activeCount">Entries of the order buffer in use this frame. Assets that
        /// depth-sort every uploaded splat ignore it; LoD assets sort only the selected cut.</param>
        public abstract void ComputeDepth(CommandBuffer cmd, Matrix4x4 matrixMv,
            ISorterResource sorterResource, GsplatResource resource, uint activeCount);
```

`GsplatAssetSpark.cs:136` 与 `GsplatAssetUncompressed.cs:97` 的 override 签名同样补上 `, uint activeCount`，方法体不动（它们仍按 `UploadedCount` 派发，行为与改前一致）。

`GsplatRendererImpl.cs:65-66` 改为：

```csharp
        public void ComputeDepth(CommandBuffer cmd, Matrix4x4 matrixMv) =>
            m_gsplatAsset.ComputeDepth(cmd, matrixMv, SorterResource, GsplatResource, m_remainingCount);
```

确认没有别的 override：

```bash
grep -rn "override void ComputeDepth" /Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat/Runtime
```

Expected：只有 Spark、Uncompressed，再加本任务新增的 Lod。

- [ ] **Step 4: `GsplatResourceLod`（追加到 `GsplatResource.cs` 末尾、命名空间内）**

```csharp
    public class GsplatResourceLod : GsplatResource
    {
        /// <summary>Two uint4 per node, uploaded straight from the .gsd Nodes section.</summary>
        public GraphicsBuffer NodesBuffer { get; private set; }
        public GraphicsBuffer PackedSH1Buffer { get; private set; }
        public GraphicsBuffer PackedSH2Buffer { get; private set; }
        public GraphicsBuffer PackedSH3Buffer { get; private set; }

        public GsplatResourceLod(uint nodeCount, byte shBands)
        {
            if (nodeCount == 0)
                return;
            NodesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)nodeCount * 2, sizeof(uint) * 4);
            if (shBands >= 1)
                PackedSH1Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)nodeCount, sizeof(uint) * 2);
            if (shBands >= 2)
                PackedSH2Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)nodeCount, sizeof(uint) * 4);
            if (shBands >= 3)
                PackedSH3Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)nodeCount, sizeof(uint) * 4);
        }

        public override void Dispose()
        {
            NodesBuffer?.Dispose();
            NodesBuffer = null;
            PackedSH1Buffer?.Dispose();
            PackedSH1Buffer = null;
            PackedSH2Buffer?.Dispose();
            PackedSH2Buffer = null;
            PackedSH3Buffer?.Dispose();
            PackedSH3Buffer = null;
        }
    }
```

- [ ] **Step 5: `Runtime/Lod/GsplatLodAsset.cs`**

```csharp
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    /// <summary>
    /// A LoD tree loaded from a <c>.gsd</c> file (spec §4–§7). <see cref="GsplatAsset.SplatCount"/>
    /// counts every node, leaves and merged interiors alike, because every node lives on the GPU;
    /// which of them are drawn each frame is decided by <see cref="GsplatLodDriver"/>.
    /// </summary>
    public class GsplatLodAsset : GsplatAsset
    {
        public override CompressionMode Compression => CompressionMode.Lod;

        public uint LeafCount;
        [HideInInspector] public uint4[] Nodes; // 2 per node, ExtSplat layout
        [HideInInspector] public uint[] PackedSH1; // 2 words per node
        [HideInInspector] public uint[] PackedSH2; // 4 words per node
        [HideInInspector] public uint[] PackedSH3; // 4 words per node
        [HideInInspector] public uint[] ChildStart;
        [HideInInspector] public ushort[] ChildCount;

        static readonly int k_lodNodesBuffer = Shader.PropertyToID("_LodNodesBuffer");
        static readonly int k_packedSH1Buffer = Shader.PropertyToID("_PackedSH1Buffer");
        static readonly int k_packedSH2Buffer = Shader.PropertyToID("_PackedSH2Buffer");
        static readonly int k_packedSH3Buffer = Shader.PropertyToID("_PackedSH3Buffer");
        static readonly int k_splatCount = Shader.PropertyToID("_SplatCount");
        static readonly int k_matrixMv = Shader.PropertyToID("_MatrixMV");
        static readonly int k_depthBuffer = Shader.PropertyToID("_DepthBuffer");
        static readonly int k_orderBuffer = Shader.PropertyToID("_OrderBuffer");

        /// <summary>Fills this asset from .gsd bytes; throws <see cref="GsdFormatException"/> on malformed input.</summary>
        public void LoadFromGsd(ReadOnlySpan<byte> bytes)
        {
            var data = GsdReader.Read(bytes);
            SplatCount = data.NodeCount;
            PrunedSplatCount = 0;
            LeafCount = data.LeafCount;
            SHBands = data.SHDegree;
            var bounds = new Bounds();
            bounds.SetMinMax(data.BoundsMin, data.BoundsMax);
            Bounds = bounds;
            Nodes = data.Nodes;
            PackedSH1 = data.SH1;
            PackedSH2 = data.SH2;
            PackedSH3 = data.SH3;
            ChildStart = data.ChildStart;
            ChildCount = data.ChildCount;
        }

        public override void Allocate()
        {
            Nodes = new uint4[SplatCount * 2];
            PackedSH1 = SHBands >= 1 ? new uint[SplatCount * 2] : Array.Empty<uint>();
            PackedSH2 = SHBands >= 2 ? new uint[SplatCount * 4] : Array.Empty<uint>();
            PackedSH3 = SHBands >= 3 ? new uint[SplatCount * 4] : Array.Empty<uint>();
            ChildStart = new uint[SplatCount];
            ChildCount = new ushort[SplatCount];
        }

        public override void LoadFromPly(string plyPath, ProgressCallback progressCallback = null,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUF, float opacityPruneThreshold = 0f) =>
            throw new NotSupportedException("GsplatLodAsset is built by gsd-build; import the .gsd file, not the .ply.");

        public override GsplatResource CreateResource() => new GsplatResourceLod(SplatCount, SHBands);

        protected override void _UploadData(GsplatResource resource)
        {
            var res = (GsplatResourceLod)resource;
            res.NodesBuffer.SetData(Nodes);
            if (SHBands >= 1)
                res.PackedSH1Buffer.SetData(PackedSH1);
            if (SHBands >= 2)
                res.PackedSH2Buffer.SetData(PackedSH2);
            if (SHBands >= 3)
                res.PackedSH3Buffer.SetData(PackedSH3);
        }

        // The selection addresses nodes by index anywhere in the tree, so a half-uploaded tree is
        // unusable: upload in one piece and report the full count only then.
        protected override async Task _UploadDataAsync(GsplatResource resource)
        {
            await Task.Yield();
            _UploadData(resource);
            resource.UploadedCount = SplatCount;
        }

        public override void SetupMaterialPropertyBlock(MaterialPropertyBlock propertyBlock, GsplatResource resource)
        {
            var res = (GsplatResourceLod)resource;
            propertyBlock.SetBuffer(k_lodNodesBuffer, res.NodesBuffer);
            if (SHBands >= 1)
                propertyBlock.SetBuffer(k_packedSH1Buffer, res.PackedSH1Buffer);
            if (SHBands >= 2)
                propertyBlock.SetBuffer(k_packedSH2Buffer, res.PackedSH2Buffer);
            if (SHBands >= 3)
                propertyBlock.SetBuffer(k_packedSH3Buffer, res.PackedSH3Buffer);
        }

        public override void ComputeDepth(CommandBuffer cmd, Matrix4x4 matrixMv, ISorterResource sorterResource,
            GsplatResource resource, uint activeCount)
        {
            // Only the selected cut is sorted: dispatch by the cut size, not the node count (spec §7).
            if (activeCount == 0)
                return;
            var res = (GsplatResourceLod)resource;
            var cs = GsplatMaterial.CalcDepthShader;
            const int kernelCalcDepthLod = 0;
            cmd.SetComputeIntParam(cs, k_splatCount, (int)activeCount);
            cmd.SetComputeMatrixParam(cs, k_matrixMv, matrixMv);
            cmd.SetComputeBufferParam(cs, kernelCalcDepthLod, k_lodNodesBuffer, res.NodesBuffer);
            cmd.SetComputeBufferParam(cs, kernelCalcDepthLod, k_depthBuffer, sorterResource.InputKeys);
            cmd.SetComputeBufferParam(cs, kernelCalcDepthLod, k_orderBuffer, sorterResource.OrderBuffer);
            cmd.DispatchCompute(cs, kernelCalcDepthLod, (int)GsplatUtils.DivRoundUp(activeCount, 1024), 1, 1);
        }

        public override void InitOrder(ISorterResource sorterResource, GsplatResource resource, bool updateBounds) =>
            throw new InvalidOperationException(
                "GsplatLodAsset: the LoD selection owns the order buffer; InitOrder must never run for it (spec D14).");
    }
}
```

- [ ] **Step 6: `GsplatSettings.cs`**

在类里（`OffscreenScale` 字段之后）加：

```csharp
        [Header("LoD (.gsd assets)")]
        [Tooltip("Most splats a .gsd asset draws per frame. A frame-level budget, not a per-renderer one: " +
                 "at most one LoD renderer may be active at a time (spec D12).")]
        public uint LodSplatBudget;

        [Tooltip("Full-width cone (degrees) around the view direction that keeps full LoD detail.")]
        [Range(0f, 180f)] public float LodConeFov0;

        [Tooltip("Full-width cone (degrees) at whose edge detail has fallen to LodConeFoveate.")]
        [Range(0f, 180f)] public float LodConeFov;

        [Tooltip("Detail scale at the LodConeFov edge.")]
        [Range(0.01f, 1f)] public float LodConeFoveate;

        [Tooltip("Detail scale behind the viewer. 0.2 makes splats behind you about 5x larger.")]
        [Range(0.01f, 1f)] public float LodBehindFoveate;

        // Below this the composite's bilinear upsample stops hiding the loss and splat edges visibly
        // step. Matches the [Range] on OffscreenScale.
        const float k_minOffscreenScale = 0.25f;
        const uint k_defaultLodSplatBudget = 500000;

        /// <summary>
        /// The offscreen scale the URP pass actually renders at. The LoD selection sizes its pixel
        /// threshold from the same value (spec D16), so both read it here.
        /// </summary>
        public float EffectiveOffscreenScale => Mathf.Clamp(OffscreenScale, k_minOffscreenScale, 1f);

        void SetLodDefaults()
        {
            LodSplatBudget = k_defaultLodSplatBudget;
            LodConeFov0 = 90f;
            LodConeFov = 120f;
            LodConeFoveate = 0.4f;
            LodBehindFoveate = 0.2f;
        }
```

`Instance` 里版本梯之外的那段升级块，条件与内容改为：

```csharp
                    var materialCount = Enum.GetValues(typeof(CompressionMode)).Length;
                    if (!settings.CompositeShader || settings.OffscreenScale <= 0f || settings.LodSplatBudget == 0 ||
                        settings.Materials == null || settings.Materials.Length != materialCount)
                    {
                        if (!settings.CompositeShader)
                            settings.CompositeShader = DefaultCompositeShader;
                        // An asset serialised before this field existed reads back as 0, which is
                        // not a resolution. Treat it as "never set" rather than clamping it to the
                        // range minimum, which would silently pick a scale nobody chose.
                        if (settings.OffscreenScale <= 0f)
                            settings.OffscreenScale = k_defaultOffscreenScale;
                        // The LoD fields and the Lod material slot arrived with .gsd support: an older
                        // asset reads them back as 0 and as a Materials array one entry short.
                        if (settings.LodSplatBudget == 0)
                            settings.SetLodDefaults();
                        if (settings.Materials == null || settings.Materials.Length != materialCount)
                            settings.Materials = DefaultMaterials;
                        EditorUtility.SetDirty(settings);
                        AssetDatabase.SaveAssets();
                    }
```

`DefaultMaterials` 里在 Spark 那行之后加：

```csharp
                materials[(int)CompressionMode.Lod] =
                    AssetDatabase.LoadAssetAtPath<GsplatMaterial>(GsplatUtils.k_PackagePath +
                                                                  "Runtime/Materials/GsplatLod.asset");
```

`Reset()` 里 `OffscreenScale = k_defaultOffscreenScale;` 之后加 `SetLodDefaults();`。

- [ ] **Step 7: `GsplatURPFeature.cs` 改用 `EffectiveOffscreenScale`**

删掉 `k_minOffscreenScale` 常量及其上方两行注释（第 67–70 行附近，注释已搬到 `GsplatSettings`），把

```csharp
                var scale = Mathf.Clamp(GsplatSettings.Instance.OffscreenScale, k_minOffscreenScale, 1f);
```

改为

```csharp
                var scale = GsplatSettings.Instance.EffectiveOffscreenScale;
```

再确认没有残留引用：`grep -n k_minOffscreenScale Packages/wu.yize.gsplat/Runtime/SRP/GsplatURPFeature.cs` 应无输出。

- [ ] **Step 8: 导入器**

`Editor/GsdImporter.cs`：

```csharp
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Gsplat.Editor
{
    /// <summary>
    /// Imports <c>.gsd</c> LoD trees written by <c>Tools~/gsd-build</c>. Parsing only: the tree was
    /// built offline, so importing costs a read and a copy.
    /// </summary>
    [ScriptedImporter(1, "gsd")]
    public class GsdImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = ScriptableObject.CreateInstance<GsplatLodAsset>();
            try
            {
                asset.LoadFromGsd(File.ReadAllBytes(ctx.assetPath));
            }
            catch (GsdFormatException e)
            {
                Object.DestroyImmediate(asset);
                ctx.LogImportError($"{ctx.assetPath}: {e.Message}");
                return;
            }

            asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            ctx.AddObjectToAsset("gsplatLodAsset", asset);
            ctx.SetMainObject(asset);
        }
    }
}
```

`Editor/GsplatImporter.cs` 的 `Compression switch` 在 `_ => throw new ArgumentOutOfRangeException()` 之前加：

```csharp
                CompressionMode.Lod => throw new NotSupportedException(
                    ".ply/.spz import supports Uncompressed or Spark; LoD assets come from gsd-build as .gsd files."),
```

- [ ] **Step 9: 刷新、触发设置升级、跑测试**

执行 §U，然后：

```bash
unity command eval --project-path $R 'var s = Gsplat.GsplatSettings.Instance; return s.Materials.Length + " " + s.LodSplatBudget + " " + (s.Materials[2] != null);'
```

Expected：`3 500000 True`。再按 §T 跑整个 `Gsplat.Editor.Tests`：全部 PASS（Task 6、7 的测试加上本任务 3 个）。

- [ ] **Step 10: 提交（子模块 + MR_Base）**

```bash
cd /Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat
git add Runtime/GsplatAsset.cs Runtime/GsplatAssetSpark.cs Runtime/GsplatAssetUncompressed.cs Runtime/GsplatResource.cs \
  Runtime/GsplatRendererImpl.cs Runtime/GsplatSettings.cs Runtime/SRP/GsplatURPFeature.cs \
  Runtime/Lod/GsplatLodAsset.cs Runtime/Lod/GsplatLodAsset.cs.meta \
  Runtime/Materials/GsplatLod.mat Runtime/Materials/GsplatLod.mat.meta Runtime/Materials/GsplatLod.asset Runtime/Materials/GsplatLod.asset.meta \
  Editor/GsdImporter.cs Editor/GsdImporter.cs.meta Editor/GsplatImporter.cs \
  Tests/Editor/GsdImporterTests.cs Tests/Editor/GsdImporterTests.cs.meta Tests/Editor/Fixtures
git commit -m "$(cat <<'EOF'
feat(lod): GsplatLodAsset, .gsd importer and the Lod material slot

Every node is uploaded; ComputeDepth gains the active count so LoD
assets depth-sort only the selected cut. Settings gain the LoD budget
and foveation defaults and share EffectiveOffscreenScale with the URP
pass.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
cd /Users/wwj/Desktop/unity/MR_Base
git add Assets/Gsplat/Settings/Resources/GsplatSettings.asset
git commit -m "$(cat <<'EOF'
chore(gsplat): upgrade GsplatSettings with the LoD budget and Lod material

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: CPU 遍历表与 Burst 遍历 job

**Files:**
- Create: `Packages/wu.yize.gsplat/Runtime/Lod/GsplatLodTree.cs`
- Create: `Packages/wu.yize.gsplat/Runtime/Lod/GsplatLodTraversal.cs`
- Create: `Packages/wu.yize.gsplat/Tests/Editor/LodTestTrees.cs`
- Create: `Packages/wu.yize.gsplat/Tests/Editor/GsplatLodTraversalTests.cs`
- Modify: `Packages/wu.yize.gsplat/package.json`、`Packages/wu.yize.gsplat/Runtime/Gsplat.asmdef`

**Interfaces:**
- Consumes: 节点载荷布局（Task 7 同一契约：`w0.xyz` 中心 f32、`w0.w` 低 16 位 alpha/D、`w1.y` 高 16 位与 `w1.z` 为 ln scale）。
- Produces（命名空间 `Gsplat`）：
  - `public sealed class GsplatLodTree : IDisposable { NativeArray<float3> Center; NativeArray<float> Size; NativeArray<uint> ChildStart; NativeArray<ushort> ChildCount; int NodeCount; GsplatLodTree(uint4[] nodes, uint[] childStart, ushort[] childCount); }`，`Size = 2·max(scale)·max(1, D)`。
  - `public struct GsplatLodView : IEquatable<GsplatLodView> { float3 Origin; float3 Forward; float PixelScaleLimit; int Budget; float ConeDot0, ConeDot, ConeFoveate, BehindFoveate; static GsplatLodView Create(float3 origin, float3 forward, float pixelScaleLimit, int budget, float coneFov0Degrees, float coneFovDegrees, float coneFoveate, float behindFoveate); }`（相等 = 所有字段精确相等）。
  - `public sealed class GsplatLodScratch : IDisposable { int Budget; NativeArray<float> HeapKey; NativeArray<uint> HeapNode; NativeArray<uint> Output; NativeArray<int> Count; GsplatLodScratch(int budget); }`
  - `[BurstCompile(CompileSynchronously = true)] public struct GsplatLodTraverseJob : IJob`；`public static class GsplatLodTraversal { static GsplatLodTraverseJob CreateJob(GsplatLodTree, in GsplatLodView, GsplatLodScratch); }`。结果在 `scratch.Output[0 .. scratch.Count[0])`。
  - 测试辅助：`static class LodTestTrees { static GsplatLodTree Complete(int branching, int depth, out int[] parent); static GsplatLodView View(float3 origin, int budget, float pixelScaleLimit = 0.001f); }`

- [ ] **Step 1: 加 Burst 依赖**

`package.json` 的 `dependencies` 改为：

```json
  "dependencies": {
    "com.unity.mathematics": "1.3.2",
    "com.unity.burst": "1.8.29"
  }
```

`Runtime/Gsplat.asmdef` 的 `references` 末尾加 `"Unity.Burst"`（在 `"Gsplat.ZstdSharp"` 之后）。

- [ ] **Step 2: 写测试树与失败的遍历测试**

`Tests/Editor/LodTestTrees.cs`：

```csharp
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using Unity.Mathematics;

namespace Gsplat.Tests
{
    static class LodTestTrees
    {
        /// <summary>
        /// A complete tree in breadth-first order (children contiguous and after their parent, as
        /// in a .gsd): the root has size 8 at the origin, every level splits each node into
        /// <paramref name="branching"/> smaller ones spread along X. That is the shape a LoD tree
        /// has — deeper nodes are smaller and together cover their parent.
        /// </summary>
        public static GsplatLodTree Complete(int branching, int depth, out int[] parent)
        {
            var centers = new List<float3> { float3.zero };
            var sizes = new List<float> { 8f };
            var parents = new List<int> { -1 };
            int levelStart = 0, levelCount = 1;
            for (var level = 1; level <= depth; ++level)
            {
                var nextStart = centers.Count;
                for (var p = levelStart; p < levelStart + levelCount; ++p)
                for (var c = 0; c < branching; ++c)
                {
                    var offset = (c - (branching - 1) * 0.5f) * sizes[p] / branching;
                    centers.Add(centers[p] + new float3(offset, 0f, 0f));
                    sizes.Add(sizes[p] / branching);
                    parents.Add(p);
                }

                levelStart = nextStart;
                levelCount *= branching;
            }

            var n = centers.Count;
            var childStart = new uint[n];
            var childCount = new ushort[n];
            for (var i = 1; i < n; ++i)
            {
                var p = parents[i];
                if (childCount[p] == 0)
                    childStart[p] = (uint)i;
                childCount[p]++;
            }

            var nodes = new uint4[n * 2];
            for (var i = 0; i < n; ++i)
                (nodes[i * 2], nodes[i * 2 + 1]) = PackNode(centers[i], sizes[i]);
            parent = parents.ToArray();
            return new GsplatLodTree(nodes, childStart, childCount);
        }

        /// <summary>A node whose derived Size is <paramref name="size"/>: scale = size / 2 on every axis, opacity 0.5.</summary>
        public static (uint4, uint4) PackNode(float3 center, float size)
        {
            var lnScale = math.f32tof16(math.log(size * 0.5f));
            var w0 = new uint4(math.asuint(center), math.f32tof16(0.5f));
            var w1 = new uint4(0u, lnScale << 16, lnScale | (lnScale << 16), 0u);
            return (w0, w1);
        }

        /// <summary>Looking down +Z with Spark's default foveation.</summary>
        public static GsplatLodView View(float3 origin, int budget, float pixelScaleLimit = 0.001f) =>
            GsplatLodView.Create(origin, new float3(0f, 0f, 1f), pixelScaleLimit, budget, 90f, 120f, 0.4f, 0.2f);
    }
}
```

`Tests/Editor/GsplatLodTraversalTests.cs`：

```csharp
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Jobs;
using Unity.Mathematics;

namespace Gsplat.Tests
{
    public class GsplatLodTraversalTests
    {
        GsplatLodTree m_tree;
        int[] m_parent;

        [SetUp] public void SetUp() => m_tree = LodTestTrees.Complete(4, 4, out m_parent); // 341 nodes, 256 leaves
        [TearDown] public void TearDown() => m_tree.Dispose();

        uint[] Select(GsplatLodView view)
        {
            using var scratch = new GsplatLodScratch(view.Budget);
            GsplatLodTraversal.CreateJob(m_tree, view, scratch).Run();
            return scratch.Output.GetSubArray(0, scratch.Count[0]).ToArray();
        }

        [TestCase(1)] [TestCase(2)] [TestCase(5)] [TestCase(17)] [TestCase(100)] [TestCase(341)] [TestCase(1000)]
        public void OutputNeverExceedsTheBudget(int budget)
        {
            var output = Select(LodTestTrees.View(new float3(0, 0, -2), budget, 0f));
            Assert.That(output.Length, Is.InRange(1, budget));
        }

        [Test]
        public void OutputIsAlwaysAValidCut()
        {
            foreach (var budget in new[] { 1, 7, 50, 400 })
            foreach (var distance in new[] { 1f, 10f, 100f })
                AssertValidCut(Select(LodTestTrees.View(new float3(0, 0, -distance), budget)), $"budget {budget} distance {distance}");
        }

        [Test]
        public void AFartherViewSelectsFewerNodes()
        {
            var near = Select(LodTestTrees.View(new float3(0, 0, -2), 1000, 0.01f)).Length;
            var far = Select(LodTestTrees.View(new float3(0, 0, -100), 1000, 0.01f)).Length;
            Assert.Less(far, near);
        }

        [TestCase(341)]
        [TestCase(3410)]
        public void UnboundedBudgetAndZeroLimitSelectEveryLeaf(int budget)
        {
            var output = Select(LodTestTrees.View(new float3(0, 0, -2), budget, 0f));
            var leaves = Enumerable.Range(0, m_tree.NodeCount).Where(i => m_tree.ChildCount[i] == 0).Select(i => (uint)i);
            CollectionAssert.AreEquivalent(leaves, output);
        }

        [Test]
        public void BudgetOfOneSelectsTheRoot() =>
            CollectionAssert.AreEqual(new uint[] { 0 }, Select(LodTestTrees.View(new float3(0, 0, -2), 1, 0f)));

        [Test]
        public void CameraOnANodeCentreStaysFinite()
        {
            var output = Select(LodTestTrees.View(float3.zero, 64, 0f)); // the root's centre
            Assert.That(output.Length, Is.InRange(1, 64));
            AssertValidCut(output, "camera at the root centre");
        }

        void AssertValidCut(uint[] output, string context)
        {
            var selected = new HashSet<uint>(output);
            Assert.AreEqual(output.Length, selected.Count, $"{context}: a node was output twice");
            for (var leaf = 0; leaf < m_parent.Length; ++leaf)
            {
                if (m_tree.ChildCount[leaf] != 0)
                    continue;
                var covering = 0;
                for (var n = leaf; n >= 0; n = m_parent[n])
                    if (selected.Contains((uint)n))
                        ++covering;
                Assert.AreEqual(1, covering, $"{context}: leaf {leaf} is covered {covering} times");
            }
        }
    }
}
```

执行 §U。Expected：编译失败，缺 `GsplatLodTree`、`GsplatLodView` 等类型。

- [ ] **Step 3: `Runtime/Lod/GsplatLodTree.cs`**

```csharp
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Gsplat
{
    /// <summary>
    /// The CPU-side traversal table (spec §6): centre and feature size per node plus the child
    /// ranges. Centre and size are derived from the node payload at bind time, never stored in the
    /// file, so they cannot disagree with what the GPU draws (D4).
    /// </summary>
    public sealed class GsplatLodTree : IDisposable
    {
        public NativeArray<float3> Center;
        public NativeArray<float> Size;
        public NativeArray<uint> ChildStart;
        public NativeArray<ushort> ChildCount;

        public int NodeCount => Center.Length;

        public GsplatLodTree(uint4[] nodes, uint[] childStart, ushort[] childCount)
        {
            var n = childStart.Length;
            if (childCount.Length != n || nodes.Length != n * 2)
                throw new ArgumentException("nodes must hold two uint4 per node, and childStart/childCount one entry per node");

            ChildStart = new NativeArray<uint>(childStart, Allocator.Persistent);
            ChildCount = new NativeArray<ushort>(childCount, Allocator.Persistent);
            Center = new NativeArray<float3>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            Size = new NativeArray<float>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            // ponytail: copies the payload once more at bind time (~2× node bytes, transient); pin the
            // managed array instead if that spike ever matters on device.
            using var payload = new NativeArray<uint4>(nodes, Allocator.TempJob);
            new DeriveJob { Nodes = payload, Center = Center, Size = Size }.Schedule(n, 4096).Complete();
        }

        [BurstCompile(CompileSynchronously = true)]
        struct DeriveJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<uint4> Nodes;
            [WriteOnly] public NativeArray<float3> Center;
            [WriteOnly] public NativeArray<float> Size;

            public void Execute(int i)
            {
                var w0 = Nodes[i * 2];
                var w1 = Nodes[i * 2 + 1];
                Center[i] = math.asfloat(w0.xyz);
                var alphaOrD = math.f16tof32(w0.w & 0xFFFFu);
                var lnScale = new float3(math.f16tof32(w1.y >> 16), math.f16tof32(w1.z & 0xFFFFu), math.f16tof32(w1.z >> 16));
                // Spark's feature_size: 2 · max(scale) · max(1, D).
                Size[i] = 2f * math.cmax(math.exp(lnScale)) * math.max(1f, alphaOrD);
            }
        }

        public void Dispose()
        {
            if (Center.IsCreated) Center.Dispose();
            if (Size.IsCreated) Size.Dispose();
            if (ChildStart.IsCreated) ChildStart.Dispose();
            if (ChildCount.IsCreated) ChildCount.Dispose();
        }
    }
}
```

- [ ] **Step 4: `Runtime/Lod/GsplatLodTraversal.cs`**

```csharp
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Gsplat
{
    /// <summary>Everything one traversal depends on, in the model's space.</summary>
    public struct GsplatLodView : IEquatable<GsplatLodView>
    {
        public float3 Origin;
        public float3 Forward;
        /// <summary>Angular size of one target pixel: nodes smaller than this are never refined.</summary>
        public float PixelScaleLimit;
        public int Budget;
        public float ConeDot0;
        public float ConeDot;
        public float ConeFoveate;
        public float BehindFoveate;

        public static GsplatLodView Create(float3 origin, float3 forward, float pixelScaleLimit, int budget,
            float coneFov0Degrees, float coneFovDegrees, float coneFoveate, float behindFoveate)
        {
            if (budget < 1)
                throw new ArgumentOutOfRangeException(nameof(budget), budget, "a LoD cut needs room for at least the root");
            var coneDot0 = coneFov0Degrees > 0f ? math.cos(math.radians(0.5f * math.clamp(coneFov0Degrees, 0f, 180f))) : 1f;
            var coneDot = coneFovDegrees > 0f ? math.cos(math.radians(0.5f * math.clamp(coneFovDegrees, 0f, 180f))) : 1f;
            return new GsplatLodView
            {
                Origin = origin,
                Forward = math.normalizesafe(forward, new float3(0f, 0f, 1f)),
                PixelScaleLimit = pixelScaleLimit,
                Budget = budget,
                ConeDot0 = coneDot0,
                ConeDot = math.min(coneDot, coneDot0),
                ConeFoveate = coneFoveate,
                BehindFoveate = behindFoveate,
            };
        }

        // Exact on purpose (spec §6): any change of pose reschedules, a still view never does.
        public bool Equals(GsplatLodView other) =>
            Origin.Equals(other.Origin) && Forward.Equals(other.Forward) && PixelScaleLimit == other.PixelScaleLimit &&
            Budget == other.Budget && ConeDot0 == other.ConeDot0 && ConeDot == other.ConeDot &&
            ConeFoveate == other.ConeFoveate && BehindFoveate == other.BehindFoveate;

        public override bool Equals(object obj) => obj is GsplatLodView other && Equals(other);
        public override int GetHashCode() => Origin.GetHashCode() ^ (Forward.GetHashCode() * 397) ^ Budget;
    }

    /// <summary>Working memory for traversals of one budget; the frontier and the output never exceed it.</summary>
    public sealed class GsplatLodScratch : IDisposable
    {
        public readonly int Budget;
        public NativeArray<float> HeapKey;
        public NativeArray<uint> HeapNode;
        public NativeArray<uint> Output;
        public NativeArray<int> Count;

        public GsplatLodScratch(int budget)
        {
            if (budget < 1)
                throw new ArgumentOutOfRangeException(nameof(budget), budget, "budget must be at least 1");
            Budget = budget;
            HeapKey = new NativeArray<float>(budget, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            HeapNode = new NativeArray<uint>(budget, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            Output = new NativeArray<uint>(budget, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            Count = new NativeArray<int>(1, Allocator.Persistent);
        }

        public void Dispose()
        {
            if (HeapKey.IsCreated) HeapKey.Dispose();
            if (HeapNode.IsCreated) HeapNode.Dispose();
            if (Output.IsCreated) Output.Dispose();
            if (Count.IsCreated) Count.Dispose();
        }
    }

    public static class GsplatLodTraversal
    {
        public static GsplatLodTraverseJob CreateJob(GsplatLodTree tree, in GsplatLodView view, GsplatLodScratch scratch)
        {
            if (view.Budget > scratch.Budget)
                throw new ArgumentException($"view budget {view.Budget} exceeds scratch capacity {scratch.Budget}");
            return new GsplatLodTraverseJob
            {
                Center = tree.Center,
                Size = tree.Size,
                ChildStart = tree.ChildStart,
                ChildCount = tree.ChildCount,
                View = view,
                HeapKey = scratch.HeapKey,
                HeapNode = scratch.HeapNode,
                Output = scratch.Output,
                Count = scratch.Count,
            };
        }
    }

    /// <summary>
    /// Budgeted LoD cut (spec §6): a port of Spark's <c>lod_tree::traverse_lod_trees</c> for one
    /// tree. The node largest on screen is refined first, so when the budget runs out the detail is
    /// spread evenly; nodes below a pixel are never refined. O(N log N) in the budget, independent
    /// of the tree size.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct GsplatLodTraverseJob : IJob
    {
        [ReadOnly] public NativeArray<float3> Center;
        [ReadOnly] public NativeArray<float> Size;
        [ReadOnly] public NativeArray<uint> ChildStart;
        [ReadOnly] public NativeArray<ushort> ChildCount;
        public GsplatLodView View;
        public NativeArray<float> HeapKey;
        public NativeArray<uint> HeapNode;
        [WriteOnly] public NativeArray<uint> Output;
        [WriteOnly] public NativeArray<int> Count;

        public void Execute()
        {
            int heapSize = 0, outCount = 0, inCut = 1; // inCut = frontier + output = splats this cut draws
            Push(ref heapSize, PixelScale(0), 0u);
            while (heapSize > 0)
            {
                var node = HeapNode[0];
                if (HeapKey[0] <= View.PixelScaleLimit)
                    break;
                int children = ChildCount[(int)node];
                if (children == 0)
                {
                    Pop(ref heapSize);
                    Output[outCount++] = node;
                    continue;
                }

                var next = inCut - 1 + children;
                if (next > View.Budget)
                    break;
                Pop(ref heapSize);
                var first = ChildStart[(int)node];
                for (var c = first; c < first + (uint)children; ++c)
                {
                    var scale = PixelScale(c);
                    if (scale <= View.PixelScaleLimit)
                        Output[outCount++] = c;
                    else
                        Push(ref heapSize, scale, c);
                }

                inCut = next;
            }

            for (var i = 0; i < heapSize; ++i)
                Output[outCount++] = HeapNode[i];
            Count[0] = outCount;
        }

        float PixelScale(uint node)
        {
            var delta = Center[(int)node] - View.Origin;
            var distance = math.max(math.length(delta), 1e-6f);
            var scale = Size[(int)node] / distance;
            var forward = math.dot(delta, View.Forward);
            float foveate;
            if (forward <= 0f)
            {
                foveate = View.BehindFoveate;
            }
            else
            {
                var dot = forward / distance;
                if (dot >= View.ConeDot0)
                    foveate = 1f;
                else if (dot >= View.ConeDot)
                    foveate = View.ConeFoveate + (1f - View.ConeFoveate) * (dot - View.ConeDot) / (View.ConeDot0 - View.ConeDot);
                else
                    foveate = View.BehindFoveate + (View.ConeFoveate - View.BehindFoveate) * dot / View.ConeDot;
            }

            return scale * foveate;
        }

        void Push(ref int size, float key, uint node)
        {
            var i = size++;
            while (i > 0)
            {
                var parent = (i - 1) >> 1;
                if (HeapKey[parent] >= key)
                    break;
                HeapKey[i] = HeapKey[parent];
                HeapNode[i] = HeapNode[parent];
                i = parent;
            }

            HeapKey[i] = key;
            HeapNode[i] = node;
        }

        void Pop(ref int size)
        {
            --size;
            var lastKey = HeapKey[size];
            var lastNode = HeapNode[size];
            var i = 0;
            while (true)
            {
                var child = 2 * i + 1;
                if (child >= size)
                    break;
                if (child + 1 < size && HeapKey[child + 1] > HeapKey[child])
                    ++child;
                if (HeapKey[child] <= lastKey)
                    break;
                HeapKey[i] = HeapKey[child];
                HeapNode[i] = HeapNode[child];
                i = child;
            }

            if (size > 0)
            {
                HeapKey[i] = lastKey;
                HeapNode[i] = lastNode;
            }
        }
    }
}
```

- [ ] **Step 5: 刷新、跑测试**

执行 §U，按 §T 跑 `--filter GsplatLodTraversalTests --filter_type testName`。Expected：全部 PASS（7 个参数化 + 5 个）。`OutputIsAlwaysAValidCut` 失败时优先检查 `inCut` 的增减与"放不下就 break"是否照搬了 Spark 的顺序。

- [ ] **Step 6: 提交（子模块）**

```bash
cd /Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat
git add package.json Runtime/Gsplat.asmdef Runtime/Lod/GsplatLodTree.cs Runtime/Lod/GsplatLodTree.cs.meta \
  Runtime/Lod/GsplatLodTraversal.cs Runtime/Lod/GsplatLodTraversal.cs.meta \
  Tests/Editor/LodTestTrees.cs Tests/Editor/LodTestTrees.cs.meta Tests/Editor/GsplatLodTraversalTests.cs Tests/Editor/GsplatLodTraversalTests.cs.meta
git commit -m "$(cat <<'EOF'
feat(lod): Burst traversal of the budgeted LoD cut

Port of Spark's traverse_lod_trees for one tree: largest-on-screen node
refined first, never past the budget or below a pixel. The traversal
table is derived from the node payload at bind time.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: 异步调度（`GsplatLodSelector`）

**Files:**
- Create: `Packages/wu.yize.gsplat/Runtime/Lod/GsplatLodSelector.cs`
- Create: `Packages/wu.yize.gsplat/Tests/Editor/GsplatLodSelectorTests.cs`

**Interfaces:**
- Consumes: `GsplatLodTree`、`GsplatLodView`、`GsplatLodScratch`、`GsplatLodTraversal.CreateJob`。
- Produces: `public sealed class GsplatLodSelector : IDisposable { GsplatLodSelector(GsplatLodTree tree, int budget); int Budget; int LastLatencyFrames; NativeArray<uint> RunNow(in GsplatLodView); bool TrySchedule(in GsplatLodView, int frame); bool TryComplete(int frame, out NativeArray<uint> indices); double MeasureMilliseconds(in GsplatLodView); }`。返回的 `NativeArray` 是内部 scratch 的子视图，只在下一次调度前有效。selector 不拥有 tree。

- [ ] **Step 1: 写失败的测试**

```csharp
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Gsplat.Tests
{
    public class GsplatLodSelectorTests
    {
        GsplatLodTree m_tree;

        [SetUp] public void SetUp() => m_tree = LodTestTrees.Complete(4, 4, out _);
        [TearDown] public void TearDown() => m_tree.Dispose();

        static GsplatLodView View(float z) => LodTestTrees.View(new float3(0f, 0f, z), 64);

        [Test]
        public void RunNowPublishesSynchronously()
        {
            using var selector = new GsplatLodSelector(m_tree, 64);
            Assert.That(selector.RunNow(View(-10f)).Length, Is.InRange(1, 64));
        }

        [Test]
        public void AScheduledTraversalPublishesOnceComplete()
        {
            using var selector = new GsplatLodSelector(m_tree, 64);
            Assert.IsTrue(selector.TrySchedule(View(-10f), 1));
            Assert.That(WaitFor(selector, 3).Length, Is.InRange(1, 64));
            Assert.AreEqual(2, selector.LastLatencyFrames);
        }

        [Test]
        public void AStillViewIsNotScheduledAgain()
        {
            using var selector = new GsplatLodSelector(m_tree, 64);
            Assert.IsTrue(selector.TrySchedule(View(-10f), 1));
            WaitFor(selector, 1);
            Assert.IsFalse(selector.TrySchedule(View(-10f), 2), "same view must not reschedule");
            Assert.IsTrue(selector.TrySchedule(View(-11f), 3), "a moved view must reschedule");
            WaitFor(selector, 3);
        }

        [Test]
        public void SchedulingWhileATraversalIsInFlightIsRefused()
        {
            using var selector = new GsplatLodSelector(m_tree, 64);
            Assert.IsTrue(selector.TrySchedule(View(-10f), 1));
            Assert.IsFalse(selector.TrySchedule(View(-20f), 1));
            WaitFor(selector, 1);
        }

        [Test]
        public void DisposeWithATraversalInFlightIsSafe()
        {
            var selector = new GsplatLodSelector(m_tree, 64);
            selector.TrySchedule(View(-10f), 1);
            Assert.DoesNotThrow(selector.Dispose);
        }

        [Test]
        public void MeasureDoesNotDisturbTheTraversalInFlight()
        {
            using var selector = new GsplatLodSelector(m_tree, 64);
            selector.TrySchedule(View(-10f), 1);
            Assert.GreaterOrEqual(selector.MeasureMilliseconds(View(-5f)), 0.0);
            Assert.That(WaitFor(selector, 2).Length, Is.InRange(1, 64));
        }

        static NativeArray<uint> WaitFor(GsplatLodSelector selector, int frame)
        {
            var watch = Stopwatch.StartNew();
            NativeArray<uint> indices;
            while (!selector.TryComplete(frame, out indices))
            {
                if (watch.Elapsed.TotalSeconds > 5)
                    Assert.Fail("traversal never completed");
                Thread.Yield();
            }

            return indices;
        }
    }
}
```

执行 §U。Expected：缺 `GsplatLodSelector` 的编译错误。

- [ ] **Step 2: `Runtime/Lod/GsplatLodSelector.cs`**

```csharp
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;

namespace Gsplat
{
    /// <summary>
    /// One LoD traversal in flight at a time (spec §6). <see cref="TrySchedule"/> starts one when
    /// idle and the view changed; <see cref="TryComplete"/> publishes it once done. Neither blocks —
    /// only <see cref="RunNow"/> does, and it is meant for bind time only (D10). The GPU keeps
    /// drawing the previous cut until a new one is published.
    /// </summary>
    public sealed class GsplatLodSelector : IDisposable
    {
        readonly GsplatLodTree m_tree;
        readonly GsplatLodScratch m_scratch;
        JobHandle m_handle;
        bool m_running;
        bool m_hasView;
        GsplatLodView m_lastView;
        int m_scheduledFrame;

        public int Budget => m_scratch.Budget;

        /// <summary>Frames between the last scheduled traversal and its publication.</summary>
        public int LastLatencyFrames { get; private set; }

        public GsplatLodSelector(GsplatLodTree tree, int budget)
        {
            m_tree = tree;
            m_scratch = new GsplatLodScratch(budget);
        }

        public NativeArray<uint> RunNow(in GsplatLodView view)
        {
            CompletePending();
            GsplatLodTraversal.CreateJob(m_tree, view, m_scratch).Run();
            m_lastView = view;
            m_hasView = true;
            LastLatencyFrames = 0;
            return Result();
        }

        public bool TrySchedule(in GsplatLodView view, int frame)
        {
            if (m_running || (m_hasView && view.Equals(m_lastView)))
                return false;
            m_handle = GsplatLodTraversal.CreateJob(m_tree, view, m_scratch).Schedule();
            JobHandle.ScheduleBatchedJobs();
            m_running = true;
            m_lastView = view;
            m_hasView = true;
            m_scheduledFrame = frame;
            return true;
        }

        /// <summary>Publishes the traversal in flight if it has finished; the array is valid until the next schedule.</summary>
        public bool TryComplete(int frame, out NativeArray<uint> indices)
        {
            indices = default;
            if (!m_running || !m_handle.IsCompleted)
                return false;
            m_handle.Complete();
            m_running = false;
            LastLatencyFrames = frame - m_scheduledFrame;
            indices = Result();
            return true;
        }

        /// <summary>
        /// Times one synchronous traversal of <paramref name="view"/> in scratch memory of its own, so
        /// the traversal in flight is untouched. For the bench; stalls the calling thread.
        /// </summary>
        public double MeasureMilliseconds(in GsplatLodView view)
        {
            using var scratch = new GsplatLodScratch(view.Budget);
            var job = GsplatLodTraversal.CreateJob(m_tree, view, scratch);
            var watch = Stopwatch.StartNew();
            job.Run();
            return watch.Elapsed.TotalMilliseconds;
        }

        NativeArray<uint> Result() => m_scratch.Output.GetSubArray(0, m_scratch.Count[0]);

        void CompletePending()
        {
            if (!m_running)
                return;
            m_handle.Complete();
            m_running = false;
        }

        public void Dispose()
        {
            CompletePending();
            m_scratch.Dispose();
        }
    }
}
```

- [ ] **Step 3: 刷新、跑测试**

执行 §U，§T 跑 `--filter GsplatLodSelectorTests --filter_type testName`。Expected：6 个 PASS，console 无 "A Native Collection has not been disposed" 之类泄漏警告。

- [ ] **Step 4: 提交（子模块）**

```bash
cd /Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat
git add Runtime/Lod/GsplatLodSelector.cs Runtime/Lod/GsplatLodSelector.cs.meta Tests/Editor/GsplatLodSelectorTests.cs Tests/Editor/GsplatLodSelectorTests.cs.meta
git commit -m "$(cat <<'EOF'
feat(lod): schedule LoD traversals without blocking the main thread

One traversal in flight; reschedule only when the view changed; bind
time runs one synchronously; dispose completes the job first.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 11: 渲染器集成（driver、renderer、sorter）

**Files:**
- Create: `Packages/wu.yize.gsplat/Runtime/Lod/GsplatLodDriver.cs`
- Create: `Packages/wu.yize.gsplat/Tests/Editor/GsplatLodRendererTests.cs`
- Modify: `Packages/wu.yize.gsplat/Runtime/GsplatRendererImpl.cs`、`GsplatRenderer.cs`、`GsplatSorter.cs`

**Interfaces:**
- Consumes: Task 8–10 全部；`ISorterResource.OrderBuffer/Initialized`；`GsplatSettings.LodSplatBudget/EffectiveOffscreenScale/LodCone*`；`GsplatSorter.DeferDraws`。
- Produces:
  - `public sealed class GsplatLodDriver : IDisposable { static int LiveCount; int Budget; int LastLatencyFrames; GsplatLodDriver(GsplatLodAsset, int budget); int Update(Camera, Transform model, ISorterResource, int frame) /* 新集合大小，无新集合返回 -1 */; double MeasureTraversalMilliseconds(Camera, Transform); static GsplatLodView BuildView(Camera, Transform model, int budget); }`
  - `GsplatRendererImpl`：`bool IsLod`、`int LodLatencyFrames`、`void UpdateLod(Camera, Transform, int frame)`、`double MeasureLodTraversalMs(Camera, Transform)`。
  - `GsplatRenderer`：`bool IsLod`、`int LodLatencyFrames`、`double MeasureLodTraversalMs()`（供 bench）。

- [ ] **Step 1: 写失败的集成测试**

```csharp
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Gsplat.Tests
{
    public class GsplatLodRendererTests
    {
        GsplatLodAsset m_asset;
        uint m_savedBudget;
        readonly List<GameObject> m_objects = new();

        [SetUp]
        public void SetUp()
        {
            m_asset = AssetDatabase.LoadAssetAtPath<GsplatLodAsset>(GsdFixture.GsdPath);
            m_savedBudget = GsplatSettings.Instance.LodSplatBudget;
            GsplatSettings.Instance.LodSplatBudget = 4;
            var camera = Create("TestCamera");
            camera.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 0f, -20f);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in m_objects)
                if (go)
                    Object.DestroyImmediate(go);
            m_objects.Clear();
            GsplatSettings.Instance.LodSplatBudget = m_savedBudget;
        }

        GameObject Create(string name)
        {
            var go = new GameObject(name);
            m_objects.Add(go);
            return go;
        }

        GsplatRenderer CreateRenderer(string name)
        {
            var renderer = Create(name).AddComponent<GsplatRenderer>();
            renderer.GsplatAsset = m_asset;
            return renderer;
        }

        [Test]
        public void PublishesACutWithinTheBudgetAndFlagsASort()
        {
            var renderer = CreateRenderer("lod");
            renderer.Update();
            Assert.IsTrue(renderer.IsLod);
            Assert.That(renderer.RemainingCount, Is.InRange(1u, 4u));
            Assert.IsTrue(renderer.ComputeSortRequired, "a fresh cut must be sorted the frame it lands (D11)");
            Assert.IsTrue(renderer.SorterResource.Initialized, "the cut is the payload; the sort must not identity-fill it (D14)");
        }

        [Test]
        public void ChangingTheBudgetRebindsWithTheNewCapacity()
        {
            var renderer = CreateRenderer("lod");
            renderer.Update();
            GsplatSettings.Instance.LodSplatBudget = 2;
            renderer.Update();
            Assert.That(renderer.RemainingCount, Is.InRange(1u, 2u));
            Assert.AreEqual(2, renderer.SorterResource.OrderBuffer.count);
            Assert.AreEqual(1, GsplatLodDriver.LiveCount);
        }

        [Test]
        public void ASecondLodRendererRefusesToRender()
        {
            var first = CreateRenderer("first");
            first.Update();
            LogAssert.Expect(LogType.Error, new Regex("at most one active LoD renderer"));
            var second = CreateRenderer("second");
            second.Update();
            Assert.Greater(first.RemainingCount, 0u);
            Assert.AreEqual(0u, second.RemainingCount);
        }

        [Test]
        public void DestroyingTheRendererReleasesItsDriver()
        {
            var renderer = CreateRenderer("lod");
            renderer.Update();
            Assert.AreEqual(1, GsplatLodDriver.LiveCount);
            Object.DestroyImmediate(renderer.gameObject);
            Assert.AreEqual(0, GsplatLodDriver.LiveCount);
        }

        [Test]
        public void CutoutsAreReportedNotSilentlyApplied()
        {
            var renderer = CreateRenderer("lod");
            var cutout = Create("cutout").AddComponent<GsplatCutout>();
            cutout.transform.SetParent(renderer.transform);
            // GsplatCutout registers itself in OnEnable, which edit mode does not call for it.
            GsplatCutout.m_RegisteredCutouts.Add(cutout);
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("do not support cutouts"));
                renderer.Update();
            }
            finally
            {
                GsplatCutout.m_RegisteredCutouts.Remove(cutout);
            }
        }

        [Test]
        public void UniformScaleIsInvisibleToTheSelection()
        {
            var camera = Create("ViewCamera").AddComponent<Camera>();
            var model = Create("Model").transform;

            camera.transform.position = new Vector3(0f, 0f, -10f);
            var plain = GsplatLodDriver.BuildView(camera, model, 64);

            model.localScale = Vector3.one * 2f;
            camera.transform.position = new Vector3(0f, 0f, -20f);
            var scaled = GsplatLodDriver.BuildView(camera, model, 64);

            Assert.AreEqual(plain.Origin.z, scaled.Origin.z, 1e-5f);
            Assert.AreEqual(plain.PixelScaleLimit, scaled.PixelScaleLimit);
        }

        [Test]
        public void AMissingMainCameraIsReported()
        {
            Object.DestroyImmediate(m_objects[0]);
            Assume.That(Camera.main == null, "another MainCamera exists in the open scene");
            var renderer = CreateRenderer("lod");
            LogAssert.Expect(LogType.Error, new Regex("needs a camera tagged MainCamera"));
            renderer.Update();
            Assert.AreEqual(0u, renderer.RemainingCount);
        }
    }
}
```

执行 §U。Expected：缺 `IsLod`、`GsplatLodDriver` 的编译错误。

- [ ] **Step 2: `Runtime/Lod/GsplatLodDriver.cs`**

```csharp
// Copyright (c) 2026 wwj
// SPDX-License-Identifier: MIT

using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR;

namespace Gsplat
{
    /// <summary>
    /// Binds one renderer's <see cref="GsplatLodAsset"/> to its order buffer (spec §6–§7): builds the
    /// traversal table, runs the selection, uploads each published cut. For LoD assets the order
    /// buffer is written here and nowhere else (D14).
    /// </summary>
    public sealed class GsplatLodDriver : IDisposable
    {
        /// <summary>Live drivers across all renderers. The budget is frame-level, so at most one may exist (D12).</summary>
        public static int LiveCount { get; private set; }

        readonly GsplatLodTree m_tree;
        readonly GsplatLodSelector m_selector;
        bool m_primed;
        bool m_disposed;

        public int Budget => m_selector.Budget;
        public int LastLatencyFrames => m_selector.LastLatencyFrames;

        public GsplatLodDriver(GsplatLodAsset asset, int budget)
        {
            m_tree = new GsplatLodTree(asset.Nodes, asset.ChildStart, asset.ChildCount);
            m_selector = new GsplatLodSelector(m_tree, budget);
            ++LiveCount;
        }

        /// <summary>
        /// Advances the selection one frame. Returns the size of a cut uploaded into
        /// <paramref name="sorter"/>'s order buffer this frame, or -1 when nothing new was published.
        /// </summary>
        public int Update(Camera camera, Transform model, ISorterResource sorter, int frame)
        {
            var view = BuildView(camera, model, Budget);
            if (!m_primed)
            {
                m_primed = true;
                return Upload(m_selector.RunNow(view), sorter);
            }

            var published = m_selector.TryComplete(frame, out var indices) ? Upload(indices, sorter) : -1;
            m_selector.TrySchedule(view, frame);
            return published;
        }

        public double MeasureTraversalMilliseconds(Camera camera, Transform model) =>
            m_selector.MeasureMilliseconds(BuildView(camera, model, Budget));

        static int Upload(NativeArray<uint> indices, ISorterResource sorter)
        {
            sorter.OrderBuffer.SetData(indices, 0, 0, indices.Length);
            sorter.Initialized = true;
            return indices.Length;
        }

        /// <summary>The view in model space, so the selection is unaffected by where the model sits.</summary>
        public static GsplatLodView BuildView(Camera camera, Transform model, int budget)
        {
            var settings = GsplatSettings.Instance;
            var worldToModel = model.worldToLocalMatrix;
            var origin = (float3)worldToModel.MultiplyPoint3x4(camera.transform.position);
            var forward = (float3)worldToModel.MultiplyVector(camera.transform.forward);
            // Splats land in the offscreen target (D16): a pixel is an offscreen pixel, not a camera one.
            float height = XRSettings.enabled && XRSettings.eyeTextureHeight > 0
                ? XRSettings.eyeTextureHeight * XRSettings.renderViewportScale
                : camera.pixelHeight;
            if (GsplatSorter.DeferDraws)
                height *= settings.EffectiveOffscreenScale;
            var limit = 2f * Mathf.Tan(0.5f * camera.fieldOfView * Mathf.Deg2Rad) / Mathf.Max(1f, height);
            return GsplatLodView.Create(origin, forward, limit, budget,
                settings.LodConeFov0, settings.LodConeFov, settings.LodConeFoveate, settings.LodBehindFoveate);
        }

        public void Dispose()
        {
            if (m_disposed)
                return;
            m_disposed = true;
            m_selector.Dispose();
            m_tree.Dispose();
            --LiveCount;
        }
    }
}
```

- [ ] **Step 3: `GsplatRendererImpl.cs`**

字段区（`ComputeCutoutsRequired` 之后）加：

```csharp
        GsplatLodDriver m_lod;

        public bool IsLod => m_gsplatAsset is GsplatLodAsset;
        public int LodLatencyFrames => m_lod?.LastLatencyFrames ?? 0;
```

`BindGsplatAsset` 末尾（上传之后）加：

```csharp
            if (gsplatAsset is GsplatLodAsset lodAsset)
                BindLod(lodAsset);
```

并在类里加：

```csharp
        void BindLod(GsplatLodAsset asset)
        {
            if (GsplatLodDriver.LiveCount > 0)
            {
                // D12: the budget belongs to the frame; a second LoD renderer would silently double it.
                Debug.LogError($"[Gsplat] '{asset.name}': at most one active LoD renderer is supported (spec D12); this one will not render.");
                return;
            }

            // The order buffer was sized to the budget for LoD assets (GsplatRenderer.OrderCapacity).
            m_lod = new GsplatLodDriver(asset, (int)SplatCount);
        }

        public void UpdateLod(Camera camera, Transform model, int frame)
        {
            m_bounds = m_gsplatAsset.Bounds;
            if (m_lod == null || GsplatResource.UploadedCount < m_gsplatAsset.SplatCount)
                return;
            var published = m_lod.Update(camera, model, SorterResource, frame);
            if (published < 0)
                return;
            m_remainingCount = (uint)published;
            ComputeSortRequired = true; // a fresh cut is unsorted: sort it this frame whatever the interval (D11)
        }

        public double MeasureLodTraversalMs(Camera camera, Transform model) =>
            m_lod?.MeasureTraversalMilliseconds(camera, model) ?? 0;
```

`ReleaseGsplatAsset` 开头加：

```csharp
            m_lod?.Dispose();
            m_lod = null;
```

- [ ] **Step 4: `GsplatRenderer.cs`**

字段区加：

```csharp
        bool m_warnedLodCutouts;
        bool m_warnedNoCamera;

        // LoD assets draw at most the budget, so their order buffer is sized to it (spec §7).
        uint OrderCapacity => GsplatAsset is GsplatLodAsset
            ? GsplatSettings.Instance.LodSplatBudget
            : GsplatAsset.SplatCount;

        public bool IsLod => m_renderer?.IsLod ?? false;
        public int LodLatencyFrames => m_renderer?.LodLatencyFrames ?? 0;

        /// <summary>One synchronous traversal from Camera.main, timed. For the bench.</summary>
        public double MeasureLodTraversalMs()
        {
            var camera = Camera.main;
            return m_renderer != null && camera ? m_renderer.MeasureLodTraversalMs(camera, transform) : 0;
        }
```

`Update()` 开头两行改为：

```csharp
            if (!GsplatAsset)
                m_prevAsset = null;
            // ponytail: a budget change rebinds and re-uploads the asset; only the bench changes it at runtime.
            if (GsplatAsset && m_renderer != null && m_prevAsset == GsplatAsset && m_renderer.SplatCount != OrderCapacity)
                m_prevAsset = null;
```

同一方法里两处 `GsplatAsset.SplatCount`（`new GsplatRendererImpl(...)` 与 `RecreateResources(...)`）都换成 `OrderCapacity`。

把

```csharp
                m_renderer.DispatchInitOrder(Cutouts, transform.localToWorldMatrix, CutoutsUpdateBounds);
```

换成

```csharp
                if (m_renderer.IsLod)
                    UpdateLod();
                else
                    m_renderer.DispatchInitOrder(Cutouts, transform.localToWorldMatrix, CutoutsUpdateBounds);
```

并在类里加：

```csharp
        void UpdateLod()
        {
            if (!m_warnedLodCutouts && Cutouts.Length > 0)
            {
                Debug.LogError($"[Gsplat] '{name}': .gsd assets do not support cutouts (spec D13); they are ignored.", this);
                m_warnedLodCutouts = true;
            }

            var camera = Camera.main;
            if (!camera)
            {
                if (!m_warnedNoCamera)
                {
                    Debug.LogError($"[Gsplat] '{name}': LoD selection needs a camera tagged MainCamera; none found, nothing is drawn.", this);
                    m_warnedNoCamera = true;
                }

                return;
            }

            m_renderer.UpdateLod(camera, transform, Time.frameCount);
        }
```

- [ ] **Step 5: `GsplatSorter.cs`**

`DispatchSort` 里的

```csharp
                if (!res.Initialized)
                {
                    m_sortPass.InitPayload(cmd, res.OrderBuffer, (uint)res.OrderBuffer.count);
                    res.Initialized = true;
                }
```

改为

```csharp
                if (!res.Initialized)
                {
                    if (gs.GsplatResource is GsplatResourceLod)
                    {
                        // D14: the LoD selection owns this buffer. Identity-filling it would draw nodes
                        // 0..count-1 of the tree — interiors and leaves mixed — instead of the cut.
                        Debug.LogError("[GsplatSorter] a LoD order buffer reached the sort before its first cut; skipping it.");
                        continue;
                    }

                    m_sortPass.InitPayload(cmd, res.OrderBuffer, (uint)res.OrderBuffer.count);
                    res.Initialized = true;
                }
```

`CanRenderGlobally` 里的警告改为如实说明类型：

```csharp
                    Debug.LogWarning(
                        $"[GsplatSorter] '{obj?.name}' uses {gs.GsplatResource?.GetType().Name ?? "no resource"}; global sort requires every active renderer to use SPARK compression. Disabling global sort for this scene — all renderers fall back to per-renderer rendering.");
```

- [ ] **Step 6: 刷新、跑全部包测试**

执行 §U，§T 跑整个 `Gsplat.Editor.Tests`。Expected：全部 PASS；`AMissingMainCameraIsReported` 若为 Inconclusive（打开的场景里另有 MainCamera）也可接受，记下来。

- [ ] **Step 7: 提交（子模块），再更新 MR_Base 的子模块指针**

```bash
cd /Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat
git add Runtime/Lod/GsplatLodDriver.cs Runtime/Lod/GsplatLodDriver.cs.meta Runtime/GsplatRendererImpl.cs Runtime/GsplatRenderer.cs Runtime/GsplatSorter.cs \
  Tests/Editor/GsplatLodRendererTests.cs Tests/Editor/GsplatLodRendererTests.cs.meta
git commit -m "$(cat <<'EOF'
feat(lod): drive LoD selections into the renderer's order buffer

Order buffers of .gsd assets are sized to the budget; each published
cut is uploaded and sorted the same frame; InitOrder never runs for
them. One LoD renderer at a time, cutouts and a missing main camera
are reported rather than ignored.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
cd /Users/wwj/Desktop/unity/MR_Base
git add Packages/wu.yize.gsplat
git commit -m "$(cat <<'EOF'
chore(gsplat): bump submodule to the LoD runtime

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 12: 生成 `.gsd` 模型并导入

**Files:**
- Modify: `.gitignore`（MR_Base）
- Create（生成，不入库）: `Assets/Assets/3dgs/Model_50w.gsd`、`Model_100w.gsd`、`Model_200w.gsd`
- Create（入库）: 三个 `.gsd.meta`

- [ ] **Step 1: 确认源坐标系**

```bash
grep -n "SourceCoordinates" /Users/wwj/Desktop/unity/MR_Base/Assets/Assets/3dgs/Model_*.ply.meta
```

枚举值：`0` Unspecified（按 RUB 处理）、`1` LDB、`2` RDB、`3` LUB、`4` RUB、`5` LDF、`6` RDF、`7` LUF、`8` RUF；没有该字段 = 导入器默认 RUB。调研时 `Model_200w` 为 `4`（RUB）、`Model_50w` 无字段（RUB）。若 `Model_100w` 不同，下面的命令对它改用对应的 `--source`。

- [ ] **Step 2: 构建 release 版 CLI 并生成三个模型**

```bash
cd "/Users/wwj/Desktop/unity/MR_Base/Packages/wu.yize.gsplat/Tools~/gsd-build" && cargo build --release
M=/Users/wwj/Desktop/unity/MR_Base/Assets/Assets/3dgs
for n in 50 100 200; do ./target/release/gsd-build "$M/Model_${n}w.ply" --source RUB; done
ls -lh $M/*.gsd
```

Expected：每个模型打印一行统计（叶子数 ≈ 源点数减去被丢弃的空点、节点数约为叶子数的 1.3–2 倍）。把三行统计原样记下，Task 14 写进结果。bhatt 在 200w 上可能要几分钟；如果超过 30 分钟，记下耗时，再用 `--quick` 另出一份 `-o Model_200w-quick.gsd` 作对照，并告诉用户。

- [ ] **Step 3: 忽略模型文件、导入**

在 `.gitignore` 的 `/[Aa]ssets/[Aa]ssets/3dgs/*.ply` 下一行加：

```
/[Aa]ssets/[Aa]ssets/3dgs/*.gsd
```

执行 §U，然后：

```bash
unity command eval --project-path $R '
var sb = new System.Text.StringBuilder();
foreach (var n in new[] { 50, 100, 200 }) {
  var a = UnityEditor.AssetDatabase.LoadAssetAtPath<Gsplat.GsplatLodAsset>($"Assets/Assets/3dgs/Model_{n}w.gsd");
  sb.Append(a == null ? $"{n}w: MISSING\n" : $"{n}w: nodes={a.SplatCount} leaves={a.LeafCount} sh={a.SHBands} bounds={a.Bounds.size}\n");
}
return sb.ToString();'
```

Expected：三行都有数，没有 MISSING；console 无 import error。

- [ ] **Step 4: 提交（MR_Base）**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git add .gitignore Assets/Assets/3dgs/Model_50w.gsd.meta Assets/Assets/3dgs/Model_100w.gsd.meta Assets/Assets/3dgs/Model_200w.gsd.meta
git commit -m "$(cat <<'EOF'
chore(gsplat-bench): add .gsd LoD builds of the 50w/100w/200w models

The .gsd files are generated by gsd-build and ignored like the .ply
sources; the .meta files keep their GUIDs stable for the bench scene.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 13: Bench——LoD 预算旋钮、固定视点、LoD 对比 sweep，外加画面等价检查

**Files:**
- Modify: `Assets/Scripts/GsplatBench/BenchKnobs.cs`、`BenchRig.cs`、`BenchSweep.cs`
- Modify（Editor 操作）: `Assets/Scenes/GsplatBench.unity`

**Interfaces:**
- Consumes: `GsplatRenderer.IsLod/LodLatencyFrames/MeasureLodTraversalMs()/RemainingCount`，`GsplatSettings.LodSplatBudget`，`GsplatLodAsset`。
- Produces（供 CLI 调用）：`BenchRig.StartLodComparison()`、`BenchRig.SelectAsset(string, bool lod)`、`BenchRig.ApplyViewpoint(int)`。

- [ ] **Step 1: `BenchKnobs.cs`——LoD 预算旋钮与"推荐配置"复位**

1. `enum Knob` 末尾加 `LodBudget,`。
2. 档位表加：

```csharp
        // 只作用于 .gsd 资产（spec D12 的全局预算）。
        static readonly int[] k_LodBudgetSteps = { 300000, 400000, 500000, 600000, 800000 };
```

3. 把各旋钮字段的初值去掉（只保留声明与注释），改由构造函数调复位方法，避免"推荐配置"写两份：

```csharp
        int m_Msaa;
        int m_Sh;
        int m_Downscale;
        int m_Sort;
        int m_Viewport;
        int m_Copies;
        int m_Foveation;
        int m_Cutout;
        int m_Offscreen;
        int m_LodBudget;

        public BenchKnobs() => ResetToRecommended();

        /// <summary>
        /// 推荐配置 = 启动值（见上方注释）。LoD 对比 sweep 每档都从这里起步，保证各档只差资产与预算。
        /// </summary>
        public void ResetToRecommended()
        {
            m_Msaa = 0;       // off
            m_Sh = 0;         // degree 0
            m_Downscale = 3;  // 0.25
            m_Sort = 4;       // 1/30
            m_Viewport = 2;   // 0.7
            m_Copies = 0;     // x1
            m_Foveation = 2;  // 0.66
            m_Cutout = 0;     // off
            m_Offscreen = 2;  // 0.5
            m_LodBudget = 2;  // 50w
        }
```

4. `KnobCount` 改为 `10`；加属性 `public int LodBudget => k_LodBudgetSteps[m_LodBudget];`。
5. `Adjust` 与 `SetValue` 的 `switch` 各加一支：

```csharp
                case Knob.LodBudget: m_LodBudget = Step(m_LodBudget, direction, k_LodBudgetSteps.Length); break;
```

```csharp
                case Knob.LodBudget: m_LodBudget = NearestInt(k_LodBudgetSteps, value); break;
```

6. `ResetToBaseline()` 末尾加 `m_LodBudget = 2; // 预算不是边际项：baseline 也取推荐值`。
7. `Apply(...)` 里 OffscreenScale 那段之后加：

```csharp
            if (gsplatSettings != null && gsplatSettings.LodSplatBudget != (uint)LodBudget)
                gsplatSettings.LodSplatBudget = (uint)LodBudget;
```

8. `NameOf` 加 `Knob.LodBudget => "LoD budget",`；`ValueOf` 加 `Knob.LodBudget => (LodBudget / 10000) + "w",`。
9. `CsvHeader` 末尾加 `,lod_budget`；`CsvRow()` 末尾加 `LodBudget.ToString(CultureInfo.InvariantCulture)`。

- [ ] **Step 2: `BenchRig.cs`——视点、资产选择、LoD 指标、设置复原、CLI 入口**

需要时在 using 区补 `using System.Linq;`。

序列化字段区（"采样时长"之后）加：

```csharp
        [Header("LoD 对比（spec §8.2）")]
        [Tooltip("固定视点：全景 / 中距 / 贴近表面。相机所在的整个 rig 被搬过去，Editor 与头显下都成立。")]
        [SerializeField] Transform[] viewpoints;
```

私有字段加 `float m_SavedOffscreenScale;`、`uint m_SavedLodBudget;`。

`Awake()` 末尾加：

```csharp
            // 旋钮直接改 GsplatSettings 资产。编辑器 Play 模式对 ScriptableObject 的修改不会自动回滚，
            // 退出时手动复原，免得把 bench 的档位写进工程设置。
            m_SavedOffscreenScale = GsplatSettings.Instance.OffscreenScale;
            m_SavedLodBudget = GsplatSettings.Instance.LodSplatBudget;
```

`OnDestroy()` 末尾加：

```csharp
            GsplatSettings.Instance.OffscreenScale = m_SavedOffscreenScale;
            GsplatSettings.Instance.LodSplatBudget = m_SavedLodBudget;
```

公开成员（放在 `CurrentAsset` 附近）：

```csharp
        public int ViewpointCount => viewpoints?.Length ?? 0;
        public string ViewpointLabel { get; private set; } = "(scene)";

        public uint DrawnTotal
        {
            get
            {
                uint total = 0;
                foreach (var renderer in m_Renderers)
                    if (renderer != null)
                        total += renderer.RemainingCount;
                return total;
            }
        }

        public bool IsLodActive => m_Renderers.Count > 0 && m_Renderers[0] != null && m_Renderers[0].IsLod;
        public int LodLatencyFrames => IsLodActive ? m_Renderers[0].LodLatencyFrames : 0;
        public double MeasureLodTraversalMs() => IsLodActive ? m_Renderers[0].MeasureLodTraversalMs() : 0;

        /// <summary>命令行入口：<c>unity command eval</c> 调它跑 LoD 对比，不需要手柄也不需要键盘。</summary>
        public void StartLodComparison()
        {
            if (!m_Sweep.Running)
                StartCoroutine(m_Sweep.Run(BenchSweep.Mode.LodComparison));
        }

        /// <summary>按名字与类型切资产。.ply 与 .gsd 导入后同名（都叫 Model_100w），只能靠类型区分。</summary>
        public bool SelectAsset(string assetName, bool lod)
        {
            if (assets == null)
                return false;
            for (var i = 0; i < assets.Length; ++i)
            {
                var asset = assets[i];
                if (asset == null || asset.name != assetName || (asset is GsplatLodAsset) != lod)
                    continue;
                if (i != m_AssetIndex)
                {
                    m_AssetIndex = i;
                    ApplyAssetToRenderers();
                }

                Metrics.ResetWindow();
                return true;
            }

            return false;
        }

        /// <summary>把相机所在 rig 的根搬到视点，使相机的世界位姿恰好等于视点。头显里的头部追踪照常叠加在上面。</summary>
        public void ApplyViewpoint(int index)
        {
            var camera = Camera.main;
            if (camera == null || viewpoints == null || index < 0 || index >= viewpoints.Length || viewpoints[index] == null)
                return;
            var cam = camera.transform;
            var root = cam.root;
            var target = viewpoints[index];
            var relRot = Quaternion.Inverse(root.rotation) * cam.rotation;
            var relPos = Quaternion.Inverse(root.rotation) * (cam.position - root.position);
            root.rotation = target.rotation * Quaternion.Inverse(relRot);
            root.position = target.position - root.rotation * relPos;
            ViewpointLabel = target.name;
        }

        public string DescribeViewpoints() => viewpoints == null
            ? string.Empty
            : string.Join(";", viewpoints.Where(v => v != null).Select(v =>
                $"{v.name}@{v.position.x:F2}/{v.position.y:F2}/{v.position.z:F2}" +
                $" r{v.eulerAngles.x:F1}/{v.eulerAngles.y:F1}/{v.eulerAngles.z:F1}"));
```

- [ ] **Step 3: `BenchSweep.cs`——LoD 对比模式**

1. `enum Mode` 加 `LodComparison,`。
2. `Step` 结构加字段与构造函数：

```csharp
            // 仅 LodComparison：切到哪个资产、哪个视点。AssetName 为 null 表示不切。
            public readonly string AssetName;
            public readonly bool AssetIsLod;
            public readonly int LodBudget;
            public readonly int Viewpoint;

            public Step(string assetName, bool assetIsLod, int lodBudget, int viewpoint)
            {
                Name = $"{assetName}{(assetIsLod ? $".gsd@{lodBudget / 10000}w" : ".ply")}-v{viewpoint}";
                Knob = default;
                Value = 0f;
                IsBaseline = false;
                AssetName = assetName;
                AssetIsLod = assetIsLod;
                LodBudget = lodBudget;
                Viewpoint = viewpoint;
            }
```

两个已有构造函数末尾补 `AssetName = null; AssetIsLod = false; LodBudget = 0; Viewpoint = -1;`。

3. 用例表与步骤表：

```csharp
        // LoD 对比（spec §8.2）：同一套推荐旋钮下，全量 .ply 与不同预算的 .gsd 对照。
        // 外层按资产、内层按视点：资产切换（整份重传）最少。
        static readonly (string Asset, bool Lod, int Budget)[] k_LodCases =
        {
            ("Model_50w", false, 0),
            ("Model_200w", false, 0),
            ("Model_100w", true, 300000),
            ("Model_100w", true, 500000),
            ("Model_100w", true, 800000),
            ("Model_200w", true, 300000),
            ("Model_200w", true, 500000),
            ("Model_200w", true, 800000),
        };

        Step[] m_Steps = k_Sequence;

        Step[] BuildLodSteps()
        {
            var steps = new List<Step>();
            foreach (var c in k_LodCases)
                for (var v = 0; v < m_Rig.ViewpointCount; ++v)
                    steps.Add(new Step(c.Asset, c.Lod, c.Budget, v));
            return steps.ToArray();
        }
```

`StepCount` 改为 `public int StepCount => m_Steps.Length;`；`Run` 与 `ConfigureStep` 里所有 `k_Sequence` 换成 `m_Steps`。

4. `Run(Mode mode)` 在 `m_Lines.Clear();` 之后、生成 `OutputPath` 之前插入：

```csharp
            m_Steps = mode == Mode.LodComparison ? BuildLodSteps() : k_Sequence;
            if (mode == Mode.LodComparison)
            {
                if (m_Steps.Length == 0)
                {
                    Abort("BenchRig 没有配置 viewpoints");
                    yield break;
                }

                // D17：拿不到 GPU 时间就不跑。墙钟时间测的是另一件事，不能拿来顶替。
                m_Rig.Metrics.ResetWindow();
                for (var i = 0; i < 60; ++i)
                    yield return null;
                if (!m_Rig.Metrics.HasGpuData)
                {
                    Abort("no GPU timing: " + m_Rig.Metrics.GpuUnavailableReason);
                    yield break;
                }
            }
```

并加：

```csharp
        void Abort(string reason)
        {
            Phase = "aborted";
            PhaseRemaining = 0f;
            Running = false;
            BenchLog.Milestone("sweep aborted: " + reason);
            Debug.LogError($"{BenchLog.Tag}|sweep aborted: {reason}");
        }
```

5. 循环里 `ConfigureStep(mode, StepIndex);` 之后加 `if (StopRequested) break;`。
6. `ConfigureStep` 开头加 LoD 分支：

```csharp
            if (mode == Mode.LodComparison)
            {
                var lodStep = m_Steps[index];
                m_Rig.Knobs.ResetToRecommended();
                if (lodStep.AssetIsLod)
                    m_Rig.Knobs.SetValue(BenchKnobs.Knob.LodBudget, lodStep.LodBudget);
                m_Rig.ApplyKnobsNow();
                if (!m_Rig.SelectAsset(lodStep.AssetName, lodStep.AssetIsLod))
                {
                    // 资产没挂上就停：缺一档的对照表比空档更容易被误读。
                    var what = $"{lodStep.AssetName}{(lodStep.AssetIsLod ? ".gsd" : ".ply")}";
                    BenchLog.Milestone($"sweep: {what} is not in BenchRig.assets");
                    Debug.LogError($"{BenchLog.Tag}|sweep: {what} is not in BenchRig.assets");
                    RequestStop();
                    return;
                }

                m_Rig.ApplyViewpoint(lodStep.Viewpoint);
                return;
            }
```

7. CSV 表头改为（在旋钮列之后插入 6 列）：

```csharp
            AppendLine("step_index,step_name,mode,valid," + BenchKnobs.CsvHeader +
                       ",asset,asset_is_lod,viewpoint,drawn_splats,lod_traversal_ms,lod_latency_frames" +
                       ",splat_total,renderer_count,gpu_source,gpu_median_ms,gpu_p99_ms,compositor_gpu_ms," +
                       "cpu_median_ms,display_interval_ms,budget_ms,refresh_hz,stereo,foveation,eye_res_scale," +
                       "samples,drift");
```

`RecordRow` 里 `m_Rig.Knobs.CsvRow(),` 之后插入：

```csharp
                m_Rig.AssetLabel,
                (m_Rig.CurrentAsset is GsplatLodAsset).ToString(),
                m_Rig.ViewpointLabel,
                m_Rig.DrawnTotal.ToString(CultureInfo.InvariantCulture),
                m_Rig.IsLodActive ? m_Rig.MeasureLodTraversalMs().ToString("F3", CultureInfo.InvariantCulture) : string.Empty,
                m_Rig.LodLatencyFrames.ToString(CultureInfo.InvariantCulture),
```

（文件头需要 `using Gsplat;`。）`WriteContextHeader` 末尾加：

```csharp
            AppendLine($"# game_view,{Screen.width}x{Screen.height}");
            AppendLine($"# viewpoints,{m_Rig.DescribeViewpoints().Replace(',', ' ')}");
```

- [ ] **Step 4: 刷新编译，确认 bench 与包测试都还是绿的**

执行 §U；§T 跑 `Gsplat.Editor.Tests`，再跑 `--filter MRBase.Build.Editor.Tests --filter_type assembly`（`LayoutConventionTests`，bench 在 `Assets/Scripts` 下受目录约定管辖）。Expected：全部 PASS。

- [ ] **Step 5: 场景里加视点与 .gsd 资产（先确认不会冲掉用户的场景）**

```bash
unity command eval --project-path $R 'var s = UnityEngine.SceneManagement.SceneManager.GetActiveScene(); return s.path + " dirty=" + s.isDirty;'
```

当前场景 `dirty=True` 就**停下**，请用户保存或放弃，不替用户决定。当前场景不是 bench 场景时：

```bash
unity command open_scene --project-path $R --path Assets/Scenes/GsplatBench.unity
unity command eval --project-path $R 'return UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;'   # 必须是 Assets/Scenes/GsplatBench.unity
```

然后：

```bash
unity command eval --project-path $R '
var rig = UnityEngine.Object.FindFirstObjectByType<MRBase.GsplatBench.BenchRig>();
var root = new UnityEngine.GameObject("Viewpoints").transform;
var names = new[] { "Overview", "Mid", "Close" };
var points = new UnityEngine.Transform[3];
for (var i = 0; i < 3; ++i) { var t = new UnityEngine.GameObject(names[i]).transform; t.SetParent(root, false); points[i] = t; }
var so = new UnityEditor.SerializedObject(rig);
var vp = so.FindProperty("viewpoints"); vp.arraySize = 3;
for (var i = 0; i < 3; ++i) vp.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
var assets = so.FindProperty("assets");
foreach (var n in new[] { 50, 100, 200 }) {
  var a = UnityEditor.AssetDatabase.LoadAssetAtPath<Gsplat.GsplatAsset>($"Assets/Assets/3dgs/Model_{n}w.gsd");
  assets.arraySize++; assets.GetArrayElementAtIndex(assets.arraySize - 1).objectReferenceValue = a;
}
so.ApplyModifiedProperties();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
return "viewpoints=" + vp.arraySize + " assets=" + assets.arraySize;'
```

- [ ] **Step 6: 选定三个视点的位姿**

四个模型是同一个场景的不同采样密度，所以一组视点对它们全部适用。在 Play 模式下迭代：

```bash
unity command editor_play --project-path $R
# 以全量 50w 为参照摆视点；i 为 0/1/2，位姿按下面的判据反复调整
unity command eval --project-path $R '
var rig = UnityEngine.Object.FindFirstObjectByType<MRBase.GsplatBench.BenchRig>();
rig.SelectAsset("Model_50w", false);
var vp = UnityEngine.GameObject.Find("Viewpoints").transform.GetChild(0);
vp.SetPositionAndRotation(new UnityEngine.Vector3(0f, 1.6f, -6f), UnityEngine.Quaternion.Euler(10f, 0f, 0f));
rig.ApplyViewpoint(0); return vp.name;'
unity command capture_game_view --project-path $R --source screen --save_path /private/tmp/claude-501/-Users-wwj-Desktop-unity-MR-Base/340e9260-a0c9-4c4b-960e-bb96788bc51b/scratchpad/vp0.png
```

（上面的位姿只是起点。）用 Read 看截图后调整，判据如下：

- **Overview**：模型主体整个在画面内，占画面宽度 60–90%。
- **Mid**：主体大约一半在画面内。
- **Close**：相机离最近的致密表面约 0.5 m，画面几乎被 splat 占满（fill 的最坏情况）。

定下之后退出 Play（`editor_stop`）。**Play 模式里改的 Transform 会被回滚**，所以要回到编辑模式，用同样的 `SetPositionAndRotation` 把三个最终位姿写进场景对象，再保存场景：

```bash
unity command eval --project-path $R 'UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); return "saved";'
```

把三组位姿记下来，Task 14 写进结果。

- [ ] **Step 7: 画面等价检查（抓大错：朝向、颜色、镜像、缩放）**

Play 模式下，每个视点 i 各抓两张：

```bash
unity command eval --project-path $R 'var rig = UnityEngine.Object.FindFirstObjectByType<MRBase.GsplatBench.BenchRig>(); rig.Knobs.SetValue(MRBase.GsplatBench.BenchKnobs.Knob.LodBudget, 800000); rig.ApplyKnobsNow(); rig.SelectAsset("Model_50w", false); rig.ApplyViewpoint(0); return "ply";'
S=/private/tmp/claude-501/-Users-wwj-Desktop-unity-MR-Base/340e9260-a0c9-4c4b-960e-bb96788bc51b/scratchpad
unity command capture_game_view --project-path $R --source screen --save_path $S/eq-ply-v0.png
unity command eval --project-path $R 'var rig = UnityEngine.Object.FindFirstObjectByType<MRBase.GsplatBench.BenchRig>(); rig.SelectAsset("Model_50w", true); rig.ApplyViewpoint(0); return "gsd";'
unity command capture_game_view --project-path $R --source screen --save_path $S/eq-gsd-v0.png
```

（换资产后先执行一次 `editor_status`，等重新绑定与上传跑完再截图。执行者不在本会话时，`S` 换成自己会话的 scratchpad 目录。）逐对比较：

- 整体形状、颜色、朝向应当一致。`.gsd` 可以略软（选点在像素阈值处停止细化）。
- 出现以下任一情况即**失败**：splat 普遍变成拉长或方向错乱的毛刺、整体偏色、镜像、位置或尺度不对。失败时回查 `LodQuatToShaderOrder`、`frame.rs` 的四元数与 SH 符号、`--source` 是否与 `.ply.meta` 一致。**不要继续进入 Task 14。**

截图路径保留，最后给用户看。

- [ ] **Step 8: 提交（MR_Base）**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git add Assets/Scripts/GsplatBench/BenchKnobs.cs Assets/Scripts/GsplatBench/BenchRig.cs Assets/Scripts/GsplatBench/BenchSweep.cs Assets/Scenes/GsplatBench.unity
git commit -m "$(cat <<'EOF'
feat(gsplat-bench): LoD budget knob, fixed viewpoints and LoD comparison sweep

Sweeps full .ply against .gsd at 30/50/80w budgets over three fixed
viewpoints under the recommended knobs; refuses to run without GPU
timing; restores GsplatSettings on exit; startable from the CLI.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 14: Editor 实测并记录结果

**Files:**
- Create: `docs/superpowers/specs/data/2026-09-28-gsplat-lod-editor.csv`
- Modify: `docs/superpowers/specs/2026-09-28-gsplat-lod-design.md`（§8.2 之后加"结果"小节）

- [ ] **Step 1: 固定 Game 视图分辨率并进入 Play**

```bash
unity command eval --project-path $R 'UnityEditor.PlayModeWindow.SetCustomRenderingResolution(2064, 2208, "Quest3 eye"); return "set";'
unity command editor_play --project-path $R
```

`SetCustomRenderingResolution` 若不可用（编译错误或异常），就跳过这一步，照常测：CSV 头里的 `# game_view` 会记下实际分辨率，在结果里如实写明分辨率没有固定成功。

- [ ] **Step 2: 启动 LoD 对比 sweep**

```bash
unity command eval --project-path $R 'UnityEngine.Object.FindFirstObjectByType<MRBase.GsplatBench.BenchRig>().StartLodComparison(); return "started";'
unity command console --project-path $R --tail 20
```

一共 24 档，每档预热 2 s、采样 6 s，另加换资产的时间，总计约 5 分钟。每隔几分钟看一次 console，直到出现 `sweep done: … -> <path>.csv`。若出现 `sweep aborted: no GPU timing`，按 D17 **就此停下**，把原因报告给用户，**不要换成墙钟时间**。

- [ ] **Step 3: 取回 CSV、退出 Play**

```bash
unity command editor_stop --project-path $R
mkdir -p /Users/wwj/Desktop/unity/MR_Base/docs/superpowers/specs/data
cp "<console 里打印的 csv 路径>" /Users/wwj/Desktop/unity/MR_Base/docs/superpowers/specs/data/2026-09-28-gsplat-lod-editor.csv
```

- [ ] **Step 4: 按判据计算**

```bash
cd /Users/wwj/Desktop/unity/MR_Base && python3 - <<'EOF'
import csv
rows = [r for r in csv.DictReader(l for l in open("docs/superpowers/specs/data/2026-09-28-gsplat-lod-editor.csv") if not l.startswith("#"))]
def gpu(asset, lod, budget, vp):
    for r in rows:
        if r["asset"] == asset and r["asset_is_lod"] == str(lod) and r["viewpoint"] == vp and (not lod or r["lod_budget"] == str(budget)):
            return float(r["gpu_median_ms"]), r["valid"], r["drawn_splats"], r["lod_traversal_ms"], r["lod_latency_frames"]
for vp in sorted({r["viewpoint"] for r in rows}):
    base50, v50, *_ = gpu("Model_50w", False, 0, vp)
    full200, v200, *_ = gpu("Model_200w", False, 0, vp)
    print(f"== {vp}: 50w.ply {base50:.2f}ms ({v50})  200w.ply {full200:.2f}ms ({v200})")
    for b in (300000, 500000, 800000):
        g100, v1, d1, t1, l1 = gpu("Model_100w", True, b, vp)
        g200, v2, d2, t2, l2 = gpu("Model_200w", True, b, vp)
        print(f"  @{b//10000}w  100w.gsd {g100:.2f}ms drawn {d1} trav {t1}ms lat {l1}f ({v1}) | "
              f"200w.gsd {g200:.2f}ms drawn {d2} trav {t2}ms lat {l2}f ({v2}) | "
              f"decoupling {abs(g200-g100)/g100*100:.1f}% | vs full200 {full200/g200:.2f}x")
    g200_50 = gpu("Model_200w", True, 500000, vp)[0]
    print(f"  criterion 1: 200w.gsd@50w / 50w.ply = {g200_50/base50:.2f} (pass <= 1.20)")
EOF
```

判据（spec §8.2，同旋钮、同视点）：

1. **LoD 有效**：`GPU(200w.gsd @ 50w) ≤ 1.2 × GPU(50w.ply 全量)`。
2. **与源大小脱钩**：同一预算下 `100w.gsd` 与 `200w.gsd` 的 GPU 时间相差 ≤ ±5%。
3. 相对 `200w.ply` 全量的收益倍数，只记录，不设门槛。
4. 每帧实画数 ≤ N（`drawn_splats` 列）。

`valid` 为 `invalid` 的行不参与判定，并在结果里注明。

- [ ] **Step 5: 把结果写进 spec**

在 spec §8.2 末尾追加一节 `### 8.3 结果（2026-09-28，Editor / Mac Metal）`，内容包括：

- 机器与 Unity 版本，取 CSV 头的 `# device`/`# unity`；Game 视图分辨率；三个视点的位姿。
- Task 12 的三行建树统计。
- 上一步的输出表：每个视点一张，列为 50w.ply、200w.ply、以及三档预算下的 100w/200w.gsd，外加 drawn、trav、lat。
- 判据 1、2、4 逐条写"通过 / 未通过"并附数字。
- **没通过的，照实写。不改阈值、不重跑挑数。** 附一句对原因的判断，比如"贴近视点 fill 未降，符合 §11 风险，由 B 解决"。
- 局限一句：Mac GPU 是相对证据，72fps 的绝对判断属于真机阶段（§10）。

- [ ] **Step 6: 提交（MR_Base）**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
git add docs/superpowers/specs/data/2026-09-28-gsplat-lod-editor.csv docs/superpowers/specs/2026-09-28-gsplat-lod-design.md
git commit -m "$(cat <<'EOF'
docs(gsplat-lod): record the Editor LoD comparison

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 7: 向用户汇报**

一段话写清：判据 1、2 各自通过没有，关键数字（200w.gsd@50w 对 50w.ply 的比值，对 200w.ply 全量快了几倍），遍历耗时与延迟，画面等价截图的路径。再把下一步列出来：真机阶段（恢复 bench 构建入口、PICO 的 GPU 计时来源）和子项目 B。**不 push，不出包。**
