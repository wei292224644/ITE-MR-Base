using System;
using System.Collections.Generic;
using AprilTag.Interop;
using UnityEngine;
// AprilTag.Interop 里也有个 Pose（原生位姿估计器的输出），与 UnityEngine.Pose 同名。
// 这里全用后者——位姿由 PlanarPoseSolver 解，不走包自带的估计器（design D2/D3）。
using Pose = UnityEngine.Pose;

/// <summary>
/// AprilTag 检测核心。探针与生产 Provider 共用同一份（design D8）——度量出的数字若不来自
/// 生产路径，判据就不成立。
///
/// 这里只用 <c>AprilTag.Interop</c> 底层，不用包自带的 <c>AprilTag.TagDetector</c>（design D2）：
/// - 后者的 <c>PoseEstimationJob</c> 走 Unity Job System，只能在主线程调度，而 <see cref="Detect"/>
///   是阻塞调用，放主线程就是掉帧；
/// - 后者还写死了 <c>fx == fy</c> 与 <c>cx, cy</c> 位于图像中心，而我们有实测内参。
///
/// <see cref="Detect"/> 全程只做 P/Invoke 与数组读写，不触碰任何 Unity API，可以留在工作线程上。
/// </summary>
public sealed class AprilTagDetectorCore : IDisposable
{
    /// <summary>一次检测结果。角点为图像像素坐标，按 AprilTag 的约定逆时针环绕。</summary>
    public readonly struct TagObservation
    {
        public readonly int Id;

        /// <summary>纠错所修正的位数。越大越可疑，用于把误检与漏检分开统计。</summary>
        public readonly int Hamming;

        /// <summary>解码裕度。同上，进日志。</summary>
        public readonly float DecisionMargin;

        public readonly Vector2 Corner0;
        public readonly Vector2 Corner1;
        public readonly Vector2 Corner2;
        public readonly Vector2 Corner3;
        public readonly Vector2 Center;

        public TagObservation(
            int id, int hamming, float decisionMargin,
            Vector2 corner0, Vector2 corner1, Vector2 corner2, Vector2 corner3, Vector2 center)
        {
            Id = id;
            Hamming = hamming;
            DecisionMargin = decisionMargin;
            Corner0 = corner0;
            Corner1 = corner1;
            Corner2 = corner2;
            Corner3 = corner3;
            Center = center;
        }
    }

    private Detector detector;
    private Family family;
    private ImageU8 image;

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// 相机缓冲区的行序。true = 第一行在图像底部。
    ///
    /// PICO 缓冲区实际是哪一种尚未由真机确定（design D6 / tasks 5.3）。选错的表现是解出的位姿
    /// 沿 Y 镜像，而不是报错——所以它必须是显式开关，不能靠试。EditMode 测试（D12）会断言
    /// 两种行序恰好相差一个 Y 镜像，真机上只剩判定属于哪一种。
    /// </summary>
    public bool BottomUpRows { get; set; }

    public AprilTagDetectorCore(int width, int height, int threadCount)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "检测器尺寸必须为正。");
        }

        Width = width;
        Height = height;

        detector = Detector.Create();
        family = Family.CreateTagStandard41h12();
        image = ImageU8.Create(width, height);
        detector.AddFamily(family);
        detector.ThreadCount = Mathf.Max(1, threadCount);
    }

    // 检测器调参（design D7）。这些直接决定速度/鲁棒性权衡，真机上必然要调，所以不写死。
    // 只在 Detect 之外设置——底层是直接写原生结构体，检测进行中改会读到半新半旧的值。

    /// <summary>四边形检测阶段的降采样倍率。只影响检测，位解码仍在全分辨率进行。</summary>
    public float QuadDecimate
    {
        get => detector.QuadDecimate;
        set => detector.QuadDecimate = value;
    }

    /// <summary>检测前的高斯模糊。噪声大时调高，代价是丢细节。</summary>
    public float QuadSigma
    {
        get => detector.QuadSigma;
        set => detector.QuadSigma = value;
    }

    /// <summary>是否对四边形边线做亚像素精修。角点精度直接进单应，默认应开。</summary>
    public int RefineEdges
    {
        get => detector.RefineEdges;
        set => detector.RefineEdges = value;
    }

    public double DecodeSharpening
    {
        get => detector.DecodeSharpening;
        set => detector.DecodeSharpening = value;
    }

    public int ThreadCount
    {
        get => detector.ThreadCount;
        set => detector.ThreadCount = Mathf.Max(1, value);
    }

    /// <summary>
    /// 在一帧 RGB32 图上做检测。可在工作线程调用。
    /// </summary>
    /// <param name="rgb32">每像素 4 字节。取自 <c>SetCameraFrameBufferfor4U</c> 推送式回调
    /// 缓冲区——已在真机验证可用的取帧路径。曾切换到 <c>AcquireVSTCameraFrameAntiDistortion</c>
    /// 拉取式去畸变帧（RGB24），但那条路径依赖的 <c>OpenVSTCamera()</c> 与这里用的
    /// <c>OpenCameraAsyncfor4U</c> 是互不相通的两个开关，真机上 <c>AcquireFrame</c> 连续
    /// 120 次全部 <c>result=-1</c>。去畸变是真实需求但和换检测器是两件事，
    /// 迁移应保持行为等价，因此退回这条已证明可用的路径，去畸变单独记为待办。</param>
    /// <param name="results">复用的输出列表，调用方持有；本方法会先清空它。</param>
    public void Detect(byte[] rgb32, List<TagObservation> results)
    {
        if (rgb32 == null) throw new ArgumentNullException(nameof(rgb32));
        if (results == null) throw new ArgumentNullException(nameof(results));
        if (detector == null) throw new ObjectDisposedException(nameof(AprilTagDetectorCore));

        int required = Width * Height * 4;
        if (rgb32.Length < required)
        {
            throw new ArgumentException(
                $"缓冲区太小：需要 {required} 字节（{Width}x{Height} RGB32），实得 {rgb32.Length}。",
                nameof(rgb32));
        }

        FillGrayscale(rgb32);

        results.Clear();
        using DetectionArray tags = detector.Detect(image);
        for (int i = 0; i < tags.Length; i++)
        {
            ref Detection tag = ref tags[i];
            results.Add(new TagObservation(
                tag.ID,
                tag.Hamming,
                tag.DecisionMargin,
                ToVector(tag.Corner1),
                ToVector(tag.Corner2),
                ToVector(tag.Corner3),
                ToVector(tag.Corner4),
                ToVector(tag.Center)));
        }
    }

    /// <summary>
    /// RGB32 → 8 位灰度，写进原生 image_u8 的行缓冲。
    ///
    /// 取绿色通道而不是加权亮度：它是 RGB32/BGR32/RGBA32 的**同一个字节位置**，所以相机给的
    /// 通道序未知也不影响结果；而标本身是黑白的，绿通道与亮度等价。上游包的 ImageConverter
    /// 对 Color32 也是这么取的，这里是同一约定换成 4 字节跨度。
    /// </summary>
    private void FillGrayscale(byte[] rgb32)
    {
        Span<byte> destination = image.Buffer;
        int stride = image.Stride;

        for (int y = 0; y < Height; y++)
        {
            int sourceRow = (BottomUpRows ? Height - 1 - y : y) * Width * 4;
            int destinationRow = y * stride;
            for (int x = 0; x < Width; x++)
            {
                destination[destinationRow + x] = rgb32[sourceRow + x * 4 + 1];
            }
        }
    }

    private static Vector2 ToVector((double x, double y) point)
    {
        return new Vector2((float)point.x, (float)point.y);
    }

    /// <summary>
    /// 由四个真角点与实测内参解出 marker 在相机坐标系下的位姿（design D3）。
    ///
    /// <paramref name="tagSizeMeters"/> 是**检测四边形**的边长，即 <c>width_at_border</c> 那一圈，
    /// 不是整张标图的幅面——tagStandard41h12 的 <c>width_at_border=5</c> 而 <c>total_width=9</c>，
    /// 检测框只有图幅的 5/9（design D4）。一律传打印后的实测值。
    /// </summary>
    public static bool TrySolvePose(
        in TagObservation observation,
        float tagSizeMeters,
        float fx, float fy, float cx, float cy,
        out Pose markerInCamera)
    {
        markerInCamera = default;
        if (tagSizeMeters <= 0f) return false;

        float half = tagSizeMeters * 0.5f;
        // 角点与模型点的对应取自 apriltag 官方位姿估计器（apriltag_pose.c:497-502）：
        //   p[0]={-s, s, 0}  p[1]={s, s, 0}  p[2]={s, -s, 0}  p[3]={-s, -s, 0}
        // 它与 PlanarPoseSolver 用的是同一套归一化（(u-cx)/fx, (v-cy)/fy, 1），可直接沿用。
        // 写反 Y 号的表现是位置对、旋转差约 180°——由 EditMode 测试断言（design D12）。
        var modelPoints = new[]
        {
            new Vector2(-half, half),
            new Vector2(half, half),
            new Vector2(half, -half),
            new Vector2(-half, -half)
        };
        var imagePoints = new[]
        {
            observation.Corner0,
            observation.Corner1,
            observation.Corner2,
            observation.Corner3
        };

        if (!PlanarPoseSolver.TrySolve(
                modelPoints, imagePoints, fx, fy, cx, cy, out Matrix4x4 markerToCamera))
        {
            return false;
        }

        markerInCamera = PlanarPoseSolver.ToUnityCameraSpace(markerToCamera);
        return true;
    }

    public void Dispose()
    {
        detector?.RemoveFamily(family);
        detector?.Dispose();
        family?.Dispose();
        image?.Dispose();

        detector = null;
        family = null;
        image = null;
    }
}
