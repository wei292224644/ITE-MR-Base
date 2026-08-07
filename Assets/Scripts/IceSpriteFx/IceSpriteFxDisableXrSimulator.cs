using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace MRBase.IceSpriteFx
{
    /// <summary>
    /// IceSpriteFxTest 没有 XR Origin。项目开了自动实例化 XR Interaction Simulator，
    /// 它会把双手 aim 绑到无父节点的 Main Camera，然后在 ProcessPointAndClick 里
    /// <c>referenceTransform.parent.InverseTransformPoint</c> 每帧 NRE。
    /// 本组件在场景启动时拆掉这个模拟器；正式 XR 场景不受影响。
    /// </summary>
    public sealed class IceSpriteFxDisableXrSimulator : MonoBehaviour
    {
        void Awake() => TearDown();

        // 模拟器可能在场景 Awake 之后才 DontDestroyOnLoad 进来，再清一次。
        void Start() => TearDown();

        static void TearDown()
        {
            XRInteractionSimulator[] sims = FindObjectsByType<XRInteractionSimulator>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < sims.Length; i++)
            {
                if (sims[i] != null)
                    Destroy(sims[i].gameObject);
            }
        }
    }
}
