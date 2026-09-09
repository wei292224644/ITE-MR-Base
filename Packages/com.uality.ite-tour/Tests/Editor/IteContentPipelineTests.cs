using System;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using Uality.IteTour.Components;
using Uality.IteTour.Config;
using Uality.IteTour.Core;
using Uality.IteTour.Data;
using Uality.IteTour.Data.Assets;

namespace Uality.IteTour.Tests
{
    public class IteContentPipelineTests
    {
        private IteRuntimeConfig _config;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<IteRuntimeConfig>();

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_config);

        /// <summary>
        /// 三个转换器缺一不可：少注册一个，对应的资源/组件/动作就退化成基类，
        /// 内容静默消失而不报错。这条一次覆盖三者的接线。
        /// </summary>
        [Test]
        public void TourSerializerSettings_WiresUpAllThreeConverters()
        {
            const string json = @"{
                ""Id"": ""t1"",
                ""Assets"": { ""a1"": { ""Type"": ""RichText"", ""Id"": ""a1"" } },
                ""Scenes"": { ""s1"": { ""Entities"": { ""e1"": {
                    ""Id"": ""e1"",
                    ""Components"": [
                        { ""ComponentType"": ""TapTrigger"",
                          ""Actions"": [ { ""Action"": ""Spin"", ""EntityId"": ""e1"" } ] }
                    ] } } } },
                ""ScenesOrder"": [ ""s1"" ]
            }";

            // 必须写 Data.IteTour：在 Uality.IteTour.* 之下，标识符 IteTour
            // 会先解析到命名空间 Uality.IteTour（CS0118）
            var tour = JsonConvert.DeserializeObject<Data.IteTour>(
                json, IteContentPipeline.CreateTourSerializerSettings());

            Assert.That(tour.Assets["a1"], Is.TypeOf<RichTextAsset>(), "AssetConverter 未接上");

            var component = tour.Scenes["s1"].Entities["e1"].Components[0];
            Assert.That(component, Is.TypeOf<TapTrigger>(), "ComponentConverter 未接上");

            Assert.That(((TapTrigger)component).Actions[0], Is.TypeOf<SpinActionParameters>(),
                "ComponentActionConverter 未接上");
        }

        [Test]
        public void SpaceSceneFolder_PrefixesSceneName()
        {
            Assert.That(IteContentPipeline.SpaceSceneFolder("demo"), Is.EqualTo("IteSpaceScene_demo"));
        }

        /// <summary>
        /// 离线且无本地缓存时，要给出带场景名与完整查找路径的可诊断错误，
        /// 而不是静默返回 null 让后续在别处炸开。
        /// </summary>
        [Test]
        public void FetchSpaceSceneAsync_OfflineWithoutCache_ThrowsWithSceneNameAndPath()
        {
            var pipeline = new IteContentPipeline(_config, isNetworkAvailable: () => false);

            var ex = Assert.ThrowsAsync<Exception>(async () =>
                await pipeline.FetchSpaceSceneAsync("ite-scene-that-was-never-cached"));

            Assert.That(ex.Message, Does.Contain("ite-scene-that-was-never-cached"));
            Assert.That(ex.Message, Does.Contain("IteSpaceScene_ite-scene-that-was-never-cached"),
                "错误信息必须带上被查找的完整路径，否则联网首跑失败时无从排查");
        }

        [Test]
        public void FetchTourAsync_OfflineWithoutCache_ThrowsWithTourId()
        {
            var pipeline = new IteContentPipeline(_config, isNetworkAvailable: () => false);

            var ex = Assert.ThrowsAsync<Exception>(async () =>
                await pipeline.FetchTourAsync("ite-tour-that-was-never-cached"));

            Assert.That(ex.Message, Does.Contain("ite-tour-that-was-never-cached"));
        }

        [Test]
        public void Constructor_RejectsNullConfig()
        {
            Assert.Throws<ArgumentNullException>(() => new IteContentPipeline(null));
        }

        /// <summary>
        /// 缺图是正常情况（不是每个空间都配了 logo 和预览图），
        /// 加载失败不能把整个场景拖垮，字段保持 null 即可。
        /// </summary>
        [Test]
        public void LoadSceneSpritesAsync_MissingFilesLeaveSpritesNullWithoutThrowing()
        {
            var pipeline = new IteContentPipeline(_config);
            var scene = new IteSpaceScene
            {
                id = "ite-scene-never-cached",
                logo = "logo.png",
                tours = new[]
                {
                    new IteSpaceScene.Tour { tourID = "t1" },
                    new IteSpaceScene.Tour { tourID = "t2" },
                }
            };

            Assert.DoesNotThrowAsync(async () => await pipeline.LoadSceneSpritesAsync(scene));

            Assert.That(scene.SpriteLogo, Is.Null);
            Assert.That(scene.tours[0].SpritePreviewImage, Is.Null);
            Assert.That(scene.tours[1].SpritePreviewImage, Is.Null);
        }

        [Test]
        public void LoadSceneSpritesAsync_ToleratesNullSceneAndNullTours()
        {
            var pipeline = new IteContentPipeline(_config);

            Assert.DoesNotThrowAsync(async () => await pipeline.LoadSceneSpritesAsync(null));
            Assert.DoesNotThrowAsync(async () =>
                await pipeline.LoadSceneSpritesAsync(new IteSpaceScene { id = "x", tours = null }));
        }

        // ---- 版本比对（design D25）----

        /// <summary>
        /// 版本查询失败时**退回本地缓存**：不下载，也不抛异常。
        ///
        /// 源实现在这里直接读 <c>serverVersion.data.version</c>，查询一失败就 NRE 冒泡，
        /// 整个场景加载失败——**即便本地已有完整可用的缓存**。网络抖动不该让导览打不开。
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        public void ShouldDownloadTourPackage_FallsBackToCacheWhenServerVersionIsUnknown(string serverVersion)
        {
            Assert.That(IteContentPipeline.ShouldDownloadTourPackage("v3", serverVersion), Is.False);
        }

        [Test]
        public void ShouldDownloadTourPackage_SkipsWhenCachedVersionMatches()
        {
            Assert.That(IteContentPipeline.ShouldDownloadTourPackage("v3", "v3"), Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("v2")]
        public void ShouldDownloadTourPackage_DownloadsWhenCacheIsMissingOrStale(string cachedVersion)
        {
            Assert.That(IteContentPipeline.ShouldDownloadTourPackage(cachedVersion, "v3"), Is.True);
        }

        /// <summary>
        /// 与 tour 侧同样的「查不到就退回缓存」：网络抖动不该让一份完好的缓存失效。
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        public void ShouldDownloadSpacePackage_FallsBackToCacheWhenServerEtagIsUnknown(string serverEtag)
        {
            Assert.That(IteContentPipeline.ShouldDownloadSpacePackage("\"etag-1\"", serverEtag), Is.False);
        }

        /// <summary>
        /// **本次唯一与 tour 侧语义不同的一条**（design D5）。
        ///
        /// tour 在版本查不到时一律不下载；场景包在**本地也没有记录**时仍须下载——
        /// 否则后续 <c>LoadJsonAsync</c> 必然读不到文件而抛异常，而多下一次即可自愈。
        /// tour 侧不存在同等风险：那种情况下抛异常本就是唯一且正确的结果。
        /// </summary>
        [TestCase(null, null)]
        [TestCase("", "")]
        [TestCase(null, "")]
        [TestCase("", null)]
        public void ShouldDownloadSpacePackage_DownloadsWhenNeitherSideHasAValue(
            string cachedEtag, string serverEtag)
        {
            Assert.That(IteContentPipeline.ShouldDownloadSpacePackage(cachedEtag, serverEtag), Is.True);
        }

        [Test]
        public void ShouldDownloadSpacePackage_SkipsWhenEtagMatches()
        {
            Assert.That(IteContentPipeline.ShouldDownloadSpacePackage("\"etag-1\"", "\"etag-1\""), Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("\"etag-0\"")]
        public void ShouldDownloadSpacePackage_DownloadsWhenCacheIsMissingOrStale(string cachedEtag)
        {
            Assert.That(IteContentPipeline.ShouldDownloadSpacePackage(cachedEtag, "\"etag-1\""), Is.True);
        }

        /// <summary>
        /// design D5 / D8 的收窄：校验器一致但内容文件已经不在时仍须下载。
        /// 不加这一条的话，目录被手工删掉、记录还在，就会每次冷启动跳过下载、
        /// 随后读取失败，永远不自愈——而清缓存的常规做法正是删目录。
        /// </summary>
        [Test]
        public void ShouldDownloadSpacePackage_DownloadsWhenValidatorMatchesButContentIsGone()
        {
            Assert.That(
                IteContentPipeline.ShouldDownloadSpacePackage("\"etag-1\"", "\"etag-1\"", contentPresent: false),
                Is.True);
        }

        [Test]
        public void ShouldDownloadTourPackage_DownloadsWhenVersionMatchesButContentIsGone()
        {
            Assert.That(
                IteContentPipeline.ShouldDownloadTourPackage("v3", "v3", contentPresent: false),
                Is.True);
        }

        [Test]
        public void ShouldDownloadSpacePackage_SkipsOnlyWhenValidatorMatchesAndContentIsPresent()
        {
            Assert.That(
                IteContentPipeline.ShouldDownloadSpacePackage("\"etag-1\"", "\"etag-1\"", contentPresent: true),
                Is.False);
        }

        [Test]
        public void ShouldDownloadTourPackage_SkipsOnlyWhenVersionMatchesAndContentIsPresent()
        {
            Assert.That(
                IteContentPipeline.ShouldDownloadTourPackage("v3", "v3", contentPresent: true),
                Is.False);
        }

        /// <summary>
        /// 内容文件在、但校验器说要更新时，仍然要下——"文件还在"只是自愈条件，
        /// 不能反过来抑制正常的版本更新。
        /// </summary>
        [Test]
        public void ShouldDownloadSpacePackage_ContentPresentDoesNotSuppressAStaleValidator()
        {
            Assert.That(
                IteContentPipeline.ShouldDownloadSpacePackage("\"etag-0\"", "\"etag-1\"", contentPresent: true),
                Is.True);
        }
    }
}
