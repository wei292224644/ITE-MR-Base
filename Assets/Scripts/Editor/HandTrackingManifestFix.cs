using System;
using System.Collections.Generic;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.OpenXR.Features;
using Unity.XR.Management.AndroidManifest.Editor;
using UnityEngine.XR.Hands.OpenXR;

/// <summary>
/// 把 <c>oculus.software.handtracking</c> 补进 Android manifest。
///
/// 为什么需要：XR Hands 1.8.1 的 <c>ModifyAndroidManifest</c> 只实现了旧的
/// <c>OnPostGenerateGradleAndroidProjectExt</c>，而 Unity 6 的内部 Android 构建管线
/// 走的是 XR Management 4.4 的 <c>ProvideManifestRequirementExt</c>（对比
/// OpenXR 包里的 ModifyAndroidManifestMeta.cs —— 它两个都实现了，所以
/// android.hardware.vr.headtracking 进得去，handtracking 进不去）。
///
/// 结果：APK 不声明手部追踪，Quest 在无手柄时直接拦截启动，logcat 报
/// common_system_dialog_app_launch_blocked_controller_required。
///
/// 本项目全程手势交互，这条声明是硬需求。等 XR Hands 上游补上新 API 后删掉此文件。
/// </summary>
class HandTrackingManifestFix : OpenXRFeatureBuildHooks
{
    // 排在 MetaQuestFeature(1) 之后即可，与 XR Hands 自己的 hook 保持一致。
    public override int callbackOrder => 10;

    // 基类据此判定：仅当 HandTracking feature 在 Android 上启用时才注入。
    public override Type featureType => typeof(HandTracking);

    protected override ManifestRequirement ProvideManifestRequirementExt() => new()
    {
        SupportedXRLoaders = new HashSet<Type> { typeof(UnityEngine.XR.OpenXR.OpenXRLoader) },
        NewElements = new List<ManifestElement>
        {
            new()
            {
                ElementPath = new List<string> { "manifest", "uses-permission" },
                Attributes = new Dictionary<string, string>
                {
                    { "name", "com.oculus.permission.HAND_TRACKING" }
                }
            },
            new()
            {
                // required=false 与 XR Hands 上游一致：设备须支持手部追踪，但不排斥手柄。
                ElementPath = new List<string> { "manifest", "uses-feature" },
                Attributes = new Dictionary<string, string>
                {
                    { "name", "oculus.software.handtracking" },
                    { "required", "false" }
                }
            }
        }
    };

    protected override void OnPreprocessBuildExt(BuildReport report) { }
    protected override void OnPostGenerateGradleAndroidProjectExt(string path) { }
    protected override void OnPostprocessBuildExt(BuildReport report) { }
}
