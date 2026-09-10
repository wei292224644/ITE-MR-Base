/// <summary>
/// 检测置信度是否足以派发。纯函数——真检测与假检测的分界是一个数字，
/// 而这个数字的边界（取到下限算不算通过）只有写成可穷举的形式才说得清。
///
/// 依据（2026-09-04 PICO 4 Ultra 实测，445 次检测）：真检测的 decision margin
/// 落在 76.5–99.6（n=444，p50=91.1）；出现过一次场上不存在的 tag 64，margin=3.8，
/// 位姿解在相机前 22 cm，照常派发并存活了一个滞回周期。双峰之间空得很大。
/// </summary>
public static class FiducialConfidencePolicy
{
    /// <summary>下限取到即通过：阈值表达的是「不低于」，不是「高于」。</summary>
    public static bool ShouldAccept(float decisionMargin, float minDecisionMargin)
        => decisionMargin >= minDecisionMargin;
}
