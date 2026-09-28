using UnityEngine;

/// <summary>
/// 手柄模型的平台换装点，挂在 rig 的 Left/Right Controller（TrackedPoseDriver 那一层）上。
///
/// <see cref="PlatformRuntime.LoadControllerModel"/> 给出平台专属模型时，隐藏 rig 默认的 XRI 模型、
/// 在本节点下原点对齐挂上平台模型；给 null 时什么都不做。平台判断留在 PlatformRuntime，
/// 这里不写 <c>#if MRBASE_*</c>。
/// </summary>
public sealed class PlatformControllerModel : MonoBehaviour
{
    [SerializeField] private bool leftHand;
    [SerializeField] private GameObject defaultVisual;

    private void Awake()
    {
        GameObject model = PlatformRuntime.LoadControllerModel(leftHand);
        if (model == null)
        {
            return;
        }

        if (defaultVisual == null)
        {
            Debug.LogError($"[PlatformControllerModel] {name} 未指定默认模型，XRI 模型会与平台模型叠在一起。", this);
        }
        else
        {
            defaultVisual.SetActive(false);
        }

        Instantiate(model, transform, false);
    }
}
