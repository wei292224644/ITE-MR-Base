using NUnit.Framework;
using Uality.IteTour.Core;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// Tour 资源在缓存目录下的相对路径。源实现把这些 <c>Path.Combine</c> 散在
    /// <c>IteTourObject.LoadAssets</c> 的 switch 里，路径拼错只会表现为「资源没出现」，
    /// 且必须上机才看得到。抽成纯函数后可离机钉住。
    ///
    /// 期望值取自源实现 <c>ite-space-tour/Assets/Scripts/ITE/IteTourObject.cs</c>，
    /// 不是取自本包实现。
    /// </summary>
    public class TourAssetPathsTests
    {
        [Test]
        public void EmwModelFolder_IsTourAssetIdVersion()
        {
            Assert.That(TourAssetPaths.EmwModelFolder("t1", "a9", 3), Is.EqualTo("t1/asset/a9/3"));
        }

        [Test]
        public void EmwModelPublishJson_LivesInTheVersionedFolder()
        {
            Assert.That(TourAssetPaths.EmwModelPublishJson("t1", "a9", 3), Is.EqualTo("t1/asset/a9/3/publish.json"));
        }

        /// <summary>glb 文件名取自 publish.json 里的 <c>id</c>，不是资源 Id。</summary>
        [Test]
        public void EmwModelGlb_IsNamedAfterThePublishJsonId()
        {
            Assert.That(
                TourAssetPaths.EmwModelGlb("t1", "a9", 3, "pub7"),
                Is.EqualTo("t1/asset/a9/3/pub7_sceneViewer.glb"));
        }

        /// <summary>
        /// 音频**不在**版本目录下，而是资源目录下——源实现如此，两者不一致但有意保留。
        /// </summary>
        [Test]
        public void EmwModelAudio_SitsOutsideTheVersionedFolder()
        {
            Assert.That(
                TourAssetPaths.EmwModelAudio("t1", "a9", "audio/intro.mp3"),
                Is.EqualTo("t1/asset/a9/audio/intro.mp3"));
        }

        [Test]
        public void RichTextAudio_IsAssetIdDotMp3()
        {
            Assert.That(TourAssetPaths.RichTextAudio("t1", "r5"), Is.EqualTo("t1/asset/r5.mp3"));
        }

        /// <summary>富文本图在 <c>raster</c> 子目录下，音频不在——两者路径形状不同。</summary>
        [Test]
        public void RichTextImage_IsUnderRasterSubfolder()
        {
            Assert.That(TourAssetPaths.RichTextImage("t1", "r5"), Is.EqualTo("t1/asset/raster/r5.png"));
        }

        [Test]
        public void Video_IsAssetIdDotMp4()
        {
            Assert.That(TourAssetPaths.Video("t1", "v2"), Is.EqualTo("t1/asset/v2.mp4"));
        }
    }
}
