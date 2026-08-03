## Why

MR 场景需要一面「年久失修的刻字石板」：铭文褪色、封存尘垢，一触引圣光后朽壳溃散成灰，石板与铭文焕发当年色彩。项目已有 Dissolve-FX-MasterKit，但缺少围绕「一触引圣光 → 壳溃成灰 → 铭文复色苏醒」的可复用交互与视觉约定。

## What Changes

- 新增「刻字石板圣物苏醒」表现：平面石板 + 封存壳溃散 + 铭文从褪色失修恢复到当年色彩
- 双层结构（封存壳 + 石板本尊）、一触触发、圣金色 Burn 溃散、大块→细灰两段 VFX、复色后安静收束
- 触发为仪式性「引光」（手部/近交互触碰），非字面场景光源（首版）
- 基于 `Dissolver` / `DissolverVFX` / Burn Dissolve / Standard Dissolve VFX；不做手绘擦拭、不做预碎刚体
- 可挂 Prefab 与可调参数（时长、金色、触点蔓延、块/灰、复色曲线）

## Capabilities

### New Capabilities

- `sacred-relic-awaken`: 刻字石板封存壳溃散、圣光引触与铭文复色苏醒的交互与视觉行为

### Modified Capabilities

- （无）

## Impact

- 依赖：Dissolve-FX / MasterKit；XR Interaction Toolkit / XR Hands
- 新增：平面刻字石板 Prefab（壳 + 本尊）、圣金 Burn、两段 VFX、`SacredRelicAwaken` 驱动
- 美术：封存壳（尘垢/锈败）；本尊石材；**铭文褪色态 + 当年彩色态**（贴图混合或双套铭文色，苏醒时 lerp/reveal）
- 不以「洗干净的白石」为终点，而以「铭文色彩归来」为高潮

## Open Assumptions

- [DECIDED] 形态为一面平面刻字石板（可立可卧），上有铭文；非匣式圣物盒（用户确认「选 1 / 平面石板」）
- [DECIDED] 苏醒终点是铭文从年久褪色恢复到「当年色彩」，不只是变干净可读
- [DECIDED] 本阶段目标是 **Demo 表现验证**（能触发展示完整节拍即可），非整条产品管线
- [DECIDED] 触发：「手部触碰/Poke = 引光」；Editor 可用点击/按键模拟，不做真实光照检测；**验收不要求真手 Poke**
- [DECIDED] 「大块→细灰」用 VFX 两段粒子，不做预碎刚体
- [DECIDED] 金光为暖金/蜂蜜金自内渗出，结束后余晖收尽；**金只用于引光/溶解边**
- [DECIDED] 铭文复色以 **朱砂朱红为主**（可点缀石青）；非描金铭文
- [DECIDED] Demo 铭文几何用 **平面字（TMP/贴图）**；浅浮雕后置
- [DECIDED] **本 Demo 不做音效**
- [DECIDED] 本尊始终存在；壳溃后驱动铭文复色（材质/TMP lerp），非末帧生成新石板
- [DECIDED] Demo 单次苏醒 + 调试 Replay（空格/点击触发，R/Reset 回 Sealed）
- [DECIDED] Demo 验收：完整序列可读「圣醒+朱红字醒」；再触不重播；Reset 可重看
- [ASSUMED] 壳材质可用 INab Burn 或轻量 dissolve 回退，以 Editor 能最快跑通节拍为准（见 probe-report）
- [ASSUMED] Demo 场景默认立碑视角摆放
- [ASSUMED] 铭文文案为占位短句，非正式文案
