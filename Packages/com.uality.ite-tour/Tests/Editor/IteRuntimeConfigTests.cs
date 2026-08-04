using NUnit.Framework;
using UnityEngine;
using Uality.IteTour.Config;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// URL 拼装是「打错一个字就静默 404」的接缝，值得钉住。
    /// 期望值取自源工程 MainConstants 的原值与 IteSpaceManagerAssets 里那条硬编码地址。
    /// </summary>
    public class IteRuntimeConfigTests
    {
        private IteRuntimeConfig _config;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<IteRuntimeConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        [Test]
        public void BuildSpaceSceneUrl_AppendsSceneNameAndZipExtension()
        {
            Assert.That(_config.BuildSpaceSceneUrl("demo"),
                Is.EqualTo("https://ite-spatial-config.uality.cn/demo.zip"));
        }

        /// <summary>tourId 在模板里出现两次，一次作目录一次作文件名前缀。</summary>
        [Test]
        public void BuildTourPackageUrl_SubstitutesTourIdTwice()
        {
            Assert.That(_config.BuildTourPackageUrl("abc"),
                Is.EqualTo("https://ite-pkg.uality.cn/abc/abc_wx.zip"));
        }

        [Test]
        public void BuildTourLatestVersionUrl_SubstitutesTourIdIntoQuery()
        {
            Assert.That(_config.BuildTourLatestVersionUrl("abc"),
                Is.EqualTo("https://api.uality.cn/ITE/Tour/LatestVersion?tourId=abc"));
        }
    }
}
