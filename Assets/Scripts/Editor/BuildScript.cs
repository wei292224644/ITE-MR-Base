using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    const string k_MRCoreScene = "Assets/Scenes/MRCore.unity";
    const string k_MarkerProbeScene = "Assets/Scenes/MarkerProbe.unity";
    const string k_PicoQrCameraProbeScene = "Assets/Scenes/PicoQrCameraProbe.unity";
    const string k_GsplatBenchScene = "Assets/Scenes/GsplatBench.unity";

    const string k_PicoOfficialCameraRenderingScene =
        "Packages/com.unity.xr.picoxr/Enterprise/Sample/CameraRendering/PXR/CameraRendering.unity";

    // 数组首项是启动场景。探针包与主包共用同一条启动路径：MRCore 先起来装配 XR，探针场景
    // 由 MRSceneDirector 以 Additive 加载 —— 与 D3 的「一个常驻核心场景」保持一致。
    //
    // 早前是反过来的：探针场景排首位，靠场景里的 MRCoreLoader 在运行时把 MRCore 拉进来。
    // 那等于第二条启动路径，而两条路径的差异只在真机上显形 —— 装配重复带入时 StaticInstance
    // 销毁后来者，漏判时静态 Instance 指向已销毁对象。删掉一条比给它加防护便宜。
    //
    // 只有探针用 sceneOverride：它们要保持极简来做测量，不该带上整套内容场景。demo 与测试
    // 场景都在 MRBase/Build/Quest 的统一包里，运行时用 MRSceneDirector 的菜单切换。
    // 探针包里 MRSceneDirector.firstScene 为空，起来后在菜单点一次进探针场景。
    static string[] ProbeScenes(string probeScene) => new[] { k_MRCoreScene, probeScene };

    const string k_OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";
    const string k_PicoLoader = "Unity.XR.PXR.PXR_Loader";

    // loader 用类型全名字符串指定，所以本程序集不需要引用 Unity.XR.OpenXR 或
    // Unity.XR.PICO —— 任一 SDK 未安装时本脚本仍能编译。
    static readonly string[] k_AndroidLoaders = { k_OpenXRLoader, k_PicoLoader };

    const string k_PicoPackageRoot = "Packages/com.unity.xr.picoxr";
    const string k_MetaPackageRoot = "Packages/com.meta.xr.sdk.core";

    const string k_PicoSettingsKey = "Unity.XR.PXR.Settings";
    const string k_PicoSettingsPath = "Assets/XR/Settings/PXR_Settings.asset";

    [MenuItem("MRBase/Build/Quest")]
    public static void BuildQuest()
    {
        Build(k_QuestProfilePath, "MRBASE_QUEST", k_OpenXRLoader, "Builds/Quest/MR_Base.apk",
            excludePluginRoot: k_PicoPackageRoot);
    }

    [MenuItem("MRBase/Build/Queue Quest")]
    public static void QueueQuest()
    {
        QueueBuild(BuildQuest, "Quest");
    }

    [MenuItem("MRBase/Build/Pico")]
    public static void BuildPico()
    {
        Build(k_PicoProfilePath, "MRBASE_PICO", k_PicoLoader, "Builds/Pico/MR_Base.apk",
            excludePluginRoot: k_MetaPackageRoot);
    }

    [MenuItem("MRBase/Build/Marker Probe/Quest Development")]
    public static void BuildMarkerProbeQuest()
    {
        Build(
            k_QuestProfilePath,
            "MRBASE_QUEST",
            k_OpenXRLoader,
            "Builds/MarkerProbe/Quest/MarkerProbe-Quest.apk",
            excludePluginRoot: k_PicoPackageRoot,
            sceneOverride: ProbeScenes(k_MarkerProbeScene),
            buildOptions: BuildOptions.Development | BuildOptions.AllowDebugging,
            applicationIdSuffix: ".markerprobe");
    }

    [MenuItem("MRBase/Build/Marker Probe/Queue Quest Development")]
    public static void QueueMarkerProbeQuest()
    {
        QueueBuild(BuildMarkerProbeQuest, "Quest Marker Probe");
    }

    [MenuItem("MRBase/Build/Marker Probe/PICO Development")]
    public static void BuildMarkerProbePico()
    {
        Build(
            k_PicoProfilePath,
            "MRBASE_PICO",
            k_PicoLoader,
            "Builds/MarkerProbe/PICO/MarkerProbe-PICO.apk",
            excludePluginRoot: k_MetaPackageRoot,
            sceneOverride: ProbeScenes(k_MarkerProbeScene),
            buildOptions: BuildOptions.Development | BuildOptions.AllowDebugging,
            applicationIdSuffix: ".markerprobe");
    }

    [MenuItem("MRBase/Build/PICO QR Camera Probe Development")]
    public static void BuildPicoQrCameraProbe()
    {
        Build(
            k_PicoProfilePath,
            "MRBASE_PICO",
            k_PicoLoader,
            "Builds/Localization/PICO/PicoQrCameraProbe.apk",
            excludePluginRoot: k_MetaPackageRoot,
            sceneOverride: ProbeScenes(k_PicoQrCameraProbeScene),
            buildOptions: BuildOptions.Development | BuildOptions.AllowDebugging,
            applicationIdSuffix: ".qrcamprobe");
    }

    /// <summary>
    /// 3DGS 性能实测装置（openspec: gsplat-quest-bench）。
    ///
    /// 与其它探针不同，场景列表里**没有 MRCore** —— 这套装置刻意不走产品启动路径，
    /// 否则 MRCore 的常驻装配会把开销混进被测数字。它是可整体删除的一次性探针，
    /// 删除时连同本菜单项一起移除。
    ///
    /// 也与其它探针不同：**不加 Development / AllowDebugging**。
    /// 其它探针要的是能连 Profiler、能断点；这套要的是「测出来的数等于将来产品跑出来的数」。
    /// AllowDebugging 会让 IL2CPP 插入调试钩子——既拖慢构建，也污染被测运行时。
    /// 装置自己有 HUD 和 .log，不依赖 Profiler 连接；Debug.Log 在 release 包里照样进 logcat。
    ///
    /// 构建成功后自动 adb 安装并拉起（<c>installAfterBuild</c>）—— 这套装置的用法是
    /// 「改一个旋钮、出一次包、戴上看数」，每轮都手动装机会把十几分钟的循环再拉长。
    /// 没插设备时只警告，不影响构建结果。
    /// </summary>
    [MenuItem("MRBase/Build/Gsplat Bench/Quest")]
    public static void BuildGsplatBenchQuest()
    {
        Build(
            k_QuestProfilePath,
            "MRBASE_QUEST",
            k_OpenXRLoader,
            "Builds/GsplatBench/GsplatBench-Quest.apk",
            excludePluginRoot: k_PicoPackageRoot,
            sceneOverride: new[] { k_GsplatBenchScene },
            buildOptions: BuildOptions.None,
            applicationIdSuffix: ".gsplatbench",
            installAfterBuild: true);
    }

    [MenuItem("MRBase/Build/Gsplat Bench/Queue Quest")]
    public static void QueueGsplatBenchQuest()
    {
        QueueBuild(BuildGsplatBenchQuest, "Quest Gsplat Bench");
    }

    [MenuItem("MRBase/Build/PICO Official CameraRendering Sample")]
    public static void BuildPicoOfficialCameraRenderingSample()
    {
        Build(
            k_PicoProfilePath,
            "MRBASE_PICO",
            k_PicoLoader,
            "Builds/Localization/PICO/PicoOfficialCameraRendering.apk",
            excludePluginRoot: k_MetaPackageRoot,
            sceneOverride: new[] { k_PicoOfficialCameraRenderingScene },
            buildOptions: BuildOptions.Development | BuildOptions.AllowDebugging);
    }

    [MenuItem("MRBase/Build/Marker Probe/Queue PICO Development")]
    public static void QueueMarkerProbePico()
    {
        QueueBuild(BuildMarkerProbePico, "PICO Marker Probe");
    }

    static Action s_QueuedBuild;
    static double s_QueuedBuildStartTime;

    static void QueueBuild(Action build, string label)
    {
        if (s_QueuedBuild != null || BuildPipeline.isBuildingPlayer)
            throw new BuildFailedException("[BuildScript] 已有构建正在排队或执行。");

        s_QueuedBuild = build;
        s_QueuedBuildStartTime = EditorApplication.timeSinceStartup + 1d;
        EditorApplication.update += StartQueuedBuild;
        Debug.Log($"[BuildScript] 已排队：{label}，将在菜单调用返回后启动。");
    }

    static void StartQueuedBuild()
    {
        if (EditorApplication.timeSinceStartup < s_QueuedBuildStartTime)
            return;

        EditorApplication.update -= StartQueuedBuild;
        var build = s_QueuedBuild;
        s_QueuedBuild = null;
        build?.Invoke();
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
        bool dryRun = false,
        string[] sceneOverride = null,
        BuildOptions buildOptions = BuildOptions.None,
        string applicationIdSuffix = null,
        bool installAfterBuild = false)
    {
        var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(profilePath);
        if (profile == null)
            throw new BuildFailedException($"[BuildScript] 找不到 Build Profile：{profilePath}");

        AssertNoVendorSpatializer();
        AssertProfileDeclaresDefine(profile, expectedDefine, profilePath);
        if (loaderTypeName == k_PicoLoader)
            EnsurePicoSettingsRegistered();

        var manager = AndroidManagerSettings();
        var restoreLoaders = SnapshotAndroidLoaders();
        var previousProfile = BuildProfile.GetActiveBuildProfile();
        List<string> restorePlugins = null;
        List<OpenXrVersionRestore> restoreOpenXrVersions = null;

        // 各测试包必须有各自的包名，否则装一个覆盖一个 —— 无法在设备上并存对照。
        // 与 loader/profile 同样的纪律：快照 + finally 还原，不在 ProjectSettings 上留 diff。
        //
        // 注意：finally 里的还原只改内存值，ProjectSettings.asset 要等 Unity 下次落盘才更新。
        // 所以构建刚结束时去 grep 那个文件，可能读到带后缀的旧值 —— 那是落盘滞后，不是没还原。
        // 以 PlayerSettings.GetApplicationIdentifier 的返回值为准。
        var androidTarget = NamedBuildTarget.Android;
        var previousApplicationId = PlayerSettings.GetApplicationIdentifier(androidTarget);

        try
        {
            if (!string.IsNullOrEmpty(applicationIdSuffix))
            {
                var testId = previousApplicationId + applicationIdSuffix;
                PlayerSettings.SetApplicationIdentifier(androidTarget, testId);
                Debug.Log($"[BuildScript] 本次包名：{testId}（构建后还原为 {previousApplicationId}）");
            }

            ApplyAndroidLoader(manager, loaderTypeName);
            restorePlugins = DisableAndroidPluginsUnder(excludePluginRoot);

            // 激活 Profile 会带入它的 scripting defines（MRBASE_QUEST / MRBASE_PICO），
            // 这是构建正确的前提，但也意味着 Editor 的 define 集合被改动 —— 必须在 finally 还原。
            BuildProfile.SetActiveBuildProfile(profile);

            if (loaderTypeName == k_OpenXRLoader)
                restoreOpenXrVersions = PatchStaleOpenXrFeatureApiVersions();

            var options = new BuildPlayerOptions
            {
                scenes = sceneOverride ?? profile.GetScenesForBuild()
                    .Where(scene => scene.enabled)
                    .Select(scene => scene.path)
                    .ToArray(),
                locationPathName = Path.GetFullPath(outputPath),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = buildOptions,
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

            // 装机放在 try 内、finally 之前：此时包名仍是本次构建实际写进 APK 的那个。
            // 刻意不用 BuildOptions.AutoRunPlayer —— 那条路会把「没插设备」算成构建失败，
            // 于是一次十几分钟的构建被一个 adb 问题判成白跑。这里安装失败只是警告，
            // APK 已经在磁盘上，可以手动装。
            if (installAfterBuild)
                InstallAndLaunch(outputPath, PlayerSettings.GetApplicationIdentifier(androidTarget));
        }
        finally
        {
            // 还原 loader、plugin 兼容性与激活的 Profile。分别避免：
            // 在 XRGeneralSettingsPerBuildTarget.asset 上留 git diff、
            // 让另一端的 native plugin 处于关闭状态、
            // 把 Editor 留在另一个平台的 define 集合下（会让另一端平台代码意外参与编译）。
            RestoreAndroidLoaders(manager, restoreLoaders);
            RestoreAndroidPlugins(restorePlugins);
            RestoreOpenXrFeatureApiVersions(restoreOpenXrVersions);
            if (previousProfile != profile)
                BuildProfile.SetActiveBuildProfile(previousProfile);
            if (!string.IsNullOrEmpty(applicationIdSuffix))
                PlayerSettings.SetApplicationIdentifier(androidTarget, previousApplicationId);
        }
    }

    // ---- 装机 ----

    /// <summary>adb 单条命令的上限。134MB 的 APK 走 USB 装机约 10~20 秒，留足余量。</summary>
    const int k_AdbTimeoutMs = 180_000;

    static void InstallAndLaunch(string apkPath, string packageName)
    {
        var adb = ResolveAdb();
        if (adb == null)
        {
            Debug.LogWarning("[BuildScript] 找不到 adb，跳过装机。APK 已生成，可手动安装。");
            return;
        }

        // -r 覆盖安装，-d 允许版本号回退（本地反复出包时 versionCode 常常不递增）。
        if (!RunAdb(adb, $"install -r -d \"{Path.GetFullPath(apkPath)}\"", out var installLog))
        {
            Debug.LogWarning($"[BuildScript] 安装失败（APK 已生成，可手动安装）：\n{installLog}");
            return;
        }

        Debug.Log($"[BuildScript] 已安装 {packageName}");

        // monkey 拉起 LAUNCHER 入口，不必知道 Activity 名。
        if (RunAdb(adb, $"shell monkey -p {packageName} -c android.intent.category.LAUNCHER 1", out var launchLog))
            Debug.Log($"[BuildScript] 已启动 {packageName}");
        else
            Debug.LogWarning($"[BuildScript] 启动失败（已安装，可在头显里手动打开）：\n{launchLog}");
    }

    /// <summary>
    /// 优先用用户在 External Tools 里指定的 SDK；没指定时回落到编辑器自带的 AndroidPlayer SDK。
    /// 后者在 macOS 的 Hub 安装里位于编辑器根目录（Unity.app 的**同级**），
    /// 在 Windows / Linux 上位于 Data 目录内 —— 两种布局都探一遍。
    /// </summary>
    static string ResolveAdb()
    {
        var exe = Application.platform == RuntimePlatform.WindowsEditor ? "adb.exe" : "adb";
        var contents = EditorApplication.applicationContentsPath;
        var editorRoot = Directory.GetParent(contents)?.Parent?.FullName;

        var roots = new[]
        {
            EditorPrefs.GetString("AndroidSdkRoot"),
            editorRoot == null ? null : Path.Combine(editorRoot, "PlaybackEngines", "AndroidPlayer", "SDK"),
            Path.Combine(contents, "PlaybackEngines", "AndroidPlayer", "SDK"),
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root))
                continue;

            var candidate = Path.Combine(root, "platform-tools", exe);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    static bool RunAdb(string adb, string arguments, out string log)
    {
        var info = new System.Diagnostics.ProcessStartInfo(adb, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(info);
        if (process == null)
        {
            log = "无法启动 adb 进程。";
            return false;
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(k_AdbTimeoutMs))
        {
            process.Kill();
            log = $"adb {arguments} 超时（>{k_AdbTimeoutMs / 1000}s）。";
            return false;
        }

        log = (stdout + stderr).Trim();

        // 退出码不够：部分 adb 版本装机失败仍返回 0，只在 stdout 里写 "Failure [...]"。
        return process.ExitCode == 0 && !log.Contains("Failure");
    }

    // ---- OpenXR package compatibility ----

    readonly struct OpenXrVersionRestore
    {
        public readonly UnityEngine.Object Feature;
        public readonly FieldInfo Field;
        public readonly string Value;

        public OpenXrVersionRestore(UnityEngine.Object feature, FieldInfo field, string value)
        {
            Feature = feature;
            Field = field;
            Value = value;
        }
    }

    /// <summary>
    /// Meta XR SDK 205 declares OpenXR 1.1.45 while the installed Unity OpenXR package is
    /// built against 1.1.53. The latter rejects an enabled feature that requests an older
    /// patch even though its own message says the request would be ignored. The source
    /// attribute is package-owned and gets copied back on every import, so changing the
    /// serialized asset is both ineffective and leaves misleading project state.
    ///
    /// Patch enabled Android feature instances in memory for the duration of the build.
    /// This requests the exact API version already used by the installed OpenXR package;
    /// the original values are restored in finally.
    /// </summary>
    static List<OpenXrVersionRestore> PatchStaleOpenXrFeatureApiVersions()
    {
        const string settingsPath = "Assets/XR/Settings/OpenXRPackageSettings.asset";
        const string requiredVersion = "1.1.53";
        var restore = new List<OpenXrVersionRestore>();

        foreach (var feature in AssetDatabase.LoadAllAssetsAtPath(settingsPath))
        {
            if (feature == null || !feature.name.EndsWith(" Android", StringComparison.Ordinal))
                continue;

            var serialized = new SerializedObject(feature);
            var enabled = serialized.FindProperty("m_enabled");
            if (enabled == null || !enabled.boolValue)
                continue;

            var field = FindInstanceField(feature.GetType(), "targetOpenXRApiVersion");
            if (field == null || field.FieldType != typeof(string))
                continue;

            var previous = field.GetValue(feature) as string;
            if (string.IsNullOrEmpty(previous) || previous == requiredVersion)
                continue;

            if (!Version.TryParse(previous, out var previousVersion) ||
                !Version.TryParse(requiredVersion, out var required) ||
                previousVersion.Major != required.Major ||
                previousVersion.Minor != required.Minor ||
                previousVersion.Build >= required.Build)
                continue;

            restore.Add(new OpenXrVersionRestore(feature, field, previous));
            field.SetValue(feature, requiredVersion);
            Debug.Log(
                $"[BuildScript] 构建期 OpenXR API 兼容：{feature.name} " +
                $"{previous} -> {requiredVersion}（仅内存，构建后还原）。");
        }

        return restore;
    }

    static FieldInfo FindInstanceField(Type type, string fieldName)
    {
        while (type != null)
        {
            var field = type.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
            type = type.BaseType;
        }

        return null;
    }

    static void RestoreOpenXrFeatureApiVersions(List<OpenXrVersionRestore> restore)
    {
        if (restore == null)
            return;

        foreach (var item in restore)
        {
            if (item.Feature != null)
                item.Field.SetValue(item.Feature, item.Value);
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

    /// <summary>
    /// 把 PXR_Settings 资产注册进 EditorBuildSettings 的 config object 表。
    ///
    /// 必要性：PICO 的 manifest 写入器 <c>PXR_Manifest</c> 里
    /// <c>PXR_XmlTools.GetSettings()</c> 就是读这张表（TryGetConfigObject("Unity.XR.PXR.Settings")）。
    /// 表里没有则返回 null，紧接着 PXR_BuildProcessor.cs:486 解引用它抛 NullReferenceException。
    /// Unity 吞掉该异常继续构建，于是 doc.Save() 从不执行 —— PICO 那 20 多项 meta-data
    /// （pvr.app.type / handtracking / com.picovr.permission.* …）一项都不进包，
    /// 而构建本身「成功」，只有真机上表现为不进 VR 模式。
    ///
    /// 这张表平时由 Project Settings &gt; XR Plug-in Management 的 PICO 页面在首次打开时填好。
    /// 本项目的 loader 是用 XRPackageMetadataStore 代码装的，绕过了那个界面，所以必须自己补。
    /// </summary>
    static void EnsurePicoSettingsRegistered()
    {
        if (EditorBuildSettings.TryGetConfigObject(k_PicoSettingsKey, out UnityEngine.Object registered) &&
            registered != null)
            return;

        var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(k_PicoSettingsPath);
        if (settings == null)
            throw new BuildFailedException(
                $"[BuildScript] 找不到 {k_PicoSettingsPath}。" +
                "请打开一次 Project Settings > XR Plug-in Management > PICO 让 SDK 生成它。");

        EditorBuildSettings.AddConfigObject(k_PicoSettingsKey, settings, true);
        Debug.Log($"[BuildScript] 已注册 {k_PicoSettingsKey} -> {k_PicoSettingsPath}");
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
