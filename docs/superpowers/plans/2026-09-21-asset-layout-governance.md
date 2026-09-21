# 目录与 asmdef 约定 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 MR_Base 定下 feature-first 的目录约定，用一个 EditMode 测试把其中机械可判的部分变成硬门禁，并把 `Assets/Tests/` 下的 5 个测试 asmdef 搬到各自模块旁。

**Architecture:** 三件产出互不依赖，按风险从低到高排：先做纯机械的测试搬迁（`AssetDatabase.MoveAsset`，无代码改动），再加校验测试 + 豁免表（豁免表是 ratchet：存量违规登记在册，新增违规才红），最后把约定条文写进 `.claude/CLAUDE.md`。校验采用白名单（只扫 `Assets/_Project` 与 `Assets/Scripts`），第三方插件目录永久在范围外。

**Tech Stack:** Unity 6 (6000.4.4f1)，Unity Test Framework（EditMode / NUnit），`unity` CLI 驱动用户已开的 Editor。

**CLI 备注：** `run_tests` 的参数是 `--mode / --filter / --filter_type / --include_explicit / --async_tests / --timeout`，它是异步命令 —— 若返回的是 job id 就用 `unity command test_status --project-path .` 轮询到结束。`--filter` 传测试名过滤；某次若不接受类名，去掉 `--filter` 跑全量、在结果里找对应用例即可，别卡在参数上。看控制台用 `unity command console --project-path . --tail <n>`（是 `--tail`，不是 `--lines`）。

**Spec:** `docs/superpowers/specs/2026-09-21-asset-layout-governance-design.md`

## Global Constraints

- **所有 Unity 操作走 `unity` CLI 打到用户已开的 Editor**，不起第二个 Unity 进程、不 headless。每个 Task 开工前先确认 `unity status --project-path . ` 为 `ready`；连不上先查 Safe Mode（`unity pipeline list`）。
- **文件移动必须用 `AssetDatabase.MoveAsset`**（即 `unity command move_asset` 或 `unity command eval`），**不要用 `git mv`**。Unity 的文件夹 `.meta` 与同名文件夹不同级（`Assets/Tests/EditMode/Core/` 的 meta 是 `Assets/Tests/EditMode/Core.meta`），手工搬会留下孤儿 meta 并让 Editor 重新生成 GUID。
- **asmdef 名字一个都不改。** `MRBase.Core.Tests`、`MRBase.Ite.Host.Tests`、`MRBase.Localization.Tests`、`MRBase.SacredRelic.Tests`、`MRBase.Transitions.Tests` 搬家后照旧，`-assemblyNames <名>` 不受影响。
- **存量代码与资源不搬。** 本计划只搬测试。`Assets/Scripts/<Module>/` 里的 Runtime 代码、`Assets/IceSpriteFx/` 等资源目录一律原地不动。
- **不碰 `Packages/`、不碰 `openspec/`、不碰任何第三方目录**（`Oculus/`、`INab Studio/`、`Samples/`、`XR/`、`XRI/`、`Plugins/`、`TextMesh Pro/`、`Assets/Resources/`）。
- 校验的白名单是 `Assets/_Project/**` 与 `Assets/Scripts/**`，**其余 `Assets/*` 一律不检**。`Assets/_Project/` 本计划不创建，测试必须容忍它不存在。
- 提交信息用中文，结尾附 `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`。

---

### Task 1: 把 5 个测试 asmdef 搬到各自模块旁

`Assets/Tests/` 整个消失，测试挪到被测代码同一棵树下。落点是 `Assets/Scripts/<Module>/Tests/EditMode/` 而**不是** `Assets/_Project/` —— feature 目录尚不存在，搬进 `_Project/` 会让测试与被测代码分居，比现状更糟。

纯移动，零代码改动。验收标准是**测试总数一个不差**。

**Files:**
- Move: `Assets/Tests/EditMode/Core/` → `Assets/Scripts/Core/Tests/EditMode/`
- Move: `Assets/Tests/EditMode/Ite/` → `Assets/Scripts/IteHost/Tests/EditMode/`
- Move: `Assets/Tests/EditMode/SacredRelic/` → `Assets/Scripts/SacredRelic/Tests/EditMode/`
- Move: `Assets/Tests/EditMode/Transitions/` → `Assets/Scripts/Transitions/Tests/EditMode/`
- Move: `Assets/Tests/EditMode/` 根上的 8 个 `.cs` + `MRBase.Localization.Tests.asmdef` → `Assets/Scripts/Localization/Tests/EditMode/`
- Delete: `Assets/Tests/`（搬空后）

**Interfaces:**
- Consumes: 无（首个 Task）
- Produces: `Assets/Scripts/<Module>/Tests/EditMode/` 五个目录，供 Task 2 的校验测试把自己放进同构位置（`Assets/Scripts/Editor/Tests/EditMode/`）

- [ ] **Step 1: 确认 Editor 在线并记录测试基线**

```bash
unity status --project-path .
unity command run_tests --project-path . --mode EditMode --timeout 600
```

把输出里的**通过数与总数**抄下来（设计文档记录的基线是 389）。这个数字是本 Task 唯一的验收依据 —— 搬完必须一模一样。

如果 `run_tests` 返回的是 job id 而非结果，用 `unity command test_status --project-path .` 轮询到结束。

- [ ] **Step 2: 建 5 个 `Tests` 父目录**

```bash
unity command eval --project-path . '
var mods = new[] { "Core", "IteHost", "SacredRelic", "Transitions", "Localization" };
foreach (var m in mods)
{
    var p = "Assets/Scripts/" + m + "/Tests";
    if (!UnityEditor.AssetDatabase.IsValidFolder(p))
        UnityEditor.AssetDatabase.CreateFolder("Assets/Scripts/" + m, "Tests");
}
UnityEditor.AssetDatabase.Refresh();
return "folders ready";
'
```

- [ ] **Step 3: 搬 4 个整目录**

`MoveAsset` 返回空字符串表示成功，非空即错误信息。任何一条非空就停下报出来，不要继续。

```bash
unity command eval --project-path . '
var moves = new (string from, string to)[] {
    ("Assets/Tests/EditMode/Core",        "Assets/Scripts/Core/Tests/EditMode"),
    ("Assets/Tests/EditMode/Ite",         "Assets/Scripts/IteHost/Tests/EditMode"),
    ("Assets/Tests/EditMode/SacredRelic", "Assets/Scripts/SacredRelic/Tests/EditMode"),
    ("Assets/Tests/EditMode/Transitions", "Assets/Scripts/Transitions/Tests/EditMode"),
};
var log = new System.Text.StringBuilder();
foreach (var m in moves)
{
    var err = UnityEditor.AssetDatabase.MoveAsset(m.from, m.to);
    log.AppendLine(string.IsNullOrEmpty(err) ? ("OK   " + m.to) : ("FAIL " + m.from + " -> " + err));
}
UnityEditor.AssetDatabase.Refresh();
return log.ToString();
'
```

预期：4 行 `OK`。

- [ ] **Step 4: 搬 Localization 的 9 个散落文件**

这 8 个测试和它们的 asmdef 直接躺在 `Assets/Tests/EditMode/` 根上（没有自己的子目录），所以要逐个移。

```bash
unity command eval --project-path . '
var dest = "Assets/Scripts/Localization/Tests/EditMode";
if (!UnityEditor.AssetDatabase.IsValidFolder(dest))
    UnityEditor.AssetDatabase.CreateFolder("Assets/Scripts/Localization/Tests", "EditMode");
var files = new[] {
    "AprilTagDetectorCoreTests.cs",
    "FiducialConfidencePolicyTests.cs",
    "MarkerStabilizerTests.cs",
    "MarkerTrackingSessionTests.cs",
    "PicoEnterpriseCameraPoseTests.cs",
    "PlanarPoseSolverTests.cs",
    "PlatformOffsetConfigTests.cs",
    "PoseMathTests.cs",
    "MRBase.Localization.Tests.asmdef",
};
var log = new System.Text.StringBuilder();
foreach (var f in files)
{
    var err = UnityEditor.AssetDatabase.MoveAsset("Assets/Tests/EditMode/" + f, dest + "/" + f);
    log.AppendLine(string.IsNullOrEmpty(err) ? ("OK   " + f) : ("FAIL " + f + " -> " + err));
}
UnityEditor.AssetDatabase.Refresh();
return log.ToString();
'
```

预期：9 行 `OK`。

- [ ] **Step 5: 确认 `Assets/Tests` 已空，删掉它**

```bash
unity command eval --project-path . '
var left = UnityEditor.AssetDatabase.FindAssets("", new[] { "Assets/Tests" });
if (left.Length > 0)
    return "还剩 " + left.Length + " 项，先查清楚再删：" +
           string.Join(", ", System.Array.ConvertAll(left, UnityEditor.AssetDatabase.GUIDToAssetPath));
UnityEditor.AssetDatabase.DeleteAsset("Assets/Tests");
UnityEditor.AssetDatabase.Refresh();
return "Assets/Tests removed";
'
```

只有返回 `Assets/Tests removed` 才往下走。返回"还剩 N 项"就停下，把清单报出来 —— 说明前两步漏了东西。

- [ ] **Step 6: 等编译完成，跑全量 EditMode 对数**

```bash
unity command recompile_status --project-path .
unity command run_tests --project-path . --mode EditMode --timeout 600
```

**通过数与总数必须和 Step 1 完全一致。** 对不上就停：多半是某个 asmdef 没跟着走，或者 `Assets/Tests` 里还有残留。不要"修一修再跑"，先把差异定位清楚。

- [ ] **Step 7: 双平台 Dry Run，确认没动到构建路径**

```bash
unity command eval --project-path . 'UnityEditor.EditorApplication.ExecuteMenuItem("MRBase/Build/Dry Run Quest"); return "queued";'
```

等控制台出结果后再跑 PICO：

```bash
unity command console --project-path . --tail 60
unity command eval --project-path . 'UnityEditor.EditorApplication.ExecuteMenuItem("MRBase/Build/Dry Run Pico"); return "queued";'
unity command console --project-path . --tail 60
```

两个都要看到配置校验通过。Dry Run 不打包，不装机。

- [ ] **Step 8: 提交**

```bash
git add -A Assets/
git status --short   # 确认只有测试文件的移动与 Assets/Tests(.meta) 的删除，没有别的
git commit -m "$(cat <<'EOF'
refactor(tests): 测试搬到各模块旁，Assets/Tests 消失

5 个测试 asmdef 从 Assets/Tests/EditMode 移到
Assets/Scripts/<Module>/Tests/EditMode。asmdef 名字不变，
-assemblyNames 照旧。纯 AssetDatabase.MoveAsset，无代码改动。

落点选 Assets/Scripts/<Module>/ 而非 _Project/：feature 目录尚不
存在，搬进 _Project 会让测试与被测代码分居。将来模块整体迁入
_Project/Features/ 时测试跟着走。

EditMode 全量回归：总数与搬迁前一致。双平台 Dry Run 通过。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: 结构校验测试 + 豁免表

把约定里机械可判的三条变成 EditMode 测试。第四个测试守豁免表本身 —— 豁免失效时提醒删除，否则表会越积越多且没人知道哪些已经修好了。

放 `Assets/Scripts/Editor/Tests/EditMode/` 的理由：`MRBase.Build.Editor` 已装着 `ManifestGuard`（构建期守卫），这个是提交期守卫，同属"工程级机械守卫"一条轴。测试 asmdef 独立，不违反条文 2。

**Files:**
- Create: `Assets/Scripts/Editor/Tests/EditMode/MRBase.Build.Editor.Tests.asmdef`
- Create: `Assets/Scripts/Editor/Tests/EditMode/LayoutConventionTests.cs`
- Create: `Assets/Scripts/Editor/Tests/EditMode/layout-waivers.txt`

**Interfaces:**
- Consumes: Task 1 建立的 `Assets/Scripts/<Module>/Tests/EditMode/` 惯例
- Produces: `MRBase.Build.Editor.Tests` 程序集，4 个 `[Test]`；`layout-waivers.txt` 的格式契约（一行一路径，相对 `Assets/`，`#` 起头为注释）

- [ ] **Step 1: 建测试 asmdef**

不引用 `MRBase.Build.Editor` —— 这个测试只扫文件系统，不调 `BuildScript`。

```bash
unity command eval --project-path . '
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Scripts/Editor/Tests"))
    UnityEditor.AssetDatabase.CreateFolder("Assets/Scripts/Editor", "Tests");
if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/Scripts/Editor/Tests/EditMode"))
    UnityEditor.AssetDatabase.CreateFolder("Assets/Scripts/Editor/Tests", "EditMode");
return "ok";
'
```

写 `Assets/Scripts/Editor/Tests/EditMode/MRBase.Build.Editor.Tests.asmdef`：

```json
{
    "name": "MRBase.Build.Editor.Tests",
    "rootNamespace": "MRBase.Build.Editor.Tests",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": true,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: 写校验测试（此时应当是红的）**

写 `Assets/Scripts/Editor/Tests/EditMode/LayoutConventionTests.cs`：

```csharp
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
    /// 与 ManifestGuard 同轴：都是工程级机械守卫，一个守构建期，一个守提交期。
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
        }

        static string AssemblyNameOf(string asmdefPath)
        {
            var stub = JsonUtility.FromJson<AsmdefStub>(File.ReadAllText(asmdefPath));
            return string.IsNullOrEmpty(stub?.name)
                ? Path.GetFileNameWithoutExtension(asmdefPath)
                : stub.name;
        }

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
                .ToList();
        }

        static bool IsWaived(string relativeCs, List<string> waivers) =>
            waivers.Any(w => string.Equals(relativeCs, w, StringComparison.Ordinal)
                             || relativeCs.StartsWith(w + "/", StringComparison.Ordinal));

        static string Report(string rule, IEnumerable<string> offenders) =>
            rule + "\n  " + string.Join("\n  ", offenders)
                 + "\n\n约定见 .claude/CLAUDE.md「目录与 asmdef 约定」。"
                 + $"确实要放行就登记到 Assets/{WaiverFile}，一行一条并写明理由。";

        // ---------- 四条规则 ----------

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
```

- [ ] **Step 3: 跑这一组测试，确认它按预期报红**

```bash
unity command recompile_status --project-path .
unity command run_tests --project-path . --mode EditMode --filter LayoutConventionTests --timeout 300
```

预期：`NoScriptFallsIntoAssemblyCSharp` **失败**，消息里列出 `Scripts/IceSpriteFx/` 下的 3 个文件：

```
Scripts/IceSpriteFx/IceSpriteFxDisableXrSimulator.cs
Scripts/IceSpriteFx/IceSpriteFxTestInput.cs
Scripts/IceSpriteFx/IceSpritePresence.cs
```

另外三个测试应当**通过**。

如果报红的文件多于这 3 个，停下来核对 —— 说明 `OwningAssembly` 的向上查找有偏差，或者白名单圈错了。不要直接往豁免表里加。

- [ ] **Step 4: 写豁免表，让测试转绿**

写 `Assets/Scripts/Editor/Tests/EditMode/layout-waivers.txt`：

```
# 目录与 asmdef 约定的豁免表。
#
# 一行一个路径，相对 Assets/，可以是文件或目录。# 起头是注释。
# 登记的是「自己的、确实违规、暂时不修」的项 —— 这就是债务清单。
# 第三方插件目录不进此表：它们在白名单外，压根不检。
#
# 还完债请把对应行删掉，EveryWaiverIsStillNeeded 会盯着失效的豁免。

# IceSpriteFx 的 3 个 MonoBehaviour 必须留在 Assembly-CSharp：
# 它们引用第三方 INab Dissolver，而 INab Studio/ 没有 asmdef，
# 任何 asmdef 程序集都引用不到它。见
# docs/superpowers/plans/2026-08-07-ice-sprite-fx.md 的 Global Constraints。
Scripts/IceSpriteFx
```

- [ ] **Step 5: 重跑，确认全绿**

```bash
unity command run_tests --project-path . --mode EditMode --filter LayoutConventionTests --timeout 300
```

预期：4 个测试全部 PASS。

- [ ] **Step 6: 造反例验红（一）—— 裸 .cs**

在白名单内、任何 asmdef 覆盖不到的地方放一个临时文件。`Assets/Scripts/` 根目录本身没有 asmdef，正合适：

```bash
unity command eval --project-path . '
System.IO.File.WriteAllText(
    UnityEngine.Application.dataPath + "/Scripts/__LayoutProbe.cs",
    "public static class __LayoutProbe { }");
UnityEditor.AssetDatabase.Refresh();
return "probe placed";
'
unity command run_tests --project-path . --mode EditMode --filter NoScriptFallsIntoAssemblyCSharp --timeout 300
```

**预期：失败**，消息里含 `Scripts/__LayoutProbe.cs`。测试仍然绿就说明校验是空转的，必须查清楚再继续。

- [ ] **Step 7: 造反例验红（二）—— Editor 目录下归错程序集**

先清掉第一个探针，再造第二个。`Assets/Scripts/Core/` 归 `MRBase.Core`（非 `.Editor`），在它下面建一个 `Editor/` 子目录但**不**给这个子目录配 asmdef —— 里面的 `.cs` 路径含 `/Editor/`，向上最近的 asmdef 却是 `MRBase.Core`，正是条文 2 要抓的形状。

```bash
unity command eval --project-path . '
var dataPath = UnityEngine.Application.dataPath;
System.IO.File.Delete(dataPath + "/Scripts/__LayoutProbe.cs");
System.IO.File.Delete(dataPath + "/Scripts/__LayoutProbe.cs.meta");

var probeDir = dataPath + "/Scripts/Core/Editor";
System.IO.Directory.CreateDirectory(probeDir);
System.IO.File.WriteAllText(probeDir + "/__LayoutProbe2.cs",
    "public static class __LayoutProbe2 { }");
UnityEditor.AssetDatabase.Refresh();
return "probe one removed, probe two placed";
'
unity command run_tests --project-path . --mode EditMode --filter EditorCodeBelongsToEditorAssembly --timeout 300
```

**预期：失败**，消息里含 `Scripts/Core/Editor/__LayoutProbe2.cs`，且说明它归属 `MRBase.Core`。

第三条规则 `EditorAssemblyHoldsOnlyEditorCode` 不单独造反例：它与刚验证的这条共用同一套 `RawViolations` 管道，只是判定方向相反，造它需要额外错放一个 asmdef，成本高于收益。

- [ ] **Step 8: 清理两个探针，确认回到全绿**

```bash
unity command eval --project-path . '
UnityEditor.AssetDatabase.DeleteAsset("Assets/Scripts/Core/Editor");
UnityEditor.AssetDatabase.Refresh();
return "probes cleared";
'
unity command run_tests --project-path . --mode EditMode --filter LayoutConventionTests --timeout 300
git status --short   # 必须没有 __LayoutProbe 的任何踪迹
```

预期：4 个测试全绿，`git status` 干净。

- [ ] **Step 9: 跑全量，确认没影响别的测试**

```bash
unity command run_tests --project-path . --mode EditMode --timeout 600
```

预期：Task 1 Step 6 记下的总数 **+ 4**（本 Task 新增的 4 个用例），全部通过。

- [ ] **Step 10: 提交**

```bash
git add Assets/Scripts/Editor/Tests
git status --short
git commit -m "$(cat <<'EOF'
test(layout): 目录与 asmdef 约定的机械校验

四个 EditMode 测试：白名单内不得有 .cs 落进 Assembly-CSharp、
Editor 目录下的代码必须归 .Editor 程序集、.Editor 程序集不得吞
Runtime 代码、失效的豁免必须删除。

白名单只含 Assets/_Project 与 Assets/Scripts，第三方插件目录永久
在范围外，装新插件不用回来改清单。.Tests 程序集不参与 Editor 位置
检查（它们整体 includePlatforms: Editor，不存在混装风险）。

豁免表落地一条：Scripts/IceSpriteFx —— 引用无 asmdef 的第三方
INab Dissolver，只能留在 Assembly-CSharp。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: 约定条文写进 CLAUDE.md

条文的家是 `.claude/CLAUDE.md` 而不是 `openspec/specs/` —— 两点理由：往 `openspec/specs/` 写 spec 应走 propose→apply→sync-specs 的完整流程，手写塞入是绕过治理；而 CLAUDE.md 每次会话加载，是唯一能在"新代码被写出来之前"影响落点的位置，本设计的全部价值正在于此。

**Files:**
- Modify: `.claude/CLAUDE.md`（在 `## Architecture` 的 **Module layout** 小节之后、**Build gotchas** 之前插入一节）

**Interfaces:**
- Consumes: Task 2 落成的测试名与豁免表路径（条文里要指过去）
- Produces: 无（终点）

- [ ] **Step 1: 插入约定节**

在 `.claude/CLAUDE.md` 的 **Module layout** 列表结束之后、`**Build gotchas the script guards against**` 那段之前，插入：

```markdown
**目录与 asmdef 约定（feature-first）。** 新代码进 `Assets/_Project/Features/<Feature>/`，
一个功能一个文件夹，代码与资源同居：

```
Assets/_Project/Features/<Feature>/
  Runtime/       代码 + MRBase.<Feature>.asmdef
  Editor/        该功能的编辑器工具 + MRBase.<Feature>.Editor.asmdef
  Tests/EditMode/  该功能的测试 + MRBase.<Feature>.Tests.asmdef
  Art/ Prefabs/ Scenes/ Settings/   按需建，没有就不建
```

`Assets/_Project/Core/` 放跨功能骨架（`MRCore.unity`、`Common`、`Platform`、`Bootstrap`）。
`Assets/Scripts/` 是存量，原地不动、逐步迁出 —— **不要**为了"统一"去批量搬它。

五条条文：

1. **每个 feature 一个 asmdef，不许有裸 `.cs` 掉进 `Assembly-CSharp`。**
   `Assembly-CSharp` 自动引用所有 asmdef 程序集，反向不行，所以裸 `.cs` 是单向死路：
   任何 asmdef 里的代码都够不着它（`Assets/Scripts/Transitions/IceSpriteTeleport.cs:30`
   的注释就是这笔账）。
2. **Editor 代码只能在自己 feature 的 `Editor/` 下**，归属名字以 `.Editor` 结尾的 asmdef。
   `MRBase.Build.Editor` 只装 `BuildScript` + `ManifestGuard`。
3. **测试紧贴被测代码**，与它同一棵树。`Assets/Tests/` 已不存在，不要重建。
4. **`_Project/` 下不新建 `Resources/`。** 它无条件全量进包、不可剥离。
   工程级 `Assets/Resources/`（SDK 生成的 `PXR_*`/`OVR*`）在范围外，不管。
5. **场景归属 feature**，只有 `MRCore.unity` 这类跨功能骨架在 `_Project/Core/Scenes/`。

**适用范围是白名单**：只有 `Assets/_Project/**` 与 `Assets/Scripts/**` 受管辖，
其余 `Assets/*` 一律在外 —— `Oculus/`、`INab Studio/`、`Samples/`、`XR/`、`XRI/`、
`Plugins/`、`TextMesh Pro/`、`Resources/` 都不检，装新插件也不用回来改清单。
**范围外 ≠ 豁免**：豁免表登记的是"自己的、违规的、将来要还的"，第三方不是债。

条文 1、2 由 `Assets/Scripts/Editor/Tests/EditMode/LayoutConventionTests.cs` 机械执行
（`MRBase.Build.Editor.Tests`，与 `ManifestGuard` 同轴：一个守构建期，一个守提交期）。
判不出的部分不检：一个 `.mat` 该归哪个 feature、`IteSceneSetup.cs` 该归 ITE 还是归构建，
都需要人读代码 —— 硬编规则只会制造假阳性。这类是文档级债务，不进豁免表。
存量违规登记在 `Assets/Scripts/Editor/Tests/EditMode/layout-waivers.txt`，还完债就删行。

设计与七条编号决策见 `docs/superpowers/specs/2026-09-21-asset-layout-governance-design.md`。
```

- [ ] **Step 2: 核对交叉引用没写错**

```bash
cd /Users/wwj/Desktop/unity/MR_Base
ls Assets/Scripts/Editor/Tests/EditMode/LayoutConventionTests.cs
ls Assets/Scripts/Editor/Tests/EditMode/layout-waivers.txt
ls docs/superpowers/specs/2026-09-21-asset-layout-governance-design.md
sed -n '28,34p' Assets/Scripts/Transitions/IceSpriteTeleport.cs   # 确认第 30 行仍是那条注释
test ! -d Assets/Tests && echo "Assets/Tests 确已消失"
```

五条都要通过。`IceSpriteTeleport.cs:30` 如果行号漂了，把条文里的行号改成实际值。

- [ ] **Step 3: 修掉 Module layout 里已经过时的一句**

`## Architecture` 的 **Module layout** 小节里有一行：

```
- `Editor` (`MRBase.Build.Editor`) — `BuildScript` only.
```

这句现在是**两处**不准：`MRBase.Build.Editor` 里还有 `ManifestGuard.cs` 和 `IteSceneSetup.cs`，
而且旁边多了 `MRBase.Build.Editor.Tests`。改成：

```
- `Editor` (`MRBase.Build.Editor`) — `BuildScript` + `ManifestGuard`。旁边的
  `MRBase.Build.Editor.Tests` 装 `LayoutConventionTests`（目录约定的提交期守卫）。
  `IteSceneSetup.cs` 目前也在这里，但它是 ITE 的功能工具、不是构建 —— 属已知的
  文档级债务，机械校验判不出来，见目录与 asmdef 约定一节。
```

同一小节里另有一行提到测试位置（`several with a matching *.Tests asmdef under Assets/Tests/EditMode/`），
把 `Assets/Tests/EditMode/` 改成 `各自模块的 Tests/EditMode/`。

- [ ] **Step 4: 提交**

```bash
git add .claude/CLAUDE.md
git diff --cached   # 通读一遍，确认没有把别的节改坏
git commit -m "$(cat <<'EOF'
docs(claude-md): 目录与 asmdef 约定入册

新增 feature-first 约定节：五条条文 + 白名单适用范围。条文 1、2 由
LayoutConventionTests 机械执行，判不出的部分明确不检、记为文档级债务。

同时修正 Module layout 两处过时表述：MRBase.Build.Editor 不再是
"BuildScript only"，测试也不在 Assets/Tests/EditMode 了。

条文落 CLAUDE.md 而非 openspec/specs/：后者应走 propose→apply→sync
完整流程，手写塞入是绕过治理；而 CLAUDE.md 每次会话加载，是唯一能在
新代码写出来之前影响落点的位置。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## 完工验收

- [ ] `Assets/Tests/` 不存在
- [ ] 5 个测试 asmdef 在 `Assets/Scripts/<Module>/Tests/EditMode/`，名字未改
- [ ] EditMode 全量通过，总数 = 搬迁前基线 + 4
- [ ] `LayoutConventionTests` 4 个全绿，豁免表只有 `Scripts/IceSpriteFx` 一条
- [ ] 两个反例都验过红：白名单内的裸 `.cs`（Task 2 Step 6）、`Editor/` 目录下归错程序集的 `.cs`（Step 7），且探针已清理
- [ ] 双平台 `Dry Run Quest` / `Dry Run Pico` 通过
- [ ] `.claude/CLAUDE.md` 有约定节，Module layout 的过时表述已修
- [ ] `git status` 干净，无残留探针文件
