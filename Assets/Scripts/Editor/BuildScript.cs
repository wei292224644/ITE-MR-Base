using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

/// <summary>
/// 两端出包的唯一入口。
///
/// Unity 6.0 的 XR loader 配置按 BuildTargetGroup 存一份（Quest 与 PICO 同属 Android
/// 共用这一份），而 Build Profile 只能覆盖 Player / Graphics / Quality Settings，碰不到它。
/// loader 又在任何场景加载之前就被读取，错配的表现是黑屏且无日志。
/// 因此构建期的 loader 设置由本脚本承担，人不应手动改 XR Plug-in Management。
/// </summary>
public static class BuildScript
{
    const string k_QuestProfilePath = "Assets/Settings/Build Profiles/Quest.asset";
    const string k_PicoProfilePath = "Assets/Settings/Build Profiles/PICO.asset";

    const string k_OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";
    const string k_PicoLoader = "Unity.XR.PXR.PXR_Loader";

    // loader 用类型全名字符串指定，所以本程序集不需要引用 Unity.XR.OpenXR 或
    // Unity.XR.PICO —— 任一 SDK 未安装时本脚本仍能编译。
    static readonly string[] k_AndroidLoaders = { k_OpenXRLoader, k_PicoLoader };

    const string k_PicoPackageRoot = "Packages/com.unity.xr.picoxr";
    const string k_MetaPackageRoot = "Packages/com.meta.xr.sdk.core";

    [MenuItem("MRBase/Build/Quest")]
    public static void BuildQuest()
    {
        Build(k_QuestProfilePath, "MRBASE_QUEST", k_OpenXRLoader, "Builds/Quest/MR_Base.apk",
            excludePluginRoot: k_PicoPackageRoot);
    }

    [MenuItem("MRBase/Build/Pico")]
    public static void BuildPico()
    {
        Build(k_PicoProfilePath, "MRBASE_PICO", k_PicoLoader, "Builds/Pico/MR_Base.apk",
            excludePluginRoot: k_MetaPackageRoot);
    }

    /// <summary>
    /// 跑完校验与 loader 设置/还原，但不打包。用于秒级确认配置正确、
    /// 以及确认 loader 错配时脚本会自行纠正。
    /// </summary>
    [MenuItem("MRBase/Build/Dry Run Quest")]
    public static void DryRunQuest()
    {
        Build(k_QuestProfilePath, "MRBASE_QUEST", k_OpenXRLoader, "Builds/Quest/MR_Base.apk", dryRun: true);
    }

    [MenuItem("MRBase/Build/Dry Run Pico")]
    public static void DryRunPico()
    {
        Build(k_PicoProfilePath, "MRBASE_PICO", k_PicoLoader, "Builds/Pico/MR_Base.apk", dryRun: true);
    }

    // 这里故意没有 BuildBoth：切换 Build Profile 会改 scripting defines，
    // 触发脚本重编译并打断正在执行的编辑器脚本。两端构建用 Tools/build-both.sh
    // 分两次调用 Unity。

    static void Build(
        string profilePath,
        string expectedDefine,
        string loaderTypeName,
        string outputPath,
        string excludePluginRoot = null,
        bool dryRun = false)
    {
        var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(profilePath);
        if (profile == null)
            throw new BuildFailedException($"[BuildScript] 找不到 Build Profile：{profilePath}");

        AssertNoVendorSpatializer();
        AssertProfileDeclaresDefine(profile, expectedDefine, profilePath);

        var manager = AndroidManagerSettings();
        var restoreLoaders = SnapshotAndroidLoaders();
        var previousProfile = BuildProfile.GetActiveBuildProfile();
        List<string> restorePlugins = null;

        try
        {
            ApplyAndroidLoader(manager, loaderTypeName);
            restorePlugins = DisableAndroidPluginsUnder(excludePluginRoot);

            // 激活 Profile 会带入它的 scripting defines（MRBASE_QUEST / MRBASE_PICO），
            // 这是构建正确的前提，但也意味着 Editor 的 define 集合被改动 —— 必须在 finally 还原。
            BuildProfile.SetActiveBuildProfile(profile);

            var options = new BuildPlayerOptions
            {
                scenes = profile.GetScenesForBuild()
                    .Where(scene => scene.enabled)
                    .Select(scene => scene.path)
                    .ToArray(),
                locationPathName = Path.GetFullPath(outputPath),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };

            if (options.scenes == null || options.scenes.Length == 0)
                throw new BuildFailedException(
                    $"[BuildScript] {profile.name} 的场景列表为空。检查 Build Profile 的 Scene List " +
                    "或 EditorBuildSettings。");

            if (dryRun)
            {
                Debug.Log(
                    $"[BuildScript] dry run 通过：profile={profile.name}，loader={loaderTypeName}，" +
                    $"场景 {options.scenes.Length} 个（{string.Join(", ", options.scenes)}），未打包。");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
                throw new BuildFailedException(
                    $"[BuildScript] 构建失败：{summary.result}，{summary.totalErrors} 个错误。");

            Debug.Log($"[BuildScript] 构建成功：{outputPath}（{summary.totalTime}）");
        }
        finally
        {
            // 还原 loader、plugin 兼容性与激活的 Profile。分别避免：
            // 在 XRGeneralSettingsPerBuildTarget.asset 上留 git diff、
            // 让另一端的 native plugin 处于关闭状态、
            // 把 Editor 留在另一个平台的 define 集合下（会让另一端平台代码意外参与编译）。
            RestoreAndroidLoaders(manager, restoreLoaders);
            RestoreAndroidPlugins(restorePlugins);
            if (previousProfile != profile)
                BuildProfile.SetActiveBuildProfile(previousProfile);
        }
    }

    // ---- 校验 ----

    /// <summary>
    /// Spatializer Plugin 是 ProjectSettings 里的全局单选项，一旦指向厂商插件，
    /// 另一端的构建产物就是错的 —— 而这个错误只在真机上表现，Editor 里看不出来。
    /// </summary>
    static void AssertNoVendorSpatializer()
    {
        var spatializer = AudioSettings.GetSpatializerPluginName();
        if (!string.IsNullOrEmpty(spatializer))
            throw new BuildFailedException(
                $"[BuildScript] Spatializer Plugin 被设为 \"{spatializer}\"。跨端项目必须使用 Unity 内置方案，" +
                "请在 Project Settings > Audio 中清空该项。");
    }

    static void AssertProfileDeclaresDefine(BuildProfile profile, string expectedDefine, string profilePath)
    {
        var defines = profile.scriptingDefines;
        if (defines != null && Array.IndexOf(defines, expectedDefine) >= 0)
            return;

        throw new BuildFailedException(
            $"[BuildScript] {profilePath} 的 Scripting Defines 中没有 {expectedDefine}。" +
            "构建意图与 Build Profile 不匹配，请在该 Profile 的 Scripting Defines 中补上。");
    }

    // ---- loader ----

    static XRManagerSettings AndroidManagerSettings()
    {
        if (!EditorBuildSettings.TryGetConfigObject(
                XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) ||
            perTarget == null)
        {
            throw new BuildFailedException(
                "[BuildScript] 找不到 XR 设置（XRGeneralSettingsPerBuildTarget）。" +
                "请先打开一次 Project Settings > XR Plug-in Management。");
        }

        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

        var manager = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        if (manager == null)
            throw new BuildFailedException("[BuildScript] Android 的 XRManagerSettings 为空。");

        return manager;
    }

    /// <summary>
    /// 只快照本项目关心的两个 Android loader。Standalone 那一档不受影响（不同 BuildTargetGroup）。
    /// </summary>
    static bool[] SnapshotAndroidLoaders()
    {
        var state = new bool[k_AndroidLoaders.Length];
        for (var i = 0; i < k_AndroidLoaders.Length; i++)
            state[i] = XRPackageMetadataStore.IsLoaderAssigned(k_AndroidLoaders[i], BuildTargetGroup.Android);
        return state;
    }

    static void ApplyAndroidLoader(XRManagerSettings manager, string loaderTypeName)
    {
        foreach (var other in k_AndroidLoaders)
        {
            if (other == loaderTypeName)
                continue;
            XRPackageMetadataStore.RemoveLoader(manager, other, BuildTargetGroup.Android);
        }

        if (!XRPackageMetadataStore.AssignLoader(manager, loaderTypeName, BuildTargetGroup.Android))
        {
            throw new BuildFailedException(
                $"[BuildScript] 无法为 Android 启用 {loaderTypeName}。" +
                "对应的 XR 插件包可能未安装。");
        }

        if (!XRPackageMetadataStore.IsLoaderAssigned(loaderTypeName, BuildTargetGroup.Android))
        {
            throw new BuildFailedException(
                $"[BuildScript] {loaderTypeName} 设置后仍未生效，中止构建以免产出黑屏包。");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[BuildScript] Android loader = {loaderTypeName}");
    }

    // ---- 另一端的 native plugin ----

    /// <summary>
    /// 把指定包下的 Android native plugin 从本次构建中排除。
    ///
    /// 必要性：native plugin 是否进包由 PluginImporter 决定，**与启用了哪个 XR loader 无关**。
    /// 所以打 Quest 包时 PICO 的 AAR 一样会被打进去，而两家都携带
    /// `lib/arm64-v8a/libopenxr_loader.so`（PICO 的 LoaderForUnitySDK 与 Meta 的 OVRPlugin），
    /// Gradle 的 MergeNativeLibsTask 会因同名文件冲突直接失败。双 APK 本身挡不住这件事。
    ///
    /// 用 <see cref="PluginImporter.SetIncludeInBuildDelegate"/> 而不是
    /// <c>SetCompatibleWithPlatform</c>：两个 SDK 都是 Git 来源的**不可变包**，改平台兼容性
    /// 需要写包内 `.meta`，Unity 会在 <c>SaveAndReimport()</c> 时静默回滚。
    /// delegate 是会话级的、不落盘，PICO SDK 自己也是用这个机制门控 PxrPlatform.aar 的。
    /// </summary>
    /// <returns>被设过 delegate 的资源路径，供 finally 清除。</returns>
    static List<string> DisableAndroidPluginsUnder(string packageRoot)
    {
        var changed = new List<string>();
        if (string.IsNullOrEmpty(packageRoot))
            return changed;

        foreach (var path in AndroidPluginPathsUnder(packageRoot))
        {
            if (!(AssetImporter.GetAtPath(path) is PluginImporter importer))
                continue;
            if (!importer.GetCompatibleWithPlatform(BuildTarget.Android))
                continue;

            importer.SetIncludeInBuildDelegate(_ => false);
            changed.Add(path);
        }

        if (changed.Count > 0)
            Debug.Log($"[BuildScript] 已为本次构建排除 {packageRoot} 下 {changed.Count} 个 Android native plugin");

        return changed;
    }

    static void RestoreAndroidPlugins(List<string> changed)
    {
        if (changed == null)
            return;

        foreach (var path in changed)
        {
            if (AssetImporter.GetAtPath(path) is PluginImporter importer)
                importer.SetIncludeInBuildDelegate(null);
        }
    }

    static IEnumerable<string> AndroidPluginPathsUnder(string packageRoot)
    {
        foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { packageRoot }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(".aar", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
                yield return path;
        }
    }

    static void RestoreAndroidLoaders(XRManagerSettings manager, bool[] state)
    {
        for (var i = 0; i < k_AndroidLoaders.Length; i++)
        {
            var loader = k_AndroidLoaders[i];
            var assigned = XRPackageMetadataStore.IsLoaderAssigned(loader, BuildTargetGroup.Android);
            if (state[i] == assigned)
                continue;

            if (state[i])
                XRPackageMetadataStore.AssignLoader(manager, loader, BuildTargetGroup.Android);
            else
                XRPackageMetadataStore.RemoveLoader(manager, loader, BuildTargetGroup.Android);
        }

        AssetDatabase.SaveAssets();
    }
}
