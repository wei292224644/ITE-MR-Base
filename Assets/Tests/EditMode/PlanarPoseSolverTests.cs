using NUnit.Framework;
using UnityEngine;

/// <summary>
/// PlanarPoseSolver 的合成角点测试。
///
/// 每个用例都先假定一个已知的 markerToCamera，把 marker 平面上的模型点投影成像素点，
/// 再把像素点喂回求解器，看它能不能把那个已知位姿还原出来。真机无关，纯数值。
///
/// 内参用的是 PICO 4 Ultra Enterprise 640x480 左目的实测值
/// （GetCameraParametersNewfor4U，2026-08-14 真机日志）。
/// </summary>
public class PlanarPoseSolverTests
{
    const float Fx = 407.01f;
    const float Fy = 407.03f;
    const float Cx = 319.50f;
    const float Cy = 239.50f;

    // 夹具 QR 外框 160 mm，四角相对码中心。
    static readonly Vector2[] SquareModel =
    {
        new Vector2(-0.08f, 0.08f),
        new Vector2(0.08f, 0.08f),
        new Vector2(0.08f, -0.08f),
        new Vector2(-0.08f, -0.08f)
    };

    /// <summary>
    /// 针孔投影。相机看向 +Z，图像 +Y 向下（OpenCV 约定，与 PICO 给的内参一致）。
    /// </summary>
    static Vector2 Project(Matrix4x4 markerToCamera, Vector2 modelPoint)
    {
        Vector3 camera = markerToCamera.MultiplyPoint3x4(new Vector3(modelPoint.x, modelPoint.y, 0f));
        return new Vector2(
            Fx * camera.x / camera.z + Cx,
            Fy * camera.y / camera.z + Cy);
    }

    static Vector2[] ProjectAll(Matrix4x4 markerToCamera, Vector2[] modelPoints)
    {
        var projected = new Vector2[modelPoints.Length];
        for (int i = 0; i < modelPoints.Length; i++)
        {
            projected[i] = Project(markerToCamera, modelPoints[i]);
        }

        return projected;
    }

    [Test]
    public void TrySolve_WithFrontalSquareAtOneMeter_RecoversTranslation()
    {
        Matrix4x4 truth = Matrix4x4.TRS(new Vector3(0f, 0f, 1f), Quaternion.identity, Vector3.one);
        Vector2[] imagePoints = ProjectAll(truth, SquareModel);

        bool solved = PlanarPoseSolver.TrySolve(
            SquareModel, imagePoints, Fx, Fy, Cx, Cy, out Matrix4x4 markerToCamera);

        Assert.IsTrue(solved, "四个非共线点应当可解。");
        Vector3 origin = markerToCamera.MultiplyPoint3x4(Vector3.zero);
        Assert.AreEqual(0f, origin.x, 0.001f);
        Assert.AreEqual(0f, origin.y, 0.001f);
        Assert.AreEqual(1f, origin.z, 0.001f);
    }

    [Test]
    public void TrySolve_WithObliqueView_RecoversRotationAndTranslation()
    {
        Matrix4x4 truth = Matrix4x4.TRS(
            new Vector3(0.12f, -0.06f, 0.85f),
            Quaternion.Euler(15f, 25f, -10f),
            Vector3.one);
        Vector2[] imagePoints = ProjectAll(truth, SquareModel);

        bool solved = PlanarPoseSolver.TrySolve(
            SquareModel, imagePoints, Fx, Fy, Cx, Cy, out Matrix4x4 markerToCamera);

        Assert.IsTrue(solved);
        Vector3 origin = markerToCamera.MultiplyPoint3x4(Vector3.zero);
        Assert.AreEqual(0.12f, origin.x, 0.001f, "位置 x");
        Assert.AreEqual(-0.06f, origin.y, 0.001f, "位置 y");
        Assert.AreEqual(0.85f, origin.z, 0.001f, "位置 z");

        for (int column = 0; column < 3; column++)
        {
            Vector3 expected = truth.GetColumn(column);
            Vector3 actual = markerToCamera.GetColumn(column);
            Assert.GreaterOrEqual(
                Vector3.Dot(expected.normalized, actual.normalized),
                0.9995f,
                $"旋转第 {column} 列偏差过大");
        }
    }

    [Test]
    public void TrySolve_WithCollinearPoints_ReturnsFalse()
    {
        Vector2[] collinearModel =
        {
            new Vector2(-0.08f, 0f),
            new Vector2(-0.02f, 0f),
            new Vector2(0.02f, 0f),
            new Vector2(0.08f, 0f)
        };
        Matrix4x4 truth = Matrix4x4.TRS(new Vector3(0f, 0f, 1f), Quaternion.identity, Vector3.one);
        Vector2[] imagePoints = ProjectAll(truth, collinearModel);

        bool solved = PlanarPoseSolver.TrySolve(
            collinearModel, imagePoints, Fx, Fy, Cx, Cy, out _);

        Assert.IsFalse(solved, "共线点定不出平面位姿，必须拒绝而不是给个看似合理的解。");
    }

    [Test]
    public void TrySolve_WithFewerThanFourPoints_ReturnsFalse()
    {
        var threePoints = new[] { SquareModel[0], SquareModel[1], SquareModel[2] };
        Matrix4x4 truth = Matrix4x4.TRS(new Vector3(0f, 0f, 1f), Quaternion.identity, Vector3.one);
        Vector2[] imagePoints = ProjectAll(truth, threePoints);

        bool solved = PlanarPoseSolver.TrySolve(
            threePoints, imagePoints, Fx, Fy, Cx, Cy, out _);

        Assert.IsFalse(solved, "单应需要 4 个点；QR Version 1 只有 3 个 finder 中心，必须在这里拦住。");
    }

    [Test]
    public void ToUnityCameraSpace_WithFrontalMarker_FacesTheCamera()
    {
        // 模型点按 +Y 朝上定义（marker 自己的朝向），而 OpenCV 图像 +Y 朝下，
        // 所以正视时解出的 R 是绕 X 转 180°，转换必须把这一下抵消掉。
        Matrix4x4 truth = Matrix4x4.TRS(
            new Vector3(0f, 0f, 1f),
            Quaternion.Euler(180f, 0f, 0f),
            Vector3.one);
        Vector2[] imagePoints = ProjectAll(truth, SquareModel);
        Assert.IsTrue(PlanarPoseSolver.TrySolve(
            SquareModel, imagePoints, Fx, Fy, Cx, Cy, out Matrix4x4 markerToCamera));

        Pose pose = PlanarPoseSolver.ToUnityCameraSpace(markerToCamera);

        Assert.AreEqual(0f, pose.position.x, 0.001f);
        Assert.AreEqual(0f, pose.position.y, 0.001f);
        Assert.AreEqual(1f, pose.position.z, 0.001f, "marker 应在相机正前方 1 m");

        // 正对相机：法线指回相机（-Z），marker 的上方就是 Unity 的上方。
        Assert.GreaterOrEqual(Vector3.Dot(pose.forward, Vector3.back), 0.999f, "法线应指回相机");
        Assert.GreaterOrEqual(Vector3.Dot(pose.up, Vector3.up), 0.999f, "marker 上方应为世界上方");
    }

    /// <summary>
    /// 真机上角点定位不可能是精确的，误差主要来自这里而不是求解器。
    /// 这个用例把 ±0.5 px 的定位噪声灌进去，量出位置误差随距离怎么涨——
    /// 它是"这套方案精度够不够"的判据，数值打进日志供归档。
    /// </summary>
    // 预算是实测值留一点余量后钉下来的回归护栏，不是设计目标：
    // 0.6 m 均值 2.2 / 最大 6.6 mm；1.0 m 均值 9.8 / 最大 31.7 mm；2.0 m 均值 79.2 / 最大 290.3 mm。
    // 误差随距离约按平方增长（ΔZ ∝ Z²·σ /(f·L)），改内参、改码尺寸或改角点精度都会动这组数。
    [TestCase(0.6f, 0.010f)]
    [TestCase(1.0f, 0.040f)]
    [TestCase(2.0f, 0.350f)]
    public void TrySolve_WithHalfPixelCornerNoise_KeepsPositionErrorWithinBudget(
        float distance, float positionErrorBudget)
    {
        const int trials = 200;
        const float noisePixels = 0.5f;

        Matrix4x4 truth = Matrix4x4.TRS(
            new Vector3(0f, 0f, distance),
            Quaternion.Euler(10f, 20f, 0f),
            Vector3.one);
        Vector2[] clean = ProjectAll(truth, SquareModel);
        Vector3 truthOrigin = truth.MultiplyPoint3x4(Vector3.zero);

        // 固定种子：精度数字要可复现，否则没法跨轮次比较。
        var random = new System.Random(20260814);
        var noisy = new Vector2[clean.Length];
        float worstError = 0f;
        double totalError = 0.0;

        for (int trial = 0; trial < trials; trial++)
        {
            for (int i = 0; i < clean.Length; i++)
            {
                noisy[i] = clean[i] + new Vector2(
                    (float)(random.NextDouble() * 2.0 - 1.0) * noisePixels,
                    (float)(random.NextDouble() * 2.0 - 1.0) * noisePixels);
            }

            bool solved = PlanarPoseSolver.TrySolve(
                SquareModel, noisy, Fx, Fy, Cx, Cy, out Matrix4x4 markerToCamera);
            Assert.IsTrue(solved, "噪声不应让求解失败。");

            float error = Vector3.Distance(markerToCamera.MultiplyPoint3x4(Vector3.zero), truthOrigin);
            totalError += error;
            worstError = Mathf.Max(worstError, error);
        }

        Debug.Log(
            $"[PlanarPoseSolver] distance={distance:F1}m noise=±{noisePixels}px " +
            $"mean={totalError / trials * 1000.0:F1}mm max={worstError * 1000f:F1}mm");

        Assert.Less(worstError, positionErrorBudget, $"{distance:F1} m 处最大位置误差超预算");
    }
}
