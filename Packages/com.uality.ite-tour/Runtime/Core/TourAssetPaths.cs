namespace Uality.IteTour.Core
{
    /// <summary>
    /// Tour 资源在缓存目录下的相对路径。纯字符串拼接，无 IO。
    ///
    /// 一律用 <c>'/'</c> 而非 <c>Path.Combine</c>：源实现用后者，在 Windows 编辑器下
    /// 会拼出反斜杠，与 Android 真机不一致——正是「跨平台走出不同行为、且只在真机
    /// 复现」那类问题。Unity 的路径 API 全平台都接受 <c>'/'</c>。
    /// </summary>
    public static class TourAssetPaths
    {
        /// <summary>EMW 模型某个版本的资源目录。</summary>
        public static string EmwModelFolder(string tourId, string assetId, int version)
            => $"{tourId}/asset/{assetId}/{version}";

        /// <summary>EMW 模型的发布描述，其中含 glb 名与音频清单。</summary>
        public static string EmwModelPublishJson(string tourId, string assetId, int version)
            => EmwModelFolder(tourId, assetId, version) + "/publish.json";

        /// <summary>glb 文件名取自 publish.json 的 <c>id</c>，不是资源 Id。</summary>
        public static string EmwModelGlb(string tourId, string assetId, int version, string publishId)
            => $"{EmwModelFolder(tourId, assetId, version)}/{publishId}_sceneViewer.glb";

        /// <summary>
        /// EMW 模型的音频。**不在**版本目录下，而是资源目录下——与 glb / publish.json
        /// 不一致，源实现即如此，原样保留。
        /// </summary>
        public static string EmwModelAudio(string tourId, string assetId, string audioPath)
            => $"{tourId}/asset/{assetId}/{audioPath}";

        /// <summary>富文本的朗读音频。</summary>
        public static string RichTextAudio(string tourId, string assetId)
            => $"{tourId}/asset/{assetId}.mp3";

        /// <summary>富文本渲染好的位图。注意在 <c>raster</c> 子目录下，与音频形状不同。</summary>
        public static string RichTextImage(string tourId, string assetId)
            => $"{tourId}/asset/raster/{assetId}.png";

        /// <summary>视频文件。播放器要的是绝对路径，由 <c>ContentAssetLoader.Resolve</c> 转。</summary>
        public static string Video(string tourId, string assetId)
            => $"{tourId}/asset/{assetId}.mp4";
    }
}
