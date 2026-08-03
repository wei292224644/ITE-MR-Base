using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MRBase.SacredRelic
{
    /// <summary>
    /// Fires the awakening. On device this is meant to be called by an XR poke or grab
    /// interactable; the keyboard and mouse paths exist so the sequence can be reviewed
    /// in the editor without a headset.
    /// </summary>
    [RequireComponent(typeof(SacredRelicFracture))]
    public class SacredRelicTrigger : MonoBehaviour
    {
        [SerializeField] bool allowEditorInput = true;

        SacredRelicFracture _relic;

        void Awake() => _relic = GetComponent<SacredRelicFracture>();

        public void Awaken() => _relic.Trigger();

        public void Reseal() => _relic.ResetToSealed();

        void Update()
        {
            if (!allowEditorInput) return;

#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.spaceKey.wasPressedThisFrame) Awaken();
                if (keyboard.rKey.wasPressedThisFrame) Reseal();
            }
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) Awaken();
#else
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0)) Awaken();
            if (Input.GetKeyDown(KeyCode.R)) Reseal();
#endif
        }

        void OnTriggerEnter(Collider other) => Awaken();
    }
}
