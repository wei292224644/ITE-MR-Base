## ADDED Requirements

### Requirement: 标记 payload 到 tourId 的解析归包所有

标记的 payload 内容由内容方定义，其形状会随内容改版变化（当前是外壳格式，预期会变成一个地址）。因此解析 SHALL 发生在包内，与内容数据同层演进。

包的扫码入口 SHALL 接受**原始 payload、标记种类、位姿**三项，自行解析出 tourId 后再走 `TourScanPolicy` 决策。宿主 MUST NOT 承担任何 payload 解析。

标记种类 SHALL 使用包自有的枚举类型表达，MUST NOT 引用宿主的 `MarkerPlatform`，也 MUST NOT 使用字符串——包对宿主零知识，而字符串拼错不报错、只是永远解析不出。

#### Scenario: 宿主推入原始 payload

- **WHEN** 宿主以原始 payload、标记种类与位姿调用包的扫码入口
- **THEN** 包解析出 tourId 并按扫码策略决策，宿主侧 MUST NOT 出现任何解析逻辑

#### Scenario: 解析规则改变时宿主不受影响

- **WHEN** Quest 侧 payload 从外壳格式改为一个地址，包内解析实现随之替换
- **THEN** 宿主的桥接、会话层与装配点 MUST NOT 需要修改

### Requirement: 两端的 payload 形状各自解析

Quest 的 payload 是 QR 文本，PICO 的 payload 是 AprilTag 的整数 ID 的字符串形式（见 `unified-marker-tracking-contract`）。包 SHALL 按标记种类分派到对应解析：

- QR 文本：按当前印制格式解析出 tourId。首版沿用外壳格式 `******{tourId}******`，该实现 SHALL 收敛在单一入口，替换时不波及其他代码。
- AprilTag ID：按空间场景描述中的绑定反查 tourId。

#### Scenario: Quest QR 文本解析出 tourId

- **WHEN** 包收到种类为 QR 文本、payload 为 `"******wm0l5qcn_ibd******"` 的扫码
- **THEN** 解析出 tourId `"wm0l5qcn_ibd"` 并进入扫码决策

#### Scenario: PICO AprilTag ID 反查出 tourId

- **WHEN** 包收到种类为 AprilTag ID、payload 为 `"250"` 的扫码，且已装配的某个 tour 的 AprilTag ID 字段为 `250`
- **THEN** 解析出该 tour 的 tourId 并进入扫码决策

### Requirement: AprilTag ID 绑定在 Tour 上，与 tourID 平级

空间场景描述中每个 tour SHALL 可携带一个专门描述该 tour 的 AprilTag ID 的字段，与 `tourID` 平级。绑定随 tour 增删自动同步，不需要维护第二张表。

该字段 SHALL 可缺省。缺省时该 tour 只是不参与 AprilTag 反查，MUST NOT 影响其装配、下载或其他任何行为——沿用 `isEnabled` 的缺省语义。

#### Scenario: 字段缺失的 tour 照常装配

- **WHEN** 空间场景描述中某个 tour 没有 AprilTag ID 字段
- **THEN** 该 tour 正常下载与装配，只是 AprilTag 扫码无法命中它，加载链 MUST NOT 报错

#### Scenario: 旧版描述文件仍可加载

- **WHEN** 加载一份完全不含该字段的空间场景描述
- **THEN** 全部 tour 正常装配，Quest 侧扫码行为与本 change 之前一致

### Requirement: 解析不出 tourId 时可见地忽略

payload 无法解析、或解析出的 tourId 不属于任何已装配的 tour 时，包 SHALL 忽略本次扫码并输出一条日志，日志中 SHALL 包含原始 payload 与标记种类。MUST NOT 静默丢弃——静默丢弃与"根本没扫到"在真机上不可区分。

#### Scenario: 无法解析的 payload

- **WHEN** 包收到种类为 QR 文本、payload 不匹配当前解析规则的扫码
- **THEN** 不进入扫码决策，且输出一条包含该原始 payload 的日志

#### Scenario: 解析出的 tourId 不在场景中

- **WHEN** 解析成功但得到的 tourId 不属于任何已装配的 tour
- **THEN** 不进入扫码决策，且输出一条日志指明该 tourId 与当前可用的 tourId 列表
