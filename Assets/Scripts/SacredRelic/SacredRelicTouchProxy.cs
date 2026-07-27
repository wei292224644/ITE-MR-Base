using UnityEngine;

namespace MRBase.SacredRelic
{
    /// <summary>
    /// Forwards mouse / trigger / XR poke messages to SacredRelicAwaken.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SacredRelicTouchProxy : MonoBehaviour
    {
        [SerializeField] SacredRelicAwaken awaken;

        void Awake()
        {
            if (awaken == null)
                awaken = GetComponentInParent<SacredRelicAwaken>();
        }

        void OnMouseDown()
        {
            awaken?.OnRelicTouched();
        }

        void OnTriggerEnter(Collider other)
        {
            awaken?.OnRelicTouched();
        }

        /// <summary>Hook for XR Interaction events (Hover/Select/Poke).</summary>
        public void OnPoke()
        {
            awaken?.OnRelicTouched();
        }
    }
}
