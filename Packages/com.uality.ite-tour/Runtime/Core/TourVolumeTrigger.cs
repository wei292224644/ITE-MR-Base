using UnityEngine;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 挂在 Tour 的触发体积上，把物理回调转给所属的 <see cref="IteTourObject"/>。
    ///
    /// 源实现用宿主的 `TriggerEvents` + prefab 里连的 `UnityEvent` 做同一件事。
    /// 改成自己找父节点，是为了让预制体少一处能连断的引用——拷到别的工程时
    /// UnityEvent 的目标很容易丢，而丢了不报错，只是区域触发不再工作。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class TourVolumeTrigger : MonoBehaviour
    {
        private IteTourObject _tourObject;

        private void Awake() => _tourObject = GetComponentInParent<IteTourObject>();

        private void OnTriggerEnter(Collider other)
            => _tourObject?.NotifyVolumeTransition(other, VolumeTransition.Enter);

        private void OnTriggerExit(Collider other)
            => _tourObject?.NotifyVolumeTransition(other, VolumeTransition.Exit);
    }
}
