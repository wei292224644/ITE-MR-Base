using UnityEngine;

namespace MRBase.Transitions
{
    /// <summary>三种传送风格。测试台用来现场比较观感，不是业务分类。</summary>
    public enum IceSpriteTeleportStyle
    {
        /// <summary>原地消散，目标点重组。中间有一段谁都不在场。</summary>
        DissolveReform,

        /// <summary>消散后一束星屑沿路径飞到目标点再重组。</summary>
        TrailFlight,

        /// <summary>本体立刻到目标点，出发点留一个淡出的残影。</summary>
        Afterimage,
    }

    public enum IceSpriteTeleportPhase
    {
        Vanishing,
        InTransit,
        Appearing,
        Done,
    }

    /// <summary>
    /// 传送的时间轴推进与位置计算。纯函数，不碰引擎帧循环也不碰第三方类型，
    /// 所以能离机单测 —— 与 <see cref="GroundUpRevealController.CalculateRevealHeight"/> 同一套路。
    ///
    /// 编排层是 Assembly-CSharp 里的 IceSpritePresence，它引用不进任何 asmdef，
    /// 所以逻辑必须待在这边才测得到。
    /// </summary>
    public static class IceSpriteTeleport
    {
        public static float TotalDuration(
            IceSpriteTeleportStyle style, float dissolveDuration, float flightDuration)
        {
            float d = Mathf.Max(0f, dissolveDuration);

            switch (style)
            {
                case IceSpriteTeleportStyle.Afterimage:
                    return d;
                case IceSpriteTeleportStyle.TrailFlight:
                    return d + Mathf.Max(0f, flightDuration) + d;
                default:
                    return d + d;
            }
        }

        public static IceSpriteTeleportPhase PhaseAt(
            IceSpriteTeleportStyle style, float elapsed, float dissolveDuration, float flightDuration)
        {
            // Afterimage 的本体从头到尾都是实体，没有消散/重组段。
            if (style == IceSpriteTeleportStyle.Afterimage) return IceSpriteTeleportPhase.Done;

            float t = Mathf.Max(0f, elapsed);
            float d = Mathf.Max(0f, dissolveDuration);

            // f = 0 时飞行段是空区间，TrailFlight 自然退化成 DissolveReform。
            float f = style == IceSpriteTeleportStyle.TrailFlight
                ? Mathf.Max(0f, flightDuration)
                : 0f;

            // 这两个和必须先落到 float 局部变量再比较：Mono 允许把纯表达式中间值
            // 留在比 float32 更宽的寄存器里参与比较，导致本该相等的边界判成"还没到"。
            // 存一次局部变量会强制按 float32 舍入，边界才会和调用方传入的字面量位一致。
            float transitEnd = d + f;
            float appearEnd = transitEnd + d;

            if (t < d) return IceSpriteTeleportPhase.Vanishing;
            if (t < transitEnd) return IceSpriteTeleportPhase.InTransit;
            if (t < appearEnd) return IceSpriteTeleportPhase.Appearing;
            return IceSpriteTeleportPhase.Done;
        }

        public static Vector3 PositionAt(
            IceSpriteTeleportStyle style, Vector3 from, Vector3 to,
            float elapsed, float dissolveDuration, float flightDuration)
        {
            if (style == IceSpriteTeleportStyle.Afterimage) return to;

            float t = Mathf.Max(0f, elapsed);
            float d = Mathf.Max(0f, dissolveDuration);

            if (t < d) return from;

            if (style == IceSpriteTeleportStyle.TrailFlight)
            {
                float f = Mathf.Max(0f, flightDuration);
                float transitEnd = d + f; // 落到局部变量再比较，理由同 PhaseAt。
                if (f > 0f && t < transitEnd) return Vector3.Lerp(from, to, (t - d) / f);
            }

            return to;
        }
    }
}
