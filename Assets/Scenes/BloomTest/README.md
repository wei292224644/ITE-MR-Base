# BloomTest

MR 下 bloom 的专用测试台。场景本身只有激励内容，XR 装配由 `MRCoreLoader`
在运行时附加加载 MRCore 提供 —— 与其它 demo 场景同一套结构。

## 出包

```
MRBase/Build/Bloom Test/Quest Development
MRBase/Build/Bloom Test/PICO Development
```

两者都经 `ProbeScenes()` 把 MRCore 一并打进包，否则 `LoadScene("MRCore")`
在真机上会失败，表现为没有相机和手部追踪。

## 场景内容

六个自发光球，材质为 URP/Lit，自发光 = 基色 × k，k ∈ {0.5, 1, 2, 4, 8, 16}。
排在半径 1.8m、眼高 1.36m 的弧上，站在原点即可一眼看全。Bloom 阈值是 1.0，
所以 k ≤ 1 不发光、k ≥ 2 逐级变强 —— **阈值切点直接可见**，这是本场景存在的
全部理由。用一个「本来就不该亮」的场景测，分不清是合成器吃掉了光晕还是内容
压根没超阈值。

场景里没有地面和灯：passthrough 下真实世界就是背景，铺地面会把它盖住；灯由
MRCore 提供。

## 这个场景要回答的问题

编辑器实测（渲 MRCore 相机到带 alpha 的 RT 读回）：

- bloom **完全没有抬高 alpha**，中间值恒为 0
- 但有约 3.9% 的像素处于 `alpha = 0 且 RGB 有亮度` —— 光晕被写进了颜色，
  而那些位置的 alpha 仍是 0

光晕在真机上的表现取决于合成器的混合方式，**编辑器测不出来**：

| 合成方式 | 结果 |
|---|---|
| 预乘 alpha（Quest/OpenXR 合成层常见） | RGB 直接叠加，光晕像加性光打在真实世界上 |
| 直混 over（straight alpha） | alpha=0 处颜色被丢弃，光晕在 passthrough 区域消失 |

戴上头显看 x2 → x16 那几个球周围有没有光晕即可判定。

## 依赖的项目级配置

bloom 由出货管线提供，本场景不带私有管线资源：

| 资源 | 关键值 |
|---|---|
| `Standalone Performant Preset` / `Standalone Balanced Preset` | `m_SupportsHDR: 1`、`m_HDRColorBufferPrecision: 1`（64bit R16G16B16A16）、`m_AllowPostProcessAlphaOutput: 1` |
| `Performant / Balanced URP Renderer Config` | `postProcessData` 指向 URP 内置 |
| MRCore 的 Main Camera | `renderPostProcessing: true`、`clearFlags: SolidColor`、背景 alpha 0 |
| `Assets/Settings/MR Bloom Profile.asset` | threshold 1.0、Fast Mode、maxIterations 4 |

**色彩缓冲精度必须是 64bit。** 32bit 是 R11G11B10，没有 alpha 通道，而背景
alpha = 0 正是虚拟内容与 passthrough 合成的前提。

## 坑

自发光材质的 `globalIlluminationFlags` 必须是 `RealtimeEmissive`。设成 `None`
或 `EmissiveIsBlack`，Unity 保存时 `MaterialEditor.FixupEmissiveFlag` 会剥掉
`_EMISSION` 关键字，自发光**静默失效** —— 材质面板看着是对的，就是不亮。

## 未验证

设备帧开销。MSAA 仍是 4，bloom 的开销大头是 tile resolve 的带宽而非模糊本身，
4x 会把这笔放大一倍。用 OVR Metrics / RenderDoc 测开关前后的 GPU 时间差，
再决定要不要降到 2。
