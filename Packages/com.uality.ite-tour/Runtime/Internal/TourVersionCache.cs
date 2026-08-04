using UnityEngine;

namespace Uality.IteTour.Internal
{
    /// <summary>
    /// 记录每个 Tour 已缓存内容的版本号，避免每次冷启动重复下载。
    ///
    /// 源实现用裸 <c>tourId</c> 当 PlayerPrefs 键，直接占用宿主的全局键名空间。
    /// 包要与其它模块共存、要能移植进任意工程，就不能这么干（design D10）。
    ///
    /// 不主动调 <c>PlayerPrefs.Save()</c>，与源实现一致：Unity 退出时会自行落盘，
    /// 崩溃丢掉的只是"已缓存"标记，代价是多下一次，不会损坏内容。
    /// </summary>
    public static class TourVersionCache
    {
        public static string KeyFor(string tourId) => $"ite.tour.{tourId}.version";

        /// <summary>没有缓存记录时返回空串（PlayerPrefs 的缺省行为）。</summary>
        public static string Get(string tourId) => PlayerPrefs.GetString(KeyFor(tourId));

        /// <summary>只应在下载并解压成功之后调用。</summary>
        public static void Set(string tourId, string version) => PlayerPrefs.SetString(KeyFor(tourId), version);

        public static void Clear(string tourId) => PlayerPrefs.DeleteKey(KeyFor(tourId));
    }
}
