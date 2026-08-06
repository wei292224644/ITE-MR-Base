using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Demo / 测试场景的入口：运行时把 MRCore 附加加载进来。
///
/// 为什么不让每个场景各带一套 XR 装配：XR Origin 应当只有一份。复制装配意味着
/// prefab 一改就要多处同步，而漏同步的表现是「某个 demo 在真机上的行为与核心
/// 场景不一致」—— 这类偏差戴上头显才会暴露，在编辑器里看不出来。
///
/// 挂在 demo 场景的一个根物体上即可，不需要任何其它接线。
/// </summary>
[DisallowMultipleComponent]
public class MRCoreLoader : MonoBehaviour
{
    [Tooltip("核心场景名。必须已加入 Build Settings，否则运行时按名加载会失败。")]
    [SerializeField] string coreSceneName = "MRCore";

    void Awake()
    {
        // 已加载则跳过：手动附加加载过、或本来就是从 MRCore 进来的场景组合。
        var scene = SceneManager.GetSceneByName(coreSceneName);
        if (scene.IsValid() && scene.isLoaded)
            return;

        // ponytail: 同步 LoadScene，MRCore 的 Awake 排在本帧末尾之后。因此 demo 内容
        // 不得在自己的 Awake/Start 里读 MRContext —— 目前没有任何 demo 脚本这么做
        // （MRContext 的调用方只有 MRBootstrap 和 PalmsTogetherGesture，两者都住在
        // MRCore 里）。哪天真需要在 Start 里拿到核心对象，改 LoadSceneAsync + 完成
        // 回调，别在这里加 sleep 或 yield 凑时序。
        SceneManager.LoadScene(coreSceneName, LoadSceneMode.Additive);
    }
}
