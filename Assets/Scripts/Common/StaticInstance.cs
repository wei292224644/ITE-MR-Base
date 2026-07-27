using UnityEngine;

public class StaticInstance<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;

    public static T Instance => _instance;

    protected virtual void Awake()
    {
        _instance = (T)(object)this;
        AfterAwake();
    }

    protected virtual void AfterAwake() { }
}
