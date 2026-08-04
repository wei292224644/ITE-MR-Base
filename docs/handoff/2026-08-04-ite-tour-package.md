# Handoff — ite-tour-package（阶段 1–3 已完成）

日期：2026-08-04
分支：`feature/palms-together-gesture-trigger`
Change：`openspec/changes/ite-tour-package/`
进度：**14/71 任务**（阶段 1、2、3 全部完成；阶段 4 未开始）
测试：`Uality.IteTour.Tests` **32/32 通过**

---

## 一句话背景

把 `/Users/wwj/Desktop/unity/ite-space-tour/Assets/Scripts/ITE`（3061 行 ITE 空间导览业务）封装成 **可移植的 Unity embedded package** `Packages/com.uality.ite-tour/`，同时适配 Quest 与 PICO。原实现零 asmdef、硬绑 `OVRManager` 与宿主单例。

**先读这三份，不要重新推导**：

| 文件 | 内容 |
|---|---|
| `openspec/changes/ite-tour-package/proposal.md` | 范围、Non-Goals、影响 |
| `openspec/changes/ite-tour-package/design.md` | **13 条决策 D1–D13 + 风险表**，所有"为什么这么做"都在这 |
| `openspec/changes/ite-tour-package/tasks.md` | 12 阶段 71 任务，勾选状态即真实进度 |
| `openspec/changes/ite-tour-package/specs/ite-tour-package/spec.md` | 11 个需求 53 个场景（验收依据） |

---

## 架构一句话

```
Packages/com.uality.ite-tour/        引用面只有官方包，零 MRBase.*，零平台 SDK
        ▲
        │ 单向（包对宿主零知识）
Assets/Scripts/IteHost/              平台差异 100% 落在这层（阶段 9，未开始）
```

对外 API 面：**装配对象 ×1 + 推入方法 ×2 + 广播事件 ×7，接口数量 0，必需行为委托 0**。

- 推入：`SubmitMarkerScan(markerId, pose)`、`SetHeadsetMounted(bool)`
- 广播：`OnLoadProgress` / `OnSpaceSceneLoaded` / `OnInitialized` / `OnTourActivated` / `OnTourDeactivated` / `OnTourSceneLoaded` / `OnScanPromptChanged`

**明确不做**（design D6）：多人共址网络（Colyseus，2313 行）不迁入，`io.colyseus.sdk` 不装。依赖方向本就是「网络 → ITE」。

---

## 已完成（阶段 1–3）

```
阶段 1 包骨架    1.1 package.json    1.2 UPM 传递解析    1.3 Runtime asmdef
                 1.4 Tests asmdef    1.5 目录骨架
阶段 2 内部工具  2.1 基础工具        2.2 动画控制器      2.3 圆角 UI 自实现
                 2.4 边界守卫测试
阶段 3 数据层    3.1 数据模型        3.1b 组件数据类     3.2 三个转换器
                 3.3 转换器测试      3.4 坐标快照测试
```

当前包内文件：

```
Packages/com.uality.ite-tour/
├── package.json                    依赖：newtonsoft 3.2.2 / gltfast 6.19.0 / sharp-zip-lib 1.4.2
├── Runtime/
│   ├── Uality.IteTour.asmdef       references: glTFast, Unity.SharpZipLib.Utils, UnityEngine.UI
│   ├── Internal/                   EventEmitter, Matrix4x4Extensions, HierarchyBoundsCalculator,
│   │                               BoxColliderWireframeDrawer, LegacyAnimationController,
│   │                               AnimationAudioController, RoundedBoxUIProperties + .shader
│   ├── Data/                       IteSpaceScene.cs, IteTourData.cs
│   │   └── Components/             ElementData.cs, TriggerData.cs, ActionData.cs
│   ├── Convert/                    AssetConverter, ComponentConverter, ComponentActionConverter
│   └── {Config,Core,Components,Prefabs}/   空（阶段 4–8）
└── Tests/Editor/                   PackageBoundaryTests(4), CoordinateConversionTests(6),
                                    ConverterTests(22), AssemblyBoundaryPolicy
```

命名空间映射：

```
ITETourAsset     -> Uality.IteTour.Data.Assets
ITETourComponent -> Uality.IteTour.Components
ITETourData      -> Uality.IteTour.Data
全局 IteSpaceScene / IteSpaces -> Uality.IteTour.Data
转换器           -> Uality.IteTour.Serialization   ← 不叫 .Convert，会遮蔽 System.Convert
```

---

## 实施中对计划做的 4 处偏差（已写回 tasks.md）

1. **`ComponentsUtils` 从 2.1 移到 6.7** — 泛型约束 `where T : BaseComponent` 与整张注册表都依赖组件层类型。
2. **新增任务 3.1b，按数据/行为拆开组件文件**（用户拍板）— 转换器分派表引用 18 个具体类型，源工程把数据类与 MonoBehaviour 混在同一文件。拆开后阶段 3 自足且可离机全测。**注意：包只有一个程序集，Data 与 Components 之间没有编译顺序约束，这纯粹是任务切分。**
3. **2.4 从「grep 检查」升级为「EditMode 测试」** — Packages→Assets 的编译禁令拦不住「引用另一个平台**包**」（`com.meta.xr.sdk.core` 就是包，加进 asmdef 能编译通过却毁掉可移植性与 PICO）。
4. **3.3 任务文修正** — 原写「三个转换器未知类型都不抛异常」，与源实现不符，改为按实际行为表征。

---

## 阻塞/待决事项（用户 2026-08-04 已裁定）

### ① `ComponentConverter` 未知类型抛异常 —— **用户裁定：可接受，保持现状**

```csharp
Component result = type switch { ... _ => null };
serializer.Populate(obj.CreateReader(), result);   // null → ArgumentNullException
```

后果：服务端新增任何组件类型，已发布客户端**整个 Tour 加载失败**，不是跳过该组件。
已由 `ConverterTests.ComponentConverter_UnknownTypeThrows_KnownForwardCompatibilityLandmine` 钉住。
**将来若要修（一句 null 判断），该测试会变红——那是预期的，说明是一次有意的行为变更。**

### ② `ApproximateTrigger` 近距离判定从未实现 —— **用户裁定：记录，后续单独改**

`Position`（且错用 `System.Numerics.Vector3` 而非 `UnityEngine.Vector3`）与 `Radius` 两个字段无任何消费方，`ApproximateTriggerUnityComponent.Constructor` 只读 `Actions`。当前行为等同 `LoadTrigger`。
**本次不改**，已记入 `tasks.md` 的 11.2 TODO 清单。

---

## 下一步：阶段 4（配置与内容管线）

```
4.1 IteRuntimeConfig ScriptableObject（URL / 场景名 / 4 个 prefab 引用）
4.2 确认 ItePropertiesUrl（projectDirectory.json）是否死配置——源工程 ITE 代码中未见消费方
4.3 包内实现下载与解压（UnityWebRequest + sharp-zip-lib）
4.4 包内实现资源加载（JSON 文本 / Sprite / AudioClip / glb）
4.5 迁移 IteSpaceManagerAssets 的管线逻辑
4.6 版本缓存键改 ite.tour.{tourId}.version + 测试锁定键名（design D10）
4.7 离线路径
4.8 场景资源加载（logo、Tour 预览图），保持 fire-and-forget 时序
```

源文件参考：`/Users/wwj/Desktop/unity/ite-space-tour/Assets/Scripts/ITE/IteSpaceManagerAssets.cs`

管线形态（design D4）：

```
空间场景 zip ──下载──解压──► {scene}.json
每个 tour：查服务端最新版本 ──► 比对本地缓存 ──► 不同则下载解压 + 写版本
           {tourId}.json ──Newtonsoft + 3 个 Converter──► IteTour
           资源加载：glb / mp3 / png / mp4 ──► 实例化 TourObject
```

**包不向宿主索取任何 IO 能力**，zip 解压属 ITE 业务范畴（用户拍板）。唯一可选委托是 `IsNetworkAvailable`（离线是宿主的产品策略）。

---

## 新会话必须知道的操作细节

**测试怎么跑**：Unity Editor 通过 MCP 可达。

```
mcp__UnityMCP__refresh_unity(mode=force, scope=all, compile=request, wait_for_ready=true)
mcp__UnityMCP__run_tests(mode=EditMode, assembly_names=["Uality.IteTour.Tests"])
mcp__UnityMCP__get_test_job(job_id=..., wait_timeout=60)
```

- `refresh_unity` 60s 超时很常见（UPM 拉包 / 域重载），**超时不等于失败**，用 `read_console` 复核。
- `read_console` 偶尔返回 `Unity session not ready ... please retry`，重试即可。
- Console 里长期有一批**无关**噪声：其它包的 `default.profraw has no meta file`，以及 MCP WebSocket 重连告警。判断编译是否干净用 `filter_text="error CS"`。

**提交纪律**（本 change 沿用）：
- 每个任务一个提交，消息格式 `task(ite-tour-package): <N.M> <标题>`
- 显式 `git add <file>`，**绝不用** `git add -A/./-u`
- 会话开始就存在的脏文件不碰。当前工作区里 `Assets/Scenes/MRCore.unity`、`Assets/Scripts/Core/PalmsTogetherGesture.cs`、`Assets/Scripts/Localization/Native/Probe/*`、`openspec/changes/palms-together-gesture-trigger/*`、`docs/*.md` 等均属**他人/并行工作**，与本 change 无关。
- Unity 常在 stage 之后才生成 `.meta`，收尾时 `git status --porcelain Packages/com.uality.ite-tour/` 补一次。

**asmdef 两个坑**：
- 测试程序集 `overrideReferences: true` → 只有 `precompiledReferences` 里列出的 DLL 可见。已含 `nunit.framework.dll` + `Newtonsoft.Json.dll`，再用别的 DLL 要补。
- Runtime 程序集 `overrideReferences: false`，Newtonsoft 以自动引用 DLL 提供（`isExplicitlyReferenced: 0`），无需显式声明。

**依赖声明只有一个来源**：包自己的 `package.json`。**不要**往工程 `Packages/manifest.json` 的 `dependencies` 里加本包或其依赖（manifest 里只该有 `testables` 那一条）。这样移植时依赖才跟着包走。

---

## 后续阶段还没碰的大件

| 阶段 | 内容 | 已知风险 |
|---|---|---|
| 5 | 运行时核心（IteTourObject / Entity / Tour 切换状态机） | 需把 tag 查找改为注入 Transform |
| 6 | 12 个组件的 MonoBehaviour 部分 | **`GLTFast.ComponentType` 与 `Components.ComponentType` 的 CS0104 二义性**（design D9，任务 6.3）——必须写全限定名 |
| 7 | `IteRuntime` 对外 API 面 | — |
| 8 | 4 个 prefab + 材质 | prefab 可能还挂着未发现的 Meta/宿主脚本；圆角材质需指向包内新 shader `Uality/IteTour/RoundedBoxUI` |
| 9 | 宿主适配层 | PICO `userPresence` 无实证（design D7） |
| 10 | 真机验证 | Quest + PICO 双端 |
| 11 | 文档 + `Documentation~/TODO.md` | TODO 清单已在 tasks.md 11.2 累积，含本文档两项裁定 |
| 12 | 验收 | **12.6：复制包到空白工程只装 3 个依赖确认编译通过——这是"可移植"唯一诚实的验证** |
