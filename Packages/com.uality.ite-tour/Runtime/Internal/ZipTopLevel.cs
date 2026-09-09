namespace Uality.IteTour.Internal
{
    /// <summary>
    /// zip 解压时如何处理条目的顶层目录（design D1）。
    ///
    /// 两类内容包的布局相反：空间场景包的条目顶层是 <c>{sceneName}/</c>，
    /// 但读取侧按 <c>{folder}/{sceneName}.json</c> 找文件，必须剥掉这一层；
    /// tour 包的条目顶层是 <c>{tourId}/</c>，落盘目标恰好是
    /// <c>persistentDataPath</c> 根，必须原样保留。
    ///
    /// 用枚举而不是 <c>bool</c>：调用点里 <c>true</c>/<c>false</c> 不携带任何
    /// 信息，而这两类包的差异恰恰是最容易记错的东西。
    /// </summary>
    public enum ZipTopLevel
    {
        /// <summary>条目路径原样落盘，不做任何改写。</summary>
        Preserve,

        /// <summary>
        /// 剥掉所有条目共有的唯一顶层目录名。条目并非全部位于同一个顶层
        /// 目录之下时，解压方必须报错并中止——MUST NOT 静默按原样落盘。
        /// </summary>
        Strip,
    }
}
