using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEngine;

/// <summary>
/// 构建期最后一道闸门：确认 manifest 里该有的厂商项都在。
///
/// 存在的理由：两端各出过一次「构建成功但 manifest 内容不对」，两次都只有装到真机上才发现
/// （Quest 弹 app_launch_blocked，PICO 不进 VR 模式）。PICO 那次尤其危险 ——
/// PXR_Manifest 内部抛了 NullReferenceException，Unity 把它当普通日志吞掉，构建照样 Succeeded。
/// 所以「构建成功」这个信号本身不可信，必须在出包前查内容。
///
/// callbackOrder 取 100000，比已知的最大值大：
///   XR Management 1 / XR Hands 10 / PICO PXR_Manifest 10000 / Meta OVRGradleGeneration 99999
/// </summary>
class ManifestGuard : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 100000;

    // 按激活的 loader 分派，不用 MRBASE_QUEST / MRBASE_PICO ——
    // 那两个是 Build Profile 的 scripting define，只进 player 程序集，编辑器程序集读不到。
    const string k_OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";
    const string k_PicoLoader = "Unity.XR.PXR.PXR_Loader";

    // 每项都对应一次真机故障，不是「配置看起来该有」。
    static readonly Dictionary<string, string[]> k_Required = new()
    {
        [k_OpenXRLoader] = new[]
        {
            "oculus.software.handtracking",          // 缺了 Quest 直接拒绝启动
            "com.oculus.permission.HAND_TRACKING",
        },
        [k_PicoLoader] = new[]
        {
            "pvr.app.type",                          // 缺了 PICO 不进 VR 模式
            "name=\"handtracking\"",
            "com.picovr.permission.HAND_TRACKING",
        },
    };

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        var loader = ActiveAndroidLoader();
        if (loader == null || !k_Required.TryGetValue(loader, out var required))
        {
            Debug.LogWarning($"[ManifestGuard] 未识别的 Android loader（{loader ?? "无"}），跳过 manifest 校验。");
            return;
        }

        // XR Management 把自己那份写进独立的 xrmanifest.androidlib 模块（不受厂商钩子覆盖），
        // 厂商钩子写 unityLibrary。合并在 gradle 阶段，此时还没合，所以两个都要查。
        var texts = new[]
            {
                Path.Combine(path, "src/main/AndroidManifest.xml"),
                Path.Combine(path, "xrmanifest.androidlib/AndroidManifest.xml"),
            }
            .Where(File.Exists)
            .Select(File.ReadAllText)
            .ToArray();

        var missing = required.Where(entry => !texts.Any(text => text.Contains(entry))).ToArray();
        if (missing.Length > 0)
            throw new BuildFailedException(
                $"[ManifestGuard] {loader} 的 AndroidManifest 缺少：{string.Join("、", missing)}。" +
                "包会构建成功但在真机上不可用，已中止。厂商的 manifest 钩子可能静默失败了 —— " +
                "在 Editor.log 里搜该钩子类名，Unity 会吞掉钩子内抛出的异常。");

        Debug.Log($"[ManifestGuard] {loader}：{required.Length} 项 manifest 声明齐全。");
    }

    static string ActiveAndroidLoader()
    {
        var settings = XRGeneralSettingsPerBuildTarget
            .XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android)?.AssignedSettings;
        return settings?.activeLoaders.FirstOrDefault()?.GetType().FullName;
    }
}
