using UnityEngine;

[CreateAssetMenu(fileName = "PlatformOffsetConfig", menuName = "MRBase/Localization/Platform Offset Config")]
public class PlatformOffsetConfig : ScriptableObject
{
    [Tooltip("Quest QR 码局部坐标系到面板内容锚点的固定偏移。默认 identity,需在面板设计稿定稿后按坐标回填,不要在现场测量。")]
    public Pose questMarkerToTargetOffset = Pose.identity;

    [Tooltip("PICO ArUco 码局部坐标系到面板内容锚点的固定偏移。默认 identity,需在面板设计稿定稿后按坐标回填,不要在现场测量。")]
    public Pose picoMarkerToTargetOffset = Pose.identity;
}
