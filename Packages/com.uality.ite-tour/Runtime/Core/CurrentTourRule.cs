using System.Collections.Generic;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 当前 Tour（C）换成谁（ite-current-tour D3、D4）。C 持有最高优先级：进入、离开别的区域都不影响它，
    /// 只有人离开 C 的体积（C 在基准里、不在当前队列里）才换成当前队列的队尾；C 为空时取队尾。
    ///
    /// 不认识展示类型与导览状态：队尾是 normal 也照取（它会挡住更早进入的 regionalTrigger），
    /// 选中之后播不播归 <see cref="TourAssembly.PlaysOnSelect"/>，什么时候判断归 <see cref="TourGuide"/>。
    /// 锚定后人本来就不在 C 的体积里时，C 不在基准里，于是保持到人走进去再走出来——不需要额外的标志。
    /// </summary>
    public static class CurrentTourRule
    {
        public static string Next(string current, IReadOnlyList<string> baseline, IReadOnlyList<string> queue)
        {
            bool left = TourIdLists.Contains(baseline, current) && !TourIdLists.Contains(queue, current);

            return string.IsNullOrEmpty(current) || left ? Tail(queue) : current;
        }

        private static string Tail(IReadOnlyList<string> queue)
            => queue == null || queue.Count == 0 ? null : queue[queue.Count - 1];
    }
}
