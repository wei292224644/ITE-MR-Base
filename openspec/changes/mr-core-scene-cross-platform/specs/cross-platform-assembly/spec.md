## ADDED Requirements

### Requirement: 双 APK 交付

系统 SHALL 为 Quest 与 PICO 分别产出独立 APK，各自只包含对应平台的 XR loader 与平台 SDK 代码。单个 APK MUST NOT 同时承载两家平台的运行时实现。

#### Scenario: 两个 Build Profile 各自出包

- **WHEN** 分别使用 `Quest.asset` 与 `PICO.asset` 两个 Build Profile 构建
- **THEN** 各产出一个 APK，Quest 包的 Android XR loader 为 `OpenXRLoader`，PICO 包为 `PXR_Loader`

### Requirement: 两条正交的 define 轴

平台装配 SHALL 使用两类互不替代的编译符号：构建意图符号（`MRBASE_QUEST` / `MRBASE_PICO`，由 Build Profile 的 Scripting Defines 提供）与包存在性符号（`MRBASE_HAS_*`，由 asmdef `versionDefines` 提供）。涉及平台 SDK 的条件编译 MUST 同时满足两轴。

#### Scenario: 包未安装时代码仍可编译

- **WHEN** 某平台的 SDK 包未安装在项目中，但对应的构建意图 define 已设置
- **THEN** 项目编译通过，平台专属程序集因 `defineConstraints` 未满足而整体跳过编译，不产生编译错误

#### Scenario: 包已安装且构建意图匹配

- **WHEN** PICO SDK 已安装且以 `PICO.asset` 构建
- **THEN** `MRBASE_PICO` 与 `MRBASE_HAS_PICO_SDK` 同时成立，PICO 平台程序集参与编译

### Requirement: 平台程序集边界

平台专属代码 SHALL 隔离在独立程序集中（`MRBase.Platform.Quest` / `MRBase.Platform.Pico` / `MRBase.Platform.Sim`），每个程序集 MUST 声明 `defineConstraints` 使其在对应包缺失时不参与编译，且 MUST NOT 被业务程序集直接引用。

#### Scenario: 业务程序集不引用平台程序集

- **WHEN** 检查业务程序集（如 `MRBase.SacredRelic`、`MRBase.Localization`）的 asmdef 引用列表
- **THEN** 其中不含任何 `MRBase.Platform.*`，也不含 `Oculus.VR` / `meta.xr.*` / `Unity.XR.PICO`

#### Scenario: 平台符号不泄漏到业务层

- **WHEN** 在业务程序集源码中检索 `#if MRBASE_`、`OVR`、`PXR_` 前缀符号
- **THEN** 命中数为 0

### Requirement: 集中式平台装配入口

系统 SHALL 提供唯一的平台装配入口 `MRBootstrap`，负责按当前平台创建能力 provider 并注入 `MRContext`。`#if MRBASE_QUEST` / `#if MRBASE_PICO` 形式的条件编译 MUST 仅出现在 `MRBootstrap` 与 `MRBase.Platform.*` 内部。

#### Scenario: 三种装配复用同一场景

- **WHEN** 分别在 Quest 构建、PICO 构建、Editor 中运行核心场景
- **THEN** `MRBootstrap` 分别装配 Quest / PICO / 模拟 provider，场景资产本身无差异

#### Scenario: 平台 define 缺失时给出明确错误

- **WHEN** 构建配置未设置任何 `MRBASE_*` 构建意图 define
- **THEN** `MRBootstrap` 在启动时输出明确的配置错误信息，指明缺失的 define 名称

### Requirement: 启动前置条件闸门

`MRBootstrap` SHALL 在装配 provider 之前检查平台前置条件（权限、平台能力开关），条件不满足时 MUST 呈现可操作的引导信息，MUST NOT 静默失败或直接抛出未处理异常终止应用。

#### Scenario: 权限缺失时引导用户

- **WHEN** 运行所需的平台权限或能力开关未授予
- **THEN** 应用显示说明缺失项与开启途径的引导界面，并在条件满足后可继续启动流程

### Requirement: 平台专属组件按平台挂载

仅在单一平台需要的组件（如 PICO 的 `PXR_Manager`）SHALL 通过 prefab 变体或运行时挂载提供，MUST NOT 存在于另一平台的构建产物中。

#### Scenario: Quest 构建不含 PICO 组件

- **WHEN** 检查 Quest 构建运行时的 XR Origin 对象
- **THEN** 其上不存在 `PXR_Manager` 组件

### Requirement: 统一打包入口

系统 SHALL 提供统一的打包入口，由其完整拥有构建流程：激活对应 Build Profile、设置该平台所需的 XR loader 与 OpenXR feature、执行一致性校验、构建、并在结束后还原其所修改的 XR 设置。构建 MUST 经由该入口执行，操作者 MUST NOT 需要手动修改 XR Plug-in Management 配置。

该入口 SHALL 同时提供菜单项与命令行两种调用方式，命令行方式 MUST 可在无人交互的环境下执行。

#### Scenario: 经打包入口构建 PICO 包

- **WHEN** 通过打包入口执行 PICO 构建，且当前 Android XR loader 为 `OpenXRLoader`
- **THEN** 入口自行将 loader 设置为 `PXR_Loader` 后完成构建，操作者未手动修改任何 XR 设置

#### Scenario: 构建后 XR 设置被还原

- **WHEN** 一次经打包入口的构建结束（无论成功或失败）
- **THEN** 其修改过的 XR 设置资产被还原至构建前状态，版本控制中不留下由本次打包产生的改动

#### Scenario: 命令行方式可用于无人环境

- **WHEN** 以命令行方式调用打包入口
- **THEN** 构建在无人交互的情况下完成，退出码可用于判定成败

#### Scenario: 单次执行不得连续构建两端

- **WHEN** 检查打包入口的实现
- **THEN** 不存在在单次编辑器脚本执行内连续构建两个平台的入口（切换构建意图 define 会触发脚本重编译并打断执行）；两端构建由外层分两次调用完成

### Requirement: 构建期一致性校验

打包入口 SHALL 在实际打包之前校验构建配置与构建意图一致，不一致时 MUST 中止构建并说明不匹配项，MUST NOT 继续产出配置错误的安装包。校验 MUST 至少覆盖：构建意图 define 与 XR loader 的对应关系、以及全局音频空间化配置未指向厂商专属插件。

#### Scenario: loader 与构建意图不匹配时中止

- **WHEN** 构建意图为 `MRBASE_PICO`，但 XR loader 无法被设置为 `PXR_Loader`（例如包未安装）
- **THEN** 构建在打包前中止，错误信息指出期望的 loader 与实际配置

#### Scenario: 厂商音频插件被误启用时中止

- **WHEN** 全局 Spatializer Plugin 指向厂商专属插件
- **THEN** 构建中止并指出该配置项

### Requirement: 两端编译回归

系统 SHALL 支持可重复执行的两端构建验证，确认 Quest 与 PICO 两个 Build Profile 均能完成构建。该验证 MUST NOT 依赖连接实体设备。

#### Scenario: 两端构建均通过

- **WHEN** 依次经打包入口的命令行方式构建两端
- **THEN** 两个 Build Profile 均构建成功；任一失败时输出失败的 Profile 与编译错误

### Requirement: 依赖版本锁定

XR 相关包版本 SHALL 在 `Packages/manifest.json` 中以精确版本固定。升级这些包 MUST 作为独立变更进行并伴随两端验证，MUST NOT 与功能改动混在同一次提交中。

#### Scenario: 依赖清单为精确版本

- **WHEN** 检查 `Packages/manifest.json` 中的 XR 相关依赖
- **THEN** 每项均为精确版本号，不含范围或浮动标记

### Requirement: 全局音频空间化配置

项目 SHALL 使用 Unity 内置的空间化方案，MUST NOT 启用任何厂商专属 Spatializer Plugin（该设置为全局单选，会使另一平台的构建产物配置错误）。

#### Scenario: Spatializer 未指向厂商插件

- **WHEN** 检查 `ProjectSettings > Audio > Spatializer Plugin`
- **THEN** 其值不为任何厂商专属插件
