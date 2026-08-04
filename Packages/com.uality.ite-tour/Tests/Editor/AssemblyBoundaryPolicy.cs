using System;
using System.Collections.Generic;
using System.Linq;

namespace Uality.IteTour.Tests
{
    /// <summary>
    /// 判定一组被引用的程序集名里，哪些违反了本包的可移植性约束。
    /// 做成纯函数是为了它自己可测——直接对真实程序集断言的话，
    /// 测试绿了也无法区分"确实干净"和"检测逻辑坏了"。
    /// </summary>
    internal static class AssemblyBoundaryPolicy
    {
        private static readonly string[] ForbiddenFragments =
        {
            "MRBase",   // 宿主程序集
            "Oculus",   // Meta / Quest
            "Meta.XR",
            "PICO",
            "PXR",
        };

        public static IReadOnlyList<string> FindViolations(IEnumerable<string> referencedAssemblyNames)
        {
            return referencedAssemblyNames
                .Where(name => ForbiddenFragments.Any(
                    fragment => name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
        }
    }
}
