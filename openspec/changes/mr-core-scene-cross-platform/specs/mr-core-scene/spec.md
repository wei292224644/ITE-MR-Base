## ADDED Requirements

### Requirement: 单一常驻核心场景

系统 SHALL 提供唯一一个常驻核心场景 `MRCore.unity`，作为应用启动后加载的场景，并且在应用生命周期内不被卸载。该场景 MUST NOT 包含任何按目标平台复制的副本 —— Quest 与 PICO 两个构建使用同一份场景文件。

#### Scenario: 应用启动加载核心场景

- **WHEN** 应用在 Quest 或 PICO 上启动
- **THEN** `MRCore.unity` 以 `LoadSceneMode.Single` 加载，且两端加载的是同一个场景资产（相同 GUID）

#### Scenario: 不存在平台专属场景副本

- **WHEN** 检查 `Assets/Scenes/` 下的场景文件
- **THEN** 不存在 `MRCore_Quest.unity` / `MRCore_Pico.unity` 之类按平台复制的核心场景

### Requirement: 核心场景在 Editor 中可直接运行

核心场景 SHALL 在不连接任何头显设备的情况下于 Unity Editor 中运行，手部数据由 `XR Interaction Simulator` 提供的 `XRHandSubsystem` provider 供给。

#### Scenario: Editor 中按 Play 出现模拟手

- **WHEN** 在 Editor 中打开 `MRCore.unity` 并按 Play，未连接任何设备
- **THEN** 模拟器实例化，键鼠可控制头部与双手，手部呈现对象出现且跟随模拟手移动

### Requirement: MRContext 服务契约

系统 SHALL 提供 `MRContext` 作为核心场景对外暴露的唯一服务入口，至少暴露相机、XR Origin、双手追踪数据的访问器。业务代码 MUST 通过 `MRContext` 获取这些引用，MUST NOT 通过跨场景查找获取。

#### Scenario: 业务场景通过 MRContext 取相机

- **WHEN** 一个 additive 加载的业务场景需要相机引用
- **THEN** 它通过 `MRContext.Instance` 取得，且该业务场景资产内不包含任何指向核心场景对象的序列化引用

#### Scenario: MRContext 可在 EditMode 测试中替换

- **WHEN** EditMode 测试需要注入替身
- **THEN** 通过既有 `StaticInstance<T>.BindInstanceForTesting` 绑定测试实例，无需加载场景

### Requirement: 业务内容以 additive 方式挂载

业务功能 SHALL 以独立场景 additive 加载到核心场景之上，且依赖方向 MUST 为「业务 → `MRContext`」单向。核心场景 MUST NOT 引用任何业务场景中的对象。

#### Scenario: 加载与卸载业务场景不影响核心

- **WHEN** 一个业务场景被 additive 加载后再卸载
- **THEN** 核心场景的 XR Origin、手部呈现、交互器保持工作，无空引用异常

### Requirement: passthrough 呈现

核心场景 SHALL 在两端均以 passthrough（透视）方式呈现，使用户能看到真实环境。相机清除配置 MUST 使虚拟内容与 passthrough 正确合成（背景不遮挡真实环境）。

#### Scenario: 两端均可见真实环境

- **WHEN** 用户在 Quest 或 PICO 上戴上头显运行应用
- **THEN** 视野中可见真实房间，虚拟内容叠加在其上，背景区域不呈现纯色遮挡

#### Scenario: passthrough 启用失败时可诊断

- **WHEN** 当前平台的 passthrough 未能启用
- **THEN** 系统输出可识别的诊断日志，指明失败平台与原因，而非静默呈现黑屏

### Requirement: 现有 demo 场景保留

本变更 MUST NOT 修改或删除既有的 `LocalizationDemo.unity`、`SacredRelicAwakenDemo.unity`、`SampleScene.unity`，它们作为回归对照保留。

#### Scenario: 既有 demo 场景可独立打开

- **WHEN** 在 Editor 中打开任一既有 demo 场景
- **THEN** 场景可正常打开且行为与本变更之前一致
