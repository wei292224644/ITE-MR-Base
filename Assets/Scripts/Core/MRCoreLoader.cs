using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 编辑器便利件：在 demo 场景里直接按 Play 时，把缺失的核心装配补进来。
///
/// 真机上的正常路径是从 MRCore 启动 —— 它始终保持加载，<see cref="MRSceneDirector"/>
/// 只用 Additive 进出内容场景，装配一直在。这种情况下本组件什么都不做。
///
/// 存在的理由只有一个：不必为了看一个 demo 而每次都从 MRCore 走一遍。
/// </summary>
[DisallowMultipleComponent]
public class MRCoreLoader : MonoBehaviour
{
    [Tooltip("核心场景名。必须已加入 Build Settings，否则运行时按名加载会失败。")]
    [SerializeField] string coreSceneName = "MRCore";

    void Awake()
    {
        // 判据是「核心装配在不在」，不是「MRCore 场景加载没加载」。按场景名判会漏：
        // 场景可能改名、可能被别的路径以别的方式带入，而漏判的后果是第二套 XR Origin
        // 被拉进来，两套装配互相打架 —— 表现为交互器重复触发、相机取错，很难往回追。
        if (MRContext.Instance != null)
            return;

        // 缺场景是构建期漏配，不是运行期异常：直接 LoadScene 只会留下一行 Unity 内部
        // 报错，然后应用继续跑在一个没有相机、没有手部追踪的空场景里 —— 又一个静默失败。
        if (!Application.CanStreamedLevelBeLoaded(coreSceneName))
        {
            Debug.LogError($"[MRCoreLoader] 场景 '{coreSceneName}' 不在构建里，未加载。" +
                           "本场景将没有 XR Origin、相机与手部追踪。" +
                           "把它加进 Build Settings，或用 MRBase/Build/* 出包。");
            return;
        }

        // ponytail: 同步 LoadScene，MRCore 的 Awake 排在本帧末尾之后。因此 demo 内容
        // 不得在自己的 Awake/Start 里读 MRContext。哪天真需要，改 LoadSceneAsync + 完成
        // 回调，别在这里加 sleep 或 yield 凑时序。
        SceneManager.LoadScene(coreSceneName, LoadSceneMode.Additive);
    }
}
