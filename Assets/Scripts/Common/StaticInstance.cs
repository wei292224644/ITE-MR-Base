using UnityEngine;

public class StaticInstance<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;

    public static T Instance => _instance;

    protected virtual void Awake()
    {
        // 重复实例必须自毁，不能让后来者顶掉前一个。
        //
        // 所有包（含探针包）都从 MRCore 启动，内容场景一律 Additive 进出，正常路径下不会
        // 有第二套装配。这里仍然守着，是因为漏判的代价不对称：无条件覆盖 _instance 的写法下，
        // 旧实例随后被销毁，_instance 指向一个 Unity 伪 null 对象 ——
        // `Instance != null` 为 false 而 `Instance` 又不是真 null，排查起来极其费解。
        if (_instance != null && !ReferenceEquals(_instance, this))
        {
            Debug.LogWarning($"[{typeof(T).Name}] 已存在实例，销毁重复的 '{name}'。" +
                             "检查是否有场景重复带入了核心装配。");
            Destroy(gameObject);
            return;
        }

        _instance = (T)(object)this;
        AfterAwake();
    }

    protected virtual void AfterAwake() { }

    /// <summary>
    /// EditMode tests may not invoke Awake; bind the singleton explicitly when needed.
    /// </summary>
    public static void BindInstanceForTesting(T instance) => _instance = instance;
}
