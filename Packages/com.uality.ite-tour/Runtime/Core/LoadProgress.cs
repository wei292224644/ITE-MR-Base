namespace Uality.IteTour.Core
{
    /// <summary>
    /// 加载进度的取值曲线。纯函数。
    ///
    /// 源实现把 <c>0.2f + 0.8f * (count / total)</c> 直接写在循环体里，两个魔数
    /// 没有名字，且 <c>total</c> 为 0 时是除零。
    /// </summary>
    public static class LoadProgress
    {
        /// <summary>场景描述解析完成。前 20% 留给它，剩下 80% 归 Tour。</summary>
        public const float SceneParsed = 0.2f;

        public static float ForTours(int completed, int total)
        {
            if (total <= 0)
            {
                return 1f;
            }

            return SceneParsed + (1f - SceneParsed) * ((float)completed / total);
        }
    }
}
