using System.IO;
using NUnit.Framework;
using Uality.IteTour.Core;
using UnityEngine.TestTools;
using UnityEngine;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 重下前清空该包的目录（design D9），以及删除前的目录名校验（design D10）。
    ///
    /// 全程在临时目录里跑，绝不碰真实的 <c>Application.persistentDataPath</c>——
    /// 这是本次改动里唯一的破坏性操作，测试自己更不该有误删的可能。
    /// </summary>
    public class ClearCachedPackageDirectoryTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ite-clear-tests-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private string MakePackageDir(string name)
        {
            string dir = Path.Combine(_root, name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "content.json"), "{}");
            return dir;
        }

        [Test]
        public void ClearsTheNamedPackageDirectory()
        {
            string target = MakePackageDir("IteSpaceScene_thirdDemo");

            IteContentPipeline.ClearCachedPackageDirectory(_root, "IteSpaceScene_thirdDemo");

            Assert.That(Directory.Exists(target), Is.False);
        }

        [Test]
        public void LeavesSiblingPackagesUntouched()
        {
            string target = MakePackageDir("tourA");
            string sibling = MakePackageDir("tourB");
            string scene = MakePackageDir("IteSpaceScene_thirdDemo");

            IteContentPipeline.ClearCachedPackageDirectory(_root, "tourA");

            Assert.That(Directory.Exists(target), Is.False);
            Assert.That(File.Exists(Path.Combine(sibling, "content.json")), Is.True,
                "只该清掉指名的那个包，其余 tour 目录必须完好");
            Assert.That(File.Exists(Path.Combine(scene, "content.json")), Is.True);
        }

        /// <summary>
        /// 空目录名与根目录本身：`Path.Combine(root, "")` 就是 root，放行等于
        /// 递归删掉整个内容缓存。tourId 来自服务端下发的场景描述，是后端生成的
        /// shortid（不会是恶意路径），但**可能为空**——同一条加载链里
        /// <c>LoadSceneSpritesAsync</c> 已经在防 tourID 为空。
        /// </summary>
        [TestCase("")]
        [TestCase(null)]
        [TestCase(".")]
        [TestCase("./")]
        [TestCase("..")]
        public void RefusesToDeleteWhenTheNameResolvesOutsideOrToTheRoot(string relativeFolder)
        {
            string survivor = MakePackageDir("tourA");
            LogAssert.ignoreFailingMessages = true;

            IteContentPipeline.ClearCachedPackageDirectory(_root, relativeFolder);

            Assert.That(Directory.Exists(_root), Is.True, "缓存根目录本身绝不能被删");
            Assert.That(File.Exists(Path.Combine(survivor, "content.json")), Is.True,
                "拒绝删除时不得波及任何已有内容");
        }

        [Test]
        public void MissingDirectoryIsNotAnError()
        {
            Assert.DoesNotThrow(() =>
                IteContentPipeline.ClearCachedPackageDirectory(_root, "never-downloaded"));
        }
    }
}
