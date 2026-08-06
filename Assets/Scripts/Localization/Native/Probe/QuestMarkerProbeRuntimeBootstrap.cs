#if MRBASE_HAS_MRUK && MRBASE_QUEST
using System;
using Meta.XR.MRUtilityKit;
using UnityEngine;

/// <summary>
/// Installs the Meta runtime objects required by MRUK only for the isolated Quest probe scene.
/// The shared scene intentionally contains no vendor prefab, so PICO builds never create these objects.
/// </summary>
internal static class QuestMarkerProbeRuntimeBootstrap
{
    private const string RuntimeObjectName = "Quest Marker Probe Runtime";

    private static GameObject runtimeObject;
    private static bool permissionRequestAttempted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForProbeScene()
    {
        if (UnityEngine.Object.FindFirstObjectByType<MarkerProbeEntry>() != null)
        {
            EnsureInitialized(out _);
        }
    }

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
            RequestScenePermissionOnce();

            bool ready = cameraRig != null && manager != null && mruk != null && mruk.SceneSettings != null;
            detail = ready
                ? $"OVRCameraRig={cameraRig.name}; OVRManager={manager.name}; MRUK={mruk.name}; " +
                  $"scenePermission={OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene)}; " +
                  $"worldLock={mruk.EnableWorldLock}"
                : "One or more Meta runtime components are still unavailable.";

            if (ready)
            {
                Debug.Log($"[MarkerProbe] Quest MRUK runtime ready: {detail}");
            }

            return ready;
        }
        catch (Exception exception)
        {
            detail = $"{exception.GetType().FullName}: {exception.Message}";
            Debug.LogError($"[MarkerProbe] Quest MRUK runtime bootstrap failed: {exception}");
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
        Debug.Log("[MarkerProbe] Requested Meta Scene permission for MRUK QR tracking.");
    }
}
#endif
