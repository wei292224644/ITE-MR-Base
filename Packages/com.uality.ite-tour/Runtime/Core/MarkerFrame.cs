using UnityEngine;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 宿主推入的标记位姿 → ITE 内容锚点坐标系（design D32）。
    ///
    /// 宿主约定（两端实测一致，Quest 来自 MRUK、PICO 来自 D30 的求解链）：
    /// X = 印刷面左边、Y = 印刷面上边、Z = 垂直印刷面朝外（Unity 左手系下看码时的常规朝向）。
    ///
    /// ITE 编辑器摆内容用的是 X = 印刷面右边、Y = 垂直印刷面朝外、Z = 印刷面下边（右手系）；
    /// 内容数据进 Unity 时 <c>ConvertToLeftHanded</c> 沿 Z 翻一次，所以锚点在 Unity 里要的是
    /// X = 右边、Y = 垂直朝外、Z = 上边——与宿主约定相差一个固定旋转，不需要镜像。
    ///
    /// 放在包里而不是宿主的平台偏移配置：这是 ITE 内容格式自己的约定，与平台无关；
    /// 平台偏移只补贴纸位置与锚点的物理差异（D30）。
    /// </summary>
    public static class MarkerFrame
    {
        public static readonly Quaternion ToContentAnchor = Quaternion.Euler(270f, 180f, 0f);

        public static Pose ToContentAnchorPose(Pose marker)
            => new Pose(marker.position, marker.rotation * ToContentAnchor);
    }
}
