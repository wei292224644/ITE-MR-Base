using UnityEngine;

public enum MarkerPlatform
{
    Quest,
    Pico,
}

/// <summary>
/// 契约唯一的事件载荷。只带平台标签与原始 payload,不做任何身份分层或收敛
/// (design D3,由 /opsx:probe 否决原三层 MarkerIdentity 方案)。
/// </summary>
public readonly struct MarkerObservation
{
    public readonly MarkerPlatform Platform;

    /// <summary>Quest:QR 原文;PICO:AprilTag 整数 ID 的 ToString()。一律非空。</summary>
    public readonly string RawPayload;

    public readonly Pose Pose;

    public MarkerObservation(MarkerPlatform platform, string rawPayload, Pose pose)
    {
        Platform = platform;
        RawPayload = rawPayload;
        Pose = pose;
    }
}
