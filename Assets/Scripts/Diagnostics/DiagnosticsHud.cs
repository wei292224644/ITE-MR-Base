using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;

/// <summary>
/// 设备内可读的诊断面板。
///
/// 存在的理由：M0 的多条验证项本质是「想知道运行时到底是什么状态」，而 Editor 的
/// Input Debugger 只能看到 Editor 里的设备 —— 最关键的一项（PICO 上是否存在
/// PicoAimHand）必须在头显里读。所以数据必须显示在设备屏幕上，并同步打到日志
/// 供 `adb logcat -s Unity:V` 留证。
/// </summary>
public class DiagnosticsHud : MonoBehaviour
{
    [Header("输出")]
    [SerializeField] TMP_Text target;

    [Tooltip("同一份内容打到 Console 的间隔秒数；0 表示不打日志。")]
    [SerializeField] float logIntervalSeconds = 2f;

    [Header("要观察的 Action")]
    [Tooltip("手部交互相关的 action。逐个显示当前值与 activeControl —— " +
             "activeControl 会告出当前是哪条 binding 在生效，这是双 binding 验证的直接证据。")]
    [SerializeField] InputActionReference[] watchedActions;

    [Header("passthrough 参考")]
    [SerializeField] Camera xrCamera;

    readonly List<XRHandSubsystem> m_HandSubsystems = new();
    readonly StringBuilder m_Builder = new();

    float m_SmoothedDeltaTime;
    float m_NextLogTime;

    void OnEnable()
    {
        foreach (var reference in EnabledActionReferences())
            reference.action.Enable();
    }

    void Update()
    {
        // 指数平滑，避免单帧尖刺；两端用同一算法保证可比。
        var dt = Time.unscaledDeltaTime;
        m_SmoothedDeltaTime = m_SmoothedDeltaTime <= 0f
            ? dt
            : Mathf.Lerp(m_SmoothedDeltaTime, dt, 0.1f);

        var report = BuildReport();

        if (target != null)
            target.text = report;

        if (logIntervalSeconds > 0f && Time.unscaledTime >= m_NextLogTime)
        {
            m_NextLogTime = Time.unscaledTime + logIntervalSeconds;
            Debug.Log("[DiagnosticsHud]\n" + report);
        }
    }

    string BuildReport()
    {
        m_Builder.Clear();

        AppendFrameRate();
        AppendHandSubsystems();
        AppendActions();
        AppendInputDevices();
        AppendPassthrough();

        return m_Builder.ToString();
    }

    void AppendFrameRate()
    {
        var fps = m_SmoothedDeltaTime > 0f ? 1f / m_SmoothedDeltaTime : 0f;
        m_Builder.Append("FPS ").Append(fps.ToString("F1"))
            .Append("   目标刷新率 ").Append(Screen.currentResolution.refreshRateRatio.value.ToString("F0"))
            .Append('\n');
    }

    void AppendHandSubsystems()
    {
        SubsystemManager.GetSubsystems(m_HandSubsystems);

        m_Builder.Append("\n== XRHandSubsystem (").Append(m_HandSubsystems.Count).Append(") ==\n");
        if (m_HandSubsystems.Count == 0)
        {
            m_Builder.Append("  无。XR 未初始化，或当前 loader 不提供手部追踪。\n");
            return;
        }

        foreach (var subsystem in m_HandSubsystems)
        {
            var descriptor = subsystem.subsystemDescriptor;

            // id 用于区分数据来源：Quest 走 OpenXR 的 provider，PICO 应显示 "PICO Hands"，
            // Editor 里应显示模拟器的 provider。
            m_Builder.Append("  id: ").Append(descriptor.id)
                .Append("  running: ").Append(subsystem.running).Append('\n');

            // 这几项直接对应 design 的硬约束 #4：PICO 未开启通用姿态数据供给，
            // 所以在 PICO 上这里应当全是 False —— 真机上一眼可验。
            m_Builder.Append("  supports aimPose=").Append(descriptor.supportsAimPose)
                .Append(" aimActivate=").Append(descriptor.supportsAimActivateValue)
                .Append(" pinchValue=").Append(descriptor.supportsPinchValue)
                .Append(" grasp=").Append(descriptor.supportsGraspValue).Append('\n');

            AppendHand("  左手", subsystem.leftHand);
            AppendHand("  右手", subsystem.rightHand);
        }
    }

    void AppendHand(string label, XRHand hand)
    {
        m_Builder.Append(label).Append(" isTracked=").Append(hand.isTracked);

        if (!hand.isTracked)
        {
            m_Builder.Append('\n');
            return;
        }

        var tracked = 0;
        for (var id = XRHandJointID.BeginMarker.ToIndex(); id < XRHandJointID.EndMarker.ToIndex(); ++id)
        {
            var joint = hand.GetJoint(XRHandJointIDUtility.FromIndex(id));
            if ((joint.trackingState & XRHandJointTrackingState.Pose) != 0)
                ++tracked;
        }

        m_Builder.Append(" 有效关节=").Append(tracked)
            .Append('/').Append(XRHandJointID.EndMarker.ToIndex() - XRHandJointID.BeginMarker.ToIndex())
            .Append('\n');
    }

    void AppendActions()
    {
        m_Builder.Append("\n== Actions ==\n");

        var any = false;
        foreach (var reference in EnabledActionReferences())
        {
            any = true;
            var action = reference.action;
            var control = action.activeControl;

            m_Builder.Append("  ").Append(action.name)
                .Append(" = ").Append(ReadValueText(action))
                .Append("  activeControl: ")
                .Append(control == null ? "(无)" : control.path)
                .Append('\n');
        }

        if (!any)
            m_Builder.Append("  未指派。请在 Inspector 的 Watched Actions 里挂上手部交互 action。\n");
    }

    static string ReadValueText(InputAction action)
    {
        var value = action.ReadValueAsObject();
        return value == null ? "null" : value.ToString();
    }

    void AppendInputDevices()
    {
        var devices = InputSystem.devices;
        m_Builder.Append("\n== InputSystem.devices (").Append(devices.Count).Append(") ==\n");

        // 期望：Quest 上出现 Meta 的手部 aim 设备，PICO 上出现 PicoAimHand。
        foreach (var device in devices)
            m_Builder.Append("  ").Append(device.name)
                .Append("  [").Append(device.layout).Append("]\n");
    }

    void AppendPassthrough()
    {
        m_Builder.Append("\n== 相机 / passthrough ==\n");

        var cam = xrCamera != null ? xrCamera : Camera.main;
        if (cam == null)
        {
            m_Builder.Append("  找不到相机。\n");
            return;
        }

        // passthrough 的启用方式是平台专属的（Pico 模式 1 下的做法仍是 design 的
        // Open Question），所以这里只报中立的、真正决定合成结果的两项：
        // clearFlags 与背景色 alpha。alpha 必须为 0，虚拟内容才能与 passthrough 正确混合。
        m_Builder.Append("  clearFlags=").Append(cam.clearFlags)
            .Append("  背景色 alpha=").Append(cam.backgroundColor.a.ToString("F2"))
            .Append('\n');
    }

    IEnumerable<InputActionReference> EnabledActionReferences()
    {
        if (watchedActions == null)
            yield break;

        foreach (var reference in watchedActions)
        {
            if (reference != null && reference.action != null)
                yield return reference;
        }
    }
}
