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
        /// 离线且无本地缓存时，要给出带场景名的可诊断错误，而不是静默返回 null
        /// 让后续在别处炸开。
        /// </summary>
        [Test]
        public void FetchSpaceSceneAsync_OfflineWithoutCache_ThrowsWithSceneName()
        {
            var pipeline = new IteContentPipeline(_config, isNetworkAvailable: () => false);

            var ex = Assert.ThrowsAsync<Exception>(async () =>
                await pipeline.FetchSpaceSceneAsync("ite-scene-that-was-never-cached"));

            Assert.That(ex.Message, Does.Contain("ite-scene-that-was-never-cached"));
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
    }
}
