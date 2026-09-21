using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MRBase.Build.Editor.Tests
{
    /// <summary>
    /// 目录与 asmdef 约定的机械校验。设计见
    /// docs/superpowers/specs/2026-09-21-asset-layout-governance-design.md
    ///
    /// 性质与 ManifestGuard 相近（都是工程级机械守卫），但**执行强度不同**：
    /// ManifestGuard 每次构建必经，本测试不会自动触发 —— 本仓无 CI、无激活的 git hook，
    /// 构建也不跑 EditMode 测试。动了目录结构 / asmdef / Editor 脚本位置后，
    /// 提交前手动跑 -assemblyNames MRBase.Build.Editor.Tests。
    ///
    /// 只检代码侧 —— 资源摆放判不出对错（一个 .mat 该归哪个 feature，机器不知道），
    /// 硬编规则只会制造假阳性并训练出"随手加豁免"的习惯。
    /// </summary>
    public sealed class LayoutConventionTests
    {
        /// <summary>
        /// 白名单：只有这两棵树受约定管辖。第三方插件目录（INab Studio/、Oculus/、
        /// Samples/、XR/、XRI/、Plugins/、TextMesh Pro/、Resources/）永久在范围外。
        /// 用白名单而非黑名单是为了零维护：以后装任何插件都自动在外，不用回来改清单。
        /// </summary>
        static readonly string[] ScopeRoots = { "_Project", "Scripts" };

        const string WaiverFile = "Scripts/Editor/Tests/EditMode/layout-waivers.txt";

        enum Rule
        {
            /// <summary>没有任何 asmdef 覆盖，落进 Assembly-CSharp。</summary>
            NoAssembly,

            /// <summary>躺在 Editor/ 目录里，却不属于 .Editor 程序集。</summary>
            EditorPathWrongAssembly,

            /// <summary>属于 .Editor 程序集，却不在 Editor/ 目录里。</summary>
            EditorAssemblyNonEditorCode,

            /// <summary>asmdef 是仅编辑器程序集，名字却没有 .Editor / .Tests 后缀。</summary>
            EditorOnlyAssemblyNotNamedEditor,

            /// <summary>asmdef 名字是 .Editor，却不是仅编辑器程序集。</summary>
            EditorNamedAssemblyNotEditorOnly,
        }

        static string AssetsRoot => Application.dataPath.Replace('\\', '/');

        // ---------- 采集 ----------

        /// <summary>白名单内的全部 .cs，路径相对 Assets/，以 / 分隔。</summary>
        static List<string> ScopedScripts()
        {
            var result = new List<string>();
            foreach (var root in ScopeRoots)
            {
                var abs = $"{AssetsRoot}/{root}";
                // _Project/ 可能尚未创建 —— 那是合法状态，不是失败。
                if (!Directory.Exists(abs)) continue;
                foreach (var f in Directory.GetFiles(abs, "*.cs", SearchOption.AllDirectories))
                    result.Add(ToRelative(f));
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>白名单内的全部 .asmdef，路径相对 Assets/，以 / 分隔。</summary>
        static List<string> ScopedAsmdefs()
        {
            var result = new List<string>();
            foreach (var root in ScopeRoots)
            {
                var abs = $"{AssetsRoot}/{root}";
                if (!Directory.Exists(abs)) continue;
                foreach (var f in Directory.GetFiles(abs, "*.asmdef", SearchOption.AllDirectories))
                    result.Add(ToRelative(f));
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        static string ToRelative(string absolute) =>
            absolute.Replace('\\', '/').Substring(AssetsRoot.Length + 1);

        /// <summary>
        /// 复刻 Unity 的归属规则：从文件所在目录向上找最近的 .asmdef，上界是 Assets/ 本身。
        /// 返回 null 表示这个 .cs 落进了 Assembly-CSharp。
        /// </summary>
        static string OwningAssembly(string relativeCs)
        {
            var dir = Path.GetDirectoryName($"{AssetsRoot}/{relativeCs}")?.Replace('\\', '/');
            while (!string.IsNullOrEmpty(dir) && dir.Length >= AssetsRoot.Length)
            {
                var asmdef = Directory
                    .GetFiles(dir, "*.asmdef", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
                if (asmdef != null) return AssemblyNameOf(asmdef);
                if (string.Equals(dir, AssetsRoot, StringComparison.Ordinal)) break;
                dir = Path.GetDirectoryName(dir)?.Replace('\\', '/');
            }
            return null;
        }

        [Serializable]
        class AsmdefStub
        {
            public string name;
            public string[] includePlatforms;
        }

        /// <summary>
        /// 解析 asmdef 并补齐缺省：name 缺省时回退到文件名，includePlatforms 缺省时
        /// JsonUtility 给 null，归一成空数组 —— 空数组在 Unity 里表示"所有平台"。
        /// </summary>
        static AsmdefStub ParseAsmdef(string asmdefPath)
        {
            var stub = JsonUtility.FromJson<AsmdefStub>(File.ReadAllText(asmdefPath))
                       ?? new AsmdefStub();
            if (string.IsNullOrEmpty(stub.name))
                stub.name = Path.GetFileNameWithoutExtension(asmdefPath);
            stub.includePlatforms ??= Array.Empty<string>();
            return stub;
        }

        static string AssemblyNameOf(string asmdefPath) => ParseAsmdef(asmdefPath).name;

        /// <summary>
        /// 仅编辑器程序集 = includePlatforms 恰好只有 Editor。
        /// 空数组表示"所有平台"，不是仅编辑器；["Editor","Android"] 也不是 ——
        /// 那样 Editor 代码会跟着进 Android 包。
        /// </summary>
        static bool IsEditorOnlyPlatform(AsmdefStub stub) =>
            stub.includePlatforms.Length == 1
            && string.Equals(stub.includePlatforms[0], "Editor", StringComparison.Ordinal);

        // 单参数 Contains 就是 Ordinal 语义；不用带 StringComparison 的重载，
        // 那个要 .NET Standard 2.1，随工程的 API Compatibility Level 设置而定。
        static bool IsEditorPath(string relativeCs) =>
            relativeCs.Contains("/Editor/");

        /// <summary>
        /// 测试程序集不参与 Editor 位置检查：它们整体 includePlatforms: ["Editor"]，
        /// 本来就是仅编辑器程序集。条文 2 针对的是 Runtime 代码被误吞进仅编辑器程序集、
        /// 出包时整个 feature 静默消失 —— 这个风险对测试不存在。
        /// （本文件自己就住在 Scripts/Editor/Tests/EditMode/，靠这条豁免。）
        /// </summary>
        static bool IsTestAssembly(string assemblyName) =>
            assemblyName.EndsWith(".Tests", StringComparison.Ordinal);

        static bool IsEditorAssembly(string assemblyName) =>
            assemblyName.EndsWith(".Editor", StringComparison.Ordinal);

        // ---------- 判定 ----------

        /// <summary>未经豁免过滤的全部违规。</summary>
        static List<(string Path, Rule Rule)> RawViolations()
        {
            var list = new List<(string, Rule)>();
            foreach (var cs in ScopedScripts())
            {
                var asm = OwningAssembly(cs);
                if (asm == null)
                {
                    list.Add((cs, Rule.NoAssembly));
                    continue;
                }

                if (IsTestAssembly(asm)) continue;

                if (IsEditorPath(cs) && !IsEditorAssembly(asm))
                    list.Add((cs, Rule.EditorPathWrongAssembly));
                else if (!IsEditorPath(cs) && IsEditorAssembly(asm))
                    list.Add((cs, Rule.EditorAssemblyNonEditorCode));
            }

            // asmdef 自身的自洽性：includePlatforms 与名字必须双向一致。
            // 违规单位是 asmdef 而非它覆盖的每个 .cs —— 一个坏 asmdef 报一条，
            // 豁免也只需写一条。
            foreach (var asmdef in ScopedAsmdefs())
            {
                var stub = ParseAsmdef($"{AssetsRoot}/{asmdef}");
                var editorOnly = IsEditorOnlyPlatform(stub);

                // .Tests 两边都不强制：PlayMode 测试跑在真机上，includePlatforms 本就该是空。
                if (IsTestAssembly(stub.name)) continue;

                if (editorOnly && !IsEditorAssembly(stub.name))
                    list.Add((asmdef, Rule.EditorOnlyAssemblyNotNamedEditor));
                else if (!editorOnly && IsEditorAssembly(stub.name))
                    list.Add((asmdef, Rule.EditorNamedAssemblyNotEditorOnly));
            }

            return list;
        }

        static List<string> Offenders(Rule rule)
        {
            var waivers = Waivers();
            return RawViolations()
                .Where(v => v.Rule == rule)
                .Select(v => v.Path)
                .Where(p => !IsWaived(p, waivers))
                .ToList();
        }

        // ---------- 豁免 ----------

        /// <summary>
        /// 豁免表登记的是"自己的、违规的、将来要还的"。第三方不进此表 —— 它们在白名单
        /// 外，永远不该被管。两者混在一起会让清单失真，一眼看不出哪些是真要还的。
        /// </summary>
        static List<string> Waivers()
        {
            var path = $"{AssetsRoot}/{WaiverFile}";
            if (!File.Exists(path)) return new List<string>();
            return File.ReadAllLines(path)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal))
                .Select(l => l.TrimEnd('/'))
                .ToList();
        }

        static bool IsWaived(string relativeCs, List<string> waivers) =>
            waivers.Any(w => string.Equals(relativeCs, w, StringComparison.Ordinal)
                             || relativeCs.StartsWith(w + "/", StringComparison.Ordinal));

        static string Report(string rule, IEnumerable<string> offenders) =>
            rule + "\n  " + string.Join("\n  ", offenders)
                 + "\n\n约定见 .claude/CLAUDE.md「目录与 asmdef 约定」。"
                 + $"确实要放行就登记到 Assets/{WaiverFile}，一行一条并写明理由。";

        // ---------- 六条规则 ----------

        [Test]
        public void NoScriptFallsIntoAssemblyCSharp()
        {
            var offenders = Offenders(Rule.NoAssembly);
            Assert.That(offenders, Is.Empty, () => Report(
                "以下 .cs 没有被任何 asmdef 覆盖，会落进 Assembly-CSharp。"
                + "Assembly-CSharp 自动引用所有 asmdef 程序集，但反向不行 —— "
                + "任何 asmdef 里的代码都引用不到它们：",
                offenders));
        }

        [Test]
        public void EditorCodeBelongsToEditorAssembly()
        {
            var offenders = Offenders(Rule.EditorPathWrongAssembly);
            Assert.That(offenders, Is.Empty, () => Report(
                "以下 .cs 躺在 Editor/ 目录里，却归属一个非 .Editor 程序集，"
                + "会被打进 Runtime 包并在真机上引用不到 UnityEditor：",
                offenders));
        }

        [Test]
        public void EditorAssemblyHoldsOnlyEditorCode()
        {
            var offenders = Offenders(Rule.EditorAssemblyNonEditorCode);
            Assert.That(offenders, Is.Empty, () => Report(
                "以下 .cs 归属 .Editor 程序集却不在 Editor/ 目录里。"
                + "多半是 asmdef 取名 Foo.Editor 却放在了 feature 根目录 —— "
                + "那样它会把 Runtime 代码一起吞进仅编辑器程序集，出包时整个 feature 静默消失：",
                offenders));
        }

        [Test]
        public void EditorOnlyAssemblyIsNamedEditor()
        {
            var offenders = Offenders(Rule.EditorOnlyAssemblyNotNamedEditor);
            Assert.That(offenders, Is.Empty, () => Report(
                "以下 asmdef 的 includePlatforms 是 [\"Editor\"]（仅编辑器程序集），"
                + "名字却没有 .Editor / .Tests 后缀。它会把自己覆盖的**全部** Runtime 代码"
                + "一起吞进仅编辑器程序集 —— 编辑器里一切正常，出包时整个 feature 静默消失。"
                + "改名加 .Editor 后缀，或把 Runtime 代码挪出它的覆盖范围：",
                offenders));
        }

        [Test]
        public void EditorNamedAssemblyIsEditorOnly()
        {
            var offenders = Offenders(Rule.EditorNamedAssemblyNotEditorOnly);
            Assert.That(offenders, Is.Empty, () => Report(
                "以下 asmdef 名字以 .Editor 结尾，includePlatforms 却不是恰好 [\"Editor\"]。"
                + "Unity 新建 asmdef 时 includePlatforms 默认为空（= 所有平台），"
                + "忘了勾 Editor 就是这个形状 —— Editor 代码会被打进 Runtime 包，"
                + "真机上引用不到 UnityEditor。在 Inspector 里把平台限定为 Editor：",
                offenders));
        }

        [Test]
        public void EveryWaiverIsStillNeeded()
        {
            var waivers = Waivers();
            if (waivers.Count == 0) Assert.Pass("豁免表为空");

            var raw = RawViolations().Select(v => v.Path).ToList();
            var stale = waivers
                .Where(w => !raw.Any(p => string.Equals(p, w, StringComparison.Ordinal)
                                          || p.StartsWith(w + "/", StringComparison.Ordinal)))
                .ToList();

            Assert.That(stale, Is.Empty, () =>
                "以下豁免已经没有对应的违规了 —— 债还完了，把它们从 "
                + $"Assets/{WaiverFile} 删掉，否则表会越积越多、"
                + "没人分得清哪些还欠着：\n  " + string.Join("\n  ", stale));
        }
    }
}
