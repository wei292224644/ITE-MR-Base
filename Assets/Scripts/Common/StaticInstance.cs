using UnityEngine;

public class StaticInstance<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;

    public static T Instance => _instance;

    protected virtual void Awake()
    {
        // 重复实例必须自毁，不能让后来者顶掉前一个。
        //
        // MRCore 常驻不卸载，而各 demo 场景为了能在编辑器里单独按 Play 仍带着 MRCoreLoader。
        // 真机上从 MRCore 启动时装配已存在，loader 会跳过；
        // 但只要有一条路径漏判，第二套装配就会进来。无条件覆盖 _instance 的写法下，
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
