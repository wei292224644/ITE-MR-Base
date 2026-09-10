using Newtonsoft.Json;
using NUnit.Framework;
using Uality.IteTour.Data;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// AprilTag ID 与 tourID 的绑定落在 <see cref="IteSpaceScene.Tour"/> 上（design D7）。
    ///
    /// 缺省必须是「不参与反查」而不是「反查到 0」——tag 0 是合法 ID（真机实测用的就是
    /// ID 0 与 250），若字段用 <c>int</c> 且缺省为 0，每个没写该字段的 tour 都会声称自己是 tag 0。
    /// </summary>
    public class SpaceSceneMarkerBindingTests
    {
        private const string SceneWithoutBinding = @"{
            ""id"": ""s1"",
            ""name"": ""thirdDemo"",
            ""tours"": [ { ""tourID"": ""wm0l5qcn_ibd"", ""displayType"": ""normal"" } ]
        }";

        private const string SceneWithBinding = @"{
            ""id"": ""s1"",
            ""name"": ""thirdDemo"",
            ""tours"": [ { ""tourID"": ""wm0l5qcn_ibd"", ""displayType"": ""normal"", ""aprilTagID"": 250 } ]
        }";

        private const string SceneBoundToTagZero = @"{
            ""id"": ""s1"",
            ""tours"": [ { ""tourID"": ""wm0l5qcn_ibd"", ""aprilTagID"": 0 } ]
        }";

        [Test]
        public void MissingBinding_LeavesTourUsableAndUnbound()
        {
            var scene = JsonConvert.DeserializeObject<IteSpaceScene>(SceneWithoutBinding);

            Assert.That(scene.tours.Length, Is.EqualTo(1));
            Assert.That(scene.tours[0].tourID, Is.EqualTo("wm0l5qcn_ibd"));
            Assert.That(scene.tours[0].isEnabled, Is.True, "缺绑定不影响装配");
            Assert.That(scene.tours[0].aprilTagID, Is.Null, "缺绑定必须是 null，不能落成 0");
        }

        [Test]
        public void PresentBinding_IsParsed()
        {
            var scene = JsonConvert.DeserializeObject<IteSpaceScene>(SceneWithBinding);

            Assert.That(scene.tours[0].aprilTagID, Is.EqualTo(250));
        }

        [Test]
        public void TagZero_IsDistinguishableFromMissing()
        {
            var bound = JsonConvert.DeserializeObject<IteSpaceScene>(SceneBoundToTagZero);
            var unbound = JsonConvert.DeserializeObject<IteSpaceScene>(SceneWithoutBinding);

            Assert.That(bound.tours[0].aprilTagID, Is.EqualTo(0));
            Assert.That(unbound.tours[0].aprilTagID, Is.Null);
        }
    }
}
