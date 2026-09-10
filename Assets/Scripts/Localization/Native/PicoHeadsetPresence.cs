using System;
using UnityEngine;
#if MRBASE_PICO && MRBASE_HAS_PICO_SDK
using Unity.XR.PXR;
#endif

/// <summary>
/// PICO 的佩戴状态读取（design D19）。
///
/// 不走 <c>InputDevices</c> 的 <c>userPresence</c>：PICO 有专门通道
/// （<c>XR_TYPE_EVENT_DATA_USER_PRESENCE_CHANGED_EXT</c> → <c>UserPresenceChangedAction</c>），
/// 赌通用输入设备是否恰好上报，读不到时整条佩戴语义就静默失效。
///
/// 订阅事件而不是每帧轮询：<c>UPxr_GetPSensorState()</c> 每次调用都会打一条
/// <c>PLog.d</c>，每帧调等于刷屏。初值轮询一次，之后跟事件走。
/// </summary>
public sealed class PicoHeadsetPresence : IDisposable
{
    private bool? _presence;
#if MRBASE_PICO && MRBASE_HAS_PICO_SDK
    private bool _subscribed;
#endif

    public PicoHeadsetPresence()
    {
#if MRBASE_PICO && MRBASE_HAS_PICO_SDK
        try
        {
            _presence = PXR_Plugin.Sensor.UPxr_GetPSensorState();
        }
        catch (Exception e)
        {
            // 读不到不是致命的：保持 null，交由上层按「平台不上报」处理，
            // 那条路径会把佩戴状态当作「已佩戴」，导览照常。
            Debug.LogWarning("[PicoHeadsetPresence] 初值读取失败：" + e.Message);
        }

        PXR_Plugin.System.UserPresenceChangedAction += HandleChanged;
        _subscribed = true;
#endif
    }

    /// <summary>交给 <c>HeadsetPresenceAdapter</c> 的读取委托。null 表示尚未拿到任何读数。</summary>
    public bool? Read() => _presence;

#if MRBASE_PICO && MRBASE_HAS_PICO_SDK
    private void HandleChanged(bool isUserPresent) => _presence = isUserPresent;
#endif

    public void Dispose()
    {
#if MRBASE_PICO && MRBASE_HAS_PICO_SDK
        if (_subscribed)
        {
            PXR_Plugin.System.UserPresenceChangedAction -= HandleChanged;
            _subscribed = false;
        }
#endif
    }
}
