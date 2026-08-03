using TMPro;
using UnityEngine;

/// <summary>
/// Spawns one effect prefab at a time so a single effect can be judged on the headset
/// without the others competing for GPU time.
///
/// It instantiates and destroys rather than toggling <c>SetActive</c> on pre-placed copies,
/// because the INab dissolvers do not survive being switched off mid-run. Disabling a
/// GameObject kills its coroutines, so the loop stops wherever it was; on re-enable
/// <c>Dissolver.OnEnable</c> resets <c>currentState</c> to <c>initialState</c> while
/// <c>DissolverAutomaticTest.lastState</c> keeps the value the killed loop left behind. Its
/// loop only acts when those two agree, so it spins forever and the mesh sits frozen
/// half-dissolved. A fresh instance runs <c>Start()</c> again, which is what puts the pair
/// back in step. Those scripts live in Assembly-CSharp, which an asmdef cannot reference —
/// so the state could not be repaired from here even if it were the tidier fix.
/// </summary>
public class EffectShowcase : MonoBehaviour
{
    [Tooltip("每次只实例化其中一个。顺序即 UP/DOWN 的遍历顺序。")]
    [SerializeField] GameObject[] prefabs;

    [Tooltip("显示 “序号/总数 名称”。留空则只切换不显示。")]
    [SerializeField] TMP_Text label;

    [Tooltip("Morph 那批骨架挂在自己原点下方，整体抬高才能和其它示例站在同一高度。")]
    [SerializeField] float morphYOffset = 1.8f;

    GameObject _current;

    public int Index { get; private set; }
    public int Count => prefabs == null ? 0 : prefabs.Length;

    void Start() => Show(Index);

    public void Next() => Show(Index + 1);

    public void Previous() => Show(Index - 1);

    /// <summary>Wraps at both ends, so the two buttons alone reach every effect.</summary>
    public void Show(int index)
    {
        int count = Count;
        if (count == 0)
        {
            if (label != null) label.text = "no effects";
            return;
        }

        // C# % keeps the sign of the dividend, so Previous() at 0 would index -1.
        Index = ((index % count) + count) % count;

        if (_current != null) Destroy(_current);

        GameObject prefab = prefabs[Index];
        if (prefab == null)
        {
            if (label != null) label.text = (Index + 1) + "/" + count + "  (missing)";
            return;
        }

        _current = Instantiate(prefab, transform);
        _current.name = prefab.name;
        _current.transform.localPosition = prefab.name.StartsWith("Morph")
            ? new Vector3(0f, morphYOffset, 0f)
            : Vector3.zero;

        if (label != null)
            label.text = (Index + 1) + "/" + count + "  " + prefab.name;
    }
}
