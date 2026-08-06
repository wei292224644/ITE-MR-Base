# BloomTest

MR 下 bloom 的专用测试台。场景本身只有激励内容，XR 装配来自常驻的 MRCore
—— 与其它内容场景同一套结构。

## 出包与进入

统一包 `MRBase/Build/Quest`，戴上头显后低头看菜单，点 **BloomTest**。

场景切换走 `MRSceneDirector`：Additive 载入新场景、卸掉上一个，MRCore 始终在。
**不能用 `LoadSceneMode.Single`** —— 它会把 MRCore 一并卸掉，在 Quest 上直接打掉
passthrough（画面变不透明黑，而所有可观测信号仍是绿的）。细节见
`MRSceneDirector.Load` 的注释。

## 场景内容

六个自发光球，材质为 URP/Lit，自发光 = 基色 × k，k ∈ {0.5, 1, 2, 4, 8, 16}。
排在半径 1.8m、眼高 1.36m 的弧上，站在原点即可一眼看全。Bloom 阈值是 1.0，
所以 k ≤ 1 不发光、k ≥ 2 逐级变强 —— **阈值切点直接可见**，这是本场景存在的
全部理由。用一个「本来就不该亮」的场景测，分不清是合成器吃掉了光晕还是内容
压根没超阈值。

场景里没有地面和灯：passthrough 下真实世界就是背景，铺地面会把它盖住；灯由
MRCore 提供。

## 结论：MR 下 bloom 可用（Quest 3 实测）

**光晕在 passthrough 上正常可见。** Quest 3 的合成走预乘 alpha —— 虚拟层的 RGB
直接叠加到真实世界上，alpha 只决定遮挡。所以 bloom 写在 `alpha = 0` 区域的光晕
不会被丢弃，表现为加性光。

推导过程留档，因为它解释了为什么这件事编辑器测不出来：

- 编辑器渲 MRCore 相机到带 alpha 的 RT 读回：bloom **完全没有抬高 alpha**，
  中间值恒为 0；但约 3.9% 的像素处于 `alpha = 0 且 RGB 有亮度`
- 这些像素的去向取决于合成器：预乘 alpha 会把 RGB 加上去，直混 over 会按
  `RGB × alpha` 抹掉。两种结果视觉差别极大，而 RenderTexture 路径和 XR
  swapchain 的最终 blit 不是同一条，编辑器量到的 alpha 不能外推到真机

判定方法（换设备或换 SDK 版本后重验）：看 x8 / x16 两个球周围有没有光晕。
有 = 预乘；只是亮度不同的实心圆 = 直混。x0.5 一定不亮、x1 临界，这个阶梯的作用
就是把「合成器吃掉了光晕」和「内容压根没超阈值」区分开。

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

## 开销（Quest 3 实测，VrApi 帧统计）

| 场景 | App 时间 | GPU |
|---|---|---|
| GroundUpRevealDemo（无 bloom 触发） | 1.4 ~ 1.8 ms | 29 ~ 32% |
| BloomTest（bloom 生效） | 7.9 ms | 74% |

几何开销可忽略（6 个球 + 6 个 TextMesh），约 6.4ms 的增量基本就是 bloom：
HDR 64bit 色彩缓冲 + 后处理强制中间纹理 + MSAA 4x 每帧 resolve 出 tile。
72Hz 的单帧预算是 13.9ms，这一项就吃掉大半。

**MSAA 4 → 2 的对照还没做。** 开销大头是 tile resolve 的带宽而非模糊本身，4x 会把
这笔放大一倍，这是目前最有把握的一笔回收。改 `Standalone Performant Preset` 的
`m_MSAA` 后重测上面两个数字即可。
