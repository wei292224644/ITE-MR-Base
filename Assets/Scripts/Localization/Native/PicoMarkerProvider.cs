#if MRBASE_PICO && MRBASE_HAS_PICO_SDK
using System;
using System.Collections.Generic;
using Unity.XR.PICO.TOBSupport;
using UnityEngine;
using UnityEngine.XR;

// Unity.XR.PICO.TOBSupport 里也有一个 Pose,与 UnityEngine.Pose 同名。
// IMarkerTrackingProvider 用的是后者,这里显式钉死,否则 CS0104 二义。
using Pose = UnityEngine.Pose;

/// <summary>
/// PICO 端的 marker 追踪。
///
/// 与 Quest 端（MRUK 的 TrackableAdded/TrackableRemoved 增量事件）不同，PICO 走的是
/// 企业 TOB 接口 <see cref="PXR_Enterprise.SetMarkerInfoCallback"/>，回调给的是**全量快照**
/// （当前这一刻所有可见 marker 的列表），没有"丢失"这个事件。所以 MarkerLost 只能靠
/// 比对前后两次快照的 id 集合算出来。
///
/// 另有两点是这套 API 的硬性前提：
/// - 企业服务必须先 Init + Bind，否则 SetMarkerInfoCallback 静默返回 -1，不报错也不回调；
/// - 实现体裹在 <c>#if (UNITY_ANDROID &amp;&amp; !UNITY_EDITOR)</c> 里，Editor 中恒返回 -1，
///   只能真机验证。
/// </summary>
public class PicoMarkerProvider : IMarkerTrackingProvider
{
    public event Action<string, Pose> MarkerResolved;
    public event Action<string> MarkerLost;

    // 上一次快照里可见的 marker id。与本次快照做差集即得 MarkerLost。
    private readonly HashSet<string> visibleIds = new HashSet<string>();

    // 本次快照的 id，复用以免每帧分配。
    private readonly HashSet<string> currentIds = new HashSet<string>();

    private bool tracking;

    public void StartTracking()
    {
        if (tracking)
        {
            return;
        }

        if (!PXR_Enterprise.InitEnterpriseService())
        {
            Debug.LogError("[PicoMarkerProvider] 企业服务初始化失败,marker 追踪不可用。" +
                           "确认设备为企业版且已开通 TOB 授权。");
            return;
        }

        PXR_Enterprise.BindEnterpriseService(OnEnterpriseServiceBound);
    }

    public void StopTracking()
    {
        if (!tracking)
        {
            return;
        }

        // TOB 没有反注册 marker 回调的 API,只能解绑整个企业服务。
        // tracking 置否是为了吞掉解绑期间仍在途的回调。
        tracking = false;
        visibleIds.Clear();
        PXR_Enterprise.UnBindEnterpriseService();
    }

    private void OnEnterpriseServiceBound(bool bound)
    {
        if (!bound)
        {
            Debug.LogError("[PicoMarkerProvider] 企业服务绑定失败,marker 追踪不可用。");
            return;
        }

        // 追踪原点模式必须与 XR 装机的 rig 一致,否则 SDK 会按错误的原点高度补偿 posY,
        // 导致每个 marker 的位姿都差一个人高 —— 这种偏差在真机上极难定位。
        int result = PXR_Enterprise.SetMarkerInfoCallback(ResolveTrackingOriginMode(), 0f, HandleMarkerInfos);
        if (result != 0)
        {
            Debug.LogError($"[PicoMarkerProvider] SetMarkerInfoCallback 返回 {result}(非 0 即失败)。");
            return;
        }

        tracking = true;
    }

    private static TrackingOriginModeFlags ResolveTrackingOriginMode()
    {
        var subsystems = new List<XRInputSubsystem>();
        SubsystemManager.GetSubsystems(subsystems);

        foreach (var subsystem in subsystems)
        {
            var mode = subsystem.GetTrackingOriginMode();
            if (mode != TrackingOriginModeFlags.Unknown)
            {
                return mode;
            }
        }

        // XR 尚未初始化时的退路。Floor 是 MR 场景的常规选择,与 XR Origin 默认一致。
        Debug.LogWarning("[PicoMarkerProvider] 取不到 XR 追踪原点模式,回退为 Floor。");
        return TrackingOriginModeFlags.Floor;
    }

    private void HandleMarkerInfos(List<MarkerInfo> markers)
    {
        if (!tracking)
        {
            return;
        }

        currentIds.Clear();

        // markers 为 null 表示这一刻一个 marker 都没看到(SDK 对空 json 返回 null),
        // 不能提前 return —— 那样上一帧还可见的 marker 就永远收不到 MarkerLost。
        if (markers != null)
        {
            foreach (var marker in markers)
            {
                if (marker.validFlag == 0)
                {
                    continue;
                }

                string rawId = marker.iMarkerId.ToString();
                currentIds.Add(rawId);
                MarkerResolved?.Invoke(rawId, ToPose(marker));
            }
        }

        foreach (string rawId in visibleIds)
        {
            if (!currentIds.Contains(rawId))
            {
                MarkerLost?.Invoke(rawId);
            }
        }

        visibleIds.Clear();
        visibleIds.UnionWith(currentIds);
    }

    // SDK 的 MarkerInfoCallback 已把右手系转成 Unity 左手系(posZ 取负、rotationX/Y 取负)
    // 并补偿了原点高度,这里只做 double → float。
    private static Pose ToPose(MarkerInfo marker)
    {
        var position = new Vector3((float)marker.posX, (float)marker.posY, (float)marker.posZ);
        var rotation = new Quaternion(
            (float)marker.rotationX,
            (float)marker.rotationY,
            (float)marker.rotationZ,
            (float)marker.rotationW);

        return new Pose(position, rotation);
    }
}
#endif
