using UnityEngine;

namespace Uality.IteTour.Components
{
    /// <summary>富文本元素的摆放计算。纯函数。</summary>
    public static class RichTextLayout
    {
        /// <summary>
        /// 没有配音时，播放器面板挪到图片左上角外侧的局部坐标。
        ///
        /// 除法**故意保持整数除法**：源实现里 <paramref name="textureWidth"/> 与
        /// <paramref name="textureHeight"/> 是 <c>int</c>，尾数被截掉，只有末尾的
        /// <c>+ 20f</c> 是浮点。改成浮点除会让所有奇数尺寸的图偏移半个像素（D12）。
        /// </summary>
        public static Vector3 AudioControllerOffset(int textureWidth, int textureHeight)
        {
            var offsetX = -((textureWidth + 210) / 2 + 20f);
            var offsetY = (textureHeight - 210) / 2 + 20f;

            return new Vector3(offsetX, offsetY, 0);
        }
    }
}
