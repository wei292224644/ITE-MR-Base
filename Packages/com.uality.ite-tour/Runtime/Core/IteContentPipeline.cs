using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Uality.IteTour.Config;
using Uality.IteTour.Data;
using Uality.IteTour.Internal;
using Uality.IteTour.Serialization;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 服务端版本查询接口的返回体。
    /// </summary>
    [Serializable]
    public class LatestVersionJsonResult
    {
        [Serializable]
        public class Data
        {
            public string tourId;
            public string version;
        }

        public Data data;
    }

    /// <summary>
    /// 内容管线的**数据半段**：拉取 → 版本校验 → 解压 → 反序列化。
    /// 产出 <see cref="IteSpaceScene"/> 与各 Tour 的 <see cref="IteTour"/>。
    ///
    /// 不负责实例化 GameObject——那半段依赖 <c>IteTourObject</c>，在阶段 5。
    /// 也不负责进度上报与事件广播，那是编排层（<c>IteRuntime</c>）的职责：
    /// 这里保持纯粹「输入 URL、输出数据」，才能离机测。
    /// </summary>
    public class IteContentPipeline
    {
        private readonly IteRuntimeConfig _config;
        private readonly Func<bool> _isNetworkAvailable;

        /// <summary>
        /// Tour 描述的反序列化设置。**三个转换器缺一不可**：少注册一个，
        /// 对应的资源/组件/动作就会退化成基类，内容静默消失而不报错。
        /// 公开出来是为了可测。
        /// </summary>
        public static JsonSerializerSettings CreateTourSerializerSettings() => new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new AssetConverter(),
                new ComponentConverter(),
                new ComponentActionConverter(),
            }
        };

        public IteContentPipeline(IteRuntimeConfig config, Func<bool> isNetworkAvailable = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            // 缺省按「有网」处理：离线是宿主的产品策略，不配就不启用
            _isNetworkAvailable = isNetworkAvailable ?? (() => true);
        }

        /// <summary>空间场景包在缓存目录下的文件夹名。</summary>
        public static string SpaceSceneFolder(string sceneName) => "IteSpaceScene_" + sceneName;

        /// <summary>
        /// 下载并解压空间场景包，解析出场景描述。
        /// 网络不可用时跳过下载，直接读本地缓存。
        /// </summary>
        /// <exception cref="Exception">场景描述读不出来，或其中不含任何 Tour。</exception>
        public async Task<IteSpaceScene> FetchSpaceSceneAsync(string sceneName)
        {
            // 注意：源实现此处没有版本校验，只要联网每次冷启动都重新下载解压
            // （Tour 包是有版本校验的，两者并不一致）。已记入 TODO，本次保持原样。
            if (_isNetworkAvailable())
            {
                await ZipContentDownloader.DownloadAndExtractAsync(
                    _config.BuildSpaceSceneUrl(sceneName),
                    SpaceSceneFolder(sceneName),
                    ZipTopLevel.Strip);
            }

            string relativePath = Path.Combine(SpaceSceneFolder(sceneName), sceneName + ".json");
            var scene = await ContentAssetLoader.LoadJsonAsync<IteSpaceScene>(relativePath);

            if (scene == null || scene.tours == null || scene.tours.Length == 0)
            {
                string fullPath = ContentAssetLoader.Resolve(relativePath);
                throw new Exception($"No tours found in IteSpaceScene: {sceneName} (path: {fullPath})");
            }

            return scene;
        }

        /// <summary>
        /// 按需更新单个 Tour 的内容包并反序列化其描述。
        /// 网络不可用时跳过版本查询与下载，直接读本地缓存。
        /// </summary>
        /// <exception cref="Exception">Tour 描述读不出来。</exception>
        // 注意：这里必须写 Data.IteTour 而不是 IteTour —— 在 Uality.IteTour.* 之下，
        // 标识符 IteTour 会先解析到命名空间 Uality.IteTour（CS0118）。
        public async Task<Data.IteTour> FetchTourAsync(string tourId)
        {
            if (_isNetworkAvailable())
            {
                await UpdateTourPackageIfStaleAsync(tourId);
            }

            var data = await ContentAssetLoader.LoadJsonAsync<Data.IteTour>(
                $"{tourId}/{tourId}.json",
                CreateTourSerializerSettings());

            if (data == null)
            {
                throw new Exception("Failed to load tour data for tour ID: " + tourId);
            }

            return data;
        }

        /// <summary>
        /// 加载空间场景的展示资源：场景 logo 与各 Tour 的预览图，就位后写回
        /// <paramref name="scene"/> 上的 Sprite 字段。
        ///
        /// 源实现是 fire-and-forget（不 await，与场景解析并行），所以这些图会在
        /// 场景描述已就绪之后才陆续出现。编排层决定要不要等、以及是否为此单独广播
        /// 一个事件（见任务 7.4 的待决项）。
        ///
        /// 缺图是正常情况，不抛异常，对应字段保持为 null。
        /// </summary>
        public async Task LoadSceneSpritesAsync(IteSpaceScene scene)
        {
            if (scene == null)
            {
                return;
            }

            // 注意：这里用的是 scene.id，而场景描述文件用的是 sceneName，两者未必相同。
            // 源实现即如此，保持原样。
            string folder = SpaceSceneFolder(scene.id);

            if (!string.IsNullOrEmpty(scene.logo))
            {
                var logoSprite = await ContentAssetLoader.LoadSpriteAsync(Path.Combine(folder, scene.logo));
                if (logoSprite != null)
                {
                    scene.SpriteLogo = logoSprite;
                }
            }

            if (scene.tours == null)
            {
                return;
            }

            foreach (var tour in scene.tours)
            {
                if (tour == null || string.IsNullOrEmpty(tour.tourID))
                {
                    continue;
                }

                var previewPath = Path.Combine(folder, "assets", tour.tourID + ".png");
                var preview = await ContentAssetLoader.LoadSpriteAsync(previewPath);
                if (preview != null)
                {
                    tour.SpritePreviewImage = preview;
                }
            }
        }

        /// <summary>
        /// 要不要重新下载 Tour 内容包。纯决策（design D25）。
        ///
        /// 服务端版本查不到时**退回本地缓存**：源实现在这里直接解引用查询结果，
        /// 一失败就 NRE 冒泡、整个场景加载失败——即便本地已有完整可用的缓存。
        /// </summary>
        public static bool ShouldDownloadTourPackage(string cachedVersion, string serverVersion)
        {
            if (string.IsNullOrEmpty(serverVersion))
            {
                return false;
            }

            return cachedVersion != serverVersion;
        }

        private async Task UpdateTourPackageIfStaleAsync(string tourId)
        {
            var latest = await ContentAssetLoader.FetchJsonAsync<LatestVersionJsonResult>(
                _config.BuildTourLatestVersionUrl(tourId));

            var serverVersion = latest?.data?.version;

            if (!ShouldDownloadTourPackage(TourVersionCache.Get(tourId), serverVersion))
            {
                return;
            }

            await ZipContentDownloader.DownloadAndExtractAsync(
                _config.BuildTourPackageUrl(tourId), relativeFolder: "", ZipTopLevel.Preserve);

            // 只在下载解压成功之后才写版本，保持源实现的顺序
            TourVersionCache.Set(tourId, serverVersion);
        }
    }
}
