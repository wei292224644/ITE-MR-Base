using System;
using Newtonsoft.Json;
using NUnit.Framework;
using Uality.IteTour.Components;
using Uality.IteTour.Data.Assets;
using Uality.IteTour.Serialization;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 三个转换器的多态分派表是内容管线的入口：分派错一个键，对应组件就静默消失。
    /// 期望值全部来自源工程的分派表，不是从迁移后的代码反推。
    /// </summary>
    public class ConverterTests
    {
        private static T Deserialize<T>(string json, JsonConverter converter)
            => JsonConvert.DeserializeObject<T>(json, converter);

        // ---- AssetConverter ----

        [TestCase("EMWModel", typeof(EMWModelAsset))]
        [TestCase("RichText", typeof(RichTextAsset))]
        [TestCase("Video", typeof(VideoAsset))]
        public void AssetConverter_DispatchesByTypeField(string type, Type expected)
        {
            var asset = Deserialize<Asset>($"{{\"Type\":\"{type}\",\"Id\":\"a1\"}}", new AssetConverter());

            Assert.That(asset, Is.TypeOf(expected));
            Assert.That(asset.Id, Is.EqualTo("a1"), "子类分派对了，但公共字段没被填充");
        }

        [Test]
        public void AssetConverter_UnknownTypeFallsBackToBaseAssetWithoutThrowing()
        {
            var asset = Deserialize<Asset>("{\"Type\":\"SomethingNew\",\"Id\":\"a1\"}", new AssetConverter());

            Assert.That(asset, Is.TypeOf<Asset>());
            Assert.That(asset.Id, Is.EqualTo("a1"));
        }

        // ---- ComponentConverter ----

        [TestCase("EMWModelRender", typeof(EMWModelRender))]
        [TestCase("RichText", typeof(RichText))]
        [TestCase("VideoPlane", typeof(VideoPlane))]
        [TestCase("PrimitiveModelRender", typeof(PrimitiveModelRender))]
        [TestCase("LoadTrigger", typeof(LoadTrigger))]
        [TestCase("TapTrigger", typeof(TapTrigger))]
        [TestCase("ApproximateTrigger", typeof(ApproximateTrigger))]
        [TestCase("PlayAnimationAction", typeof(PlayAnimationAction))]
        [TestCase("SpinAction", typeof(SpinAction))]
        [TestCase("ToggleVisibilityAction", typeof(ToggleVisibilityAction))]
        public void ComponentConverter_DispatchesByComponentTypeField(string type, Type expected)
        {
            var component = Deserialize<Component>($"{{\"ComponentType\":\"{type}\"}}", new ComponentConverter());

            Assert.That(component, Is.TypeOf(expected));
        }

        /// <summary>
        /// PlayAudioAction 的键值是 "PlayAudioActionComponent"，比类名多了后缀，
        /// 与其余十个都不同。这是服务端约定的原值，单独钉一条，防止有人"顺手统一"。
        /// </summary>
        [Test]
        public void ComponentConverter_PlayAudioActionUsesLegacyKeyWithComponentSuffix()
        {
            Assert.That(ComponentType.PlayAudioAction, Is.EqualTo("PlayAudioActionComponent"));

            var component = Deserialize<Component>(
                "{\"ComponentType\":\"PlayAudioActionComponent\"}", new ComponentConverter());

            Assert.That(component, Is.TypeOf<PlayAudioAction>());
        }

        /// <summary>
        /// 表征一颗**前向兼容地雷**：未知组件类型不是被跳过，而是直接抛异常，
        /// 于是整个 Tour 加载失败。服务端新增任何组件类型，已发布的客户端
        /// 就整体加载不出内容。
        ///
        /// 这是迁移前既有的行为，按"行为等价优先"原样保留。写成断言是为了
        /// 它可见 —— 将来决定加那句 null 判断时，这个测试会变红，
        /// 提醒改动者这是一次有意的行为变更。
        /// </summary>
        [Test]
        public void ComponentConverter_UnknownTypeThrows_KnownForwardCompatibilityLandmine()
        {
            Assert.Throws<ArgumentNullException>(() =>
                Deserialize<Component>("{\"ComponentType\":\"SomethingNew\"}", new ComponentConverter()));
        }

        // ---- ComponentActionConverter ----

        [TestCase("PlayAnimation", typeof(PlayAnimationActionParameters))]
        [TestCase("PlayAudio", typeof(PlayAudioActionParameters))]
        [TestCase("Spin", typeof(SpinActionParameters))]
        [TestCase("ToggleVisibility", typeof(ToggleVisibilityActionParameters))]
        public void ComponentActionConverter_DispatchesByActionField(string action, Type expected)
        {
            var componentAction = Deserialize<ComponentAction>(
                $"{{\"Action\":\"{action}\",\"EntityId\":\"e1\"}}", new ComponentActionConverter());

            Assert.That(componentAction, Is.TypeOf(expected));
            Assert.That(componentAction.EntityId, Is.EqualTo("e1"));
        }

        [Test]
        public void ComponentActionConverter_UnknownActionFallsBackToBaseWithoutThrowing()
        {
            var componentAction = Deserialize<ComponentAction>(
                "{\"Action\":\"SomethingNew\",\"EntityId\":\"e1\"}", new ComponentActionConverter());

            Assert.That(componentAction, Is.TypeOf<ComponentAction>());
            Assert.That(componentAction.EntityId, Is.EqualTo("e1"));
        }

        [Test]
        public void ComponentActionConverter_PopulatesNestedParameters()
        {
            var componentAction = Deserialize<ComponentAction>(
                "{\"Action\":\"Spin\",\"EntityId\":\"e1\",\"Parameters\":{\"Axis\":\"y\",\"Revolutions\":3}}",
                new ComponentActionConverter());

            var spin = (SpinActionParameters)componentAction;
            Assert.That(spin.Parameters, Is.Not.Null, "嵌套参数没被填充，动作会拿到一份空配置");
            Assert.That(spin.Parameters.Axis, Is.EqualTo("y"));
            Assert.That(spin.Parameters.Revolutions, Is.EqualTo(3));
        }
    }
}
