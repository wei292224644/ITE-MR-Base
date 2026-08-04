using NUnit.Framework;
using UnityEngine;
using Uality.IteTour.Data;
using Uality.IteTour.Internal;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// ITE 内容以右手系导出，进 Unity 前要转换。这组是**表征测试**：
    /// 期望值全部从源工程的实现手工推导，而不是从迁移后的实现反推——
    /// 否则搬运时抄错一行，测试照样是绿的。
    /// </summary>
    public class CoordinateConversionTests
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public void ConvertToLeftHanded_LeavesIdentityUnchanged()
        {
            var result = Matrix4x4.identity.ConvertToLeftHanded();

            Assert.That(result.GetPosition().magnitude, Is.LessThan(Tolerance));
            Assert.That(Quaternion.Angle(result.rotation, Quaternion.identity), Is.LessThan(Tolerance));
            Assert.That((result.lossyScale - Vector3.one).magnitude, Is.LessThan(Tolerance));
        }

        // flipZ * M * flipZ 把平移的 z 取反。
        [Test]
        public void ConvertToLeftHanded_NegatesTranslationZ()
        {
            var source = Matrix4x4.TRS(new Vector3(1f, 2f, 3f), Quaternion.identity, Vector3.one);

            var result = source.ConvertToLeftHanded();

            Assert.That(result.GetPosition(), Is.EqualTo(new Vector3(1f, 2f, -3f)).Using(Vector3Comparer));
        }

        // FlipRotY 是右乘一个绕 Y 的 180 度旋转，不动平移。
        [Test]
        public void FlipRotY_AddsHalfTurnAroundYWithoutMovingPosition()
        {
            var source = Matrix4x4.TRS(new Vector3(1f, 2f, 3f), Quaternion.identity, Vector3.one);

            var result = source.FlipRotY();

            Assert.That(result.GetPosition(), Is.EqualTo(new Vector3(1f, 2f, 3f)).Using(Vector3Comparer));
            Assert.That(Quaternion.Angle(result.rotation, Quaternion.Euler(0f, 180f, 0f)), Is.LessThan(Tolerance));
        }

        [Test]
        public void TourMatrix_ParsesColumnMajorArrayAndNegatesTranslationZ()
        {
            var tour = new IteSpaceScene.Tour
            {
                // 每个子数组是一列；第 3 列是平移
                transform = new[]
                {
                    new[] { 1f, 0f, 0f, 0f },
                    new[] { 0f, 1f, 0f, 0f },
                    new[] { 0f, 0f, 1f, 0f },
                    new[] { 1f, 2f, 3f, 1f },
                }
            };

            var matrix = tour.Matrix4X4;

            Assert.That(matrix.GetPosition(), Is.EqualTo(new Vector3(1f, 2f, -3f)).Using(Vector3Comparer));
            Assert.That(Quaternion.Angle(matrix.rotation, Quaternion.identity), Is.LessThan(Tolerance));
        }

        [Test]
        public void EntityMatrix_NegatesTranslationZAndAddsHalfTurnAroundY()
        {
            var entity = new Entity
            {
                pos = new Vector3(1f, 2f, 3f),
                rot = Vector3.zero,
                scale = Vector3.one,
            };

            var matrix = entity.Matrix4X4;

            Assert.That(matrix.GetPosition(), Is.EqualTo(new Vector3(1f, 2f, -3f)).Using(Vector3Comparer));
            Assert.That(Quaternion.Angle(matrix.rotation, Quaternion.Euler(0f, 180f, 0f)), Is.LessThan(Tolerance));
        }

        /// <summary>
        /// 锁定一处**已知的既有不一致**：同样的位姿输入，Tour 与 Entity 得到的
        /// 朝向差 180 度，因为 Entity 多做了一次 FlipRotY。
        ///
        /// 这不是本次引入的缺陷，迁移前就是这样，行为等价优先所以原样保留。
        /// 写成测试是为了：将来真要统一时，是一次有意的决定 + 一个变红的测试，
        /// 而不是有人顺手"修好了"却没人发现内容位姿全变了。
        /// </summary>
        [Test]
        public void TourAndEntity_DisagreeOnRotationByHalfTurn_KnownLegacyInconsistency()
        {
            var tour = new IteSpaceScene.Tour
            {
                transform = new[]
                {
                    new[] { 1f, 0f, 0f, 0f },
                    new[] { 0f, 1f, 0f, 0f },
                    new[] { 0f, 0f, 1f, 0f },
                    new[] { 1f, 2f, 3f, 1f },
                }
            };
            var entity = new Entity
            {
                pos = new Vector3(1f, 2f, 3f),
                rot = Vector3.zero,
                scale = Vector3.one,
            };

            Assert.That(tour.Matrix4X4.GetPosition(),
                Is.EqualTo(entity.Matrix4X4.GetPosition()).Using(Vector3Comparer),
                "两者的平移换算本来就应该一致");

            Assert.That(Quaternion.Angle(tour.Matrix4X4.rotation, entity.Matrix4X4.rotation),
                Is.EqualTo(180f).Within(0.01f),
                "Entity 比 Tour 多一次 FlipRotY，这是迁移前既有的不一致");
        }

        private static readonly System.Collections.Generic.IComparer<Vector3> Vector3Comparer =
            new Vector3ApproximateComparer(Tolerance);

        private class Vector3ApproximateComparer : System.Collections.Generic.IComparer<Vector3>
        {
            private readonly float _tolerance;

            public Vector3ApproximateComparer(float tolerance) => _tolerance = tolerance;

            public int Compare(Vector3 a, Vector3 b) => (a - b).magnitude < _tolerance ? 0 : 1;
        }
    }
}
