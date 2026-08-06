using System.Collections.Generic;
using UnityEngine;

public readonly struct MarkerProbeVisualAnchorState
{
    public MarkerProbeVisualAnchorState(Pose markerPose, string labelText, bool visible)
    {
        MarkerPose = markerPose;
        LabelText = labelText;
        Visible = visible;
    }

    public Pose MarkerPose { get; }
    public string LabelText { get; }
    public bool Visible { get; }
}

/// <summary>
/// Probe-only visualization boundary. This component never creates business anchors.
/// </summary>
[DisallowMultipleComponent]
public sealed class MarkerProbeVisualAnchorManager : MonoBehaviour
{
    private sealed class VisualAnchor
    {
        public GameObject Root;
        public Transform Cube;
        public TextMesh Label;
        public Material Material;
        public Pose MarkerPose;
    }

    [Header("Probe-only marker visualization")]
    [SerializeField] private float cubeSizeMeters = 0.08f;
    [SerializeField] private float labelNormalClearanceMeters = 0.04f;

    private readonly Dictionary<int, VisualAnchor> anchors = new Dictionary<int, VisualAnchor>();

    public int ActiveAnchorCount
    {
        get
        {
            int count = 0;
            foreach (VisualAnchor anchor in anchors.Values)
            {
                if (anchor.Root != null && anchor.Root.activeSelf)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public void ShowOrUpdate(
        int sourceInstanceId,
        string qrContent,
        string markerId,
        Pose markerPose,
        Rect? qrPlaneRect = null)
    {
        if (!anchors.TryGetValue(sourceInstanceId, out VisualAnchor anchor) || anchor.Root == null)
        {
            anchor = CreateAnchor(sourceInstanceId, markerId);
            anchors[sourceInstanceId] = anchor;
        }

        anchor.MarkerPose = markerPose;
        anchor.Root.transform.SetPositionAndRotation(markerPose.position, markerPose.rotation);
        ApplyPlaneLayout(anchor, qrPlaneRect);
        anchor.Root.SetActive(true);
        anchor.Label.text = FormatLabel(qrContent, markerId);
    }

    public bool TryGetAnchorState(int sourceInstanceId, out MarkerProbeVisualAnchorState state)
    {
        if (!anchors.TryGetValue(sourceInstanceId, out VisualAnchor anchor) || anchor.Root == null)
        {
            state = default;
            return false;
        }

        state = new MarkerProbeVisualAnchorState(
            anchor.MarkerPose,
            anchor.Label != null ? anchor.Label.text : string.Empty,
            anchor.Root.activeSelf);
        return true;
    }

    public void Hide(int sourceInstanceId)
    {
        if (anchors.TryGetValue(sourceInstanceId, out VisualAnchor anchor) && anchor.Root != null)
        {
            anchor.Root.SetActive(false);
        }
    }

    public void Clear()
    {
        foreach (VisualAnchor anchor in anchors.Values)
        {
            DestroyOwnedObject(anchor.Material);
            DestroyOwnedObject(anchor.Root);
        }

        anchors.Clear();
    }

    private void LateUpdate()
    {
        Camera viewer = Camera.main;
        if (viewer == null)
        {
            return;
        }

        foreach (VisualAnchor anchor in anchors.Values)
        {
            if (anchor.Root == null || !anchor.Root.activeSelf || anchor.Label == null)
            {
                continue;
            }

            Vector3 viewerToLabel = anchor.Label.transform.position - viewer.transform.position;
            if (viewerToLabel.sqrMagnitude > Mathf.Epsilon)
            {
                anchor.Label.transform.rotation = Quaternion.LookRotation(viewerToLabel, viewer.transform.up);
            }
        }
    }

    private VisualAnchor CreateAnchor(int sourceInstanceId, string markerId)
    {
        var root = new GameObject($"MarkerProbeVisual_{markerId ?? "unparsed"}_{sourceInstanceId}");
        root.transform.SetParent(transform, false);

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Tracked Marker Cube";
        cube.transform.SetParent(root.transform, false);
        cube.transform.localPosition = Vector3.zero;
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = Vector3.one * cubeSizeMeters;
        if (cube.TryGetComponent(out Collider collider))
        {
            DestroyOwnedObject(collider);
        }

        if (cube.TryGetComponent(out Renderer renderer))
        {
            Material source = renderer.sharedMaterial;
            Shader fallbackShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = source != null
                ? new Material(source)
                : new Material(fallbackShader);
            material.name = $"Marker Probe {markerId ?? "unparsed"} Material";
            material.color = ResolveColor(markerId);
            renderer.sharedMaterial = material;
        }

        var labelObject = new GameObject("QR and MarkerID Label");
        labelObject.transform.SetParent(root.transform, false);
        labelObject.transform.localPosition = Vector3.zero;
        labelObject.transform.localRotation = Quaternion.identity;
        var label = labelObject.AddComponent<TextMesh>();
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.fontSize = 64;
        label.characterSize = 0.005f;
        label.color = Color.white;

        return new VisualAnchor
        {
            Root = root,
            Cube = cube.transform,
            Label = label,
            Material = cube.TryGetComponent(out Renderer cubeRenderer)
                ? cubeRenderer.sharedMaterial
                : null,
            MarkerPose = Pose.identity
        };
    }

    private void ApplyPlaneLayout(VisualAnchor anchor, Rect? qrPlaneRect)
    {
        Vector2 planeCenter = qrPlaneRect?.center ?? Vector2.zero;
        float cubeHalfSize = anchor.Cube.localScale.z * 0.5f;
        anchor.Cube.localPosition = new Vector3(planeCenter.x, planeCenter.y, cubeHalfSize);
        anchor.Label.transform.localPosition = new Vector3(
            planeCenter.x,
            planeCenter.y,
            cubeSizeMeters + labelNormalClearanceMeters);
    }

    private static string FormatLabel(string qrContent, string markerId)
    {
        return $"QR: {qrContent ?? "<null>"}\nMarkerID: {markerId ?? "<unparsed>"}";
    }

    private static Color ResolveColor(string markerId)
    {
        return markerId == "250"
            ? new Color(0.1f, 0.65f, 1f, 1f)
            : new Color(0.15f, 1f, 0.35f, 1f);
    }

    private static void DestroyOwnedObject(Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void OnDestroy()
    {
        Clear();
    }
}
