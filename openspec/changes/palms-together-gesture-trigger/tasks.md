## 1. 路径 A（JointMath）与测试

- [x] 1.1 整理 `PalmsTogetherGesture`：拆出或保留路径 A 纯判据，确保静态 `Matches` 与 hold/迟滞可用
- [x] 1.2 确认 `PalmsTogetherGestureTests` 覆盖标准合掌、过远、手背相对、指尖反向、握拳对撞；缺口则补测
- [x] 1.3 在 Unity 中跑通路径 A 的 EditMode 测试套件并修复失败项

## 2. 路径 B（HandPoseComposite）

- [x] 2.1 创建平掌 `XRHandShape`（及必要的左右 `XRHandPose`）资产，朝向条件指向对方手
- [x] 2.2 实现路径 B：双 Pose `CheckConditions` AND 腕距阈值，复用或平行 hold/迟滞
- [x] 2.3 手丢失 / 目标 Transform 缺失时安全失败（不抛异常）

## 3. 门面与 A/B 切换

- [x] 3.1 门面并行求值 A/B，暴露 `IsHeldA`/`IsHeldB`（或等价）与 `ActiveSource`
- [x] 3.2 对外 `Performed`/`Released`/`IsHeld` 仅跟随 `ActiveSource`（默认 A）
- [x] 3.3 在核心场景（如 `MRCore.unity`）挂载门面实例并接好路径 B 所需手部目标引用

## 4. 诊断 HUD

- [x] 4.1 `DiagnosticsHud` 同时显示 A、B 命中/保持与当前 ActiveSource；实例缺失时安全跳过
- [x] 4.2 Play 模式下确认 HUD 出现双路径状态字段

## 5. 验收核对

- [x] 5.1 对照 `specs/palms-together-gesture/spec.md` 逐条勾选
- [x] 5.2 grep 确认判定相关代码无 SacredRelic / Marker / 玩法业务类型引用
