## 1. 内容缓存

- [x] 1.1 查实可用的空间场景与 tour 清单（`thirdDemo`，5 个 tour，包体积与版本）
- [x] 1.2 摆好 `IteSpaceScene_thirdDemo/`（剥掉 zip 多出的一层，tours 裁剪为 `wm0l5qcn_ibd`）
- [x] 1.3 摆好 `wm0l5qcn_ibd/` tour 内容包（29 MB）
- [x] 1.4 写 `cache-layout.md`，步骤精确到文件

## 2. 配置

- [ ] 2.1 `Assets/Settings/ITE/IteRuntimeConfig.asset` 的 `sceneName` 改为 `thirdDemo`
- [ ] 2.2 确认 `tourObjectPrefab` 指向包内 `Runtime/Prefabs/Tour.prefab`，且该 prefab 上有 `IteTourObject`

## 3. 激活器

- [ ] 3.1 新增 `Assets/Scripts/IteHost/IteRenderSmokeDriver.cs`（`MRBase.Ite.Host` 命名空间）
  - 序列化字段：目标 `tourId`（默认 `wm0l5qcn_ibd`）、`sceneLoadTimeout`（秒）
  - 从同物体的 `IteHostBootstrap` 取 `Runtime`；`Runtime` 为 null 时报错并禁用自己
  - 订阅 `OnLoadProgress` / `OnSpaceSceneLoaded` / `OnInitialized` / `OnTourSceneLoaded`，统一 `[ITE Smoke]` 前缀
  - `OnInitialized` 后调一次 `ActivateTour(tourId)`；返回 false 时报错并列出可用 tourId
  - 激活后启动超时计时，超时仍未收到 `OnTourSceneLoaded` 则报错（`Enable()` 是 fire-and-forget，异常不冒泡）
  - `OnDestroy` 里退订
- [ ] 3.2 `IteHostBootstrap.Runtime` 在 `Start` 的 `await` 之前就已赋值，确认激活器的取用时机不会拿到 null（必要时改为等待一帧或订阅式）

## 4. 场景

- [ ] 4.1 新建 `Assets/Scenes/IteRenderTest.unity`
  - Main Camera（位置退到能看见 `TourRoot` 原点）、Directional Light
  - `AnchorRoot`（空）→ 子物体 `TourRoot`（空）
  - `ITE Host`：`IteHostBootstrap`（config / anchorRoot / tourRoot / xrCamera=Main Camera / `networkAvailable = false`）+ `IteRenderSmokeDriver`
- [ ] 4.2 确认场景**未**加入 `EditorBuildSettings`

## 5. 从 MRCore 摘除 ITE

- [ ] 5.1 `MRCore.unity` 删除 `ITE Host`：GameObject 1597420651、Transform 1597420652、MonoBehaviour 1597420653
- [ ] 5.2 从父节点 Transform 614014891 的 `m_Children` 移除 `{fileID: 1597420652}`
- [ ] 5.3 在编辑器中打开 `MRCore.unity`，确认层级正常、无 missing 引用、Console 无 `[ITE]` 输出

## 6. 验收

- [ ] 6.1 打开 `IteRenderTest.unity` 按 Play：日志依次出现 进度 → 场景描述已解析 → `OnInitialized` → `ActivateTour` → `OnTourSceneLoaded`
- [ ] 6.2 Hierarchy 中 `TourRoot` 下出现 `wm0l5qcn_ibd` 实例，其下有实体节点，节点上有 `EMWModelRender` / `LoadTrigger` / `PlayAnimationAction`
- [ ] 6.3 Game 视图中能看到 glb 模型（`ysodu11w_dow_sceneViewer.glb`）
- [ ] 6.4 断网重跑一次，结果一致
- [ ] 6.5 截图存档到 `openspec/changes/ite-render-smoke-scene/`

## 7. 收尾

- [ ] 7.1 若 6.3 不通过：按 spec 的"缺资源时不被误判为渲染失败"分段归因，把结论追加进 `design.md`
- [ ] 7.2 为 D5（空间场景 zip 解压层级不匹配）单开一个 change，形状约束见 `design.md` D5
