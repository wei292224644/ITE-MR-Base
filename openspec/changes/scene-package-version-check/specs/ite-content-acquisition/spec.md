## MODIFIED Requirements

### Requirement: 解压方必须显式声明 zip 的顶层目录语义

ITE 分发的两类内容包对"zip 里有没有顶层目录"的期望是**相反**的：空间场景包解压到 `IteSpaceScene_{sceneName}/`，其内容不应再多一层 `{sceneName}/`；tour 包解压到 `persistentDataPath` 根，其内容必须保留顶层 `{tourId}/`。

解压入口 SHALL 接受一个显式声明该语义的参数，两类包的调用点 MUST 各自明确传值。解压行为 MUST NOT 依赖 zip 实际内容碰巧匹配目标布局。

读取侧 MUST NOT 通过"依次尝试多个候选路径"来吸收这个差异——那会把两类包的结构差异藏进一次运气好的存在性判断，使真实布局无法从代码中读出。

空间场景包的实际分发产物由 macOS Finder 压缩生成，除文件条目外还含 0 字节的**显式目录条目**（`{sceneName}/`、`{sceneName}/assets/`）与 `__MACOSX/` 影子条目。剥顶层目录的实现 MUST 正确处理目录条目：顶层目录条目本身在剥掉前缀后为空，MUST 被跳过而非落盘。

#### Scenario: 空间场景包剥掉顶层目录

- **WHEN** `thirdDemo.zip`（条目含 `thirdDemo/`、`thirdDemo/thirdDemo.json`、`thirdDemo/assets/`、`thirdDemo/assets/logo.png`，另含 `__MACOSX/` 条目）被解压到 `IteSpaceScene_thirdDemo/`
- **THEN** 落盘结果为 `IteSpaceScene_thirdDemo/thirdDemo.json` 与 `IteSpaceScene_thirdDemo/assets/logo.png`，而非 `IteSpaceScene_thirdDemo/thirdDemo/...`
- **AND** `IteSpaceScene_thirdDemo/` 之下不存在 `thirdDemo/` 或 `__MACOSX/` 目录

#### Scenario: tour 包保留顶层目录

- **WHEN** `{tourId}_wx.zip`（条目形如 `{tourId}/{tourId}.json`）被解压到 `persistentDataPath` 根
- **THEN** 落盘结果为 `persistentDataPath/{tourId}/{tourId}.json`

#### Scenario: 声明与实际不符时可归因

- **WHEN** 调用方声明需要剥掉顶层目录，但 zip 的条目并非全部位于同一个顶层目录之下
- **THEN** 系统 SHALL 输出一条指明该 zip 与该声明的错误日志，MUST NOT 静默按原样落盘

### Requirement: 联网与离线是两条明确的路径

`networkAvailable` 为 false 时，加载链 MUST NOT 发起任何网络请求——包括空间场景包校验器查询与下载、tour 版本查询与 tour 包下载——直接读本地缓存。

`networkAvailable` 为 true 时，两类内容包 SHALL 各自按缓存记录与服务端校验器比对，决定是否重新下载：空间场景包比对 HTTP `ETag`，tour 包比对版本 API 返回的版本号。比对一致的包 MUST NOT 被重新下载。

校验器记录 MUST 只在下载并解压成功之后写入，使失败的一次下载不会被后续冷启动误判为已缓存。

#### Scenario: 离线不发请求

- **WHEN** `networkAvailable` 为 false 且本地缓存完整
- **THEN** 加载链完整完成，且未发起任何 HTTP 请求

#### Scenario: 联网首跑填充缓存

- **WHEN** `networkAvailable` 为 true 且本地无任何缓存
- **THEN** 空间场景包与其列出的全部 tour 包被下载解压到正确布局，各自的校验器记录被写入，加载链完成

#### Scenario: 联网复跑命中 tour 版本缓存

- **WHEN** `networkAvailable` 为 true，本地缓存的 tour 版本与版本 API 返回值一致
- **THEN** 该 tour 包 MUST NOT 被重新下载

#### Scenario: 联网复跑命中场景包 ETag 缓存

- **WHEN** `networkAvailable` 为 true，本地记录的空间场景包 `ETag` 与服务端返回值一致
- **THEN** 该场景包 MUST NOT 被重新下载，直接读本地缓存

#### Scenario: 场景包内容变更后重新下载

- **WHEN** `networkAvailable` 为 true，服务端返回的 `ETag` 与本地记录不一致
- **THEN** 该场景包被重新下载解压，解压成功后本地记录被更新为新的 `ETag`

#### Scenario: 解压失败不写校验器记录

- **WHEN** 场景包下载成功但解压抛错
- **THEN** 本地 `ETag` 记录 MUST NOT 被更新，下次冷启动 SHALL 重新尝试下载

## ADDED Requirements

### Requirement: 校验器不可得时不得让完好的缓存失效

服务端校验器查询失败（请求失败、响应缺少 `ETag`）时，系统 MUST NOT 因此让加载链失败，也 MUST NOT 无条件重新下载。

本地已有该内容包的校验器记录时，SHALL 退回本地缓存。本地没有记录时，SHALL 执行下载——否则后续读取必然失败。

"本地有无缓存"SHALL 以校验器记录是否存在为准，MUST NOT 以内容文件是否存在为准：文件存在只说明下过，不说明下全了，而校验器记录只在解压成功后写入。

#### Scenario: 校验器查不到但本地有缓存

- **WHEN** 场景包 `ETag` 查询失败，且本地存在该场景包的 `ETag` 记录
- **THEN** MUST NOT 重新下载，直接读本地缓存，加载链正常完成

#### Scenario: 校验器查不到且本地无缓存

- **WHEN** 场景包 `ETag` 查询失败，且本地不存在该场景包的 `ETag` 记录
- **THEN** SHALL 执行下载解压

#### Scenario: tour 版本查不到时退回缓存

- **WHEN** tour 版本 API 返回空版本号
- **THEN** 该 tour 包 MUST NOT 被重新下载，直接读本地缓存
