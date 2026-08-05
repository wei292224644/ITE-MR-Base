using NUnit.Framework;
using UnityEngine;
using Uality.IteTour.Components;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 富文本没有配音时，播放器面板要挪到图片左上角外侧。
    ///
    /// 源实现里 <c>width</c> / <c>height</c> 是 <c>int</c>，所以 <c>(width + 210) / 2</c>
    /// 走的是**整数除法**，尾数被截掉；末尾的 <c>+ 20f</c> 才转成浮点。看着像浮点算式，
    /// 实际不是。这里用奇数尺寸把这个行为钉死——照搬时最容易被「顺手改成 2f」改掉。
    ///
    /// 期望值取自源实现 <c>RichTextElement.Constructor</c>。
    /// </summary>
    public class RichTextLayoutTests
    {
        [Test]
        public void AudioControllerOffset_UsesIntegerDivision()
        {
            // (101 + 210) / 2 = 155（不是 155.5）  → -(155 + 20) = -175
            // (101 - 210) / 2 = -54（向零截断，不是 -54.5）→ -54 + 20 = -34
            var offset = RichTextLayout.AudioControllerOffset(101, 101);

            Assert.That(offset, Is.EqualTo(new Vector3(-175f, -34f, 0f)));
        }

        [Test]
        public void AudioControllerOffset_EvenSizes()
        {
            // (100 + 210) / 2 = 155 → -175 ;  (300 - 210) / 2 = 45 → 65
            var offset = RichTextLayout.AudioControllerOffset(100, 300);

            Assert.That(offset, Is.EqualTo(new Vector3(-175f, 65f, 0f)));
        }
    }
}
