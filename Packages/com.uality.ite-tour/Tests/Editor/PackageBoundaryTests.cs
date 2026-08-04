using System.Linq;
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

        [Test]
        public void FindViolations_FlagsPlatformSdkReferences()
        {
            var violations = AssemblyBoundaryPolicy.FindViolations(new[]
            {
                "UnityEngine", "Oculus.VR", "Unity.XR.PICO", "Meta.XR.Sdk", "PXR.TobSupport"
            });

            Assert.That(violations, Is.EquivalentTo(new[]
            {
                "Oculus.VR", "Unity.XR.PICO", "Meta.XR.Sdk", "PXR.TobSupport"
            }));
        }

        // 大小写敏感的守卫等于没有守卫：程序集名的大小写写法不受约束，
        // 漏判时测试照样是绿的，而可移植性已经没了。
        [Test]
        public void FindViolations_IsCaseInsensitiveAndLeavesCleanNamesAlone()
        {
            var violations = AssemblyBoundaryPolicy.FindViolations(new[]
            {
                "Unity.XR.Pico", "oculus.vr",
                "glTFast", "Unity.SharpZipLib.Utils", "UnityEngine.UI", "UnityEngine.CoreModule"
            });

            Assert.That(violations, Is.EquivalentTo(new[] { "Unity.XR.Pico", "oculus.vr" }));
        }

        // 上面三个测试证明了检测器有效，这个才是真正的守卫：把它用在真实程序集上。
        [Test]
        public void RuntimeAssembly_ReferencesNoHostOrPlatformAssemblies()
        {
            var runtimeAssembly = typeof(Uality.IteTour.Internal.EventEmitter).Assembly;
            var referencedNames = runtimeAssembly.GetReferencedAssemblies().Select(a => a.Name);

            var violations = AssemblyBoundaryPolicy.FindViolations(referencedNames);

            Assert.That(violations, Is.Empty,
                "Uality.IteTour 引用了会破坏可移植性的程序集: " + string.Join(", ", violations));
        }
    }
}
