using UnityEngine;

/// <summary>
/// 共面点单目位姿求解：已知 marker 上若干共面点的物理坐标和它们在图像上的像素坐标，
/// 解出 marker 到相机的刚体变换。
///
/// 尺度来自 marker 的物理尺寸，不需要深度传感器——这正是 PICO 端不能照搬 Quest
/// 环境射线方案（依赖房间扫描）的替代路径。
///
/// 相机约定为 OpenCV：光轴 +Z，图像 +X 向右、+Y 向下，
/// u = fx·X/Z + cx，v = fy·Y/Z + cy。PICO 的 GetCameraParametersNewfor4U 给的就是这套内参。
/// 转 Unity 相机坐标系是调用方的事，本类只做纯 CV 数学。
/// </summary>
public static class PlanarPoseSolver
{
    /// <param name="modelPoints">marker 局部平面坐标（米），z 恒为 0。</param>
    /// <param name="imagePoints">对应的像素坐标，顺序与 modelPoints 一致。</param>
    /// <param name="markerToCamera">marker → 相机的变换。</param>
    /// <returns>点数不足、数量不匹配或退化（共线）时返回 false。</returns>
    public static bool TrySolve(
        Vector2[] modelPoints,
        Vector2[] imagePoints,
        float fx,
        float fy,
        float cx,
        float cy,
        out Matrix4x4 markerToCamera)
    {
        markerToCamera = Matrix4x4.identity;

        if (modelPoints == null || imagePoints == null)
        {
            return false;
        }

        int count = modelPoints.Length;
        if (count < 4 || imagePoints.Length != count)
        {
            return false;
        }

        if (fx <= 0f || fy <= 0f)
        {
            return false;
        }

        if (!TrySolveHomography(modelPoints, imagePoints, count, out double[] h))
        {
            return false;
        }

        // G = K⁻¹H。K⁻¹ 是上三角，直接按元素展开，不建矩阵类。
        double g00 = (h[0] - cx * h[6]) / fx;
        double g01 = (h[1] - cx * h[7]) / fx;
        double g02 = (h[2] - cx * h[8]) / fx;
        double g10 = (h[3] - cy * h[6]) / fy;
        double g11 = (h[4] - cy * h[7]) / fy;
        double g12 = (h[5] - cy * h[8]) / fy;
        double g20 = h[6];
        double g21 = h[7];
        double g22 = h[8];

        var column1 = new Vector3d(g00, g10, g20);
        var column2 = new Vector3d(g01, g11, g21);
        var translation = new Vector3d(g02, g12, g22);

        double norm1 = column1.Magnitude;
        double norm2 = column2.Magnitude;
        if (norm1 < 1e-12 || norm2 < 1e-12)
        {
            return false;
        }

        // H 只定到一个尺度因子。R 的前两列都是单位长度，取两者平均把这个因子定下来。
        double scale = 2.0 / (norm1 + norm2);

        // marker 必须在相机前方；符号错了整体取反。
        if (translation.z * scale < 0.0)
        {
            scale = -scale;
        }

        Vector3d r1 = column1 * scale;
        Vector3d r2 = column2 * scale;
        Vector3d t = translation * scale;

        // 数值误差会让 r1/r2 不严格正交，正交化后才是合法旋转矩阵。
        // ponytail: Gram-Schmidt 偏向 r1；若真机精度不够，换对称正交化（极分解）。
        r1 = r1.Normalized;
        r2 = (r2 - r1 * Vector3d.Dot(r2, r1)).Normalized;
        Vector3d r3 = Vector3d.Cross(r1, r2);

        markerToCamera.SetColumn(0, new Vector4((float)r1.x, (float)r1.y, (float)r1.z, 0f));
        markerToCamera.SetColumn(1, new Vector4((float)r2.x, (float)r2.y, (float)r2.z, 0f));
        markerToCamera.SetColumn(2, new Vector4((float)r3.x, (float)r3.y, (float)r3.z, 0f));
        markerToCamera.SetColumn(3, new Vector4((float)t.x, (float)t.y, (float)t.z, 1f));
        return true;
    }

    /// <summary>
    /// 把 <see cref="TrySolve"/> 解出的 OpenCV 相机系变换转成 Unity 相机系的 Pose。
    ///
    /// 两套约定只差图像 Y 轴的朝向（OpenCV 向下、Unity 向上），所以位置和各轴向量
    /// 各翻一次 y 即可。marker 的 +Z 是板面法线，转换后指回相机——挂在这个 Pose 上的物体
    /// 默认就是正对观察者的。
    /// </summary>
    public static Pose ToUnityCameraSpace(Matrix4x4 markerToCamera)
    {
        Vector3 position = FlipY(markerToCamera.MultiplyPoint3x4(Vector3.zero));
        Vector3 up = FlipY(markerToCamera.MultiplyVector(Vector3.up));
        Vector3 normal = FlipY(markerToCamera.MultiplyVector(Vector3.forward));
        return new Pose(position, Quaternion.LookRotation(normal, up));
    }

    static Vector3 FlipY(Vector3 value) => new Vector3(value.x, -value.y, value.z);

    /// <summary>
    /// DLT 求单应矩阵，行主序返回 9 个元素。
    ///
    /// 两侧都先做 Hartley 归一化（质心移到原点、平均距离缩到 √2）。像素坐标量级是几百，
    /// 不归一化时法方程的条件数会差几个数量级，斜视角下解直接发散。
    /// </summary>
    static bool TrySolveHomography(
        Vector2[] modelPoints,
        Vector2[] imagePoints,
        int count,
        out double[] homography)
    {
        homography = null;

        Normalize(modelPoints, count, out double modelScale, out double modelCenterX, out double modelCenterY);
        Normalize(imagePoints, count, out double imageScale, out double imageCenterX, out double imageCenterY);
        if (modelScale <= 0.0 || imageScale <= 0.0)
        {
            return false;
        }

        // h33 固定为 1，8 个未知量。用法方程 AᵀA·h = Aᵀb 统一处理 4 点精确解和多点最小二乘。
        // ponytail: h33≈0（marker 平面过相机光心）时该参数化会失效，实际取景中不会发生。
        var normalEquations = new double[8, 9];
        var row = new double[8];

        for (int i = 0; i < count; i++)
        {
            double x = (modelPoints[i].x - modelCenterX) * modelScale;
            double y = (modelPoints[i].y - modelCenterY) * modelScale;
            double u = (imagePoints[i].x - imageCenterX) * imageScale;
            double v = (imagePoints[i].y - imageCenterY) * imageScale;

            row[0] = x; row[1] = y; row[2] = 1.0;
            row[3] = 0.0; row[4] = 0.0; row[5] = 0.0;
            row[6] = -u * x; row[7] = -u * y;
            Accumulate(normalEquations, row, u);

            row[0] = 0.0; row[1] = 0.0; row[2] = 0.0;
            row[3] = x; row[4] = y; row[5] = 1.0;
            row[6] = -v * x; row[7] = -v * y;
            Accumulate(normalEquations, row, v);
        }

        if (!TrySolveLinearSystem(normalEquations, out double[] solution))
        {
            return false;
        }

        // 归一化空间的 H，再左乘 T_image⁻¹、右乘 T_model 还原到原始像素/物理坐标。
        double[] normalized =
        {
            solution[0], solution[1], solution[2],
            solution[3], solution[4], solution[5],
            solution[6], solution[7], 1.0
        };

        homography = Denormalize(
            normalized,
            modelScale, modelCenterX, modelCenterY,
            imageScale, imageCenterX, imageCenterY);
        return true;
    }

    static void Normalize(Vector2[] points, int count, out double scale, out double centerX, out double centerY)
    {
        centerX = 0.0;
        centerY = 0.0;
        for (int i = 0; i < count; i++)
        {
            centerX += points[i].x;
            centerY += points[i].y;
        }

        centerX /= count;
        centerY /= count;

        double meanDistance = 0.0;
        for (int i = 0; i < count; i++)
        {
            double dx = points[i].x - centerX;
            double dy = points[i].y - centerY;
            meanDistance += System.Math.Sqrt(dx * dx + dy * dy);
        }

        meanDistance /= count;
        scale = meanDistance > 1e-12 ? System.Math.Sqrt(2.0) / meanDistance : 0.0;
    }

    static void Accumulate(double[,] normalEquations, double[] row, double target)
    {
        for (int i = 0; i < 8; i++)
        {
            double value = row[i];
            if (value == 0.0)
            {
                // 一半的行天然是稀疏的，跳过省一半乘法。
                continue;
            }

            for (int j = 0; j < 8; j++)
            {
                normalEquations[i, j] += value * row[j];
            }

            normalEquations[i, 8] += value * target;
        }
    }

    /// <summary>8×9 增广矩阵的高斯消元，列主元。</summary>
    static bool TrySolveLinearSystem(double[,] augmented, out double[] solution)
    {
        solution = new double[8];

        for (int column = 0; column < 8; column++)
        {
            int pivotRow = column;
            double pivotMagnitude = System.Math.Abs(augmented[column, column]);
            for (int candidate = column + 1; candidate < 8; candidate++)
            {
                double magnitude = System.Math.Abs(augmented[candidate, column]);
                if (magnitude > pivotMagnitude)
                {
                    pivotMagnitude = magnitude;
                    pivotRow = candidate;
                }
            }

            // 主元消失即为退化输入（共线点、重复点）。
            if (pivotMagnitude < 1e-12)
            {
                return false;
            }

            if (pivotRow != column)
            {
                for (int j = column; j < 9; j++)
                {
                    (augmented[column, j], augmented[pivotRow, j]) = (augmented[pivotRow, j], augmented[column, j]);
                }
            }

            double pivot = augmented[column, column];
            for (int candidate = column + 1; candidate < 8; candidate++)
            {
                double factor = augmented[candidate, column] / pivot;
                if (factor == 0.0)
                {
                    continue;
                }

                for (int j = column; j < 9; j++)
                {
                    augmented[candidate, j] -= factor * augmented[column, j];
                }
            }
        }

        for (int i = 7; i >= 0; i--)
        {
            double accumulator = augmented[i, 8];
            for (int j = i + 1; j < 8; j++)
            {
                accumulator -= augmented[i, j] * solution[j];
            }

            solution[i] = accumulator / augmented[i, i];
        }

        return true;
    }

    static double[] Denormalize(
        double[] normalized,
        double modelScale, double modelCenterX, double modelCenterY,
        double imageScale, double imageCenterX, double imageCenterY)
    {
        // T_model = [[s,0,-s·mx],[0,s,-s·my],[0,0,1]]，先右乘。
        var scaled = new double[9];
        for (int r = 0; r < 3; r++)
        {
            double a = normalized[r * 3];
            double b = normalized[r * 3 + 1];
            double c = normalized[r * 3 + 2];
            scaled[r * 3] = a * modelScale;
            scaled[r * 3 + 1] = b * modelScale;
            scaled[r * 3 + 2] = c - modelScale * (a * modelCenterX + b * modelCenterY);
        }

        // T_image⁻¹ = [[1/s,0,cx],[0,1/s,cy],[0,0,1]]，再左乘。
        var result = new double[9];
        double inverseImageScale = 1.0 / imageScale;
        for (int c = 0; c < 3; c++)
        {
            result[c] = scaled[c] * inverseImageScale + scaled[6 + c] * imageCenterX;
            result[3 + c] = scaled[3 + c] * inverseImageScale + scaled[6 + c] * imageCenterY;
            result[6 + c] = scaled[6 + c];
        }

        return result;
    }

    /// <summary>
    /// 分解过程对精度敏感，Vector3 的 float 会在斜视角下吃掉有效位，这里用 double。
    /// </summary>
    readonly struct Vector3d
    {
        public readonly double x;
        public readonly double y;
        public readonly double z;

        public Vector3d(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public double Magnitude => System.Math.Sqrt(x * x + y * y + z * z);

        public Vector3d Normalized
        {
            get
            {
                double magnitude = Magnitude;
                return magnitude < 1e-12 ? this : this * (1.0 / magnitude);
            }
        }

        public static Vector3d operator *(Vector3d value, double scalar) =>
            new Vector3d(value.x * scalar, value.y * scalar, value.z * scalar);

        public static Vector3d operator -(Vector3d left, Vector3d right) =>
            new Vector3d(left.x - right.x, left.y - right.y, left.z - right.z);

        public static double Dot(Vector3d left, Vector3d right) =>
            left.x * right.x + left.y * right.y + left.z * right.z;

        public static Vector3d Cross(Vector3d left, Vector3d right) =>
            new Vector3d(
                left.y * right.z - left.z * right.y,
                left.z * right.x - left.x * right.z,
                left.x * right.y - left.y * right.x);
    }
}
