using System.IO;
using NUnit.Framework;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// zip 内容来自远端服务器，是信任边界。源实现直接 Path.Combine(outputFolder, entry.Name)
    /// 未做任何校验，一个名为 "../../x" 的条目就能写到解压目录之外（zip slip）。
    ///
    /// 这是本次**有意偏离**「行为等价优先」的地方：安全防护不在 D12 的豁免范围内。
    /// </summary>
    public class ZipEntryPathTests
    {
        private static string Root => Path.Combine(Path.GetTempPath(), "ite-extract");

        [TestCase("../evil.txt")]
        [TestCase("a/../../evil.txt")]
        [TestCase("../../../../etc/passwd")]
        public void TryResolve_RejectsParentDirectoryEscape(string entryName)
        {
            var ok = ZipEntryPath.TryResolve(Root, entryName, out _);

            Assert.That(ok, Is.False, $"条目名 \"{entryName}\" 逃出了解压目录，必须拒绝");
        }

        [Test]
        public void TryResolve_RejectsAbsoluteEntryName()
        {
            // Path.Combine 遇到绝对路径会直接返回它，前缀检查是最后一道拦截
            var absolute = Path.Combine(Path.GetTempPath(), "elsewhere", "evil.txt");

            var ok = ZipEntryPath.TryResolve(Root, absolute, out _);

            Assert.That(ok, Is.False);
        }

        [TestCase("model.glb")]
        [TestCase("asset/raster/a1.png")]
        [TestCase("a/b/../c.txt")]
        public void TryResolve_AcceptsPathsThatStayInside(string entryName)
        {
            var ok = ZipEntryPath.TryResolve(Root, entryName, out var fullPath);

            Assert.That(ok, Is.True, $"条目名 \"{entryName}\" 在目录内，不该被拒");
            Assert.That(fullPath, Does.StartWith(Path.GetFullPath(Root)));
        }

        [Test]
        public void TryResolve_RejectsEmptyEntryName()
        {
            Assert.That(ZipEntryPath.TryResolve(Root, "", out _), Is.False);
            Assert.That(ZipEntryPath.TryResolve(Root, null, out _), Is.False);
        }

        /// <summary>
        /// 解析结果正好**等于**根目录本身的形态。解压时这只是个没用的条目，但
        /// <c>IteContentPipeline.ClearCachedPackageDirectory</c> 拿同一个判断来守
        /// 递归删除（design D10）——放行就等于 <c>Directory.Delete(persistentDataPath,
        /// recursive: true)</c>，整个内容缓存没了。
        /// </summary>
        [TestCase(".")]
        [TestCase("./")]
        [TestCase("a/..")]
        public void TryResolve_RejectsPathsResolvingToTheRootItself(string entryName)
        {
            Assert.That(ZipEntryPath.TryResolve(Root, entryName, out _), Is.False,
                $"\"{entryName}\" 解析成根目录本身，放行会让递归删除清空整个缓存");
        }
    }
}
