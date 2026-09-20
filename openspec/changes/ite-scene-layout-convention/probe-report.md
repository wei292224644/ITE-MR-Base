# Probe Report: ite-scene-layout-convention

> Generated: 2026-09-20
> Summary: 5 questions, 6 decisions, 4 open assumptions
> 起因：会话原本在 probe 扫码轴向问题（见 `openspec/changes/marker-anchor-axis-correction/probe-report.md`），
> 用户中途改为先整理 ITE 的两个场景。轴向问题的调查结论已另存，不在本次范围内。

## 现状（读码所得，非推测）

两个 ITE 场景的根层（解析场景 YAML 得到，Editor 未开）：

```
IteTour.unity（上机）                    IteTourSpace.unity（桌面迭代）
- ITE Tour Rig  [prefab]                - ITE Tour Rig  [prefab]   ←同一个
    ITE Host {IteHostBootstrap}             ITE Host {IteHostBootstrap}
    AnchorRoot → TourRoot                   AnchorRoot → TourRoot
- ITE HMD Panel {IteHmdPanel}           - Editor Rig
- ITE Device Marker Rig                     Camera {IteEditorFakeScan, IteEditorFly, Camera}
    {IteDeviceMarkerRig}                - Debug HUD {IteEditorHud}
  （AxisGizmo 作为预制体覆盖             - Directional Light
    挂在 AnchorRoot 下）
```

- 共享装配已抽成 `Assets/Prefabs/ITE/IteTourRig.prefab`（`IteTourRig / ITE Host / AnchorRoot / TourRoot`）。
- 两场景的真实差异只有一件事：**谁提供相机与扫码源**（上机真相机 + `IteDeviceMarkerRig`，桌面假相机 + `IteEditorFakeScan`）。
- `MRCore.unity` 根层已存在分组约定：`-- Management --` / `-- Lighting --` / `-- UI --` / `-- XR --`。

## Confirmed decisions

### Scope & intent

- **Question**: "重新整理场景"指哪种组织方式——(A) 差异部分收成 harness 预制体、(B) 根层分组命名约定、(C) 合并成单场景？
- **AI recommendation**: A（两场景各剩两个根对象，结构约束靠预制体锁住）；对 B 持保留（每场景才 3–4 个根对象）；反对 C（桌面脚手架会进包，多一条只在真机现形的失效路径）。证据：`Assets/Prefabs/ITE/IteTourRig.prefab`、`IteBootstrap.cs:63-80`
- **User confirmation**: "B + A 吧"——两者都要。

- **Question**: 范围是否只限两个 ITE 场景？
- **AI recommendation**: 只动 `IteTour.unity` 与 `IteTourSpace.unity`；`MRCore` 已符合约定（约定来源），`MarkerHookTest` 根层只有一个对象、套分组头负收益，Demo 场景（`GsplatBench` 等）是可整块移除的临时物。场景 YAML 的 diff 基本不可审阅，横跨四五个场景时出问题分不清是哪个搭坏的。证据：`Assets/Scenes/MarkerHookTest.unity` 根层仅 `MarkerHookTestRig`；`.claude/CLAUDE.md` 关于 `GsplatBench` 的段落
- **User confirmation**: 可以。

### Design

- **Question**: harness 预制体横跨 UI 与 XR 两个分类，只能放进一个分组——分组与预制体边界如何对齐？
- **AI recommendation**: 分组按"属于哪类"，预制体边界按"换平台时整块换掉的是什么"，根层对齐成：

  ```
  IteTour.unity                        IteTourSpace.unity
  - -- Management --                   - -- Management --
      ITE Tour Rig  [prefab]               ITE Tour Rig  [prefab]   ←同一个
  - -- Harness --                      - -- Harness --
      ITE Device Harness [prefab]          ITE Editor Harness [prefab]
        ITE Device Marker Rig                Camera {IteEditorFakeScan, IteEditorFly}
        ITE HMD Panel                        Debug HUD
                                             Directional Light
  ```

  即新增 `-- Harness --` 分组（`MRCore` 没有，因为它没有"整块可替换的上机/桌面脚手架"这个概念），UI/XR 分类下沉到 harness 预制体内部。灯光进 editor harness 而非单开 `-- Lighting --`：它只为桌面迭代存在，跟 harness 一起换掉最干净。
  `AxisGizmo` 不动，仍挂在 `AnchorRoot` 下——它画的就是锚点自己的坐标系。
  证据：`MRCore.unity` 现有分组名；`.claude/CLAUDE.md` 关于 `AxisGizmo` 的段落
- **User confirmation**: 可以（含 (a) 新增 `-- Harness --`、(b) 灯光进 editor harness）。

- **Question**: 分组头带来的新风险如何守？
- **AI recommendation**: 分组空物体与预制体根**必须留在世界原点、无旋转、无缩放**。`IteBootstrap.Validate()`（`IteBootstrap.cs:73-79`）检查 `AnchorRoot.parent.localToWorldMatrix == identity`，但只在**运行时**跑；分组头是新增的、最容易被手滑拖歪的一层，搭坏的表现是"内容错位、无日志"——本工程最不想要的失效类型。故补一条 EditMode 守卫测试，打开两个场景断言：(a) 分组头与预制体根 `localToWorldMatrix == identity`；(b) `TourRoot.parent == AnchorRoot`。放 `Assets/Tests/EditMode/`，按一模块一 asmdef 的惯例进 `MRBase.Ite.Host.Tests`。
- **User confirmation**: 可以。

### Impact

- **Question**: 跨预制体引用怎么办？四个脚本都持有指向 `IteHostBootstrap` 的序列化引用，而 `IteHostBootstrap` 在**另一个**预制体（`IteTourRig`）里；预制体资产存不了场景引用。
- **AI recommendation**: 现有两个场景不会坏（实例覆盖仍在），坏的是"以后往新场景里拖一个 harness 预制体"：

  | 脚本 | 字段 | host 为 null 时 |
  |---|---|---|
  | `IteDeviceMarkerRig.cs:28` | `host` | 有兜底 `FindFirstObjectByType` + `LogError`（:60,:65） |
  | `IteHmdPanel.cs:26` | `host` | 有兜底（:65） |
  | `IteEditorFakeScan.cs:15` | `host` | **静默**：`EnsureSession()` 的 `if (host != null)` 跳过 `AttachMarkerSession`（:56-59），会话建了没人接 |
  | `IteEditorHud.cs:14` | `host` | **静默**：`TryHook()` 在 `host == null` 时直接 return（:43），HUD 永远空 |

  故给 `IteEditorFakeScan` 与 `IteEditorHud` 补上与另外两个一致的兜底（`FindFirstObjectByType<IteHostBootstrap>()` + 找不到时 `Debug.LogError`），让 harness 预制体自己接线，场景连线退化成加速路径。模式已在仓库里（`IteDeviceMarkerRig.cs:58-66`），非新发明。
- **User confirmation**: 可以——两处兜底算在本次改动内。

### Success criteria

- **Question**: 完成判据是否止于离机（不打包）？
- **AI recommendation**: 三条，全部离机：
  1. `Unity -runTests -testPlatform EditMode -assemblyNames MRBase.Ite.Host.Tests` 全绿，含新增守卫测试。
  2. Editor 里打开 `IteTourSpace.unity` 进 Play，按假扫码键，`IteEditorHud` 显示出装配好的 tour——这条正是上面两处静默失效的现场复现。
  3. `IteTour.unity` 只做静态检查（层级正确、`IteHostBootstrap` 引用未变红），**不打包上机**。

  真机验证攒到处理轴向问题时一起出包：场景重组不改任何运行时语义，单独烧一次 Quest/PICO 打包不划算。
- **User confirmation**: 可以。

## Open assumptions [NEEDS CLARIFICATION]

- [ ] `[ASSUMED]` 分组头用 `MRCore.unity` 的字面写法 `-- Management --`（两侧各两个连字符 + 空格），新增的写作 `-- Harness --`。未与用户逐字确认。— affects: tasks.md、守卫测试里的名称断言
- [ ] `[ASSUMED]` 两个 harness 预制体落在 `Assets/Prefabs/ITE/`，命名 `IteDeviceHarness.prefab` / `IteEditorHarness.prefab`，与 `IteTourRig.prefab` 同目录同前缀。— affects: tasks.md
- [ ] `[ASSUMED]` 守卫测试用 `EditorSceneManager.OpenScene` 打开两个场景做断言，进 `MRBase.Ite.Host.Tests`（该 asmdef 已存在，见 `Temp/obj/MRBase.Ite.Host.Tests`）。若该 asmdef 没有引用 `UnityEditor`，需改为新建一个 Editor 测试 asmdef。— affects: design.md、tasks.md
- [ ] `[ASSUMED]` 两个 ITE 场景当前在工作区已是 modified 状态（`git status` 显示 `M Assets/Scenes/IteTour.unity`、`M Assets/Scenes/IteTourSpace.unity`），本次重组会叠在那些未提交改动之上。未确认那些改动是否要先提交或丢弃。— affects: 执行前的第一步

## 执行前提

`unity status --project-path .` 当前**没有 Editor 连着**。按 `.claude/CLAUDE.md`，所有场景/预制体改动必须打进用户正开着的 Editor（`unity command eval`），不另起 Unity 进程。执行阶段第一步是让用户把该工程的 Editor 开起来。

## Suggested next step

- [ ] 运行 `/opsx:propose ite-scene-layout-convention` 生成 artifacts（会读取本报告）
