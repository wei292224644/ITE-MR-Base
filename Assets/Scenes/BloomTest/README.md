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
| `Standalone Performant Preset` / `Standalone Balanced Preset` | `m_SupportsHDR: 1`、`m_HDRColorBufferPrecision: 1`（64bit R16G16B16A16）、`m_AllowPostProcessAlphaOutput: 1`、`m_MSAA: 1`（关，见下方抗锯齿对照） |
| `Performant / Balanced URP Renderer Config` | `postProcessData` 指向 URP 内置 |
| MRCore 的 Main Camera | `renderPostProcessing: true`、`antialiasing: FXAA`、`clearFlags: SolidColor`、背景 alpha 0 |
| `Assets/Settings/MR Bloom Profile.asset` | threshold 1.0、Fast Mode、maxIterations 4 |

**色彩缓冲精度必须是 64bit。** 32bit 是 R11G11B10，没有 alpha 通道，而背景
alpha = 0 正是虚拟内容与 passthrough 合成的前提。

## 坑

自发光材质的 `globalIlluminationFlags` 必须是 `RealtimeEmissive`。设成 `None`
或 `EmissiveIsBlack`，Unity 保存时 `MaterialEditor.FixupEmissiveFlag` 会剥掉
`_EMISSION` 关键字，自发光**静默失效** —— 材质面板看着是对的，就是不亮。

## 开销与抗锯齿（Quest 3 实测，VrApi 帧统计，BloomTest 场景）

同一构建，只改抗锯齿配置：

| 配置 | App 时间 | GPU | CPU&GPU |
|---|---|---|---|
| MSAA 4 | 7.47 ~ 7.54 ms | 77 ~ 78% | 14.7 ~ 15.3 ms |
| MSAA 2 | 4.86 ~ 4.99 ms | 58 ~ 59% | 8.7 ~ 9.5 ms |
| **MSAA 关 + FXAA**（当前配置） | **4.14 ~ 4.21 ms** | **53%** | 7.9 ~ 10.3 ms |

三档都稳定 72fps。72Hz 的单帧预算是 13.9ms —— MSAA 4 时 `CPU&GPU` 已经超预算，
靠合成器兜着。

参照：GroundUpRevealDemo（无 bloom 触发）是 1.4 ~ 1.8ms / GPU 29 ~ 32%。本场景几何
可忽略（6 个球 + 6 个 TextMesh），差额基本都是 bloom 这条链的钱。

**为什么关 MSAA 反而能配后处理 AA：** bloom 已经强制了中间纹理和 UberPost pass，
FXAA 搭在同一个 pass 里跑，边际成本很小；而 MSAA 是另一笔独立开销 —— 每帧把多采样
颜色缓冲 resolve 出 tile 再写回，纯带宽。开销大头一直是这笔 resolve，不是模糊运算。
所以在「已经付了后处理钱」的场景里，post-AA 优于 MSAA，与常规 VR 建议相反。

没试 TAA：URP 的 TAA 在 XR 下支持不完整，且头动叠加 passthrough 重投影会拖影。
SMAA 比 FXAA 贵不少，FXAA 已够用就没往下试。

**换设备或改配置后要重看的两点**（编辑器测不出，只能真机）：
1. 边缘质量 —— FXAA 按亮度找边混色，比 MSAA 糊，VR 分辨率下会被放大
2. 轮廓有没有半透明描边或彩边 —— FXAA 在已 resolve 的颜色缓冲上混色，而这套依赖
   alpha=0 做 passthrough 合成。处理不当会在真实世界上糊出一圈边，比锯齿更难看
