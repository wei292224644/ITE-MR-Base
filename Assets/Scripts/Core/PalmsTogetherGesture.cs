using System;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;

/// <summary>
/// 双手合十语义触发器（门面）。并行求值路径 A（JointMath）与路径 B（HandPoseComposite），
/// 对外 <see cref="Performed"/> / <see cref="Released"/> / <see cref="IsHeld"/> 仅跟随
/// <see cref="ActiveSource"/>（默认 B：官方 Shape/Pose 复合，真机对比后选定）。
/// 诊断可读 <see cref="IsHeldA"/> / <see cref="IsHeldB"/>。
/// </summary>
public class PalmsTogetherGesture : MonoBehaviour
{
    public enum Source
    {
        JointMath = 0,
        HandPoseComposite = 1,
    }

    [SerializeField] PalmsTogetherJointMath.Tuning tuning = PalmsTogetherJointMath.Tuning.Default;

    [Tooltip("要保持多久才算数，秒。")]
    [SerializeField] float holdSeconds = 0.3f;

    [Tooltip("松开时阈值放宽的倍数。")]
    [SerializeField, Min(1f)] float releaseSlack = 1.4f;

    [Tooltip("对外事件由哪条路径驱动。默认 HandPoseComposite（B）。A 仍并行求值供 HUD 对比。")]
    [SerializeField] Source activeSource = Source.HandPoseComposite;

    [Header("Path B — HandPoseComposite")]
    [SerializeField] XRHandPose leftHandPose;
    [SerializeField] XRHandPose rightHandPose;

    [Tooltip("路径 B 用来指向「对方手腕」的代理 Transform；为空则运行时自动创建。")]
    [SerializeField] Transform leftWristProxy;
    [SerializeField] Transform rightWristProxy;

    /// <summary>合十保持够久，触发一次（仅活动路径）。</summary>
    public event Action Performed;

    /// <summary>从合十状态散开（仅活动路径）。</summary>
    public event Action Released;

    public Source ActiveSource
    {
        get => activeSource;
        set => activeSource = value;
    }

    /// <summary>对外保持态：跟随活动路径。</summary>
    public bool IsHeld => activeSource == Source.JointMath ? IsHeldA : IsHeldB;

    public bool IsHeldA => trackerA.IsHeld;
    public bool IsHeldB => trackerB.IsHeld;

    /// <summary>本帧路径 A 原始匹配（未过 hold）。</summary>
    public bool RawMatchA { get; private set; }

    /// <summary>本帧路径 B 原始匹配（未过 hold）。</summary>
    public bool RawMatchB { get; private set; }

    readonly PalmsTogetherHoldTracker trackerA = new();
    readonly PalmsTogetherHoldTracker trackerB = new();
    readonly XRHandJointsUpdatedEventArgs leftArgs = new();
    readonly XRHandJointsUpdatedEventArgs rightArgs = new();

    Source lastEmittingSource = Source.HandPoseComposite;
    bool lastEmittedHeld;

    void Awake()
    {
        EnsureProxies();
    }

    void Update()
    {
        var hands = MRContext.Instance == null ? null : MRContext.Instance.Hands;

        bool rawA = EvaluatePathA(hands, trackerA.IsHeld ? tuning.Relaxed(releaseSlack) : tuning);
        bool rawB = EvaluatePathB(hands, trackerB.IsHeld
            ? tuning.maxWristGap * releaseSlack
            : tuning.maxWristGap);

        RawMatchA = rawA;
        RawMatchB = rawB;

        trackerA.Tick(rawA, holdSeconds, Time.deltaTime, out var edgeA);
        trackerB.Tick(rawB, holdSeconds, Time.deltaTime, out var edgeB);

        EmitForActiveSource(edgeA, edgeB);
    }

    void EmitForActiveSource(bool? edgeA, bool? edgeB)
    {
        // 切换活动源时：若新旧保持态不一致，补一条边沿，避免订阅方卡在旧状态。
        bool heldNow = IsHeld;
        if (activeSource != lastEmittingSource)
        {
            if (lastEmittedHeld && !heldNow)
                Released?.Invoke();
            else if (!lastEmittedHeld && heldNow)
                Performed?.Invoke();

            lastEmittingSource = activeSource;
            lastEmittedHeld = heldNow;
            return;
        }

        bool? edge = activeSource == Source.JointMath ? edgeA : edgeB;
        if (!edge.HasValue)
        {
            lastEmittedHeld = heldNow;
            return;
        }

        if (edge.Value)
            Performed?.Invoke();
        else
            Released?.Invoke();

        lastEmittedHeld = heldNow;
    }

    bool EvaluatePathA(XRHandSubsystem hands, PalmsTogetherJointMath.Tuning t)
    {
        if (hands == null)
            return false;

        var left = hands.leftHand;
        var right = hands.rightHand;
        if (!left.isTracked || !right.isTracked)
            return false;

        return PalmsTogetherJointMath.Matches(
            left.rootPose, right.rootPose,
            MiddleCurl(left), MiddleCurl(right), t);
    }

    bool EvaluatePathB(XRHandSubsystem hands, float maxWristGap)
    {
        if (hands == null || leftHandPose == null || rightHandPose == null)
            return false;

        var left = hands.leftHand;
        var right = hands.rightHand;
        if (!left.isTracked || !right.isTracked)
            return false;

        if (leftWristProxy == null || rightWristProxy == null)
            return false;

        // 代理指向「对方手腕」—— Pose 的 HandToTarget 朝向条件吃 Transform。
        leftWristProxy.SetPositionAndRotation(left.rootPose.position, left.rootPose.rotation);
        rightWristProxy.SetPositionAndRotation(right.rootPose.position, right.rootPose.rotation);

        if (leftHandPose.relativeOrientation != null)
            leftHandPose.relativeOrientation.targetTransform = rightWristProxy;
        if (rightHandPose.relativeOrientation != null)
            rightHandPose.relativeOrientation.targetTransform = leftWristProxy;

        leftArgs.hand = left;
        rightArgs.hand = right;

        try
        {
            if (!leftHandPose.CheckConditions(leftArgs))
                return false;
            if (!rightHandPose.CheckConditions(rightArgs))
                return false;
        }
        catch (Exception)
        {
            // Pose / Shape 在目标或追踪异常时不应拖垮 Update。
            return false;
        }

        return PalmsTogetherJointMath.WristGapOk(left.rootPose, right.rootPose, maxWristGap);
    }

    static float MiddleCurl(XRHand hand)
    {
        var shape = hand.CalculateFingerShape(XRHandFingerID.Middle, XRFingerShapeTypes.FullCurl);
        return shape.TryGetFullCurl(out float curl) ? curl : 0f;
    }

    void EnsureProxies()
    {
        if (leftWristProxy == null)
        {
            var go = new GameObject("PalmsTogether_LeftWristProxy");
            go.transform.SetParent(transform, false);
            leftWristProxy = go.transform;
        }

        if (rightWristProxy == null)
        {
            var go = new GameObject("PalmsTogether_RightWristProxy");
            go.transform.SetParent(transform, false);
            rightWristProxy = go.transform;
        }
    }

    /// <summary>兼容旧测试入口：转发到路径 A 纯判据。</summary>
    public static bool Matches(Pose leftWrist, Pose rightWrist,
                               float leftCurl, float rightCurl, in PalmsTogetherJointMath.Tuning t) =>
        PalmsTogetherJointMath.Matches(leftWrist, rightWrist, leftCurl, rightCurl, t);
}
