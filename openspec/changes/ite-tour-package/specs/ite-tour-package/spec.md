## ADDED Requirements

### Requirement: 包边界与依赖方向
ITE 业务 SHALL 作为 embedded package 存在于 `Packages/` 下，并且 MUST NOT 引用宿主 `Assets/` 中的任何程序集或平台 SDK。依赖方向 MUST 单向为「宿主 → 包」。

#### Scenario: 包的程序集引用面
- **WHEN** 检查包运行时程序集的 `references` 与源码 `using`
- **THEN** 只出现 Unity 官方包与内置模块（Newtonsoft、gltfast、sharp-zip-lib、video、unitywebrequest、ugui、animation、audio）
- **THEN** 不出现任何 `MRBase.*` 程序集

#### Scenario: 包内无平台分支
- **WHEN** 在包源码中检索 `OVR`、`PXR_`、`MRBASE_QUEST`、`MRBASE_PICO`、`Oculus`、`Meta.XR`
- **THEN** 无匹配结果
- **THEN** 平台相关行为只存在于宿主适配层

#### Scenario: 违反边界时的反馈
- **WHEN** 有人在包内引用宿主程序集中的类型
- **THEN** Unity 编译失败
- **THEN** 失败发生在写下引用时，而非移植时

#### Scenario: 与其它模块共存
- **WHEN** ITE 包与宿主既有 `MarkerAnchorService` 消费链同时运行
- **THEN** 两者各自独立消费标记事件，互不改变对方行为
- **THEN** `MarkerAnchorService`、`AnchorRegistry`、`AnchorEntity` 的既有实现无需修改

### Requirement: 运行时装配契约
包 SHALL 通过单一装配对象完成初始化，装配项 MUST 只含配置数据、场景 Transform 引用与可选策略委托。调用方 MUST NOT 需要实现任何 `interface`。

#### Scenario: 完整装配
- **WHEN** 宿主提供配置资产、锚点根、Tour 根、相机 Transform 并启动运行时
- **THEN** 运行时创建成功并开始内容管线
- **THEN** 宿主未实现任何接口，也未提供任何必需的行为委托

#### Scenario: 必需装配项缺失
- **WHEN** 配置资产或任一必需 Transform 为空
- **THEN** 运行时拒绝启动并记录具体缺失项
- **THEN** 不进入内容管线，也不产生空引用异常

#### Scenario: 可选策略委托缺省
- **WHEN** 宿主未提供网络可用性委托
- **THEN** 运行时按「网络可用」处理
- **THEN** 内容管线正常执行远端版本校验与下载

#### Scenario: 场景引用不依赖 tag
- **WHEN** 运行时需要锚点、Tour 根或相机 Transform
- **THEN** 使用装配时注入的引用
- **THEN** 不通过 `FindGameObjectWithTag` 查找场景对象

### Requirement: 标记扫描推入与 Tour 切换
包 SHALL 提供推入方法接收外部标记扫描结果（标记 ID 与 Pose），并按 Tour 的展示类型决定激活、切换或重锚。包 MUST NOT 自行访问任何平台扫描 SDK。

#### Scenario: 首次强制扫码
- **WHEN** 运行时处于「必须扫码」状态，且推入的标记 ID 匹配某个已加载 Tour
- **THEN** 该 Tour 被激活，并按推入的 Pose 完成锚定
- **THEN** 「必须扫码」状态解除

#### Scenario: 暂停期间推入被忽略
- **WHEN** 运行时处于暂停状态时收到标记推入
- **THEN** 不激活、不切换、不重锚任何 Tour

#### Scenario: 标记 ID 无匹配 Tour
- **WHEN** 推入的标记 ID 不对应任何已加载 Tour
- **THEN** 当前激活状态保持不变
- **THEN** 不抛出异常

#### Scenario: normal 类型切换
- **WHEN** 非强制扫码状态下推入的标记匹配一个 `normal` 类型 Tour，且它不是当前激活 Tour
- **THEN** 当前 Tour 被停用，该 Tour 被激活并按推入 Pose 锚定

#### Scenario: regionalTrigger 二次锚定
- **WHEN** 推入的标记匹配的正是当前激活的 `regionalTrigger` 类型 Tour，且它当前允许二次锚定
- **THEN** 该 Tour 按新 Pose 重新锚定，且不销毁重建其场景内容
- **THEN** 该 Tour 转为不再允许二次锚定，直至下次停用后重置

#### Scenario: regionalTrigger 已锚定后重复扫描
- **WHEN** 推入的标记匹配当前激活的 `regionalTrigger` Tour，但它已完成二次锚定
- **THEN** 位置保持不变

#### Scenario: 待扫描集合的过滤
- **WHEN** 待扫描 Tour 集合非空，且推入的标记 ID 不在该集合内
- **THEN** 该次推入被忽略

#### Scenario: 重复推入不被去重
- **WHEN** 同一标记 ID 被连续多次推入
- **THEN** 每次推入都按上述规则独立求值
- **THEN** 运行时不因「该 ID 已处理过」而永久屏蔽后续推入

### Requirement: 区域触发进出
`normal` 与 `regionalTrigger` 类型的 Tour SHALL 通过各自的触发体积响应相机进出，并据此维护待扫描 Tour 集合与激活选择。`alwaysDisplayed` 类型 MUST NOT 参与区域触发。

#### Scenario: 相机进入触发体积
- **WHEN** 相机进入某个非 `alwaysDisplayed` Tour 的触发体积
- **THEN** 该 Tour 加入待扫描集合

#### Scenario: 相机离开触发体积
- **WHEN** 相机离开该触发体积
- **THEN** 该 Tour 移出待扫描集合

#### Scenario: 进入后选择激活
- **WHEN** 待扫描集合变化且当前激活 Tour 不在集合内、且不处于强制扫码状态
- **THEN** 当前 Tour 被停用
- **THEN** 若集合内存在 `regionalTrigger` 类型 Tour，则从中选出一个激活

#### Scenario: 已激活 Tour 仍在集合内
- **WHEN** 待扫描集合包含当前激活 Tour
- **THEN** 不执行停用与重选

#### Scenario: alwaysDisplayed 不受影响
- **WHEN** 相机进出 `alwaysDisplayed` 类型 Tour 所在区域
- **THEN** 该 Tour 始终保持显示
- **THEN** 待扫描集合与激活选择不因它而改变

### Requirement: 头显佩戴状态推入
包 SHALL 提供推入方法接收头显佩戴状态，并据此暂停或恢复导览。包 MUST NOT 直接订阅任何平台的头显事件。

#### Scenario: 摘下头显
- **WHEN** 宿主推入「未佩戴」
- **THEN** 当前激活 Tour 被停用
- **THEN** 运行时进入暂停状态，后续标记推入被忽略

#### Scenario: 重新戴上头显
- **WHEN** 宿主推入「已佩戴」
- **THEN** 运行时解除暂停
- **THEN** 运行时回到「必须扫码」状态，要求重新扫码后才恢复导览

### Requirement: 内容管线与离线
包 SHALL 自行完成空间场景与 Tour 内容的获取、版本校验、解压、缓存与反序列化，MUST NOT 向宿主索取文件读写、下载或解压能力。

#### Scenario: 联网首次加载
- **WHEN** 网络可用且本地无缓存
- **THEN** 下载并解压空间场景包，读取场景描述
- **THEN** 对每个 Tour 查询服务端最新版本，下载并解压其内容包，写入版本缓存

#### Scenario: 版本未变化
- **WHEN** 服务端版本与本地缓存版本一致
- **THEN** 跳过该 Tour 的下载与解压，直接使用本地内容

#### Scenario: 版本已变化
- **WHEN** 服务端版本与本地缓存版本不同
- **THEN** 重新下载并解压该 Tour 内容
- **THEN** 更新本地版本缓存

#### Scenario: 离线加载
- **WHEN** 网络不可用
- **THEN** 跳过全部下载与版本查询，直接读取本地缓存内容
- **THEN** 本地缓存完整时导览可正常进行

#### Scenario: 空间场景缺失或无 Tour
- **WHEN** 场景描述无法读取，或其中不含任何 Tour
- **THEN** 加载失败并给出可诊断的错误
- **THEN** 加载中状态被正确复位，不停留在「加载中」

#### Scenario: 单个 Tour 数据缺失
- **WHEN** 某个 Tour 的描述文件无法读取
- **THEN** 加载失败并指明具体 Tour ID

#### Scenario: 端点可配置
- **WHEN** 检查内容管线使用的服务地址
- **THEN** 空间场景地址、Tour 内容包地址、版本查询地址均来自配置资产
- **THEN** 源码中不含硬编码域名

#### Scenario: 资源类型加载
- **WHEN** Tour 内容包含 EMW 模型、富文本、视频三类资源
- **THEN** 分别完成 glb 与其动画音频、音频与图片、视频本地路径的准备
- **THEN** 未知资源类型被跳过且不中断加载

### Requirement: 持久化键命名空间
包写入的所有持久化键 SHALL 带有包专属前缀，MUST NOT 占用宿主的全局键名空间。

#### Scenario: 版本缓存键
- **WHEN** 包写入某个 Tour 的内容版本
- **THEN** 键名带包专属前缀且包含该 Tour ID
- **THEN** 不使用裸 Tour ID 作为键名

#### Scenario: 与宿主键不冲突
- **WHEN** 宿主使用与某 Tour ID 同名的持久化键
- **THEN** 双方互不覆盖

### Requirement: Tour 场景构建
Tour 激活时 SHALL 按实体描述构建对象树，组件 MUST 按「元素 → 触发器 → 动作」的顺序分批装配。Tour 停用时 SHALL 销毁其场景内容。

#### Scenario: 组件装配顺序
- **WHEN** 某实体同时含有元素、触发器与动作组件
- **THEN** 元素组件先于触发器组件装配，触发器组件先于动作组件装配
- **THEN** 触发器与动作在装配时可获取到已就绪的元素组件

#### Scenario: 实体初始可见性
- **WHEN** 非 `alwaysDisplayed` 类型 Tour 的实体被构建
- **THEN** 实体按其描述中的启用标志设置初始可见性

#### Scenario: 未知组件类型
- **WHEN** 实体描述中出现运行时未注册的组件类型
- **THEN** 该组件被跳过
- **THEN** 同实体的其余组件正常装配

#### Scenario: Tour 停用
- **WHEN** Tour 被停用
- **THEN** 其场景内容被销毁
- **THEN** 其二次锚定许可被重置

#### Scenario: 无场景数据
- **WHEN** Tour 数据中不含任何场景
- **THEN** 记录可诊断错误并跳过构建
- **THEN** 不抛出未处理异常

### Requirement: 组件运行时语义
包 SHALL 提供元素、触发器、动作三类运行时组件，触发器 MUST 通过实体事件通道向目标实体派发动作。

#### Scenario: 加载触发器
- **WHEN** Tour 场景内容构建完成
- **THEN** 加载触发器向其配置的目标实体派发动作

#### Scenario: 点击触发器
- **WHEN** 同实体上的元素被点击
- **THEN** 点击触发器向其配置的目标实体派发动作

#### Scenario: 动作参数覆盖
- **WHEN** 动作组件收到带参数的派发
- **THEN** 以派发携带的参数覆盖其装配时的默认参数并执行

#### Scenario: 可见性切换动作
- **WHEN** 可见性切换动作被派发且配置了时长
- **THEN** 实体以缩放动画完成显示或隐藏
- **THEN** 配置为「仅一次」时，后续派发不再切换

#### Scenario: 旋转动作
- **WHEN** 旋转动作被派发
- **THEN** 实体按配置的轴、方向、单圈时长与圈数旋转
- **THEN** 实体被禁用或重新触发时旋转复位到初始姿态

#### Scenario: 动画动作
- **WHEN** 动画动作被派发
- **THEN** 按配置的起止时间、速度、延迟、循环、往复与重复次数播放
- **THEN** 实体被禁用时动画停止

#### Scenario: 音频动作
- **WHEN** 音频动作被派发
- **THEN** 关联富文本元素的音频开始播放

#### Scenario: 组件销毁时反注册
- **WHEN** 任一触发器或动作组件被销毁
- **THEN** 其在事件通道与实体生命周期回调上的注册被移除
- **THEN** 不残留指向已销毁对象的回调

### Requirement: 状态广播
包 SHALL 以事件形式广播加载进度与导览状态，MUST NOT 直接调用宿主的 UI 或业务对象。订阅 MUST 为可选。

#### Scenario: 加载进度
- **WHEN** 内容管线推进
- **THEN** 广播单调不减的进度值，起始为 0、完成为 1

#### Scenario: 场景元数据就绪
- **WHEN** 空间场景描述解析完成
- **THEN** 广播场景元数据，供宿主展示标题与 Tour 列表
- **THEN** 此时图片资源尚未就绪，对应 Sprite 字段为 null

#### Scenario: 场景图片就绪
- **WHEN** 场景 logo 与各 Tour 预览图加载完成
- **THEN** 广播场景图片就绪事件，携带同一场景对象
- **THEN** 图片加载与 Tour 装配并行，MUST NOT 阻塞后者

#### Scenario: 初始化完成
- **WHEN** 全部 Tour 实例化完成
- **THEN** 广播初始化完成事件

#### Scenario: Tour 激活与停用
- **WHEN** 某 Tour 被激活或停用
- **THEN** 分别广播对应事件并携带该 Tour ID

#### Scenario: Tour 场景就绪
- **WHEN** 某 Tour 的实体树构建完成
- **THEN** 广播该事件并携带该 Tour ID

#### Scenario: 扫码提示状态
- **WHEN** 「是否需要提示扫码」及其候选 Tour 集合发生变化
- **THEN** 广播提示状态与候选 Tour ID 列表
- **THEN** 宿主自行决定是否渲染以及如何渲染

#### Scenario: 无订阅者
- **WHEN** 宿主未订阅任何广播事件
- **THEN** 导览功能不受影响
- **THEN** 不产生空引用异常

### Requirement: 宿主平台适配层
宿主 SHALL 提供适配层，把平台标记扫描与头显佩戴状态转发进包。平台差异 MUST 全部落在该层。

#### Scenario: 标记来源转发
- **WHEN** 平台标记提供方解析出标记 ID 与 Pose
- **THEN** 适配层调用包的标记推入方法
- **THEN** 该转发不经过会做首次去重的既有锚点服务

#### Scenario: Quest 与 PICO 使用同一条标记来源
- **WHEN** 构建目标为 Quest 或 PICO
- **THEN** 适配层通过既有的跨平台标记提供方抽象获取事件
- **THEN** 适配层本身不区分平台

#### Scenario: 头显佩戴状态转发
- **WHEN** 头显佩戴状态发生变化
- **THEN** 适配层在状态变化的边沿调用包的佩戴状态推入方法
- **THEN** 状态未变化时不重复推入

#### Scenario: 平台能力缺失
- **WHEN** 当前平台无法获取头显佩戴状态
- **THEN** 适配层记录该情况并保持导览处于已佩戴状态
- **THEN** 不因此阻断标记扫描与导览功能
