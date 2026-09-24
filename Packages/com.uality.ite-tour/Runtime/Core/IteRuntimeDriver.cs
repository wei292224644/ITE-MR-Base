using System.Collections;
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

        private void LateUpdate() => Director?.EndOfFrame();

        /// <summary>
        /// 在 OnEnable 而不是 Start 里启动（ite-current-tour D14）：对象停用时 Unity 会停掉它上面的协程，
        /// 而 Start 只跑一次，再启用就回不来——LateUpdate 照常恢复，结算窗口却从此关不上，当前 Tour
        /// 再也不换，且不报错。编辑器非播放态不跑（OnEnable 不会被调用），EditMode 测试直接调 AfterPhysicsStep。
        /// </summary>
        private void OnEnable() => StartCoroutine(NotifyPhysicsSteps());

        /// <summary>
        /// 每个物理步之后通知一次，给锚定结算窗口用（ite-current-tour D9）。Unity 每个物理步的顺序是
        /// FixedUpdate → 物理模拟 → OnTrigger* → WaitForFixedUpdate，所以这里返回时，这一步的区域进出
        /// 已经全部到达。
        /// </summary>
        private IEnumerator NotifyPhysicsSteps()
        {
            var wait = new WaitForFixedUpdate();
            while (true)
            {
                yield return wait;
                Director?.AfterPhysicsStep();
            }
        }
    }
}
