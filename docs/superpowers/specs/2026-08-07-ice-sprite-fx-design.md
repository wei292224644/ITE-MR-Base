# 冰灵出现 / 消失 / 传送特效 —— 设计

> 状态：已评审，待实施
> 日期：2026-08-07
> 范围：**纯特效测试台**。不接手势，不接业务，不做锚点定位。

## 1. 目标

复刻《原神》派蒙式的「凭空物质化」演出，作用于测试模型
`Assets/Assets/Models/ice-sprite/source/pet_501001.fbx`（skinned mesh + generic rig，自带 anim stack，单张贴图）。

提供三个可在编辑器里反复触发的动作：

- `Appear()` —— 星屑聚合，本体物质化，弹性落位
- `Vanish()` —— 反向：本体溶解成星屑飘散
- `TeleportTo(target)` —— 三种风格，Inspector 下拉切换现场比较

不在本次范围内：手势触发、锚点/位置策略、跟随、常驻 idle 行为、上设备的性能优化。

## 2. 效果拆解

派蒙出场是五层叠加，不是单个粒子特效：

| 层 | 表现 | 时间 |
|---|---|---|
| 1. 星屑汇聚 | 贴着模型表面的星点浮现 | 0 → 0.4s |
| 2. 闪光爆点 | 中心一次强曝光 | 0.4s |
| 3. 本体显现 | 模型从溶解态扫成实体，边缘描金 | 0.35 → 0.8s |
| 4. 弹性落位 | scale 小→大 overshoot 回弹 | 0.4 → 0.9s |
| 5. 余屑飘散 | 剩余星点向外上方飘、淡出 | 0.8 → 1.5s |

**关键点**：观感来自第 3 层（dissolve）+ 第 4 层（弹性曲线）。纯粒子会像烟花，不像「物质化」。
第 1/5 层的星屑必须**贴着蒙皮网格表面**生成、随骨骼动画走，否则脱节。

## 3. 复用与自写的边界

第三方资产 `Assets/INab Studio/`（Dissolve FX + Dissolve FX Master Kit，Asset Store 付费包，已在仓库内）
已经覆盖第 1、3 层以及两者的数值同步。

| 层 | 来源 |
|---|---|
| 本体溶解 / 显现 | INab `Standard Dissolve.shadergraph` |
| 表面星屑（蒙皮采样） | INab `Skinned Standard Materialize / Dissolve Template.vfx` |
| 材质与 VFX 数值同步 | INab `DissolverVFX.cs` |
| 时间轴 + 曲线 + duration | INab `Dissolver.cs`（`Materialize()` / `Dissolve()`） |
| **弹性落位** | 自写，一条 `AnimationCurve` |
| **闪光爆点** | 自写，`Light` 强度脉冲（走已配好的全局 Bloom） |
| **传送编排** | 自写，三种 style |
| **键盘测试驱动** | 自写 |

`Appear()` 本质就是 `Dissolver.Materialize()`。自写部分只补 MasterKit 没有的四件事。

## 4. 决策记录

### 决策 1：用 MasterKit 的溶解体系，不自写 shader 与 mesh 采样

**选了什么**：本体溶解走 INab shadergraph，表面星屑走 INab 的 skinned mesh 采样 VFX。

**替代方案**：自写 dissolve shader + 自写 uniform mesh 采样 compute buffer。

**为什么否决**：本仓库已经照着 MasterKit 的烘焙格式写过一遍 ——
`Assets/Scripts/SacredRelic/RelicDustVfx.cs` 里的 `TriangleSample` 结构体
（`Vector2 coord` + `uint index`）与 `UniformMeshBuffer` 就是 MasterKit `UniformMeshBaker` 的格式。
不是引入新依赖，是既有技术栈。

### 决策 2：主用 Skinned **Standard** 模板，同时接上 Axis 供切换

**选了什么**：`Skinned Standard Materialize / Dissolve Template.vfx` 为默认；
`DissolverVFX` 的 `dissolveEffect` / `materializeEffect` 字段同时准备 Axis 版本，Inspector 拖拽切换。

**替代方案**：只用 Axis；或只用 Standard。

**为什么这样选**：

- **形状匹配**：Standard 是噪声阈值，全身各处同时聚合 —— 派蒙出现是**无方向性**的整体物质化。
  Axis 是沿轴扫描带，观感是「从地面长出来」，那是召唤阵 / 祭坛的语言，不是精灵凭空出现的语言。
- **但 Axis 要留着**：skinned 能直接用的 vfx 只有 4 个模板；而 Core 编号变体里
  Axis 1–12 是 Dissolve + Materialize **成对**，Standard 只有 1 号带 Materialize，2–12 全是 Dissolve。
  想快速试观感，现成参数只能从 Axis 那边抄。测试台的目的就是比较，两条线都接的成本是零代码。

**明确不成立的理由（曾考虑过，已证伪）**：
「Standard 的噪声在世界空间采样，角色移动时溶解带会游」—— 不成立。
`Standard Dissolve.shadergraph` 有 `Use Triplanar UVs` 与 `Triplanar Space` 开关，设为 Object 即稳定。
不要拿这条当选型依据。

### 决策 3：新脚本不建 asmdef，落 Assembly-CSharp

**选了什么**：新脚本放 `Assets/Scripts/IceSpriteFx/`，**不建 asmdef**。

**替代方案**：放进 `MRBase.Transitions` asmdef；或给 INab 补一个 asmdef；或用 UnityEvent / 反射解耦。

**为什么否决**：`Assets/INab Studio/` 没有 asmdef，`Dissolver` 与 `DissolverVFX` 在 `Assembly-CSharp` 里。
asmdef 引用不到 `Assembly-CSharp`（方向是反的），所以放进任何现有 asmdef 都编译不过。
给第三方资产补 asmdef 会污染其升级路径；为一个测试台造反射/事件间接层是纯粹的过度设计。
测试台不需要程序集边界。

**若日后转正**：届时再给 INab 补 asmdef，或把编排层抽进 `MRBase.Transitions`
并用接口隔开对 `Dissolver` 的依赖。现在不做。

### 决策 4：新建独立测试场景，不复用 EffectShowcaseDemo

**选了什么**：新建 `Assets/Scenes/IceSpriteFxTest.unity`。

**替代方案**：复用已有的 `Assets/Scenes/EffectShowcaseDemo.unity` + `EffectShowcase.cs`。

**为什么否决**：`EffectShowcase` 是 prefab 轮播器（`Next` / `Previous` / `Show(index)`，71 行），
语义是「切换多个特效 prefab」；冰灵测试台是「对同一个角色跑三种动作」，且传送需要场景里的锚点。
塞进去要跟轮播的启停逻辑抢控制权。

### 决策 5：传送三种风格全做，枚举切换

**选了什么**：`TeleportStyle` 枚举，三种都实现。

**为什么**：测试台的产出是「哪种好看」这个判断，不是「跑通一种」。
成本集中在 `TrailFlight` 的路径粒子，另两种几乎是编排。

## 5. 组件设计

```
Assets/Scripts/IceSpriteFx/          ← 无 asmdef，落 Assembly-CSharp
├── IceSpritePresence.cs             ← 编排层
└── IceSpriteFxTestInput.cs          ← 键盘驱动
```

### IceSpritePresence

对外只有三个动作和一个风格开关：

```csharp
public enum TeleportStyle { DissolveReform, TrailFlight, Afterimage }

public void Appear();
public void Vanish();
public void TeleportTo(Vector3 target);
public TeleportStyle style;
```

内部职责：

- 转调 `Dissolver.Materialize()` / `Dissolve()`
- 叠加弹性 scale 曲线（`AnimationCurve`，overshoot 后回弹）
- 叠加 `Light` 强度脉冲（爆点）
- 按 `style` 编排传送

它**不**碰 dissolve 的实现细节，也不碰 VFX 的参数 —— 那些归 `Dissolver` / `DissolverVFX` 的 Inspector。
调参在 Inspector，逻辑在代码，两者不混。

### 三种传送

| Style | 实现 | 新增资产 |
|---|---|---|
| `DissolveReform` | `Vanish()` → 等 `Dissolver.duration` → 移位 → `Appear()` | 无 |
| `TrailFlight` | `Vanish()` → 拖尾粒子沿 A→B 直线飞 → 到点 `Appear()` | 1 个 ParticleSystem |
| `Afterimage` | 立即移位到 B；A 点留 `SkinnedMeshRenderer.BakeMesh()` 快照 + 半透材质淡出 | 1 个快照 GameObject |

`BakeMesh()` 每次传送调用一次，成本可忽略。

### IceSpriteFxTestInput

键盘：`1` = Appear，`2` = Vanish，`3` = TeleportTo(下一个锚点)。
沿用 `SacredRelicTrigger` 的既有形状（`#if ENABLE_INPUT_SYSTEM` 双分支）。

## 6. 编辑器装配步骤

1. 从 `pet_501001.fbx` 提取材质（当前 `materialLocation: 1`，外部材质）。
   以 `Core URP/Dissolve Materials/Skinned Mesh Templates/1/Skinned Standard 1.mat` 为起点复制一份，
   `_BaseMap` 换成 `pet_501001.png`。
2. 克隆 `Skinned Standard Materialize / Dissolve Template.vfx`，绑定冰灵的 `SkinnedMeshRenderer`。
   同时克隆一份 Axis 版本备用。
3. 挂 `Dissolver` + `DissolverVFX` + `IceSpritePresence` + `IceSpriteFxTestInput`。
4. 新场景 `Assets/Scenes/IceSpriteFxTest.unity`：地面 + 光 + 冰灵 + 两个传送锚点。
   编辑器内 Play 即可，暂不进 MRCore 场景切换菜单。

### 开放项：噪声源模式

`Standard Dissolve` 的噪声有两种模式，装配时两种都要试：

- **Triplanar**（三平面投影）—— 不依赖 UV 质量
- **Guide Texture**（走模型 UV）—— 动画下更稳、更便宜

冰灵是成品游戏角色模型，UV 大概率完整，预期 Guide Texture 更优。以实测为准。

## 7. 验证

编排逻辑（传送状态推进、目标位置计算）不依赖渲染，用 EditMode 测试覆盖：

- `TeleportTo` 在三种 style 下最终位置都等于 target
- 传送进行中重复调用不会把状态机推乱

视觉部分靠测试场景人工看，不写自动化测试。

## 8. 风险

VFX Graph 是 compute 驱动，PICO / Quest 上有实际成本。测试台阶段不管，
但转正前必须测 App 时间 —— 本项目曾为 bloom 从 7.5ms 压到 4.2ms（`eab3a0a`），渲染预算是紧的。
