using System;
using System.Collections.Generic;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 这次区域变化是人动了，还是锚定把体积挪了（ite-current-tour D9）。
    ///
    /// 帧末拿「基准 → 当前队列」判断人有没有离开当前 Tour；基准是上一帧末的队列。锚定会把所有体积挪到
    /// 真实位置，下一个物理步里到达的进出是锚定造成的——这段时间叫结算窗口：窗口里的变化不拿来判断，
    /// 关窗时直接并入基准。
    ///
    /// 窗口按物理步计数、不按时长：本工程物理在 FixedUpdate 里自动模拟，每一步的顺序是
    /// FixedUpdate → 模拟（transform 改动此时同步进物理）→ OnTrigger* → WaitForFixedUpdate，
    /// 所以锚定后第一次 WaitForFixedUpdate 返回时，锚定造成的进出已全部到达。不认识展示类型与当前 Tour。
    /// </summary>
    public sealed class RegionBaseline
    {
        private IReadOnlyList<string> _baseline = Array.Empty<string>();
        private int _stepsToSettle;

        /// <summary>上一帧末（或关窗时）的队列。</summary>
        public IReadOnlyList<string> Baseline => _baseline;

        public bool IsSettling => _stepsToSettle > 0;

        /// <summary>
        /// 锚定挪动了体积：接下来 <paramref name="physicsSteps"/> 个物理步里到达的进出都算锚定造成的。
        /// 窗口已开着时取较长的那个。
        /// </summary>
        public void BeginSettle(int physicsSteps)
        {
            if (physicsSteps < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(physicsSteps), physicsSteps, "至少等一个物理步");
            }

            _stepsToSettle = Math.Max(_stepsToSettle, physicsSteps);
        }

        /// <summary>一个物理步的触发回调全部到达之后调用。这一步关上窗口时返回 true，基准设为 <paramref name="current"/>。</summary>
        public bool AfterPhysicsStep(IReadOnlyList<string> current)
        {
            if (_stepsToSettle == 0)
            {
                return false;
            }

            _stepsToSettle--;
            if (_stepsToSettle > 0)
            {
                return false;
            }

            _baseline = current ?? Array.Empty<string>();
            return true;
        }

        /// <summary>帧末：基准前移到 <paramref name="current"/>。</summary>
        public void Advance(IReadOnlyList<string> current) => _baseline = current ?? Array.Empty<string>();
    }
}
