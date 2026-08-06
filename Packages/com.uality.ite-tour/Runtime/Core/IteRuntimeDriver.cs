using UnityEngine;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 包自带的帧驱动。<see cref="IteRuntime"/> 在装配时创建一个，销毁时带走。
    ///
    /// 不做成「宿主每帧调 <c>ite.Tick()</c>」：忘了调不会报错，只是区域触发从此不再结算——
    /// 又一个只在真机上现形的静默失效（同 design D26 的 tag 问题）。包自己持有驱动，
    /// 宿主就没有忘的机会。
    /// </summary>
    internal class IteRuntimeDriver : MonoBehaviour
    {
        internal TourDirector Director;

        private void LateUpdate() => Director?.FlushRegionTransitions();
    }
}
