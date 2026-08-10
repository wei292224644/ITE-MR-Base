## 1. 工程接入（不污染主工程）

- [x] 1.1 通过 Package Manager 以 git URL 引入 `wuyize25/gsplat-unity` v1.4.0（锁 tag，不用 main）
- [x] 1.2 新建 bench 专属 URP Asset + Universal Renderer Data，在其上添加 `Gsplat URP Feature`
- [x] 1.3 确认该 Renderer Data 的 Render Graph **Compatibility Mode 为关闭**（gsplat-unity 硬要求）
- [x] 1.4 bench 场景在 `Awake` 里把 `QualitySettings.renderPipeline` 切到 bench URP Asset —— 比新建 Quality Level 更彻底，`QualitySettings.asset` 与既有 `Build Profiles/Quest.asset`、`PICO.asset` 一个字节都没动
- [x] 1.5 打开 Player Settings → Frame Timing Stats（`enableFrameTimingStats` 0 → 1）
- [x] 1.6 复核 Android Vulkan 前置：`Apply display rotation during rendering` 保持未勾选（当前 `vulkanEnablePreTransform: 0`）
- [x] 1.7 新建 `Assets/Scripts/GsplatBench/MRBase.GsplatBench.asmdef`，只引用 TextMeshPro / XR 必需项与 gsplat 包；**不引用** `MRBase.Core`、`MRBase.Diagnostics`

## 2. Bench 场景

- [x] 2.1 新建 `Assets/Scenes/GsplatBench.unity`，独立可运行，不 additive 依赖 `MRCore`
- [x] 2.2 放置 XR Origin + 相机，使用 bench 专属渲染配置
- [x] 2.3 放置单个 `GsplatRenderer`（默认单 renderer，见 design 风险项）
- [x] 2.4 World-space Canvas + TMP，贴视野边角，随头部跟随；确认在 XR 下可读不遮挡主视野

## 3. 测量条件锁定

- [x] 3.1 实现启动时锁定：固定 `renderViewportScale`、固定 FFR 等级、记录锁定时的刷新率
- [x] 3.2 全部走厂商中立的 Unity XR API（`XRSettings` / `XRDisplaySubsystem`），provider 不支持时 try/catch 降级并把真实回读值显示出来。**ASW/SpaceWarp 没有中立 API**：改为按「显示帧间隔 ÷ 刷新率周期 ≈ 2」检测折半并标 drift —— 症状检测比厂商开关更可靠，因为掉帧导致的合成器折半本来也关不掉（见 design D2）
- [x] 3.3 每帧读取上述各项**实时值**（不是缓存的设定值）
- [x] 3.4 实时值与锁定值不一致时，把当前档标记 `invalid` 并在 HUD 与日志给出可见提示

## 4. 指标采集

- [x] 4.1 接 `FrameTimingManager.GetLatestTimings()`，取 GPU 与 CPU 帧时间
- [x] 4.2 滑窗保存样本，计算中位数与 1% low
- [x] 4.3 计算并显示当前刷新率下的帧预算与占用比例
- [x] 4.4 `FrameTimingManager` 无有效数据时显式显示不可用，**不得**用 FPS 反推冒充
- [x] 4.5 采集 splat 总数、活动 renderer 数量、显存与系统内存

## 5. HUD

- [x] 5.1 实现 bench HUD：分段 `Append*` + `StringBuilder`，沿用既有 `DiagnosticsHud` 的形状（TMP target + `reportIntervalSeconds` / `logIntervalSeconds` 双间隔）
- [x] 5.2 段落：帧时间 / 预算、splat 与内存、旋钮矩阵、锁定条件实时值、sweep 进度
- [x] 5.3 文本刷新限到 ≤5Hz，`.log` 写入 ≤0.5Hz；HUD 报告加 4000 字符硬上限（见 design D13）
- [x] 5.4 提供 HUD 整体开关，供采样期间关闭

## 6. 旋钮控制器

- [x] 6.1 定义旋钮集合：MSAA、SH degree、Splat Downscale、排序刷新间隔、`renderViewportScale`、renderer 副本数。**SPI/Multi-pass 移除**（构建期设置，运行时不可切，见 design D9）；**新增副本数**（用户 ply 最大仅 55.7 万，不叠副本够不到天花板，见 design D10）
- [x] 6.2 手柄绑定：切换当前旋钮 / 调值 / 重置 / 启动 sweep
- [x] 6.3 每个旋钮改动即时作用于渲染，无需重进场景
- [x] 6.4 旋钮当前值可被 HUD 与产出记录读取为快照

## 7. Sweep 与产出

- [x] 7.1 定义累加序列：`baseline → +MSAA off → +sort 1/30 → +SH0 → +downscale 0.25 → +viewScale 0.7`（去掉 +SPI，见 design D9）
- [x] 7.2 实现每档「预热 N 秒（不计入）→ 采样 M 秒 → 取中位数」
- [x] 7.3 实现单变量模式（每档仅从 baseline 改一项）作为归因回归手段
- [x] 7.4 sweep 可中断，已完成档的数据完整写出
- [x] 7.5 CSV 写入 `Application.persistentDataPath`：档序号/名称、旋钮快照、splat 数与 renderer 数、GPU 中位数与 1% low、CPU 中位数、锁定条件实时值、有效性标记
- [x] 7.6 CSV 头部写入可复现上下文：设备标识、资产标识与原始 splat 数、剪枝阈值、sweep 模式、锁定条件、装置版本
- [x] 7.7 逐档记录与周期快照写 `.log` 文件（`BenchLog`，带时间戳），**不走 `Debug.Log`**；Console 只留就绪 / sweep 起止 / 文件路径三类里程碑（见 design D12）

## 8. 资产与档位

- [x] 8.1 导入用户提供的 ply。**实际 splat 数与文件名不符**：`Model_30w`=286,358 / `Model_50w`=338,003 / `Model_100w`=556,529，均为 SH degree 3、未剪枝、已按 Spark 压缩导入。最大值 55.7 万远低于 proposal 要求的 200 万
- [ ] 8.2 用 `OpacityPruneThreshold` 切出数量档位（0 / 0.01 / 0.05 / …），记录每档实际剩余 splat 数
- [ ] 8.3 若用户提供「环境」与「物件」两份，各自建一套档位，报告分开记录不做平均

## 9. 校准与自证

- [x] 9.0 漂移原因无界增长的回归自检：锁定后制造持续越界条件，连续 `Poll` 5000 次确认 `DriftReason` 长度恒定、`ClearDrift` 后归零（编辑器内已跑通，见 design D13）
- [ ] 9.1 装置开销自证：HUD 开 / 关 各采样一次，报告 GPU 中位数之差
- [ ] 9.2 与 OVR Metrics Tool 对照一次 GPU 时间读数，记录偏差幅度
- [ ] 9.3 偏差 >20% 时在报告中显式标注，并判断是否需要为 Quest 单独接厂商 API 做锚点（design Open Question 3）

## 10. 真机执行与报告

- [ ] 10.1 Quest 3 上跑完整 sweep，取回 CSV
- [~] 10.2 PICO 4 Ultra：**用户裁掉，本轮不跑**
- [ ] 10.3 整理产出为一张「旋钮 → 边际 Δ GPU ms」表 + 「splat 数量 → GPU ms」曲线
- [ ] 10.4 在报告中显式标注已知偏差：Opacity Prune 同时降低数量与平均混合层数，数量曲线偏乐观（design D8）
- [ ] 10.5 依据数据回答下一步投资方向：减点 / LOD / 独立 RT 分辨率解耦，并作为后续变更的输入

## 11. 验收核对

- [x] 11.1 主工程既有程序集无一引用 bench asmdef 或 gsplat 包（已核：`grep` 全部 `.asmdef` 无命中）
- [x] 11.2 既有 `Performant` / `Balanced URP Renderer Config` 上无 `Gsplat URP Feature`（已核：feature 脚本 guid 命中数为 0；bench renderer 上为 1）
- [ ] 11.3 删除 bench 目录、场景、专属 URP 资产与包依赖后，主工程仍可编译运行
- [ ] 11.4 sweep 在真机跑完并产出完整 CSV（本变更的验收线；不设性能目标）
