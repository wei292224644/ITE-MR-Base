using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AprilTag.Interop;
using NUnit.Framework;
using UnityEngine;
// AprilTag.Interop 里也有个 Pose，与 UnityEngine.Pose 同名。这里全用后者。
using Pose = UnityEngine.Pose;

/// <summary>
/// 用**真原生检测器**跑的集成测试（design D12）。
///
/// 包里的 macOS plugin 是 universal binary 且 .meta 中 Editor 已启用，所以整条
/// 检测 → 角点 → <see cref="PlanarPoseSolver"/> 链路可以在 Editor 内验证，不必等真机。
/// 这把三件事从真机挪了进来：角点顺序与模型点的对应、位姿原点与轴向、行序约定。
///
/// 每个用例都先假定一个已知的 markerToCamera，把标图按该位姿透视变形成一帧合成相机图，
/// 再喂回检测器，看能不能把那个已知位姿还原出来。
///
/// 缺少原生插件的平台（Editor 未启用该 plugin）整体跳过，不算失败。
/// </summary>
public class AprilTagDetectorCoreTests
{
    // 相机内参：PICO 4 Ultra Enterprise 左目 640x480 实测值。
    const int ImageWidth = 640;
    const int ImageHeight = 480;
    const float Fx = 407.01f;
    const float Fy = 407.03f;
    const float Cx = 319.50f;
    const float Cy = 239.50f;

    // tagStandard41h12：width_at_border=5、total_width=9（apriltag 源码尾部）。
    // 检测器认的四边形只有图幅的 5/9——160 mm 图幅对应 88.9 mm 检测框。
    const int WidthAtBorder = 5;
    const int TotalWidth = 9;
    const float TagSizeMeters = 0.0889f;
    const float ModuleMeters = TagSizeMeters / WidthAtBorder;
    const float TagImageMeters = TotalWidth * ModuleMeters;

    const int TestTagId = 0;

    // 合成渲染的超采样倍率。硬边缘会让角点精修没有亚像素信息可用，
    // 不做抗锯齿的话测出来的精度是渲染的锯齿而不是求解器的能力。
    const int SuperSample = 3;

    static byte[] tagImage;
    static int tagImageWidth;
    static int tagImageHeight;

    [OneTimeSetUp]
    public void RenderReferenceTag()
    {
        try
        {
            using Family family = Family.CreateTagStandard41h12();
            tagImage = RenderTagToBytes(family, TestTagId, out tagImageWidth, out tagImageHeight);
        }
        catch (DllNotFoundException)
        {
            Assert.Ignore("本平台的 Editor 未启用 AprilTag 原生插件，跳过检测器集成测试。");
        }

        Assert.AreEqual(TotalWidth, tagImageWidth, "标图幅面应为 total_width 模块见方");
        Assert.AreEqual(TotalWidth, tagImageHeight);
    }

    [Test]
    public void Detect_WithFrontalTag_ReportsExpectedId()
    {
        Matrix4x4 truth = Matrix4x4.TRS(new Vector3(0f, 0f, 0.8f), Quaternion.identity, Vector3.one);
        byte[] frame = RenderFrame(truth);

        using var core = new AprilTagDetectorCore(ImageWidth, ImageHeight, 1);
        var results = new List<AprilTagDetectorCore.TagObservation>();
        core.Detect(frame, results);

        Assert.AreEqual(1, results.Count, "合成图里只有一个标，检出数应为 1");
        Assert.AreEqual(TestTagId, results[0].Id);
        Assert.AreEqual(0, results[0].Hamming, "无噪声合成图不应需要纠错");
    }

    /// <summary>
    /// 这个用例同时钉死两件事：角点顺序与模型点的对应关系，以及位姿的原点和轴向。
    /// 角点顺序若旋转一位，还原出的旋转会差 90°，断言立刻红。
    /// </summary>
    [Test]
    public void TrySolvePose_WithObliqueTag_RecoversTruthPose()
    {
        Matrix4x4 truth = Matrix4x4.TRS(
            new Vector3(0.06f, -0.03f, 0.7f),
            Quaternion.Euler(12f, 20f, -8f),
            Vector3.one);
        byte[] frame = RenderFrame(truth);

        using var core = new AprilTagDetectorCore(ImageWidth, ImageHeight, 1);
        var results = new List<AprilTagDetectorCore.TagObservation>();
        core.Detect(frame, results);
        Assert.AreEqual(1, results.Count);

        Assert.IsTrue(AprilTagDetectorCore.TrySolvePose(
            results[0], TagSizeMeters, Fx, Fy, Cx, Cy, out Pose solved));

        Pose expected = PlanarPoseSolver.ToUnityCameraSpace(truth);
        float positionError = Vector3.Distance(solved.position, expected.position);
        float angleError = Quaternion.Angle(solved.rotation, expected.rotation);

        Debug.Log(
            $"[AprilTagDetectorCore] oblique: posErr={positionError * 1000f:F1}mm angErr={angleError:F2}deg");

        // 预算按合成渲染的极限留的护栏，不是真机指标：渲染量化 + 角点精修的联合误差。
        Assert.Less(positionError, 0.010f, "位置误差超预算");
        Assert.Less(angleError, 3.0f, "角度误差超预算——先怀疑角点顺序，再怀疑求解器");
    }

    /// <summary>
    /// 行序约定（design D6）。行序翻转是**镜像**而不是旋转，apriltag 解不了镜像的码——
    /// 所以选错行序的表现是**一个都检不出**，而不是解出一个 Y 镜像的位姿。
    ///
    /// 这对真机是个干净的判据：装上去如果一个都不认，先怀疑行序，不用去分析位姿对不对。
    /// </summary>
    [Test]
    public void Detect_WithWrongRowOrder_FindsNothingBecauseMirroredTagCannotDecode()
    {
        Matrix4x4 truth = Matrix4x4.TRS(
            new Vector3(0.05f, 0.02f, 0.7f),
            Quaternion.Euler(0f, 15f, 0f),
            Vector3.one);
        byte[] frame = RenderFrame(truth);

        using var core = new AprilTagDetectorCore(ImageWidth, ImageHeight, 1);
        var correct = new List<AprilTagDetectorCore.TagObservation>();
        var mirrored = new List<AprilTagDetectorCore.TagObservation>();

        core.BottomUpRows = false;
        core.Detect(frame, correct);
        core.BottomUpRows = true;
        core.Detect(frame, mirrored);

        Assert.AreEqual(1, correct.Count, "行序正确时应检出");
        Assert.AreEqual(0, mirrored.Count,
            "行序错误时应一个都检不出——镜像的码解不了。若这里检出了，说明标本身是镜像对称的，用例失效");
    }

    /// <summary>
    /// 无设备时先摸出工作包络的下界。这些数字是合成图上的**乐观上界**——
    /// 真机还有运动模糊、光照不均和镜头畸变，实测只会更差，所以它只能用来证伪，不能用来验收。
    /// </summary>
    [TestCase(0.5f, 0f)]
    [TestCase(1.0f, 0f)]
    [TestCase(2.0f, 0f)]
    [TestCase(1.0f, 30f)]
    [TestCase(1.0f, 45f)]
    [TestCase(1.0f, 60f)]
    public void Detect_AcrossEnvelope_ReportsWhereItStillWorks(float distance, float tiltDegrees)
    {
        Matrix4x4 truth = Matrix4x4.TRS(
            new Vector3(0f, 0f, distance),
            Quaternion.Euler(0f, tiltDegrees, 0f),
            Vector3.one);
        byte[] frame = RenderFrame(truth);

        using var core = new AprilTagDetectorCore(ImageWidth, ImageHeight, 1);
        var results = new List<AprilTagDetectorCore.TagObservation>();
        core.Detect(frame, results);

        float quadPixels = TagSizeMeters * Fx / distance;
        bool detected = results.Count == 1 && results[0].Id == TestTagId;
        float positionError = float.NaN;
        if (detected && AprilTagDetectorCore.TrySolvePose(
                results[0], TagSizeMeters, Fx, Fy, Cx, Cy, out Pose solved))
        {
            positionError = Vector3.Distance(
                solved.position, PlanarPoseSolver.ToUnityCameraSpace(truth).position);
        }

        Debug.Log(
            $"[AprilTagDetectorCore] envelope d={distance:F1}m tilt={tiltDegrees:F0}deg " +
            $"quad={quadPixels:F0}px detected={detected} posErr={positionError * 1000f:F1}mm");

        Assert.IsTrue(detected, $"{distance:F1} m / {tiltDegrees:F0}° 未检出（检测框 {quadPixels:F0} px）");
    }

    /// <summary>
    /// 诊断用：把渲染出的标图和合成帧的统计打出来，用来定位是哪一级坏的。
    /// 不做断言——它是查问题的工具，不是判据。
    /// </summary>
    [Test]
    public void Diagnostic_DumpRenderedTagAndFrameStats()
    {
        var tagDump = new System.Text.StringBuilder();
        tagDump.AppendLine($"tag image {tagImageWidth}x{tagImageHeight}:");
        for (int row = 0; row < tagImageHeight; row++)
        {
            for (int column = 0; column < tagImageWidth; column++)
            {
                tagDump.Append(tagImage[row * tagImageWidth + column] > 127 ? '.' : '#');
            }
            tagDump.AppendLine();
        }
        Debug.Log(tagDump.ToString());

        Matrix4x4 truth = Matrix4x4.TRS(new Vector3(0f, 0f, 0.8f), Quaternion.identity, Vector3.one);
        byte[] frame = RenderFrame(truth);

        int black = 0, white = 0, other = 0;
        for (int i = 0; i < frame.Length; i += 3)
        {
            if (frame[i] < 64) black++;
            else if (frame[i] > 192) white++;
            else other++;
        }
        Debug.Log($"frame histogram: black={black} white={white} mid={other} total={frame.Length / 3}");

        // 把标所在区域的中心横扫一行打出来，看是不是真有黑白结构。
        var scan = new System.Text.StringBuilder("center row scan: ");
        int y = ImageHeight / 2;
        for (int x = ImageWidth / 2 - 60; x < ImageWidth / 2 + 60; x += 2)
        {
            scan.Append(frame[(y * ImageWidth + x) * 3] > 127 ? '.' : '#');
        }
        Debug.Log(scan.ToString());
    }

    // ---- 合成渲染 ----

    /// <summary>
    /// 反向映射渲染：对每个相机像素反投影到 marker 平面，再采样标图。
    /// 正向 splat 会留洞，反向不会。
    ///
    /// 输出 RGB32（4 字节/像素），与官方 CameraRendering 样例 / 生产路径
    /// <c>SetCameraFrameBufferfor4U</c> 一致（design D13）。VST 去畸变拉取（RGB24）已关闭。
    /// </summary>
    static byte[] RenderFrame(Matrix4x4 markerToCamera)
    {
        var frame = new byte[ImageWidth * ImageHeight * 4];

        Vector3 origin = markerToCamera.MultiplyPoint3x4(Vector3.zero);
        Vector3 axisX = markerToCamera.MultiplyVector(Vector3.right);
        Vector3 axisY = markerToCamera.MultiplyVector(Vector3.up);
        Vector3 normal = markerToCamera.MultiplyVector(Vector3.forward);

        float halfImageMeters = TagImageMeters * 0.5f;
        float planeOffset = Vector3.Dot(normal, origin);
        float sampleStep = 1f / SuperSample;

        for (int y = 0; y < ImageHeight; y++)
        {
            for (int x = 0; x < ImageWidth; x++)
            {
                int accumulated = 0;
                for (int sy = 0; sy < SuperSample; sy++)
                {
                    for (int sx = 0; sx < SuperSample; sx++)
                    {
                        float pixelX = x + (sx + 0.5f) * sampleStep;
                        float pixelY = y + (sy + 0.5f) * sampleStep;
                        var ray = new Vector3((pixelX - Cx) / Fx, (pixelY - Cy) / Fy, 1f);

                        float denominator = Vector3.Dot(normal, ray);
                        // 视线与标面平行：射不中，算背景。
                        if (Mathf.Abs(denominator) < 1e-6f)
                        {
                            accumulated += 255;
                            continue;
                        }

                        Vector3 hit = ray * (planeOffset / denominator);
                        // 命中点在标背后（标转到侧面时会出现）同样算背景。
                        if (hit.z <= 0f)
                        {
                            accumulated += 255;
                            continue;
                        }

                        Vector3 local = hit - origin;
                        float u = Vector3.Dot(local, axisX);
                        float v = Vector3.Dot(local, axisY);

                        accumulated += SampleTag(u, v, halfImageMeters);
                    }
                }

                int gray = accumulated / (SuperSample * SuperSample);
                int index = (y * ImageWidth + x) * 4;
                frame[index] = (byte)gray;
                frame[index + 1] = (byte)gray;
                frame[index + 2] = (byte)gray;
                frame[index + 3] = 255;
            }
        }

        return frame;
    }

    /// <summary>标图外面是白色静区。标本身外两圈是黑的（reversed_border），没有静区就检不出来。</summary>
    static int SampleTag(float u, float v, float halfImageMeters)
    {
        if (u < -halfImageMeters || u > halfImageMeters ||
            v < -halfImageMeters || v > halfImageMeters)
        {
            return 255;
        }

        int column = Mathf.Clamp((int)((u + halfImageMeters) / ModuleMeters), 0, tagImageWidth - 1);
        // v 已经与标图行序同向：投影用的是 OpenCV 约定（图像 +Y 向下），单位旋转下
        // marker 局部 +Y 就是图像向下，和 PlanarPoseSolverTests.Project 一致。
        // 这里再翻一次就成了**镜像**而不是旋转，apriltag 解不了镜像的码。
        int row = Mathf.Clamp((int)((v + halfImageMeters) / ModuleMeters), 0, tagImageHeight - 1);
        return tagImage[row * tagImageWidth + column];
    }

    // ---- apriltag_to_image：包里没包装，这里自己 P/Invoke ----

    [DllImport("AprilTag", EntryPoint = "apriltag_to_image")]
    static extern IntPtr ApriltagToImage(IntPtr family, uint index);

    [DllImport("AprilTag", EntryPoint = "image_u8_destroy")]
    static extern void ImageU8Destroy(IntPtr image);

    /// <summary>
    /// 用库自己渲染标图作为真值来源，避免手抄码表。
    /// image_u8_t 的布局是 { int width; int height; int stride; uint8_t *buf; }。
    /// </summary>
    static byte[] RenderTagToBytes(Family family, int id, out int width, out int height)
    {
        IntPtr native = ApriltagToImage(family.DangerousGetHandle(), (uint)id);
        Assert.AreNotEqual(IntPtr.Zero, native, "apriltag_to_image 返回空");

        try
        {
            width = Marshal.ReadInt32(native, 0);
            height = Marshal.ReadInt32(native, 4);
            int stride = Marshal.ReadInt32(native, 8);
            IntPtr buffer = Marshal.ReadIntPtr(native, IntPtr.Size == 8 ? 16 : 12);

            var pixels = new byte[width * height];
            var row = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(buffer + y * stride, row, 0, stride);
                Array.Copy(row, 0, pixels, y * width, width);
            }

            return pixels;
        }
        finally
        {
            ImageU8Destroy(native);
        }
    }
}
