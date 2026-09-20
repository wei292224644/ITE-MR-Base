using UnityEngine;

namespace Uality.IteTour.Core
{
    /// <summary>
    /// 锚定的几何：把整套空间重新钉到「被扫中的那个 Tour」上。
    ///
    /// 做法是 <c>TourRoot.local = 被扫中 Tour 局部矩阵的逆</c>，再把标记世界位姿写进
    /// <c>AnchorRoot.local</c>；两者相乘，被扫中的 Tour 就落在标记上，其余 Tour 保持
    /// 场景描述里彼此的相对布局。
    ///
    /// 提成纯函数而不是留在 <see cref="IteTourObject"/> 里：这条链是本工程出错最多的地方
    /// （design D30、D32 都是锚定方向），而长在 MonoBehaviour 上时它一条离机测试都没有——
    /// 方向错了只能靠真机目视，而目视分不清「转了 180°」与「转了 180° 再转回来」。
    /// </summary>
    public static class TourAnchoring
    {
        /// <summary>
        /// 被扫中 Tour 相对 <c>TourRoot</c> 的局部矩阵 → 该写进 <c>TourRoot</c> 的局部位姿。
        ///
        /// 只取平移与旋转：<c>TourRoot</c> 经 <c>SetLocalPositionAndRotation</c> 写入，
        /// 缩放丢掉是既有行为（见 <see cref="HasUnitScale"/>）。
        /// </summary>
        public static Pose TourRootLocal(Matrix4x4 tourLocal)
        {
            var inverse = tourLocal.inverse;
            return new Pose(inverse.GetPosition(), inverse.rotation);
        }

        /// <summary>
        /// 锚定之后，某个 Tour 会落在世界的哪儿。<paramref name="tourLocal"/> 传被扫中的那个时，
        /// 结果就是标记位姿本身——这是整条链的不变式。
        /// </summary>
        public static Pose ResolveWorld(Pose anchor, Matrix4x4 scannedLocal, Matrix4x4 tourLocal)
        {
            var rootLocal = TourRootLocal(scannedLocal);
            var world = Matrix4x4.TRS(anchor.position, anchor.rotation, Vector3.one)
                        * Matrix4x4.TRS(rootLocal.position, rootLocal.rotation, Vector3.one)
                        * tourLocal;

            return new Pose(world.GetPosition(), world.rotation);
        }

        /// <summary>
        /// 缩放是否接近 1。<c>TourRoot</c> 只写位置与旋转，所以场景描述里若带了缩放，
        /// 它会被静默丢掉——内容大小不对、无任何日志。调用方据此出声。
        /// </summary>
        public static bool HasUnitScale(Matrix4x4 tourLocal)
        {
            var scale = tourLocal.lossyScale;
            return Mathf.Abs(scale.x - 1f) < 1e-3f
                && Mathf.Abs(scale.y - 1f) < 1e-3f
                && Mathf.Abs(scale.z - 1f) < 1e-3f;
        }
    }
}
