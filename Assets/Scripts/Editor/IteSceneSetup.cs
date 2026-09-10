using System.IO;
using MRBase.Ite.Host;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Uality.IteTour.Config;

/// <summary>
/// ITE 装配层的生成器。
///
/// 为什么是脚本而不是手摆：装配层要在**两个场景**里保持一致（编辑器验收场景与设备场景），
/// 而锚定层级搭错不报任何错，只让内容出现在错误位置。手摆两遍迟早不一样；
/// 生成一次、两边共用同一个 prefab，`IteBootstrap.Validate()` 的两条前提只需保证一次。
///
/// 与 <c>BuildScript</c> 同一个理由：这个工程的失败模式是静默的，能自动化的装配就不该靠人记。
/// </summary>
public static class IteSceneSetup
{
    public const string RigPrefabPath = "Assets/Prefabs/ITE/IteTourRig.prefab";
    public const string DeviceScenePath = "Assets/Scenes/IteTour.unity";

    private const string ConfigPath = "Assets/Settings/ITE/IteRuntimeConfig.asset";
    private const string StabilizerProfilePath = "Assets/Settings/ITE/MarkerStabilizerProfile.asset";
    private const string PlatformOffsetPath = "Assets/Settings/ITE/PlatformOffsetConfig.asset";

    [MenuItem("MRBase/Setup/Create ITE Rig Prefab")]
    public static void CreateRigPrefab()
    {
        EnsureConfigAssets();
        EnsureFolder(Path.GetDirectoryName(RigPrefabPath));

        var root = new GameObject("ITE Tour Rig");
        try
        {
            // AnchorRoot 必须是根级（父级 identity），TourRoot 必须是它的**直接**子物体。
            // 两条都由 IteBootstrap.Validate() 校验，违反时只错位、不报错。
            var anchorRoot = new GameObject("AnchorRoot");
            anchorRoot.transform.SetParent(root.transform, false);

            var tourRoot = new GameObject("TourRoot");
            tourRoot.transform.SetParent(anchorRoot.transform, false);

            var hostObject = new GameObject("ITE Host");
            hostObject.transform.SetParent(root.transform, false);
            var host = hostObject.AddComponent<IteHostBootstrap>();

            var so = new SerializedObject(host);
            so.FindProperty("config").objectReferenceValue = LoadOrWarn<IteRuntimeConfig>(ConfigPath);
            so.FindProperty("anchorRoot").objectReferenceValue = anchorRoot.transform;
            so.FindProperty("tourRoot").objectReferenceValue = tourRoot.transform;
            so.FindProperty("stabilizerProfile").objectReferenceValue =
                LoadOrWarn<MarkerStabilizerProfile>(StabilizerProfilePath);
            so.FindProperty("platformOffsets").objectReferenceValue =
                LoadOrWarn<PlatformOffsetConfig>(PlatformOffsetPath);
            // 相机与启动时机是**两端的差异点**，留给各自的场景决定：
            // 编辑器场景指桌面相机并自启动；设备场景留空由 rig 解析、由 rig 触发。
            so.FindProperty("xrCamera").objectReferenceValue = null;
            so.FindProperty("startOnAwake").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, RigPrefabPath);
            Debug.Log("[IteSceneSetup] 已生成装配 prefab：" + RigPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
    }

    [MenuItem("MRBase/Setup/Create ITE Device Scene")]
    public static void CreateDeviceScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
        if (prefab == null)
        {
            CreateRigPrefab();
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 设备场景不带相机、不带光、不带 HUD：相机来自 MRCore 的 XR Origin，
        // 这个场景是**加性**加载在它之上的内容场景。
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        rig.name = "ITE Tour Rig";

        var host = rig.GetComponentInChildren<IteHostBootstrap>(true);

        var inputObject = new GameObject("ITE Device Marker Rig");
        var deviceRig = inputObject.AddComponent<IteDeviceMarkerRig>();
        var deviceSo = new SerializedObject(deviceRig);
        deviceSo.FindProperty("host").objectReferenceValue = host;
        deviceSo.ApplyModifiedPropertiesWithoutUndo();

        var panelObject = new GameObject("ITE HMD Panel");
        var panel = panelObject.AddComponent<IteHmdPanel>();
        var panelSo = new SerializedObject(panel);
        panelSo.FindProperty("host").objectReferenceValue = host;
        panelSo.ApplyModifiedPropertiesWithoutUndo();

        EnsureFolder(Path.GetDirectoryName(DeviceScenePath));
        EditorSceneManager.SaveScene(scene, DeviceScenePath);
        Debug.Log("[IteSceneSetup] 已生成设备内容场景：" + DeviceScenePath);
    }

    [MenuItem("MRBase/Setup/Create ITE Rig Prefab And Device Scene")]
    public static void CreateAll()
    {
        CreateRigPrefab();
        CreateDeviceScene();
    }

    /// <summary>
    /// 把编辑器验收场景里手摆的装配层换成 prefab 实例，两端从此共用同一份。
    ///
    /// 保留该场景自己的差异点：桌面相机与自启动。驱动层（假扫码、HUD）的引用重新指向
    /// 新的装配点——UnityEvent 式的引用丢了不报错，只是不工作，所以这里逐个重接而不是让人记。
    /// </summary>
    [MenuItem("MRBase/Setup/Migrate Editor Scene To Rig Prefab")]
    public static void MigrateEditorScene()
    {
        const string editorScenePath = "Assets/Scenes/IteTourSpace.unity";

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
        if (prefab == null)
        {
            CreateRigPrefab();
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
        }

        var scene = EditorSceneManager.OpenScene(editorScenePath, OpenSceneMode.Single);

        var oldHost = Object.FindAnyObjectByType<IteHostBootstrap>(FindObjectsInactive.Include);
        if (oldHost == null)
        {
            Debug.LogError("[IteSceneSetup] 场景里没有 IteHostBootstrap，无法迁移。");
            return;
        }

        if (PrefabUtility.IsPartOfPrefabInstance(oldHost))
        {
            Debug.Log("[IteSceneSetup] 该场景已经在用 prefab 实例，跳过。");
            return;
        }

        var oldSo = new SerializedObject(oldHost);
        var desktopCamera = oldSo.FindProperty("xrCamera").objectReferenceValue;
        var oldAnchorRoot = oldSo.FindProperty("anchorRoot").objectReferenceValue as Transform;

        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        rig.name = "ITE Tour Rig";
        var newHost = rig.GetComponentInChildren<IteHostBootstrap>(true);

        var newSo = new SerializedObject(newHost);
        newSo.FindProperty("xrCamera").objectReferenceValue = desktopCamera;
        newSo.FindProperty("startOnAwake").boolValue = true;
        newSo.ApplyModifiedPropertiesWithoutUndo();

        Repoint("host", Object.FindObjectsByType<IteEditorFakeScan>(FindObjectsInactive.Include), newHost);
        Repoint("host", Object.FindObjectsByType<IteEditorHud>(FindObjectsInactive.Include), newHost);

        // 旧的手摆装配层整块删掉，避免场景里同时存在两套 AnchorRoot。
        if (oldAnchorRoot != null)
        {
            Object.DestroyImmediate(oldAnchorRoot.gameObject);
        }

        Object.DestroyImmediate(oldHost.gameObject);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, editorScenePath);
        Debug.Log("[IteSceneSetup] 编辑器验收场景已改用装配 prefab。");
    }

    private static void Repoint<T>(string propertyName, T[] components, Object value) where T : Object
    {
        foreach (var component in components)
        {
            var so = new SerializedObject(component);
            var property = so.FindProperty(propertyName);
            if (property == null)
            {
                continue;
            }

            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// 两份配置资产没有就现建，用出厂缺省值。缺了它们扫码仍能工作（桥接会退回出厂参数），
    /// 但那样参数就没有可调的落点，真机调参无处回填。
    /// </summary>
    private static void EnsureConfigAssets()
    {
        EnsureFolder("Assets/Settings/ITE");
        EnsureAsset<MarkerStabilizerProfile>(StabilizerProfilePath);
        EnsureAsset<PlatformOffsetConfig>(PlatformOffsetPath);
    }

    private static void EnsureAsset<T>(string path) where T : ScriptableObject
    {
        if (AssetDatabase.LoadAssetAtPath<T>(path) != null)
        {
            return;
        }

        AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<T>(), path);
        Debug.Log("[IteSceneSetup] 已创建配置资产：" + path);
    }

    private static T LoadOrWarn<T>(string path) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
        {
            Debug.LogWarning($"[IteSceneSetup] 找不到 {path}，对应字段留空。");
        }

        return asset;
    }

    private static void EnsureFolder(string folder)
    {
        folder = folder.Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        var parts = folder.Split('/');
        var current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }
}
