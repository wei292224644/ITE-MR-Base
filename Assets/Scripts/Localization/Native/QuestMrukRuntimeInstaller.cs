#if MRBASE_HAS_MRUK && MRBASE_QUEST
using System;
using Meta.XR.MRUtilityKit;
using UnityEngine;

/// <summary>
/// Installs the Meta runtime objects required by MRUK. Promoted out of the (since deleted)
/// Quest probe so the contract's Quest observation source does not need its own copy of this
/// bring-up logic — design D10 / D13.
///
/// Callers must invoke <see cref="EnsureInitialized"/> themselves from their own
/// <c>Start()</c>. There is deliberately no <c>[RuntimeInitializeOnLoadMethod]</c> trigger:
/// every consumer scene is loaded additively by <c>MRSceneDirector</c> on top of
/// <c>MRCore</c>, so an <c>AfterSceneLoad</c> hook fires while only <c>MRCore</c> exists and
/// can never see the component it is looking for. Such a hook was tried and removed (D10) —
/// it silently did nothing while looking like it handled bring-up.
///
/// The shared scene intentionally contains no vendor prefab, so PICO builds never create these objects.
/// </summary>
public static class QuestMrukRuntimeInstaller
{
    private const string RuntimeObjectName = "Quest Marker Probe Runtime";

    private static GameObject runtimeObject;
    private static bool permissionRequestAttempted;

    public static bool EnsureInitialized(out string detail)
    {
        try
        {
            OVRCameraRig cameraRig = UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>();
            OVRManager manager = UnityEngine.Object.FindFirstObjectByType<OVRManager>();
            MRUK mruk = MRUK.Instance;

            if (cameraRig == null && manager == null && mruk == null)
            {
                // Configure components while inactive so their Awake methods see the final settings.
                runtimeObject = new GameObject(RuntimeObjectName);
                runtimeObject.SetActive(false);
                cameraRig = runtimeObject.AddComponent<OVRCameraRig>();
                cameraRig.disableEyeAnchorCameras = true;
                manager = runtimeObject.AddComponent<OVRManager>();
                mruk = runtimeObject.AddComponent<MRUK>();
                mruk.SceneSettings = CreateSettings();
                mruk.EnableWorldLock = false;
                runtimeObject.SetActive(true);
            }
            else
            {
                GameObject host = cameraRig != null
                    ? cameraRig.gameObject
                    : manager != null
                        ? manager.gameObject
                        : mruk.gameObject;

                if (cameraRig == null)
                {
                    cameraRig = host.AddComponent<OVRCameraRig>();
                }

                cameraRig.disableEyeAnchorCameras = true;
                if (manager == null)
                {
                    manager = host.AddComponent<OVRManager>();
                }

                if (mruk == null)
                {
                    mruk = host.AddComponent<MRUK>();
                }

                mruk.SceneSettings ??= CreateSettings();
                mruk.EnableWorldLock = false;
            }

            DisableRigCameras(cameraRig);
            RestoreEyeFovPremultipliedAlpha();
            RequestScenePermissionOnce();

            bool ready = cameraRig != null && manager != null && mruk != null && mruk.SceneSettings != null;
            detail = ready
                ? $"OVRCameraRig={cameraRig.name}; OVRManager={manager.name}; MRUK={mruk.name}; " +
                  $"scenePermission={OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene)}; " +
                  $"worldLock={mruk.EnableWorldLock}"
                : "One or more Meta runtime components are still unavailable.";

            if (ready)
            {
                Debug.Log($"[QuestMrukRuntimeInstaller] Quest MRUK runtime ready: {detail}");
            }

            return ready;
        }
        catch (Exception exception)
        {
            detail = $"{exception.GetType().FullName}: {exception.Message}";
            Debug.LogError($"[QuestMrukRuntimeInstaller] Quest MRUK runtime bootstrap failed: {exception}");
            return false;
        }
    }

    private static MRUK.MRUKSettings CreateSettings()
    {
        var configuration = new OVRAnchor.TrackerConfiguration
        {
            QRCodeTrackingEnabled = true
        };
        return new MRUK.MRUKSettings
        {
            DataSource = MRUK.SceneDataSource.Device,
            LoadSceneOnStartup = false,
            RoomPrefabs = Array.Empty<GameObject>(),
            SceneJsons = Array.Empty<TextAsset>(),
            TrackerConfiguration = configuration
        };
    }

    /// <summary>
    /// 本工程的 Quest passthrough 走 OpenXR + AR Foundation（<c>PlatformRuntime</c> 装 ARCameraManager），
    /// 而这里为了 MRUK 又建了一个 Meta 自己的 OVRCameraRig/OVRManager —— 两套合成路径并存。
    /// OVRManager 起来后会接管 Meta 运行时的合成器配置，其中 eyeFovPremultipliedAlphaMode 决定
    /// 应用层的 alpha 怎么和 passthrough 合成。它一旦被置成 false（非预乘），半透明物体会被按
    /// 直通 alpha 再乘一次，暗色半透明材质就塌成纯黑 —— 手部用的 Unity_Hand_Dark 正是暗色
    /// Transparent 材质，症状就是"半透明手变全黑、腕部真实皮肤还在"。
    ///
    /// Meta 的默认值是 true，且官方 EnableUnpremultipliedAlpha Building Block 明确警告不要乱关。
    /// 这里读一次真实值：非 true 就恢复，并把前后值打进日志，好在真机上把因果钉死。
    /// </summary>
    private static void RestoreEyeFovPremultipliedAlpha()
    {
        try
        {
            bool before = OVRManager.eyeFovPremultipliedAlphaModeEnabled;
            if (!before)
            {
                OVRManager.eyeFovPremultipliedAlphaModeEnabled = true;
            }

            Debug.Log(
                $"[QuestMrukRuntimeInstaller] eyeFovPremultipliedAlphaMode before={before} " +
                $"after={OVRManager.eyeFovPremultipliedAlphaModeEnabled}");
        }
        catch (Exception exception)
        {
            // 读不到就只记一笔：这条是诊断用，不能反过来把 MRUK 装配搞崩。
            Debug.LogWarning($"[QuestMrukRuntimeInstaller] 读取 eyeFovPremultipliedAlphaMode 失败：{exception.Message}");
        }
    }

    private static void DisableRigCameras(OVRCameraRig cameraRig)
    {
        if (cameraRig == null)
        {
            return;
        }

        cameraRig.disableEyeAnchorCameras = true;
        foreach (Camera camera in cameraRig.GetComponentsInChildren<Camera>(true))
        {
            camera.enabled = false;
            if (camera.CompareTag("MainCamera"))
            {
                camera.tag = "Untagged";
            }
        }

        foreach (AudioListener listener in cameraRig.GetComponentsInChildren<AudioListener>(true))
        {
            listener.enabled = false;
        }
    }

    private static void RequestScenePermissionOnce()
    {
        if (permissionRequestAttempted ||
            OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene))
        {
            return;
        }

        permissionRequestAttempted = true;
        OVRPermissionsRequester.Request(new[] { OVRPermissionsRequester.Permission.Scene });
        Debug.Log("[QuestMrukRuntimeInstaller] Requested Meta Scene permission for MRUK QR tracking.");
    }
}
#endif
