using TMPro;
using UnityEngine;

/// <summary>
/// 运行时可见的 RGB 坐标轴(X 红 / Y 绿 / Z 蓝),每根杆只朝正方向长,头显里能分清正负。
/// 默认按 ITE 内容的**编辑器坐标系**画:ITE 内容以右手系导出,进 Unity 时
/// <c>ConvertToLeftHanded</c> 沿 Z 翻了一次,所以把本物体的 Z 再翻回去显示,
/// 看到的就是内容作者眼中的 X=纸右、Y=垂直纸面、Z=纸下边。
/// </summary>
[AddComponentMenu("MR Base/Diagnostics/Axis Gizmo")]
public sealed class AxisGizmo : MonoBehaviour
{
    [SerializeField] private float length = 0.24f;
    [Tooltip("显示的坐标系相对本物体的旋转。")]
    [SerializeField] private Vector3 frameEuler;
    [Tooltip("勾选时 Z 轴反向显示(ITE 编辑器的右手系)。取消则显示 Unity 原始左手系。")]
    [SerializeField] private bool authoringHanded = true;

    private readonly Transform[] labels = new Transform[3];
    private Camera mainCamera;
    private bool built;

    public static AxisGizmo Create(Transform parent, float length, Quaternion frameRotation, bool authoringHanded)
    {
        var go = new GameObject("Axis Gizmo");
        go.transform.SetParent(parent, false);
        var gizmo = go.AddComponent<AxisGizmo>();
        gizmo.length = length;
        gizmo.frameEuler = frameRotation.eulerAngles;
        gizmo.authoringHanded = authoringHanded;
        gizmo.Build();
        return gizmo;
    }

    private void Start()
    {
        if (!built)
        {
            Build();
        }
    }

    private void Build()
    {
        built = true;
        float thickness = length / 12f;

        var origin = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        origin.name = "Origin";
        Destroy(origin.GetComponent<Collider>());
        origin.transform.SetParent(transform, false);
        origin.transform.localScale = Vector3.one * thickness * 2.4f;
        Paint(origin, Color.white);

        // 镜像只放在这个子节点上:负缩放会把挂在下面的东西左右翻,杆子对称无所谓,文字不行。
        var frame = new GameObject("Frame").transform;
        frame.SetParent(transform, false);
        frame.localRotation = Quaternion.Euler(frameEuler);
        frame.localScale = authoringHanded ? new Vector3(1f, 1f, -1f) : Vector3.one;

        labels[0] = CreateAxis(frame, Vector3.right, Color.red, "X", thickness);
        labels[1] = CreateAxis(frame, Vector3.up, Color.green, "Y", thickness);
        labels[2] = CreateAxis(frame, Vector3.forward, Color.blue, "Z", thickness);
    }

    private Transform CreateAxis(Transform frame, Vector3 axis, Color color, string axisName, float thickness)
    {
        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "Axis_" + axisName;
        Destroy(bar.GetComponent<Collider>());
        bar.transform.SetParent(frame, false);
        bar.transform.localPosition = axis * (length * 0.5f);
        bar.transform.localScale = Vector3.one * thickness + axis * (length - thickness);
        Paint(bar, color);

        // 文字挂在不镜像的本物体下,位置按 frame 的变换换算过去。
        var labelObject = new GameObject("AxisLabel_" + axisName);
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition =
            frame.localRotation * Vector3.Scale(frame.localScale, axis * (length + thickness * 3f));
        // TMP 的 fontSize 是世界单位,不缩小的话字有几米高。
        labelObject.transform.localScale = Vector3.one * 0.02f;
        var label = labelObject.AddComponent<TextMeshPro>();
        label.text = axisName;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 3f;
        label.rectTransform.sizeDelta = new Vector2(0.3f, 0.3f);
        return labelObject.transform;
    }

    private void LateUpdate()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return;
            }
        }

        foreach (Transform label in labels)
        {
            if (label == null)
            {
                continue;
            }

            Vector3 direction = label.position - mainCamera.transform.position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                label.rotation = Quaternion.LookRotation(direction);
            }
        }
    }

    private static void Paint(GameObject go, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit");
        var renderer = go.GetComponent<Renderer>();
        if (shader != null)
        {
            renderer.material = new Material(shader);
        }

        renderer.material.color = color;
    }
}
