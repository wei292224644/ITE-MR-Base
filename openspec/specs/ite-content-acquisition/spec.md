# ite-content-acquisition Specification

## Purpose
ITE 两类内容包（空间场景包与 tour 包）的下载、解压布局、联网/离线路径与版本缓存。解压语义必须由调用方显式声明，读取侧不得靠多路径试探吸收布局差异。
## Requirements
### Requirement: 解压方必须显式声明 zip 的顶层目录语义

ITE 分发的两类内容包对"zip 里有没有顶层目录"的期望是**相反**的：空间场景包解压到 `IteSpaceScene_{sceneName}/`，其内容不应再多一层 `{sceneName}/`；tour 包解压到 `persistentDataPath` 根，其内容必须保留顶层 `{tourId}/`。

解压入口 SHALL 接受一个显式声明该语义的参数，两类包的调用点 MUST 各自明确传值。解压行为 MUST NOT 依赖 zip 实际内容碰巧匹配目标布局。

读取侧 MUST NOT 通过"依次尝试多个候选路径"来吸收这个差异——那会把两类包的结构差异藏进一次运气好的存在性判断，使真实布局无法从代码中读出。

#### Scenario: 空间场景包剥掉顶层目录

- **WHEN** `thirdDemo.zip`（条目形如 `thirdDemo/thirdDemo.json`，另含 `__MACOSX/` 条目）被解压到 `IteSpaceScene_thirdDemo/`
- **THEN** 落盘结果为 `IteSpaceScene_thirdDemo/thirdDemo.json`，而非 `IteSpaceScene_thirdDemo/thirdDemo/thirdDemo.json`

#### Scenario: tour 包保留顶层目录

- **WHEN** `{tourId}_wx.zip`（条目形如 `{tourId}/{tourId}.json`）被解压到 `persistentDataPath` 根
- **THEN** 落盘结果为 `persistentDataPath/{tourId}/{tourId}.json`

#### Scenario: 声明与实际不符时可归因

- **WHEN** 调用方声明需要剥掉顶层目录，但 zip 的条目并非全部位于同一个顶层目录之下
- **THEN** 系统 SHALL 输出一条指明该 zip 与该声明的错误日志，MUST NOT 静默按原样落盘

### Requirement: 空间场景描述读取失败必须可归因

`FetchSpaceSceneAsync` 读不到空间场景描述时抛出的异常信息 SHALL 包含场景名与被查找的路径，使"内容没下来"与"内容渲染失败"可以从日志区分。

#### Scenario: 缓存缺失

- **WHEN** 离线运行且 `IteSpaceScene_{sceneName}/{sceneName}.json` 不存在
- **THEN** 抛出的异常信息包含 `sceneName` 与该路径

### Requirement: 联网与离线是两条明确的路径

`networkAvailable` 为 false 时，加载链 MUST NOT 发起任何网络请求——包括空间场景包下载、tour 版本查询与 tour 包下载——直接读本地缓存。

`networkAvailable` 为 true 时，空间场景包每次重新下载；tour 包按版本比对决定是否重新下载。

#### Scenario: 离线不发请求

- **WHEN** `networkAvailable` 为 false 且本地缓存完整
- **THEN** 加载链完整完成，且未发起任何 HTTP 请求

#### Scenario: 联网首跑填充缓存

- **WHEN** `networkAvailable` 为 true 且本地无任何缓存
- **THEN** 空间场景包与其列出的全部 tour 包被下载解压到正确布局，加载链完成

#### Scenario: 联网复跑命中版本缓存

- **WHEN** `networkAvailable` 为 true，本地缓存的 tour 版本与版本 API 返回值一致
- **THEN** 该 tour 包 MUST NOT 被重新下载
