using System.Collections.Generic;
using NUnit.Framework;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// <see cref="ZipTopLevel.Strip"/> 语义的纯逻辑测试。做成纯函数就是为了不必
    /// 构造真实 zip 文件也能测——真实 zip 的落盘验证见 <c>ZipContentDownloaderTests</c>。
    /// </summary>
    public class ZipTopLevelResolverTests
    {
        [Test]
        public void TryFindCommonTopLevel_FindsSharedTopDirectory()
        {
            var ok = ZipTopLevelResolver.TryFindCommonTopLevel(
                new[] { "thirdDemo/thirdDemo.json", "thirdDemo/assets/a1.png" },
                out string top);

            Assert.That(ok, Is.True);
            Assert.That(top, Is.EqualTo("thirdDemo"));
        }

        [Test]
        public void TryFindCommonTopLevel_IgnoresMacosxEntries()
        {
            var ok = ZipTopLevelResolver.TryFindCommonTopLevel(
                new[] { "__MACOSX/thirdDemo", "__MACOSX/thirdDemo/._thirdDemo.json", "thirdDemo/thirdDemo.json" },
                out string top);

            Assert.That(ok, Is.True, "__MACOSX/ 条目是打包工具的影子文件，不该参与公共顶层判定");
            Assert.That(top, Is.EqualTo("thirdDemo"));
        }

        [Test]
        public void TryFindCommonTopLevel_RejectsMultipleTopDirectories()
        {
            var ok = ZipTopLevelResolver.TryFindCommonTopLevel(
                new[] { "a/x.json", "b/y.json" },
                out _);

            Assert.That(ok, Is.False, "两个不同的顶层目录，不存在唯一公共顶层");
        }

        [Test]
        public void TryFindCommonTopLevel_RejectsEntryOutsideAnyDirectory()
        {
            var ok = ZipTopLevelResolver.TryFindCommonTopLevel(
                new[] { "a/x.json", "loose.json" },
                out _);

            Assert.That(ok, Is.False, "loose.json 不在任何目录之下，破坏了唯一公共顶层的前提");
        }

        [Test]
        public void TryFindCommonTopLevel_RejectsEmptyOrAllMacosxInput()
        {
            Assert.That(ZipTopLevelResolver.TryFindCommonTopLevel(new List<string>(), out _), Is.False);
            Assert.That(ZipTopLevelResolver.TryFindCommonTopLevel(
                new[] { "__MACOSX/a", "__MACOSX/b/c" }, out _), Is.False);
        }

        [TestCase("__MACOSX", true)]
        [TestCase("__MACOSX/thirdDemo/._thirdDemo.json", true)]
        [TestCase("thirdDemo/thirdDemo.json", false)]
        [TestCase("", false)]
        public void IsMacosxEntry_MatchesOnlyMacosxPrefix(string entryName, bool expected)
        {
            Assert.That(ZipTopLevelResolver.IsMacosxEntry(entryName), Is.EqualTo(expected));
        }
    }
}
