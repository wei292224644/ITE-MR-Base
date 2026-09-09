using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Uality.IteTour.Internal;
using Unity.SharpZipLib.Zip;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// <see cref="ZipContentDownloader.ExtractAsync"/> 的落盘验证：真实构造 zip 文件，
    /// 覆盖 <see cref="ZipTopLevel.Preserve"/> / <see cref="ZipTopLevel.Strip"/> 两种语义，
    /// 以及"声明 Strip 但 zip 无公共顶层目录"时的报错路径。
    ///
    /// 全部走 <see cref="ZipContentDownloader.ExtractSync"/>（同步），不用 <c>async Task</c>
    /// 测试方法：Unity 的 Test Runner 里跑 <c>async Task</c> 测试、内部再 <c>await Task.Run(...)</c>，
    /// 续体会抢主线程，曾经真实死锁过 Editor（主线程卡死，CPU 却是空的）。
    /// </summary>
    public class ZipContentDownloaderTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ite-zip-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        private string ZipPath => Path.Combine(_tempDir, "content.zip");
        private string OutputFolder => Path.Combine(_tempDir, "out");

        private void CreateZip(IEnumerable<(string entryName, string content)> entries)
        {
            using (FileStream fs = File.Create(ZipPath))
            using (var zipStream = new ZipOutputStream(fs))
            {
                zipStream.SetLevel(0);
                foreach (var (entryName, content) in entries)
                {
                    var entry = new ZipEntry(entryName);
                    byte[] bytes = Encoding.UTF8.GetBytes(content ?? "");
                    entry.Size = bytes.Length;
                    zipStream.PutNextEntry(entry);
                    zipStream.Write(bytes, 0, bytes.Length);
                    zipStream.CloseEntry();
                }
                zipStream.Finish();
            }
        }

        [Test]
        public void ExtractSync_Preserve_KeepsEntryPathsAsIs()
        {
            CreateZip(new[]
            {
                ("wm0l5qcn_ibd/wm0l5qcn_ibd.json", "{}"),
                ("wm0l5qcn_ibd/assets/model.glb", "glb-bytes"),
            });

            ZipContentDownloader.ExtractSync(ZipPath, OutputFolder, ZipTopLevel.Preserve);

            Assert.That(File.Exists(Path.Combine(OutputFolder, "wm0l5qcn_ibd", "wm0l5qcn_ibd.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(OutputFolder, "wm0l5qcn_ibd", "assets", "model.glb")), Is.True);
        }

        [Test]
        public void ExtractSync_Strip_RemovesCommonTopLevelDirectory()
        {
            CreateZip(new[]
            {
                ("thirdDemo/thirdDemo.json", "{}"),
                ("thirdDemo/assets/logo.png", "png-bytes"),
            });

            ZipContentDownloader.ExtractSync(ZipPath, OutputFolder, ZipTopLevel.Strip);

            Assert.That(File.Exists(Path.Combine(OutputFolder, "thirdDemo.json")), Is.True,
                "Strip 应剥掉 thirdDemo/ 这一层，读取侧按 {folder}/{sceneName}.json 找文件");
            Assert.That(File.Exists(Path.Combine(OutputFolder, "assets", "logo.png")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(OutputFolder, "thirdDemo")), Is.False);
        }

        [Test]
        public void ExtractSync_Strip_IgnoresMacosxEntries()
        {
            CreateZip(new[]
            {
                ("__MACOSX/thirdDemo", ""),
                ("__MACOSX/thirdDemo/._thirdDemo.json", "resource-fork-bytes"),
                ("thirdDemo/thirdDemo.json", "{}"),
            });

            ZipContentDownloader.ExtractSync(ZipPath, OutputFolder, ZipTopLevel.Strip);

            Assert.That(File.Exists(Path.Combine(OutputFolder, "thirdDemo.json")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(OutputFolder, "__MACOSX")), Is.False,
                "__MACOSX/ 是打包工具的影子文件，不该落盘，也不该干扰公共顶层判定");
        }

        /// <summary>
        /// 条目形状照实测的真包构造：`thirdDemo.zip`（macOS Finder 对文件夹右键压缩的产物）
        /// 除文件条目外还含 0 字节的显式目录条目 `thirdDemo/`、`thirdDemo/assets/`。
        ///
        /// 其余用例构造的 zip 只有文件条目，从未走到 <c>TryStrip</c> 里
        /// "剥掉前缀后为空串 → 跳过" 那一支，而真包每次解压都会走到。
        /// </summary>
        [Test]
        public void ExtractSync_Strip_HandlesExplicitDirectoryEntries()
        {
            CreateZip(new[]
            {
                ("thirdDemo/", ""),
                ("thirdDemo/thirdDemo.json", "{}"),
                ("thirdDemo/assets/", ""),
                ("thirdDemo/assets/logo.png", "png-bytes"),
                ("__MACOSX/thirdDemo/._thirdDemo.json", "resource-fork-bytes"),
            });

            ZipContentDownloader.ExtractSync(ZipPath, OutputFolder, ZipTopLevel.Strip);

            Assert.That(File.Exists(Path.Combine(OutputFolder, "thirdDemo.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(OutputFolder, "assets", "logo.png")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(OutputFolder, "thirdDemo")), Is.False,
                "顶层目录条目剥掉前缀后一无所剩，应被跳过而不是落成一个空目录");
            Assert.That(Directory.Exists(Path.Combine(OutputFolder, "__MACOSX")), Is.False);
        }

        [Test]
        public void ExtractSync_Strip_ThrowsWhenNoCommonTopLevelDirectory()
        {
            CreateZip(new[]
            {
                ("a/x.json", "{}"),
                ("b/y.json", "{}"),
            });

            Assert.Throws<InvalidDataException>(() =>
                ZipContentDownloader.ExtractSync(ZipPath, OutputFolder, ZipTopLevel.Strip));

            Assert.That(Directory.Exists(OutputFolder), Is.False,
                "报错必须中止解压，MUST NOT 把条目按原样落盘");
        }

        [Test]
        public void ExtractSync_RejectsZipSlipEntryRegardlessOfTopLevelMode()
        {
            CreateZip(new[]
            {
                ("tourId/tour.json", "{}"),
                ("../evil.txt", "escaped"),
            });

            ZipContentDownloader.ExtractSync(ZipPath, OutputFolder, ZipTopLevel.Preserve);

            Assert.That(File.Exists(Path.Combine(OutputFolder, "tourId", "tour.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(_tempDir, "evil.txt")), Is.False);
        }
    }
}
