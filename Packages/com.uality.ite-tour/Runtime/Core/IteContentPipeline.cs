using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Uality.IteTour.Config;
using Uality.IteTour.Data;
using Uality.IteTour.Internal;
using Uality.IteTour.Serialization;
using UnityEngine;

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
            string relativePath = Path.Combine(SpaceSceneFolder(sceneName), sceneName + ".json");

            if (_isNetworkAvailable())
            {
                await UpdateSpacePackageIfStaleAsync(sceneName, relativePath);
            }

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

        /// <summary>
        /// tour 包的完整重下判定，与场景包同构（design D8）。tour 目录动辄几十上百 MB，
        /// 正是有人腾磁盘时会删的东西，只认版本记录会让它永久卡死。
        /// </summary>
        public static bool ShouldDownloadTourPackage(string cachedVersion, string serverVersion, bool contentPresent)
            => ShouldDownloadTourPackage(cachedVersion, serverVersion) || !contentPresent;

        /// <summary>
        /// 要不要重新下载空间场景包。纯决策（design D3），不碰文件系统——
        /// 「内容文件还在不在」由调用方另行判断（design D5）。
        ///
        /// 与 <see cref="ShouldDownloadTourPackage"/> 只差一处：服务端校验器查不到、
        /// 且**本地也没有记录**时仍然下载。tour 侧那种情况下不下载是对的（后续读取
        /// 抛异常是唯一且正确的结果）；场景包这里多下一次即可自愈，不该为了对称而
        /// 放弃自愈。
        /// </summary>
        public static bool ShouldDownloadSpacePackage(string cachedEtag, string serverEtag)
        {
            if (string.IsNullOrEmpty(serverEtag))
            {
                return string.IsNullOrEmpty(cachedEtag);
            }

            return cachedEtag != serverEtag;
        }

        /// <summary>
        /// 空间场景包的完整重下判定：校验器说要下，**或**本地内容文件已经不在。
        /// 后半句是 design D5 的收窄——只认记录的话，目录被手工删掉后每次冷启动
        /// 都会跳过下载再读取失败，永远不自愈。
        /// </summary>
        public static bool ShouldDownloadSpacePackage(string cachedEtag, string serverEtag, bool contentPresent)
            => ShouldDownloadSpacePackage(cachedEtag, serverEtag) || !contentPresent;

        /// <summary>
        /// 校验器值在日志里的呈现。空与"有值但不同"必须能一眼分开——本改动的失败
        /// 形态是静默退化成"每次都重下"，日志是唯一的信号（design D11）。
        /// </summary>
        private static string DescribeValidator(string value)
            => string.IsNullOrEmpty(value) ? "<无>" : value;

        /// <summary>
        /// 清空某个内容包的缓存目录。
        ///
        /// <paramref name="relativeFolder"/> 由调用方按包类型显式给出，**不是**解压的
        /// outputFolder（design D9）：tour 包解压到 persistentDataPath 根，拿 outputFolder
        /// 来删会清空整个缓存。
        ///
        /// 删除前过一遍 <see cref="ZipEntryPath.TryResolve"/>（design D10）。要防的不是
        /// 路径穿越（tourId 是后端生成的 shortid），而是**空值**：空串与根目录拼接的结果
        /// 就是根目录本身。同一条加载链里 <see cref="LoadSceneSpritesAsync"/> 已经在防
        /// tourID 为空，说明这个字段实践中确实可能为空。
        /// </summary>
        public static void ClearCachedPackageDirectory(string cacheRoot, string relativeFolder)
        {
            if (!ZipEntryPath.TryResolve(cacheRoot, relativeFolder, out string directory))
            {
                Debug.LogError($"[IteTour] 拒绝删除：目录名为空或逃出缓存根目录 — \"{relativeFolder}\"");
                return;
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        /// <summary>
        /// 要不要重下空间场景包，以及重下。跳过需要两个条件同时成立：校验器一致，
        /// 且内容文件确实还在磁盘上（design D5）——只认记录的话，目录被手工删掉后
        /// 每次冷启动都会跳过下载再读取失败，永远不自愈。
        /// </summary>
        private async Task UpdateSpacePackageIfStaleAsync(string sceneName, string relativePath)
        {
            string url = _config.BuildSpaceSceneUrl(sceneName);
            string serverEtag = await ContentAssetLoader.FetchEtagAsync(url);
            string cachedEtag = SpacePackageEtagCache.Get(sceneName);
            bool contentPresent = File.Exists(ContentAssetLoader.Resolve(relativePath));

            if (!ShouldDownloadSpacePackage(cachedEtag, serverEtag, contentPresent))
            {
                Debug.Log($"[IteTour] 场景包 {sceneName} 命中缓存，跳过下载 (ETag {DescribeValidator(cachedEtag)})");
                return;
            }

            string reason = contentPresent ? "" : "；本地内容文件不存在";
            Debug.Log($"[IteTour] 场景包 {sceneName} 需要更新：本地 {DescribeValidator(cachedEtag)} / " +
                      $"服务端 {DescribeValidator(serverEtag)}{reason}");

            string folder = SpaceSceneFolder(sceneName);
            bool downloaded = await ZipContentDownloader.DownloadAndExtractAsync(
                url, folder, ZipTopLevel.Strip,
                onDownloaded: () => ClearCachedPackageDirectory(Application.persistentDataPath, folder));

            // 解压抛错时异常向上冒泡，走不到这里——失败的一次下载不会被后续冷启动
            // 误判为已缓存（design D6）。校验器本身没查到时也不写，否则会把空值当成
            // 一个"版本"记下来。
            if (downloaded && !string.IsNullOrEmpty(serverEtag))
            {
                SpacePackageEtagCache.Set(sceneName, serverEtag);
            }
        }

        private async Task UpdateTourPackageIfStaleAsync(string tourId)
        {
            var latest = await ContentAssetLoader.FetchJsonAsync<LatestVersionJsonResult>(
                _config.BuildTourLatestVersionUrl(tourId));

            var serverVersion = latest?.data?.version;
            string cachedVersion = TourVersionCache.Get(tourId);

            // 与场景包同构（design D8）：只认版本记录的话，tour 目录被手工删掉后每次
            // 冷启动都会跳过下载再读取失败，永远不自愈。tour 目录动辄几十上百 MB，
            // 正是有人腾磁盘时会删的东西。
            bool contentPresent = File.Exists(ContentAssetLoader.Resolve($"{tourId}/{tourId}.json"));

            if (!ShouldDownloadTourPackage(cachedVersion, serverVersion, contentPresent))
            {
                Debug.Log($"[IteTour] tour {tourId} 命中缓存，跳过下载 (版本 {DescribeValidator(cachedVersion)})");
                return;
            }

            string reason = contentPresent ? "" : "；本地内容文件不存在";
            Debug.Log($"[IteTour] tour {tourId} 需要更新：本地 {DescribeValidator(cachedVersion)} / " +
                      $"服务端 {DescribeValidator(serverVersion)}{reason}");

            // 删的是 {tourId}/，**不是** relativeFolder —— 后者是空串，指 persistentDataPath
            // 根，删它会清空全部缓存（design D9）。
            bool downloaded = await ZipContentDownloader.DownloadAndExtractAsync(
                _config.BuildTourPackageUrl(tourId), relativeFolder: "", ZipTopLevel.Preserve,
                onDownloaded: () => ClearCachedPackageDirectory(Application.persistentDataPath, tourId));

            // 只在下载解压成功之后才写版本，保持源实现的顺序
            if (downloaded && !string.IsNullOrEmpty(serverVersion))
            {
                TourVersionCache.Set(tourId, serverVersion);
            }
        }
    }
}
