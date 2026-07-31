using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;

/// <summary>
/// 场景里那几个「到处都要用、但到处都得重新找一遍」的对象的取用点。
///
/// 只收敛查找，不承担生命周期 —— 装配与前置检查是 <see cref="MRBootstrap"/> 的事。
/// </summary>
public class MRContext : StaticInstance<MRContext>
{
    [SerializeField] XROrigin origin;

    /// <summary>XR Origin 下的那台相机。<c>Camera.main</c> 在多相机场景里不可靠。</summary>
    public Camera Camera => origin == null ? null : origin.Camera;

    public XROrigin Origin => origin;

    /// <summary>
    /// 当前在跑的手部子系统。可能为 null —— XR 子系统的启动晚于 Awake，
    /// 且 loader 不提供手部追踪时永远为 null。调用方必须判空。
    /// </summary>
    public XRHandSubsystem Hands
    {
        get
        {
            if (cachedHands != null && cachedHands.running)
                return cachedHands;

            cachedHands = null;
            SubsystemManager.GetSubsystems(handsBuffer);
            foreach (var subsystem in handsBuffer)
            {
                if (!subsystem.running)
                    continue;
                cachedHands = subsystem;
                break;
            }

            return cachedHands;
        }
    }

    readonly List<XRHandSubsystem> handsBuffer = new();
    XRHandSubsystem cachedHands;

    protected override void AfterAwake()
    {
        if (origin == null)
            origin = FindFirstObjectByType<XROrigin>();
    }
}
