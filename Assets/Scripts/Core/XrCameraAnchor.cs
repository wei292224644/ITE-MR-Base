using UnityEngine;

/// <summary>
/// 把本物体挂到 XR 相机下，保持一个固定的头部相对位姿。
///
/// 存在的理由：拆分场景后，头显内的 HUD 与它要跟随的相机不在同一个场景里 ——
/// 相机随 MRCore 附加加载进来，HUD 留在各自的 demo 场景。跨场景的父子关系无法
/// 在编辑器里预先接线，只能运行时建立。
///
/// 为什么在 Update 里轮询而不是 Start 里取一次：MRCore 是附加加载的，其
/// <see cref="MRContext"/> 的 Awake 排在本场景 Start 之后；而且 XR 相机在 loader
/// 起来之前可能为 null。轮询到拿到为止是这里唯一不依赖加载时序的写法。
/// </summary>
[DisallowMultipleComponent]
public class XrCameraAnchor : MonoBehaviour
{
    [Tooltip("相对相机的位置，米。z 为正表示在眼前。")]
    [SerializeField] Vector3 localPosition = new Vector3(0f, 0f, 0.5f);

    [Tooltip("相对相机的朝向，欧拉角。")]
    [SerializeField] Vector3 localEulerAngles = Vector3.zero;

    [Tooltip("等不到相机时，多少秒后放弃并告警。0 表示一直等。")]
    [SerializeField] float timeoutSeconds = 10f;

    bool m_Attached;
    float m_Elapsed;

    void Update()
    {
        if (m_Attached)
            return;

        var context = MRContext.Instance;
        var cam = context == null ? null : context.Camera;
        if (cam == null)
        {
            if (timeoutSeconds <= 0f)
                return;

            m_Elapsed += Time.unscaledDeltaTime;
            if (m_Elapsed >= timeoutSeconds)
            {
                m_Attached = true; // 别每帧刷屏
                Debug.LogWarning($"[XrCameraAnchor] {timeoutSeconds} 秒内没拿到 XR 相机，" +
                                 $"'{name}' 保持在场景原位。检查 MRCore 是否被附加加载。");
            }
            return;
        }

        transform.SetParent(cam.transform, false);
        transform.localPosition = localPosition;
        transform.localEulerAngles = localEulerAngles;
        m_Attached = true;
    }
}
