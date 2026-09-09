# Unity 多场景无缝切换方案

> **过时（方案未落地）**：本文推荐 Addressables + Bootstrap Scene。现行实现是常驻 `MRCore.unity` + `MRSceneDirector` Additive 切场景，没有 Addressables。不要按本文再搭第二条启动路径。详见 `.claude/CLAUDE.md` Architecture。

> 状态：方案摘要  
> 适用平台：PICO、Meta Quest 及其他 Android 一体机  
> 更新日期：2026-08-04

## 1. 目标

在大型 MR/VR 内容中，将不同主题或关卡拆分为独立场景，并利用隧道、气闸门、电梯等缓冲空间隐藏资源加载过程，实现玩家感知上的连续体验。

本方案的核心不是让切换过程“没有开销”，而是：

- 将下载、加载、激活和卸载分阶段执行。
- 将高开销步骤安排在玩家看不到前后场景的时间窗口内。
- 控制移动头显上的峰值内存和主线程卡顿。
- 保持 XR 原点、多人网络状态和物理空间坐标连续。

## 2. 总体结论

推荐使用以下 Unity 能力组合：

```text
常驻 Bootstrap Scene
    + Addressables 资源管理
    + Additive Scene 异步加载
    + 隧道/气闸门缓冲场景
    + 明确的切换状态机
```

不要使用 `LoadSceneMode.Single` 直接从场景 A 跳到场景 B，也不要把所有大型内容放进同一个 Scene 后仅靠启用和禁用根节点管理。

## 3. 场景划分

```text
Bootstrap.unity                 始终驻留
├── XR Origin
├── 物理世界原点与平台适配层
├── SceneStreamingManager
├── NetworkManager
├── GameStateService
├── 公共 UI 与音频
└── 公共对象池

World_A.unity                   按需加载和卸载
├── A 场景模型、碰撞和灯光
├── A 场景特效与音频
└── A 场景局部逻辑

Transition_Tunnel.unity         小型缓冲场景
├── 隧道主体
├── 入口门和出口门
├── 遮挡结构
├── 加载触发器
└── 切换反馈

World_B.unity                   按需加载和卸载
└── B 场景内容
```

### 3.1 Bootstrap Scene

Bootstrap 必须小且稳定，整个应用生命周期内不卸载。以下对象只允许存在一份：

- XR Origin、主相机和 AudioListener。
- PICO/Meta 平台 SDK 的全局管理对象。
- 多人网络管理器。
- 物理世界原点与公共坐标转换服务。
- UI、全局音频、存档和场景切换管理器。

不建议把大量对象随意放入 `DontDestroyOnLoad`。统一由 Bootstrap 管理，可以避免重复 EventSystem、重复监听器和销毁顺序不确定等问题。

### 3.2 World Scene

每个世界场景只保存本场景专属内容，包括：

- 静态模型和碰撞体。
- 烘焙光照、Light Probe 和 Reflection Probe。
- 场景专属音频、特效和玩法对象。
- 本场景的 NavMesh 或路径数据。

World Scene 不应直接引用另一个 World Scene 中的 GameObject。跨场景状态通过稳定 ID、数据对象或 Bootstrap 中的服务传递。

### 3.3 Transition Scene

缓冲场景必须保持轻量、独立，并能够完全遮挡前后世界。优先采用：

- 弯曲隧道。
- 双门气闸。
- 电梯或移动平台。
- 短暂扫描、验证或机关交互。

入口门关闭后，玩家不能再看到或返回旧场景；出口门只有在新场景完成激活和多人同步后才能打开。

## 4. 两种切换策略

### 4.1 重叠加载

适用于场景较小、设备内存充足的情况。

```text
Bootstrap + Tunnel + World_A + 正在加载的 World_B
```

流程：

1. 玩家接近隧道时预加载 World_B。
2. 玩家进入隧道并关闭入口门。
3. 激活 World_B。
4. 打开出口门。
5. 异步卸载 World_A。

优点是等待时间短，缺点是切换期间 A、B 同时占用内存。

峰值内存需要按下式评估：

```text
峰值内存 ≈ 常驻资源 + World_A + World_B + Tunnel + 加载临时开销
```

### 4.2 门控卸载

这是 PICO/Quest 等移动头显的默认推荐策略。

流程：

1. 玩家仍在 World_A 时，只预下载 World_B 的依赖到本地缓存。
2. 玩家进入隧道。
3. 关闭入口门并禁止返回。
4. 卸载 World_A，释放其 Addressables 引用。
5. 加载并激活 World_B。
6. 完成坐标、光照、物理和多人状态初始化。
7. 打开出口门。

它会牺牲少量隧道内等待时间，但显著降低峰值内存和系统杀进程风险。

## 5. 推荐切换时序

```text
World_A Active
    ↓ 玩家接近入口
DownloadDependencies(World_B)
    ↓ 玩家进入隧道
Close Entrance Door
    ↓ 完全遮挡 World_A
Unload World_A                  门控卸载策略
    ↓
Load World_B Additive
    ↓
Activate World_B
    ↓
SetActiveScene(World_B)
    ↓
Restore World State
    ↓
Rebuild Light Probes / Enable Physics
    ↓
Wait All Clients Ready          多人模式
    ↓
Open Exit Door
    ↓
Cleanup Transition Resources
```

下载、加载和激活是三个不同阶段：

- `Addressables.DownloadDependenciesAsync`：将资源放入本地缓存，不代表资源已全部进入运行内存。
- `Addressables.LoadSceneAsync`：加载场景及依赖，可使用 Additive 模式。
- `SceneInstance.ActivateAsync`：正式激活对象，会触发 `Awake`、`OnEnable`、物理注册等工作。

场景激活仍可能产生主线程峰值，因此应在隧道转角、关门、短暂黑暗或低视觉负载时执行。

### 5.1 延迟激活注意事项

可以使用 `activateOnLoad: false` 预加载场景，但不要让场景长期停留在等待激活状态。底层异步场景操作在等待激活时可能阻塞后续异步场景队列。

推荐顺序：

1. 加载 World_B 并等待可激活。
2. 在安全时机激活 World_B。
3. 再发起其他场景加载或卸载操作。

不要设计成“World_B 卡在等待激活状态，同时依赖 World_A 的异步卸载完成”。

## 6. SceneStreamingManager 状态机

建议使用显式状态机，禁止用多个 Trigger 各自直接调用加载 API。

```csharp
public enum SceneTransitionState
{
    Idle,
    PreDownloading,
    EnteringBuffer,
    ClosingEntrance,
    UnloadingSource,
    LoadingDestination,
    ActivatingDestination,
    InitializingDestination,
    WaitingForClients,
    OpeningExit,
    Cleanup,
    Failed
}
```

管理器至少负责：

- 防止重复触发切换。
- 保存所有 Addressables Handle。
- 维护当前场景、目标场景和切换方向。
- 控制入口门、出口门和玩家通行权限。
- 汇报下载、加载和初始化进度。
- 处理超时、加载失败和客户端掉线。
- 记录每个阶段耗时和内存峰值。

发生失败时，玩家应安全停留在隧道内，保持地面和碰撞有效，并显示可恢复提示；不能让玩家进入尚未完成加载的空间。

## 7. Addressables 分组

建议分为以下资源组：

```text
Global_Shared
├── 公共 Shader 和材质
├── 通用贴图与音效
├── 玩家模型
└── 通用交互 Prefab

World_A
├── A 场景
└── A 专属资源

World_B
├── B 场景
└── B 专属资源

Transition
└── 隧道专属资源
```

公共资源应显式放入共享组。否则同一个材质、贴图或 Mesh 可能作为隐式依赖被复制到多个 AssetBundle，增加包体和同时加载时的内存占用。

每次构建内容后执行 Addressables Analyze，重点检查：

- Duplicate Bundle Dependencies。
- Bundle Layout。
- 场景是否意外引用了其他世界资源。
- `Resources` 目录是否包含重复资源。

## 8. 初始化与性能控制

### 8.1 分帧初始化

不要在新场景所有对象的 `Awake` 或 `OnEnable` 中同时执行重任务。推荐将初始化拆成：

```text
Scene Loaded
→ Essential Initialized
→ Collision Ready
→ Visual Ready
→ Gameplay Ready
→ Background Initialized
```

AI、对象生成、特效预热和次要音频可以在出口门打开后继续分帧初始化。

### 8.2 Shader 与特效预热

- 控制 Shader Variant 数量。
- 在隧道阶段提前触发关键材质和粒子效果。
- 避免出口开启时第一次显示大量不同 Shader。
- 将对象池预热拆分到多个帧。

### 8.3 资源释放

卸载 Scene 不保证所有资源立即释放。以下引用都会阻止回收：

- 未释放的 Addressables Handle。
- 静态字段持有的场景对象。
- 未取消的事件订阅。
- 公共对象池中的场景专属实例。
- 仍在播放或缓存的音频、动画和特效。

`Resources.UnloadUnusedAssets()` 可能造成明显停顿，不应在玩家可见和自由移动阶段频繁调用。如确实需要，应安排在双门关闭或短暂遮挡期间，并通过真机 Profiling 决定是否保留。

## 9. XR 坐标连续性

物理世界坐标系必须属于 Bootstrap，不能随 World Scene 卸载。

```text
PhysicalWorldOrigin
└── ContentRoot
    ├── World_A_Root
    ├── Transition_Root
    └── World_B_Root
```

切换时移动或校准 World Root，不要销毁、重建或任意移动 XR Origin。对于分别制作的场景，可使用入口/出口锚点计算目标根节点变换。

必须验证：

- PICO 和 Quest 使用相同的 Unity 世界轴约定。
- 新场景地面高度与真实地面一致。
- 隧道入口和出口的虚实位置连续。
- 场景切换不会触发平台 Recenter 或重新创建相机。

## 10. 光照、物理与音频

- 每个 World Scene 可以拥有独立烘焙光照。
- A、B 重叠期间避免同时启用两个 Directional Light、Global Volume 和全局雾效。
- Additive 加载或卸载带有 Light Probe 的场景后，调用 `LightProbes.TetrahedralizeAsync()`。
- 设置新 World Scene 为 Active Scene，确保后续实例化对象进入正确场景。
- 出口开启前确认碰撞体、NavMesh 和交互对象已经可用。
- 环境音在隧道内交叉淡化，避免突然中断暴露切换过程。

## 11. 多人同步

多人模式由服务器或房主统一提交场景切换，不能由每个客户端独立开门。

```text
Server: Prepare World_B
    ↓
Clients: Download / Load
    ↓
Clients: Report Ready
    ↓
Server: Wait for all required clients or timeout policy
    ↓
Server: Commit activation on an agreed network tick
    ↓
Server: Open exit door
```

需要定义：

- 哪些玩家必须 Ready 才能继续。
- 慢客户端的最大等待时间。
- 掉线玩家是否阻塞队伍。
- Late Join 玩家应加载哪个世界。
- 场景状态和持久化对象如何恢复。

## 12. 验收指标

必须在 PICO 和 Quest 真机上记录，不能只依赖 Editor 测试。

### 12.1 性能

- 切换全过程无崩溃、系统杀进程或明显追踪丢失。
- 峰值内存低于目标设备的项目安全阈值，并保留运行余量。
- 场景激活时没有不可接受的连续掉帧。
- 隧道可停留时间覆盖 P95 真机加载时长。
- Addressables Handle 在往返切换后不存在持续增长。

### 12.2 功能

- A → B 和 B → A 均可重复执行。
- 连续往返 20 次后内存回到稳定区间。
- 加载失败时玩家保持在安全缓冲区。
- XR Origin、地面高度和世界朝向不跳变。
- 多台设备只在全部满足策略后开启出口。
- 玩家掉线、重连和 Late Join 行为符合设计。

### 12.3 内容制作规范

- World Scene 不包含 XR Origin 和全局管理器。
- World Scene 之间没有直接 GameObject 引用。
- 公共资源位于共享 Addressables 组。
- 每个场景拥有唯一稳定 ID、入口锚点和出口锚点。
- 所有可卸载资源都有明确所有者和释放路径。

## 13. 推荐实施顺序

1. 创建最小 Bootstrap Scene，并迁移 XR Origin 和全局管理器。
2. 制作一个灰盒隧道和 A/B 两个小型测试场景。
3. 实现单机 SceneStreamingManager 状态机。
4. 接入 Addressables 下载、加载、激活和释放流程。
5. 建立真机耗时、内存和错误日志。
6. 验证 XR 原点与虚实位置连续性。
7. 接入多人 Ready/Commit 协议。
8. 替换为正式内容并进行资源分组优化。
9. 在最低性能目标设备上完成长时间往返压力测试。

## 14. Unity 官方参考

- [LoadSceneMode.Additive](https://docs.unity3d.com/6000.1/Documentation/ScriptReference/SceneManagement.LoadSceneMode.Additive.html)
- [SceneManager.LoadSceneAsync](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.SceneManager.LoadSceneAsync.html)
- [Addressables 加载场景](https://docs.unity3d.com/Packages/com.unity.addressables@1.21/manual/LoadingScenes.html)
- [Addressables 卸载场景](https://docs.unity3d.com/Packages/com.unity.addressables@1.21/api/UnityEngine.AddressableAssets.Addressables.UnloadSceneAsync.html)
- [Addressables 预下载依赖](https://docs.unity3d.com/Packages/com.unity.addressables@1.20/api/UnityEngine.AddressableAssets.Addressables.DownloadDependenciesAsync.html)
- [避免资源重复](https://docs.unity3d.com/current/Manual/assets-avoid-duplication.html)
- [LightProbes.TetrahedralizeAsync](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LightProbes.TetrahedralizeAsync.html)

## 15. 决策摘要

本项目默认采用以下决策：

- 使用 Bootstrap + Additive World Scene 架构。
- World Scene 使用 Addressables 管理。
- 隧道采用双门或弯道设计，保证完全遮挡。
- PICO/Quest 默认使用门控卸载策略。
- XR Origin、网络和物理世界原点始终驻留。
- 多人切换采用服务端 Prepare → Ready → Commit 协议。
- 所有场景切换必须经过统一状态机，不允许业务 Trigger 直接加载场景。
