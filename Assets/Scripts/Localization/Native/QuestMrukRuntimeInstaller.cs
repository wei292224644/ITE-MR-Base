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
    /// eyeFovPremultipliedAlphaMode 决定应用层 alpha 怎么和 passthrough 合成；Meta 默认 true，
    /// 官方 EnableUnpremultipliedAlpha Building Block 明确警告不要乱关。这里读一次真实值，
    /// 非 true 就恢复，并把前后值打进日志。
    ///
    /// 注意：它**不是**「手部变黑」的原因。那个猜测已被真机否定（2026-09-11：getter 走原生
    /// ovrp_GetEyeFovPremultipliedAlphaMode，读数恒为 true，且未创建 OVRManager 的场景里手同样
    /// 发黑）。真因是 XRI 手部材质缺 URP 的 _SURFACE_TYPE_TRANSPARENT 关键字、混合参数为不透明，
    /// 见提交「fix(hands): XRI 手部材质补齐 URP 透明表面关键字」。这里只保留为守护与诊断。
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
