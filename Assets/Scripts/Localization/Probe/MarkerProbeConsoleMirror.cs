using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

using Debug = UnityEngine.Debug;

/// <summary>
/// Human-readable live mirror. JSONL remains the authoritative, unthrottled evidence stream.
/// </summary>
public sealed class MarkerProbeConsoleMirror
{
    private const string Prefix = "[MarkerProbe]";

    private readonly object mirrorGate = new object();
    private readonly string sessionId;
    private readonly string logFilePath;
    private readonly double poseIntervalSeconds;
    private readonly Dictionary<string, double> nextPoseLogTime = new Dictionary<string, double>();

    public MarkerProbeConsoleMirror(string sessionId, string logFilePath, double poseIntervalSeconds)
    {
        this.sessionId = sessionId ?? "none";
        this.logFilePath = logFilePath ?? "none";
        this.poseIntervalSeconds = Math.Max(0.1d, poseIntervalSeconds);
    }

    public void LogSessionStarted(string state)
    {
        Debug.Log($"{Prefix} session={sessionId} state={state} log={logFilePath}");
    }

    public void LogState(string eventType, string state, string detail = null)
    {
        Debug.Log(
            $"{Prefix} session={sessionId} event={eventType ?? "unknown"} " +
            $"state={state ?? "unknown"}{FormatDetail(detail)}");
    }

    public void LogError(string eventType, MarkerProbeErrorContext error)
    {
        if (error == null)
        {
            return;
        }

        Debug.LogError(
            $"{Prefix} session={sessionId} event={eventType ?? "unknown"} " +
            $"errorCode={error.errorCode ?? "none"} exception={error.exceptionType ?? "none"} " +
            $"message={error.message ?? "none"}");
    }

    public bool TryLogPose(MarkerProbeLogEvent logEvent)
    {
        MarkerProbePoseSnapshot pose = logEvent?.pose?.unityPose;
        if (pose?.position == null || pose.rotation == null)
        {
            return false;
        }

        string markerKey = logEvent.markerId ?? "unknown";
        double now = logEvent.monotonicTimeSeconds > 0d
            ? logEvent.monotonicTimeSeconds
            : Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        lock (mirrorGate)
        {
            if (nextPoseLogTime.TryGetValue(markerKey, out double nextAllowed) && now < nextAllowed)
            {
                return false;
            }

            nextPoseLogTime[markerKey] = now + poseIntervalSeconds;
        }

        Debug.Log(
            $"{Prefix} session={sessionId} event={logEvent.eventType ?? "pose"} " +
            $"run={logEvent.runId ?? "none"} marker={markerKey} valid=" +
            $"{(logEvent.validFlagAvailable ? logEvent.validFlag.ToString() : "n/a")} " +
            $"position=({pose.position.x:F4},{pose.position.y:F4},{pose.position.z:F4}) " +
            $"rotation=({pose.rotation.x:F4},{pose.rotation.y:F4},{pose.rotation.z:F4},{pose.rotation.w:F4})");
        return true;
    }

    private static string FormatDetail(string detail)
    {
        return string.IsNullOrEmpty(detail) ? string.Empty : $" detail={detail}";
    }
}
