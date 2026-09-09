using UnityEngine;

namespace Uality.IteTour.Internal
{
    /// <summary>
    /// 记录空间场景包已缓存内容的 HTTP <c>ETag</c>，避免每次冷启动重复下载。
    ///
    /// 与 <see cref="TourVersionCache"/> 平行而非合并（design D4）：两类包的键名空间
    /// 本就该分开，两个小类型比一个带 scope 参数的类型更直白。
    /// </summary>
    public static class SpacePackageEtagCache
    {
        public static string KeyFor(string sceneName) => $"ite.space.{sceneName}.etag";

        /// <summary>没有缓存记录时返回空串（PlayerPrefs 的缺省行为）。</summary>
        public static string Get(string sceneName) => PlayerPrefs.GetString(KeyFor(sceneName));

        /// <summary>只应在下载并解压成功之后调用（design D6）。</summary>
        public static void Set(string sceneName, string etag) => PlayerPrefs.SetString(KeyFor(sceneName), etag);

        public static void Clear(string sceneName) => PlayerPrefs.DeleteKey(KeyFor(sceneName));
    }
}
