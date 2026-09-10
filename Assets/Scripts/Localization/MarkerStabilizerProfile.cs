using System;
using UnityEngine;

/// <summary>
/// 防抖参数，**两端各一套**（design D9）。
///
/// 不共用一组：两端的噪声特性与派发速率都不同 —— Quest 的 trackable 由空间锚背书、
/// 70 Hz 派发；PICO 的位姿由单应解出、相邻两帧抖 2–5 度、5.6 Hz 派发。
/// 但两端走同一条代码路径，差异只在这里的数字，避免故障只能在各自真机上复现。
///
/// 缺省值是**起点不是结论**：真机调参把实测值回填到本资产（tasks 9.3）。
/// </summary>
[CreateAssetMenu(fileName = "MarkerStabilizerProfile", menuName = "MRBase/Localization/Marker Stabilizer Profile")]
public class MarkerStabilizerProfile : ScriptableObject
{
    [Serializable]
    public struct Settings
    {
        [Tooltip("小于这个距离的位移被视为未移动（米）")]
        public float positionThreshold;

        [Tooltip("小于这个角度的旋转被视为未移动（度）。必须大于该平台的实测抖动带，否则永不判稳")]
        public float rotationThreshold;

        [Tooltip("平滑时间常数（秒）。必须明显大于帧间隔，否则平滑退化为逐帧跳变")]
        public float smoothTime;

        [Tooltip("连续稳定多久算判稳（秒）。是时间不是次数——两端派发速率差 12.5 倍")]
        public float stableSeconds;

        public MarkerStabilizer CreateStabilizer()
            => new MarkerStabilizer(positionThreshold, rotationThreshold, smoothTime, stableSeconds);
    }

    [Header("Quest")]
    [Tooltip("MRUK 的 trackable 由空间锚背书，预期比 PICO 松得多")]
    public Settings quest = new Settings
    {
        positionThreshold = 0.05f,
        rotationThreshold = 1f,
        smoothTime = 0.2f,
        stableSeconds = 0.4f,
    };

    [Header("PICO")]
    [Tooltip("单应解出的位姿实测抖 2–5 度，角度阈值必须高于这个带宽")]
    public Settings pico = new Settings
    {
        positionThreshold = 0.05f,
        rotationThreshold = 6f,
        smoothTime = 0.3f,
        stableSeconds = 0.5f,
    };

    public Settings For(MarkerPlatform platform)
        => platform == MarkerPlatform.Pico ? pico : quest;
}
