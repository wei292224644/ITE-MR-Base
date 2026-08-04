using NUnit.Framework;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 守卫包的可移植性。
    ///
    /// Unity 的 Packages→Assets 编译禁令挡住了"引用宿主程序集"这条路，
    /// 但挡不住"引用另一个平台**包**"—— `com.meta.xr.sdk.core` 就是包，
    /// 把它加进 asmdef 能编译通过，却会同时毁掉可移植性与 PICO 支持。
    /// 这组测试补的就是编译器管不到的那一半。
    /// </summary>
    public class PackageBoundaryTests
    {
        [Test]
        public void FindViolations_FlagsHostAssemblyReference()
        {
            var violations = AssemblyBoundaryPolicy.FindViolations(new[] { "UnityEngine", "MRBase.Common" });

            Assert.That(violations, Is.EquivalentTo(new[] { "MRBase.Common" }));
        }
    }
}
