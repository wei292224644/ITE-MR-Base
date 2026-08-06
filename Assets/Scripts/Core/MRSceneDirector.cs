using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// MRCore 作为初始化场景的入口：承载核心装配，并负责在各内容场景之间切换。
///
/// 常驻靠的是「初始化场景从不卸载」，不是 DontDestroyOnLoad。内容场景一律 Additive
/// 进出，MRCore 始终在，装配留在自己场景里即可。DDOL 在这个结构下是多余的第二套机制。
///
/// 为什么不做成 prefab：XR Origin 的层级极深（Camera Offset / 双手 / 双控制器 / 各自的
/// 交互器与可视化），做成嵌套 prefab 后 override 难以维护，而场景形态可以直接打开编辑。
/// </summary>
[DisallowMultipleComponent]
public class MRSceneDirector : StaticInstance<MRSceneDirector>
{
    [Tooltip("启动后自动加载的内容场景。留空则停在初始化场景，等菜单选择。")]
    [SerializeField] string firstScene = "";

    /// <summary>Build Settings 里除本场景外的全部场景名，顺序与构建索引一致。</summary>
    public IReadOnlyList<string> ContentScenes => m_ContentScenes;

    /// <summary>当前已加载的内容场景名；尚未加载任何内容场景时为空串。</summary>
    public string CurrentScene { get; private set; } = "";

    readonly List<string> m_ContentScenes = new();
    string m_BootSceneName;

    protected override void AfterAwake()
    {
        m_BootSceneName = gameObject.scene.name;

        // 这里不做 DontDestroyOnLoad：内容场景一律 Additive 进出，初始化场景从不卸载，
        // 核心装配留在自己场景里就已经是常驻的。DDOL 只会把对象搬进另一个场景、
        // 给这里留一个空壳，用两套机制做同一件事。
        CollectContentScenes();

        // 代价是「不许 Single」从优化建议变成了承重约束：没有 DDOL 兜底，一次 Single
        // 会把整套 XR 装配连根销毁。所以在这里守住，而不是只写在注释里。
        SceneManager.sceneLoaded += WarnOnSingleLoad;

        if (!string.IsNullOrEmpty(firstScene))
            Load(firstScene);
    }

    void OnDestroy() => SceneManager.sceneLoaded -= WarnOnSingleLoad;

    static void WarnOnSingleLoad(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single)
            return;

        Debug.LogError(
            $"[MRSceneDirector] 场景 '{scene.name}' 是用 LoadSceneMode.Single 加载的。" +
            "核心装配（XR Origin / EventSystem / MRContext）已被销毁，本次运行不会再恢复。" +
            "切场景请走 MRSceneDirector.Load()，它用 Additive 加载并卸掉上一个。");
    }

    /// <summary>
    /// 场景清单直接从 Build Settings 读，不另外维护一份 —— 两份清单迟早会漂移，
    /// 而漂移的表现是菜单上有个按钮点了没反应，得到真机上才发现。
    /// </summary>
    void CollectContentScenes()
    {
        m_ContentScenes.Clear();
        for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            var path = SceneUtility.GetScenePathByBuildIndex(i);
            var name = Path.GetFileNameWithoutExtension(path);
            if (name == m_BootSceneName)
                continue;
            m_ContentScenes.Add(name);
        }

        if (m_ContentScenes.Count == 0)
            Debug.LogWarning("[MRSceneDirector] Build Settings 里除初始化场景外没有其它场景，" +
                             "切换菜单会是空的。把 demo 场景加进 Build Settings。");
    }

    /// <summary>
    /// 切到指定内容场景：先 Additive 加载新的，再卸掉上一个。
    ///
    /// 不能用 LoadSceneMode.Single。Single 会把初始化场景一并卸掉，而这会在 Quest 上
    /// 打掉 passthrough —— 画面变成不透明黑，且所有可观测信号都还是绿的：
    /// ARSession 仍是 SessionTracking、相机子系统 running=1、相机 clearFlags=SolidColor
    /// 且背景 alpha=0、72fps、XR session FOCUSED、日志照常打「passthrough 已装配」。
    ///
    /// 真机实测的因果：启动后不切场景 → passthrough 正常；一次 Single 切换 → 立刻变黑；
    /// 改成 Additive → 恢复，反复切换也不退化。为什么卸掉初始化场景会打掉 passthrough，
    /// 没有查到确切机制，这里记的是观测事实，不是推断。
    /// </summary>
    public void Load(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || sceneName == CurrentScene)
            return;

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[MRSceneDirector] 场景 '{sceneName}' 不在构建里，未切换。" +
                           "把它加进 Build Settings。");
            return;
        }

        StartCoroutine(SwitchTo(sceneName));
    }

    IEnumerator SwitchTo(string sceneName)
    {
        var previous = CurrentScene;
        CurrentScene = sceneName;

        var load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        while (load != null && !load.isDone)
            yield return null;

        // 新场景设为激活场景：运行时 Instantiate 的对象、以及场景级的 RenderSettings
        // 都跟着它走，行为与 Single 时一致。
        var loaded = SceneManager.GetSceneByName(sceneName);
        if (loaded.IsValid() && loaded.isLoaded)
            SceneManager.SetActiveScene(loaded);

        // 后卸旧的：先卸会出现一帧没有任何内容场景，切换时闪一下。
        if (!string.IsNullOrEmpty(previous) && previous != sceneName)
        {
            var old = SceneManager.GetSceneByName(previous);
            if (old.IsValid() && old.isLoaded)
                yield return SceneManager.UnloadSceneAsync(old);
        }
    }

    /// <summary>卸掉当前内容场景，回到只剩核心装配的状态。</summary>
    public void UnloadCurrent()
    {
        if (string.IsNullOrEmpty(CurrentScene))
            return;

        var scene = SceneManager.GetSceneByName(CurrentScene);
        if (scene.IsValid() && scene.isLoaded)
            SceneManager.UnloadSceneAsync(scene);
        CurrentScene = "";
    }
}
