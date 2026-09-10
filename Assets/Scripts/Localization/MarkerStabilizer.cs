using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 位姿防抖：平滑 + 稳定判定，判稳时发一次 <see cref="Stabilized"/>。
///
/// 移植自源工程的 <c>AnchorObject</c>，但两处按实测改掉了（design D9 / D22）：
///
/// 1. **窗口是时间不是次数。** 两端派发速率实测差 12.5 倍（PICO 5.6 Hz / Quest 70 Hz），
///    帧计数制下同一个阈值 30 在 Quest 上是 0.43 s、在 PICO 上是 5.4 s ——
///    举着码不动五秒半，现场表现仍是「扫不到」。同一条链路上的丢失滞回本就是时间制。
/// 2. **平滑用时间常数，不是 <c>Clamp01(deltaTime / smoothTime)</c>。** 后者在
///    smoothTime 小于帧间隔时恒为 1，平滑位姿每帧跳到目标，于是「平滑 vs 目标」的差
///    就等于相邻两帧的原始抖动，阈值永远过不去——PICO 上一次都不触发的真正原因。
///
/// 三项参数按平台各存一套，见 <c>MarkerStabilizerProfile</c>；硬件不是纸面上的理想值，
/// 这些必须留成可调旋钮。
/// </summary>
public class MarkerStabilizer
{
    private class TrackedMarker
    {
        public Pose SmoothedPose;
        public Pose TargetPose;
        public float StableSeconds;
        public bool HasFiredStableEvent;
    }

    private readonly float positionThreshold;
    private readonly float rotationThreshold;
    private readonly float smoothTime;
    private readonly float stableSeconds;

    private readonly Dictionary<string, TrackedMarker> tracked = new Dictionary<string, TrackedMarker>();

    public event Action<string, Pose> Stabilized;

    /// <param name="smoothTime">
    /// 平滑的时间常数（秒）。应当明显大于帧间隔，否则平滑退化为逐帧跳变。
    /// </param>
    /// <param name="stableSeconds">连续稳定多久算判稳。</param>
    public MarkerStabilizer(
        float positionThreshold = 0.05f,
        float rotationThreshold = 1f,
        float smoothTime = 0.2f,
        float stableSeconds = 0.5f)
    {
        this.positionThreshold = positionThreshold;
        this.rotationThreshold = rotationThreshold;
        this.smoothTime = smoothTime;
        this.stableSeconds = stableSeconds;
    }

    public void Feed(string rawId, Pose rawPose, float deltaTime)
    {
        if (!tracked.TryGetValue(rawId, out var marker))
        {
            marker = new TrackedMarker { SmoothedPose = rawPose, TargetPose = rawPose };
            tracked[rawId] = marker;
        }

        marker.TargetPose = rawPose;

        float positionDelta = Vector3.Distance(marker.SmoothedPose.position, marker.TargetPose.position);
        float angleDelta = Quaternion.Angle(marker.SmoothedPose.rotation, marker.TargetPose.rotation);
        bool moved = positionDelta > positionThreshold || angleDelta > rotationThreshold;

        if (moved)
        {
            marker.StableSeconds = 0f;
            marker.HasFiredStableEvent = false;
        }

        // 本次也计入稳定时长（含移动后的第一次），与移植前的计数语义一致。
        marker.StableSeconds += deltaTime;

        float t = SmoothingFactor(deltaTime);
        marker.SmoothedPose = new Pose(
            Vector3.Lerp(marker.SmoothedPose.position, marker.TargetPose.position, t),
            Quaternion.Slerp(marker.SmoothedPose.rotation, marker.TargetPose.rotation, t));

        if (!marker.HasFiredStableEvent && marker.StableSeconds >= stableSeconds)
        {
            marker.HasFiredStableEvent = true;
            Stabilized?.Invoke(rawId, marker.SmoothedPose);
        }
    }

    /// <summary>
    /// 指数平滑：<c>1 - e^(-dt/τ)</c>。与帧率无关，且永远取不到 1 ——
    /// 这正是旧实现缺的那条性质。τ &lt;= 0 视为不平滑。
    /// </summary>
    private float SmoothingFactor(float deltaTime)
    {
        if (smoothTime <= 0f)
        {
            return 1f;
        }

        return 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / smoothTime);
    }

    /// <summary>当前平滑位姿，供测试与诊断查看。未跟踪该 id 时返回 <see cref="Pose.identity"/>。</summary>
    public Pose SmoothedPose(string rawId)
        => tracked.TryGetValue(rawId, out var marker) ? marker.SmoothedPose : Pose.identity;

    public void Reset(string rawId) => tracked.Remove(rawId);
}
