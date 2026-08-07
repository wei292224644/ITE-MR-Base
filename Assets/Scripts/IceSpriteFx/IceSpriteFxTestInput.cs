using MRBase.Transitions;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MRBase.IceSpriteFx
{
    /// <summary>
    /// 编辑器里反复触发特效用的键盘驱动。形状抄自 SacredRelicTrigger，
    /// 双分支是为了新旧 Input 后端都能跑。
    ///
    /// 1 = 出现   2 = 消失   3 = 传送到下一个锚点
    /// Q / W / E = 切传送风格（DissolveReform / TrailFlight / Afterimage）
    /// </summary>
    [RequireComponent(typeof(IceSpritePresence))]
    public sealed class IceSpriteFxTestInput : MonoBehaviour
    {
        [Tooltip("按 3 时在这些点之间轮着传送。至少放两个。")]
        [SerializeField] Transform[] anchors;

        IceSpritePresence _presence;
        int _nextAnchor;

        void Awake() => _presence = GetComponent<IceSpritePresence>();

        void NextTeleport()
        {
            if (anchors == null || anchors.Length == 0) return;

            _nextAnchor = (_nextAnchor + 1) % anchors.Length;
            Transform target = anchors[_nextAnchor];
            if (target != null) _presence.TeleportTo(target.position);
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame) _presence.Appear();
            if (keyboard.digit2Key.wasPressedThisFrame) _presence.Vanish();
            if (keyboard.digit3Key.wasPressedThisFrame) NextTeleport();

            if (keyboard.qKey.wasPressedThisFrame) _presence.style = IceSpriteTeleportStyle.DissolveReform;
            if (keyboard.wKey.wasPressedThisFrame) _presence.style = IceSpriteTeleportStyle.TrailFlight;
            if (keyboard.eKey.wasPressedThisFrame) _presence.style = IceSpriteTeleportStyle.Afterimage;
#else
            if (Input.GetKeyDown(KeyCode.Alpha1)) _presence.Appear();
            if (Input.GetKeyDown(KeyCode.Alpha2)) _presence.Vanish();
            if (Input.GetKeyDown(KeyCode.Alpha3)) NextTeleport();

            if (Input.GetKeyDown(KeyCode.Q)) _presence.style = IceSpriteTeleportStyle.DissolveReform;
            if (Input.GetKeyDown(KeyCode.W)) _presence.style = IceSpriteTeleportStyle.TrailFlight;
            if (Input.GetKeyDown(KeyCode.E)) _presence.style = IceSpriteTeleportStyle.Afterimage;
#endif
        }
    }
}
