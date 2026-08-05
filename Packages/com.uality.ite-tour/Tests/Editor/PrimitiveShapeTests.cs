using NUnit.Framework;
using UnityEngine;
using Uality.IteTour.Components;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 几何体元素的形状与尺寸。源实现把「什么形状」这一个决策**查了两次**：
    /// <c>GetPrimitiveType()</c> 的 switch 把未知类型兜底成 Cube，而设置缩放的那个
    /// switch 对未知类型只打警告、不设缩放。两处对同一决策的口径不一致（design D19）。
    ///
    /// 这里收敛成一次 <c>Resolve</c> + 两个投影，行为与源实现逐点一致。
    /// </summary>
    public class PrimitiveShapeTests
    {
        private static PrimitiveModelRender Data(string type) => new PrimitiveModelRender
        {
            PrimitiveType = type,
            boxWidth = 2, boxHeight = 3, boxDepth = 4,
            planeWidth = 5, planeHeight = 6,
            sphereRadius = 7,
        };

        [TestCase("box", PrimitiveShape.Box)]
        [TestCase("plane", PrimitiveShape.Plane)]
        [TestCase("sphere", PrimitiveShape.Sphere)]
        [TestCase("BOX", PrimitiveShape.Box)]
        [TestCase("cylinder", PrimitiveShape.Unsupported)]
        [TestCase("", PrimitiveShape.Unsupported)]
        [TestCase(null, PrimitiveShape.Unsupported)]
        public void Resolve_MapsTypeStringCaseInsensitively(string type, PrimitiveShape expected)
        {
            Assert.That(PrimitiveShapes.Resolve(type), Is.EqualTo(expected));
        }

        [TestCase(PrimitiveShape.Box, PrimitiveType.Cube)]
        [TestCase(PrimitiveShape.Plane, PrimitiveType.Plane)]
        [TestCase(PrimitiveShape.Sphere, PrimitiveType.Sphere)]
        [TestCase(PrimitiveShape.Unsupported, PrimitiveType.Cube)]
        public void ToUnity_FallsBackToCube(PrimitiveShape shape, PrimitiveType expected)
        {
            Assert.That(PrimitiveShapes.ToUnity(shape), Is.EqualTo(expected));
        }

        [Test]
        public void TryGetLocalScale_Box_IsWidthHeightDepth()
        {
            Assert.That(PrimitiveShapes.TryGetLocalScale(PrimitiveShape.Box, Data("box"), out var scale), Is.True);
            Assert.That(scale, Is.EqualTo(new Vector3(2, 3, 4)));
        }

        /// <summary>plane 的高映射到 Z，Y 恒为 1 —— Unity 的 Plane 躺在 XZ 面上。</summary>
        [Test]
        public void TryGetLocalScale_Plane_MapsHeightToZAndPinsYToOne()
        {
            Assert.That(PrimitiveShapes.TryGetLocalScale(PrimitiveShape.Plane, Data("plane"), out var scale), Is.True);
            Assert.That(scale, Is.EqualTo(new Vector3(5, 1, 6)));
        }

        [Test]
        public void TryGetLocalScale_Sphere_IsUniformRadius()
        {
            Assert.That(PrimitiveShapes.TryGetLocalScale(PrimitiveShape.Sphere, Data("sphere"), out var scale), Is.True);
            Assert.That(scale, Is.EqualTo(new Vector3(7, 7, 7)));
        }

        /// <summary>
        /// 未知形状**不设缩放**（返回 false），与源实现一致 —— 它对未知类型只打警告，
        /// 于是 Cube 保持 (1,1,1)。这是「兜底成 Cube」与「不改缩放」的组合，
        /// 现在由同一个 <c>Resolve</c> 结果驱动，不再是两处 switch 各说各话。
        /// </summary>
        [Test]
        public void TryGetLocalScale_Unsupported_LeavesScaleAlone()
        {
            Assert.That(
                PrimitiveShapes.TryGetLocalScale(PrimitiveShape.Unsupported, Data("cylinder"), out _), Is.False);
        }
    }
}
