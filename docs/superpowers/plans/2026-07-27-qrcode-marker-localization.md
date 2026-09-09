# 二维码/标记扫描定位(Quest + PICO)Implementation Plan

> **过时（2026-09-09）**：本文记录的是已放弃的 `IMarkerTrackingProvider` / PICO 原生 ArUco / `AnchorRegistry` 方案，不要按它实现。现行契约是 `IMarkerObservationSource` + `MarkerTrackingSession`（PICO AprilTag，Quest MRUK QR）。见 `openspec/specs/unified-marker-tracking-contract/spec.md`。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 Quest 3/3S 与 PICO 4 Ultra/Enterprise 用户扫描现场标记后,把从网络动态获取的虚拟内容准确锚定到该标记对应的真实世界位置。

**Architecture:** 平台无关的 `MarkerAnchorService` 通过 `IMarkerTrackingProvider` 消费两端原生标记识别 API(Quest MRUK QR / PICO ArUco),原始 pose 先过 `MarkerStabilizer` 消抖,稳定后查 `AnchorRegistry`(数据来自网络 `IAnchorDataSource`,不写死在场景里)找到 `AnchorEntityData`,叠加 `PlatformOffsetConfig` 的面板标定偏移后,生成一个 `AnchorEntity`(同一个类多个实例)异步下载并展示内容。架构模式参考同项目组 `ite-space-tour`(`/Users/wwj/Desktop/unity/ite-space-tour/Assets/Scripts/ITE`、`AnchorObject.cs`)已验证的扫描稳定化 + 网络拉表 + 动态内容装配模式。

**Tech Stack:** Unity 6000.4.4f1,C#,Unity Test Framework(NUnit,EditMode),Newtonsoft.Json(项目已有),Meta XR Core SDK + MRUK(Quest),PICO Unity Integration SDK Enterprise(PICO)。

## Global Constraints

- Meta XR Core SDK(含 MRUK):调研时最新 v205.0(2026-07-22)。**实施前必须重新查询当前最新版本**,不得直接套用此号,以 https://developers.meta.com/horizon/downloads/package/meta-xr-core-sdk/ 当时页面为准。
- PICO Unity Integration SDK:调研时最新 v3.4.0(2026-02-27)。**实施前必须重新查询当前最新版本**,以 https://github.com/Pico-Developer/PICO-Unity-Integration-SDK/releases 当时页面为准。
- 目标设备:Quest 3 / 3S,PICO 4 Ultra / PICO 4 Enterprise(已确认企业开发授权)。
- 不做跨会话持久化:每次进入 App 重新扫码即可。
- 不做多设备/多用户坐标对齐。
- PICO ArUco 官方 Demo 显示同时最多追踪 10 个不同 ID,`AnchorEntityData.PicoMarkerId` 取值范围按此约束规划。
- 物理标记必须做成同一张刚性面板(QR + ArUco 同版式,相对坐标由印刷设计稿决定),不得用两张独立贴纸。
- **锚点配置表数据必须来自外部网络,不得写死/序列化在场景或 Editor 资产里**;当前还没有真实后端,先用 `IAnchorDataSource` 抽象接口 + 本地 JSON 模拟,后续替换为真实 HTTP 实现时业务层不用改。
- **锚点挂载的虚拟内容必须运行时动态下载**,不得用固定本地 Prefab 引用;本计划先实现最简单的图片内容类型跑通下载-展示流程,模型/视频等更重的内容类型与 `BaseComponent`/Trigger/Action 组件编排引擎是后续需求,本计划不做。

---

## 文件结构

新建文件:

- `Assets/Scripts/Localization/MRBase.Localization.asmdef` — 平台无关核心运行时程序集
- `Assets/Scripts/Common/StaticInstance.cs` — 单例基类(移植自 `ite-space-tour`)
- `Assets/Scripts/Common/FileUtils.cs` — 本地/远程文件加载工具(移植自 `ite-space-tour`,裁剪掉暂不需要的 glb/zip 方法)
- `Assets/Scripts/Common/FetchUtils.cs` — JSON 网络请求工具(移植自 `ite-space-tour`)
- `Assets/Scripts/Localization/PoseMath.cs` — 位姿合成工具函数
- `Assets/Scripts/Localization/AnchorEntityData.cs` — 单个锚点的数据 DTO(网络 JSON 反序列化)
- `Assets/Scripts/Localization/IAnchorDataSource.cs` — 锚点表网络数据源抽象接口
- `Assets/Scripts/Localization/LocalJsonAnchorDataSource.cs` — 本地 JSON 模拟数据源(还没后端时用)
- `Assets/Scripts/Localization/AnchorRegistry.cs` — 运行时锚点表(从 `IAnchorDataSource` 填充,不是 ScriptableObject)
- `Assets/Scripts/Localization/PlatformOffsetConfig.cs` — 两平台全局偏移常量(ScriptableObject,面板设计稿标定值,这个仍是本地资产,不是网络数据)
- `Assets/Scripts/Localization/IMarkerTrackingProvider.cs` — 标记识别 provider 接口
- `Assets/Scripts/Localization/MockMarkerProvider.cs` — 测试/编辑器用假 provider
- `Assets/Scripts/Localization/MarkerStabilizer.cs` — 扫描消抖(移植自 `AnchorObject.cs` 的稳定化逻辑,泛化成多 rawId 版本)
- `Assets/Scripts/Localization/IContentLoader.cs` — 内容下载抽象接口
- `Assets/Scripts/Localization/ImageContentLoader.cs` — 图片内容下载实现(MVP 内容类型)
- `Assets/Scripts/Localization/AnchorEntity.cs` — 扫描实体(MonoBehaviour,同一个类多个实例,`async Create()` 自我装配)
- `Assets/Scripts/Localization/MarkerAnchorService.cs` — 核心业务逻辑(MonoBehaviour)
- `Assets/Scripts/Localization/Native/MRBase.Localization.Native.asmdef` — 平台原生 provider 程序集
- `Assets/Scripts/Localization/Native/MarkerTrackingBootstrapper.cs` — 运行时按平台选择 provider + 数据源并启动
- `Assets/Scripts/Localization/Native/QuestMarkerProvider.cs` — Quest 原生 provider(`#if MRBASE_QUEST`)
- `Assets/Scripts/Localization/Native/PicoMarkerProvider.cs` — PICO 原生 provider(`#if MRBASE_PICO`)
- `Assets/Tests/EditMode/MRBase.Localization.Tests.asmdef` — 测试程序集
- `Assets/Tests/EditMode/PoseMathTests.cs`
- `Assets/Tests/EditMode/AnchorRegistryTests.cs`
- `Assets/Tests/EditMode/LocalJsonAnchorDataSourceTests.cs`
- `Assets/Tests/EditMode/MockMarkerProviderTests.cs`
- `Assets/Tests/EditMode/MarkerStabilizerTests.cs`
- `Assets/Tests/EditMode/AnchorEntityTests.cs`
- `Assets/Tests/EditMode/MarkerAnchorServiceTests.cs`

修改文件:

- `Packages/manifest.json` — 新增 Meta XR Core SDK / MRUK、PICO Unity Integration SDK 依赖(Task 11、12)

---

### Task 1: 程序集脚手架 + 平台 Scripting Define

**Files:**
- Create: `Assets/Scripts/Localization/MRBase.Localization.asmdef`
- Create: `Assets/Tests/EditMode/MRBase.Localization.Tests.asmdef`

**Interfaces:**
- 产出:两个程序集名 `MRBase.Localization`、`MRBase.Localization.Tests`,后续所有代码任务都放进这两个程序集。

- [ ] **Step 1: 创建核心运行时程序集**

`Assets/Scripts/Localization/MRBase.Localization.asmdef`:

```json
{
    "name": "MRBase.Localization",
    "rootNamespace": "",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

**注意**:`rootNamespace` 留空——本计划的类沿用 `ite-space-tour` 源代码的约定,大多放在全局命名空间(不加 `namespace` 包裹),保持和被移植代码一致的风格,不强行套命名空间。

- [ ] **Step 2: 创建测试程序集**

`Assets/Tests/EditMode/MRBase.Localization.Tests.asmdef`:

```json
{
    "name": "MRBase.Localization.Tests",
    "rootNamespace": "",
    "references": [
        "MRBase.Localization",
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

- [ ] **Step 3: 创建两套 Build Profile**

`File > Build Profiles` → 新建 `Quest`、`PICO` 两个 Profile,平台都选 Android。分别在 `Scripting Define Symbols` 加:
- `Quest` Profile:`MRBASE_QUEST`
- `PICO` Profile:`MRBASE_PICO`

**验证(手动)**:确认两个 Build Profile 已创建且 Scripting Define Symbols 字段各自正确显示,留到 Task 11/12 完成后再实际验证 `#if` 生效。

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Localization/MRBase.Localization.asmdef Assets/Tests/EditMode/MRBase.Localization.Tests.asmdef
git commit -m "chore: scaffold Localization assembly definitions"
```

---

### Task 2: 移植 StaticInstance / FileUtils / FetchUtils

**Files:**
- Create: `Assets/Scripts/Common/StaticInstance.cs`
- Create: `Assets/Scripts/Common/FileUtils.cs`
- Create: `Assets/Scripts/Common/FetchUtils.cs`

**Interfaces:**
- Produces: `StaticInstance<T>.Instance`;`FileUtils.Instance.LoadJsonByUrlAsync<T>(string, JsonSerializerSettings)`、`.LoadSpriteByUrl(string, bool)`、`.LoadTextureByUrl(string, bool)`、`.SaveJsonTextToFile(string, string)`;`FetchUtils.FetchJsonAsync<T>(string)`、`.FetchTextAsync(string)`。后续 `LocalJsonAnchorDataSource`(Task 4)、`ImageContentLoader`(Task 8)依赖这些方法。

这三个类照抄 `/Users/wwj/Desktop/unity/ite-space-tour/Assets/Scripts/Utils/{StaticInstance,FileUtils,FetchUtils}.cs`,已经过生产验证,不重新设计。**裁剪点**:`FileUtils` 里依赖 `Unity.SharpZipLib.Zip` 和 `GLTFast` 的方法(`DownloadAndExtractZip`、`ExtractZipAsync`、`GetFileHash`、`LoadGlbByUrl`、`LoadVideoByUrl`、`LoadAudioClipByUrl`)本计划暂不需要,先不移植,避免引入还用不上的包依赖(YAGNI,等真的要做模型/音频内容时再补)。

- [ ] **Step 1: 创建 StaticInstance**

```csharp
using UnityEngine;

public class StaticInstance<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;

    public static T Instance => _instance;

    protected virtual void Awake()
    {
        _instance = (T)(object)this;
        AfterAwake();
    }

    protected virtual void AfterAwake() { }
}
```

- [ ] **Step 2: 创建裁剪版 FileUtils**

```csharp
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public class FileUtils : StaticInstance<FileUtils>
{
    public async Task SaveJsonTextToFile(string fileName, string data)
    {
        string filePath = Path.Combine(Application.persistentDataPath, fileName + ".json");
        if (!File.Exists(filePath))
        {
            File.Create(filePath).Dispose();
        }
        await File.WriteAllTextAsync(filePath, data);
    }

    public async Task<T> LoadJsonByUrlAsync<T>(string url, JsonSerializerSettings settings = null)
    {
        string uri = Path.Combine(Application.persistentDataPath, url);

        if (!File.Exists(uri))
        {
            return default;
        }

        return await Task.Run(() =>
        {
            string json = File.ReadAllText(uri);
            return JsonConvert.DeserializeObject<T>(json, settings);
        });
    }

    public async Task<Texture2D> LoadTextureByUrl(string url, bool isLocal = true)
    {
        if (isLocal)
        {
            url = Path.Combine(Application.persistentDataPath, url);
            if (!File.Exists(url))
            {
                return null;
            }
            url = "file://" + url;
        }

        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
        {
            www.downloadHandler = new DownloadHandlerTexture();
            await www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                return null;
            }

            return DownloadHandlerTexture.GetContent(www);
        }
    }

    public async Task<Sprite> LoadSpriteByUrl(string url, bool isLocal = true)
    {
        Texture2D texture = await LoadTextureByUrl(url, isLocal);
        if (texture == null)
        {
            return null;
        }

        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
    }
}
```

- [ ] **Step 3: 创建 FetchUtils**

```csharp
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public abstract class FetchUtils
{
    public static async Task<T> FetchJsonAsync<T>(string url)
    {
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            await www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("[FetchUtils] Error fetching JSON: " + www.error);
                return default;
            }

            return JsonConvert.DeserializeObject<T>(www.downloadHandler.text);
        }
    }

    public static async Task<string> FetchTextAsync(string url)
    {
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            await www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("[FetchUtils] Error fetching text: " + www.error);
                return null;
            }

            return www.downloadHandler.text;
        }
    }
}
```

- [ ] **Step 4: 场景里挂 FileUtils**

`FileUtils` 是 `StaticInstance`,需要场景里有一个挂了该组件的 GameObject 才能用 `FileUtils.Instance`。放进 Task 7 的 Demo 场景里一起搭(先记录这个依赖,不在本 Task 里创建场景)。

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Common
git commit -m "chore: port StaticInstance/FileUtils/FetchUtils from ite-space-tour"
```

---

### Task 3: PoseMath 位姿合成

**Files:**
- Create: `Assets/Scripts/Localization/PoseMath.cs`
- Test: `Assets/Tests/EditMode/PoseMathTests.cs`

**Interfaces:**
- Produces: `PoseMath.Compose(Pose parent, Pose localOffset) : Pose` — `MarkerAnchorService`(Task 9)用它把稳定后的 pose 和 `PlatformOffsetConfig` 的偏移合成最终 `AnchorEntity` 摆放的 pose。

- [ ] **Step 1: 写失败测试**

```csharp
using NUnit.Framework;
using UnityEngine;

public class PoseMathTests
{
    [Test]
    public void Compose_WithIdentityOffset_ReturnsParentPose()
    {
        var parent = new Pose(new Vector3(1, 2, 3), Quaternion.Euler(0, 45, 0));

        var result = PoseMath.Compose(parent, Pose.identity);

        Assert.AreEqual(parent.position, result.position);
        Assert.AreEqual(parent.rotation, result.rotation);
    }

    [Test]
    public void Compose_WithTranslationOffset_RotatesOffsetIntoParentFrame()
    {
        var parent = new Pose(Vector3.zero, Quaternion.Euler(0, 90, 0));
        var offset = new Pose(new Vector3(1, 0, 0), Quaternion.identity);

        var result = PoseMath.Compose(parent, offset);

        Assert.AreEqual(0f, result.position.x, 0.0001f);
        Assert.AreEqual(0f, result.position.y, 0.0001f);
        Assert.AreEqual(-1f, result.position.z, 0.0001f);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

用 Unity Test Runner(`Window > General > Test Runner` → EditMode)或 UnityMCP 的 `run_tests(mode="EditMode", test_names=["PoseMathTests"])` + `get_test_job`。预期:编译失败,找不到 `PoseMath`。

- [ ] **Step 3: 实现**

```csharp
using UnityEngine;

public static class PoseMath
{
    public static Pose Compose(Pose parent, Pose localOffset)
    {
        Vector3 position = parent.position + parent.rotation * localOffset.position;
        Quaternion rotation = parent.rotation * localOffset.rotation;
        return new Pose(position, rotation);
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Localization/PoseMath.cs Assets/Tests/EditMode/PoseMathTests.cs
git commit -m "feat: add PoseMath pose composition helper"
```

---

### Task 4: AnchorEntityData + IAnchorDataSource + LocalJsonAnchorDataSource + AnchorRegistry

**Files:**
- Create: `Assets/Scripts/Localization/AnchorEntityData.cs`
- Create: `Assets/Scripts/Localization/IAnchorDataSource.cs`
- Create: `Assets/Scripts/Localization/LocalJsonAnchorDataSource.cs`
- Create: `Assets/Scripts/Localization/AnchorRegistry.cs`
- Test: `Assets/Tests/EditMode/AnchorRegistryTests.cs`
- Test: `Assets/Tests/EditMode/LocalJsonAnchorDataSourceTests.cs`

**Interfaces:**
- Consumes: `FileUtils.Instance.LoadJsonByUrlAsync<T>`(Task 2)。
- Produces: `AnchorEntityData { AnchorId, QuestPayload, PicoMarkerId, ContentImageUrl, ContentVersion }`;`IAnchorDataSource.FetchAsync() : Task<List<AnchorEntityData>>`;`AnchorRegistry.LoadAsync(IAnchorDataSource)`、`.TryResolve(string, out AnchorEntityData) : bool`、`.SetEntitiesForTesting(List<AnchorEntityData>)`(测试用)。`MarkerAnchorService`(Task 9)依赖 `AnchorRegistry.TryResolve`;`MarkerTrackingBootstrapper`(Task 11)依赖 `AnchorRegistry.LoadAsync` + 一个 `IAnchorDataSource` 实例。

- [ ] **Step 1: 写 AnchorRegistry 失败测试**

```csharp
using System.Collections.Generic;
using NUnit.Framework;

public class AnchorRegistryTests
{
    private AnchorRegistry registry;

    [SetUp]
    public void SetUp()
    {
        var data = new AnchorEntityData
        {
            AnchorId = "anchor_a",
            QuestPayload = "QR_A",
            PicoMarkerId = 3
        };

        registry = new AnchorRegistry();
        registry.SetEntitiesForTesting(new List<AnchorEntityData> { data });
    }

    [Test]
    public void TryResolve_MatchesByQuestPayload()
    {
        Assert.IsTrue(registry.TryResolve("QR_A", out var result));
        Assert.AreEqual("anchor_a", result.AnchorId);
    }

    [Test]
    public void TryResolve_MatchesByPicoMarkerId()
    {
        Assert.IsTrue(registry.TryResolve("3", out var result));
        Assert.AreEqual("anchor_a", result.AnchorId);
    }

    [Test]
    public void TryResolve_ReturnsFalse_WhenNoMatch()
    {
        Assert.IsFalse(registry.TryResolve("unknown", out var result));
        Assert.IsNull(result);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

预期:编译失败,找不到 `AnchorEntityData` / `AnchorRegistry`。

- [ ] **Step 3: 实现 AnchorEntityData**

```csharp
[System.Serializable]
public class AnchorEntityData
{
    public string AnchorId;
    public string QuestPayload;
    public int PicoMarkerId = -1;
    public string ContentImageUrl;
    public string ContentVersion;
}
```

- [ ] **Step 4: 实现 IAnchorDataSource + AnchorRegistry**

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IAnchorDataSource
{
    Task<List<AnchorEntityData>> FetchAsync();
}
```

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;

public class AnchorRegistry
{
    private List<AnchorEntityData> entities = new List<AnchorEntityData>();

    public async Task LoadAsync(IAnchorDataSource dataSource)
    {
        entities = await dataSource.FetchAsync() ?? new List<AnchorEntityData>();
    }

    public bool TryResolve(string rawId, out AnchorEntityData data)
    {
        foreach (var entity in entities)
        {
            if (entity == null) continue;
            if (entity.QuestPayload == rawId || entity.PicoMarkerId.ToString() == rawId)
            {
                data = entity;
                return true;
            }
        }

        data = null;
        return false;
    }

    public void SetEntitiesForTesting(List<AnchorEntityData> list) => entities = list;
}
```

- [ ] **Step 5: 运行测试确认通过**

- [ ] **Step 6: 写 LocalJsonAnchorDataSource 失败测试**

```csharp
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

public class LocalJsonAnchorDataSourceTests
{
    private const string TestFileName = "test_anchor_registry.json";
    private string testFilePath;

    [SetUp]
    public void SetUp()
    {
        testFilePath = Path.Combine(Application.persistentDataPath, TestFileName);

        var json = @"{
            ""Anchors"": [
                { ""AnchorId"": ""anchor_a"", ""QuestPayload"": ""QR_A"", ""PicoMarkerId"": 3 }
            ]
        }";

        File.WriteAllText(testFilePath, json);
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(testFilePath)) File.Delete(testFilePath);
    }

    [Test]
    public async Task FetchAsync_ParsesAnchorsFromLocalJsonFile()
    {
        var source = new LocalJsonAnchorDataSource(TestFileName);

        var result = await source.FetchAsync();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("anchor_a", result[0].AnchorId);
        Assert.AreEqual("QR_A", result[0].QuestPayload);
        Assert.AreEqual(3, result[0].PicoMarkerId);
    }
}
```

- [ ] **Step 7: 运行测试确认失败**

预期:编译失败,找不到 `LocalJsonAnchorDataSource`。同时确认 `FileUtils.Instance` 在测试里能用——EditMode 测试若场景里没有 `FileUtils` 组件实例会拿到 `null`,需要在测试的 `[SetUp]` 里补一个:

```csharp
if (FileUtils.Instance == null)
{
    new GameObject("FileUtils").AddComponent<FileUtils>();
}
```

把这行加进 `LocalJsonAnchorDataSourceTests.SetUp()` 顶部。

- [ ] **Step 8: 实现 LocalJsonAnchorDataSource**

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;

[System.Serializable]
public class AnchorEntityDataListWrapper
{
    public List<AnchorEntityData> Anchors;
}

public class LocalJsonAnchorDataSource : IAnchorDataSource
{
    private readonly string relativeJsonPath;

    public LocalJsonAnchorDataSource(string relativeJsonPath)
    {
        this.relativeJsonPath = relativeJsonPath;
    }

    public async Task<List<AnchorEntityData>> FetchAsync()
    {
        var wrapper = await FileUtils.Instance.LoadJsonByUrlAsync<AnchorEntityDataListWrapper>(relativeJsonPath);
        return wrapper?.Anchors ?? new List<AnchorEntityData>();
    }
}
```

- [ ] **Step 9: 运行测试确认通过**

- [ ] **Step 10: Commit**

```bash
git add Assets/Scripts/Localization/AnchorEntityData.cs Assets/Scripts/Localization/IAnchorDataSource.cs Assets/Scripts/Localization/LocalJsonAnchorDataSource.cs Assets/Scripts/Localization/AnchorRegistry.cs Assets/Tests/EditMode/AnchorRegistryTests.cs Assets/Tests/EditMode/LocalJsonAnchorDataSourceTests.cs
git commit -m "feat: add network-backed AnchorRegistry with local JSON data source"
```

---

### Task 5: PlatformOffsetConfig

**Files:**
- Create: `Assets/Scripts/Localization/PlatformOffsetConfig.cs`
- Test: `Assets/Tests/EditMode/PlatformOffsetConfigTests.cs`

**Interfaces:**
- Produces: `PlatformOffsetConfig { questMarkerToTargetOffset : Pose, picoMarkerToTargetOffset : Pose }`。`MarkerAnchorService`(Task 9)读取其中一个字段做 `PoseMath.Compose` 的第二个参数。这个仍是本地 ScriptableObject 资产(面板设计稿标定值,不是网络锚点表数据)。

- [ ] **Step 1: 写失败测试**

```csharp
using NUnit.Framework;
using UnityEngine;

public class PlatformOffsetConfigTests
{
    [Test]
    public void DefaultOffsets_AreIdentity()
    {
        var config = ScriptableObject.CreateInstance<PlatformOffsetConfig>();

        Assert.AreEqual(Pose.identity.position, config.questMarkerToTargetOffset.position);
        Assert.AreEqual(Pose.identity.rotation, config.questMarkerToTargetOffset.rotation);
        Assert.AreEqual(Pose.identity.position, config.picoMarkerToTargetOffset.position);
        Assert.AreEqual(Pose.identity.rotation, config.picoMarkerToTargetOffset.rotation);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

- [ ] **Step 3: 实现**

```csharp
using UnityEngine;

[CreateAssetMenu(fileName = "PlatformOffsetConfig", menuName = "MRBase/Localization/Platform Offset Config")]
public class PlatformOffsetConfig : ScriptableObject
{
    [Tooltip("Quest QR 码局部坐标系到面板内容锚点的固定偏移。默认 identity,需在面板设计稿定稿后按坐标回填,不要在现场测量。")]
    public Pose questMarkerToTargetOffset = Pose.identity;

    [Tooltip("PICO ArUco 码局部坐标系到面板内容锚点的固定偏移。默认 identity,需在面板设计稿定稿后按坐标回填,不要在现场测量。")]
    public Pose picoMarkerToTargetOffset = Pose.identity;
}
```

- [ ] **Step 4: 运行测试确认通过**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Localization/PlatformOffsetConfig.cs Assets/Tests/EditMode/PlatformOffsetConfigTests.cs
git commit -m "feat: add PlatformOffsetConfig for panel-derived marker offsets"
```

---

### Task 6: IMarkerTrackingProvider + MockMarkerProvider

**Files:**
- Create: `Assets/Scripts/Localization/IMarkerTrackingProvider.cs`
- Create: `Assets/Scripts/Localization/MockMarkerProvider.cs`
- Test: `Assets/Tests/EditMode/MockMarkerProviderTests.cs`

**Interfaces:**
- Produces: `IMarkerTrackingProvider { event Action<string,Pose> MarkerResolved; event Action<string> MarkerLost; void StartTracking(); void StopTracking(); }`;`MockMarkerProvider : IMarkerTrackingProvider` 额外提供 `SimulateMarkerResolved(string, Pose)`、`SimulateMarkerLost(string)`。`MarkerAnchorService`(Task 9)、`QuestMarkerProvider`/`PicoMarkerProvider`(Task 11、12)都实现/消费这个接口。

- [ ] **Step 1: 写失败测试**

```csharp
using NUnit.Framework;
using UnityEngine;

public class MockMarkerProviderTests
{
    [Test]
    public void SimulateMarkerResolved_RaisesMarkerResolvedEvent()
    {
        var provider = new MockMarkerProvider();
        string capturedId = null;
        Pose capturedPose = default;
        provider.MarkerResolved += (id, pose) => { capturedId = id; capturedPose = pose; };

        var testPose = new Pose(new Vector3(1, 2, 3), Quaternion.identity);
        provider.SimulateMarkerResolved("QR_A", testPose);

        Assert.AreEqual("QR_A", capturedId);
        Assert.AreEqual(testPose.position, capturedPose.position);
    }

    [Test]
    public void SimulateMarkerLost_RaisesMarkerLostEvent()
    {
        var provider = new MockMarkerProvider();
        string capturedId = null;
        provider.MarkerLost += id => capturedId = id;

        provider.SimulateMarkerLost("QR_A");

        Assert.AreEqual("QR_A", capturedId);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

- [ ] **Step 3: 实现**

```csharp
using System;
using UnityEngine;

public interface IMarkerTrackingProvider
{
    event Action<string, Pose> MarkerResolved;
    event Action<string> MarkerLost;

    void StartTracking();
    void StopTracking();
}
```

```csharp
using System;
using UnityEngine;

public class MockMarkerProvider : IMarkerTrackingProvider
{
    public event Action<string, Pose> MarkerResolved;
    public event Action<string> MarkerLost;

    public bool IsTracking { get; private set; }

    public void StartTracking() => IsTracking = true;
    public void StopTracking() => IsTracking = false;

    public void SimulateMarkerResolved(string rawId, Pose pose) => MarkerResolved?.Invoke(rawId, pose);
    public void SimulateMarkerLost(string rawId) => MarkerLost?.Invoke(rawId);
}
```

- [ ] **Step 4: 运行测试确认通过**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Localization/IMarkerTrackingProvider.cs Assets/Scripts/Localization/MockMarkerProvider.cs Assets/Tests/EditMode/MockMarkerProviderTests.cs
git commit -m "feat: add IMarkerTrackingProvider and MockMarkerProvider"
```

---

### Task 7: MarkerStabilizer(扫描消抖,移植自 AnchorObject.cs)

**Files:**
- Create: `Assets/Scripts/Localization/MarkerStabilizer.cs`
- Test: `Assets/Tests/EditMode/MarkerStabilizerTests.cs`

**Interfaces:**
- Produces: `MarkerStabilizer(float positionThreshold, float rotationThreshold, float smoothTime, int stableFrameThreshold)` 构造函数;`.Feed(string rawId, Pose rawPose, float deltaTime)`;`event Action<string, Pose> Stabilized`。`MarkerAnchorService`(Task 9)把 provider 的原始事件喂给这个类,只消费 `Stabilized` 事件。

原生 API 吐出来的 pose 逐帧抖动,不能直接用。这个类把 `ite-space-tour` 的 `AnchorObject.cs`(只能同时跟踪一个目标)泛化成按 `rawId` 分别维护稳定状态(因为我们的设计不限制同时只能扫一个标记)。

- [ ] **Step 1: 写失败测试**

```csharp
using NUnit.Framework;
using UnityEngine;

public class MarkerStabilizerTests
{
    [Test]
    public void Feed_SamePoseRepeatedly_FiresStabilizedAfterThreshold()
    {
        var stabilizer = new MarkerStabilizer(positionThreshold: 0.05f, rotationThreshold: 1f, smoothTime: 0.001f, stableFrameThreshold: 3);
        int firedCount = 0;
        string firedId = null;
        stabilizer.Stabilized += (id, pose) => { firedCount++; firedId = id; };

        var pose = new Pose(new Vector3(1, 2, 3), Quaternion.identity);

        stabilizer.Feed("A", pose, 1f);
        Assert.AreEqual(0, firedCount);

        stabilizer.Feed("A", pose, 1f);
        Assert.AreEqual(0, firedCount);

        stabilizer.Feed("A", pose, 1f);
        Assert.AreEqual(1, firedCount);
        Assert.AreEqual("A", firedId);
    }

    [Test]
    public void Feed_PoseKeepsMoving_NeverFiresStabilized()
    {
        var stabilizer = new MarkerStabilizer(positionThreshold: 0.05f, rotationThreshold: 1f, smoothTime: 0.001f, stableFrameThreshold: 3);
        int firedCount = 0;
        stabilizer.Stabilized += (_, __) => firedCount++;

        for (int i = 0; i < 10; i++)
        {
            stabilizer.Feed("A", new Pose(new Vector3(i, 0, 0), Quaternion.identity), 1f);
        }

        Assert.AreEqual(0, firedCount);
    }

    [Test]
    public void Feed_FiresOnce_ThenRefiresOnlyAfterMovingAgain()
    {
        var stabilizer = new MarkerStabilizer(positionThreshold: 0.05f, rotationThreshold: 1f, smoothTime: 0.001f, stableFrameThreshold: 2);
        int firedCount = 0;
        stabilizer.Stabilized += (_, __) => firedCount++;

        var poseA = new Pose(new Vector3(1, 0, 0), Quaternion.identity);
        stabilizer.Feed("A", poseA, 1f);
        stabilizer.Feed("A", poseA, 1f); // 第2次达到阈值,触发
        stabilizer.Feed("A", poseA, 1f); // 仍稳定,不重复触发
        Assert.AreEqual(1, firedCount);

        var poseB = new Pose(new Vector3(5, 0, 0), Quaternion.identity);
        stabilizer.Feed("A", poseB, 1f); // 移动了,重新计数
        stabilizer.Feed("A", poseB, 1f); // 再次达到阈值,重新触发
        Assert.AreEqual(2, firedCount);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

- [ ] **Step 3: 实现**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

public class MarkerStabilizer
{
    private class TrackedMarker
    {
        public Pose SmoothedPose;
        public Pose TargetPose;
        public int StableFrameCount;
        public bool HasFiredStableEvent;
    }

    private readonly float positionThreshold;
    private readonly float rotationThreshold;
    private readonly float smoothTime;
    private readonly int stableFrameThreshold;

    private readonly Dictionary<string, TrackedMarker> tracked = new Dictionary<string, TrackedMarker>();

    public event Action<string, Pose> Stabilized;

    public MarkerStabilizer(float positionThreshold = 0.05f, float rotationThreshold = 1f, float smoothTime = 0.01f, int stableFrameThreshold = 30)
    {
        this.positionThreshold = positionThreshold;
        this.rotationThreshold = rotationThreshold;
        this.smoothTime = smoothTime;
        this.stableFrameThreshold = stableFrameThreshold;
    }

    public void Feed(string rawId, Pose rawPose, float deltaTime)
    {
        if (!tracked.TryGetValue(rawId, out var marker))
        {
            marker = new TrackedMarker { SmoothedPose = rawPose, TargetPose = rawPose };
            tracked[rawId] = marker;
        }

        marker.TargetPose = rawPose;

        float positionDelta = Vector3.Distance(marker.SmoothedPose.position, marker.TargetPose.position);
        float angleDelta = Quaternion.Angle(marker.SmoothedPose.rotation, marker.TargetPose.rotation);
        bool moved = positionDelta > positionThreshold || angleDelta > rotationThreshold;

        if (moved)
        {
            marker.StableFrameCount = 0;
            marker.HasFiredStableEvent = false;
        }
        else
        {
            marker.StableFrameCount++;
        }

        float t = smoothTime > 0f ? Mathf.Clamp01(deltaTime / smoothTime) : 1f;
        marker.SmoothedPose = new Pose(
            Vector3.Lerp(marker.SmoothedPose.position, marker.TargetPose.position, t),
            Quaternion.Slerp(marker.SmoothedPose.rotation, marker.TargetPose.rotation, t));

        if (!marker.HasFiredStableEvent && marker.StableFrameCount >= stableFrameThreshold)
        {
            marker.HasFiredStableEvent = true;
            Stabilized?.Invoke(rawId, marker.SmoothedPose);
        }
    }

    public void Reset(string rawId) => tracked.Remove(rawId);
}
```

- [ ] **Step 4: 运行测试确认通过**

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Localization/MarkerStabilizer.cs Assets/Tests/EditMode/MarkerStabilizerTests.cs
git commit -m "feat: add MarkerStabilizer ported from ite-space-tour AnchorObject debounce logic"
```

**⚠️ 待调优提醒**:阈值(0.05m/1°)与稳定帧数(30)是从 `AnchorObject.cs` 借来的经验值,是在 Quest 上调出来的。PICO 端的抖动特性未经实测,Task 12 真机验证时如果 PICO 上体感不对,回来调这几个构造参数,不代表这里的实现有 bug。

---

### Task 8: IContentLoader + ImageContentLoader + AnchorEntity

**Files:**
- Create: `Assets/Scripts/Localization/IContentLoader.cs`
- Create: `Assets/Scripts/Localization/ImageContentLoader.cs`
- Create: `Assets/Scripts/Localization/AnchorEntity.cs`
- Test: `Assets/Tests/EditMode/AnchorEntityTests.cs`

**Interfaces:**
- Consumes: `FileUtils.Instance.LoadSpriteByUrl`(Task 2)、`AnchorEntityData`(Task 4)。
- Produces: `IContentLoader.LoadImageAsync(string url, string version) : Task<Sprite>`;`AnchorEntity.Create(AnchorEntityData data, IContentLoader loader) : Task`。`MarkerAnchorService`(Task 9)实例化 `AnchorEntity` 并调用 `Create`。

**内容类型范围**:本 Task 只做图片内容(MVP,验证"下载-展示"流程),不做磁盘缓存版本比对(`ContentVersion` 字段先保留但暂不使用于跳过重复下载——那需要额外的本地文件缓存逻辑,属于后续增强,不在本计划范围,已记录在 spec 第 10 节)。模型/视频类内容是后续需求。

- [ ] **Step 1: 写 AnchorEntity 失败测试**

```csharp
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class FakeContentLoader : IContentLoader
{
    public Sprite SpriteToReturn;
    public Task<Sprite> LoadImageAsync(string url, string version) => Task.FromResult(SpriteToReturn);
}

public class AnchorEntityTests
{
    private GameObject hostObject;
    private Texture2D texture;

    [TearDown]
    public void TearDown()
    {
        if (hostObject != null) Object.DestroyImmediate(hostObject);
        if (texture != null) Object.DestroyImmediate(texture);
    }

    [Test]
    public async Task Create_WithValidImage_AddsSpriteRendererChild()
    {
        hostObject = new GameObject("Test");
        var entity = hostObject.AddComponent<AnchorEntity>();
        texture = new Texture2D(1, 1);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f);
        var loader = new FakeContentLoader { SpriteToReturn = sprite };

        var data = new AnchorEntityData { AnchorId = "a", ContentImageUrl = "http://x/y.png", ContentVersion = "1" };

        await entity.Create(data, loader);

        var content = hostObject.transform.Find("Content");
        Assert.IsNotNull(content);
        Assert.AreEqual(sprite, content.GetComponent<SpriteRenderer>().sprite);
    }

    [Test]
    public async Task Create_WithFailedDownload_DoesNotAddContentAndLogsWarning()
    {
        hostObject = new GameObject("Test");
        var entity = hostObject.AddComponent<AnchorEntity>();
        var loader = new FakeContentLoader { SpriteToReturn = null };
        var data = new AnchorEntityData { AnchorId = "a", ContentImageUrl = "http://x/y.png", ContentVersion = "1" };

        LogAssert.Expect(LogType.Warning, "[AnchorEntity] 锚点 a 内容下载失败,本次不展示内容");

        await entity.Create(data, loader);

        Assert.IsNull(hostObject.transform.Find("Content"));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

预期:编译失败,找不到 `IContentLoader` / `AnchorEntity`。

- [ ] **Step 3: 实现 IContentLoader + ImageContentLoader**

```csharp
using System.Threading.Tasks;
using UnityEngine;

public interface IContentLoader
{
    Task<Sprite> LoadImageAsync(string url, string version);
}
```

```csharp
using System.Threading.Tasks;
using UnityEngine;

public class ImageContentLoader : IContentLoader
{
    public async Task<Sprite> LoadImageAsync(string url, string version)
    {
        return await FileUtils.Instance.LoadSpriteByUrl(url, isLocal: false);
    }
}
```

- [ ] **Step 4: 实现 AnchorEntity**

```csharp
using System.Threading.Tasks;
using UnityEngine;

public class AnchorEntity : MonoBehaviour
{
    public async Task Create(AnchorEntityData data, IContentLoader contentLoader)
    {
        name = $"AnchorEntity_{data.AnchorId}";

        if (string.IsNullOrEmpty(data.ContentImageUrl))
        {
            return;
        }

        var sprite = await contentLoader.LoadImageAsync(data.ContentImageUrl, data.ContentVersion);
        if (sprite == null)
        {
            Debug.LogWarning($"[AnchorEntity] 锚点 {data.AnchorId} 内容下载失败,本次不展示内容");
            return;
        }

        var contentObject = new GameObject("Content");
        contentObject.transform.SetParent(transform, false);
        var renderer = contentObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
    }
}
```

- [ ] **Step 5: 运行测试确认通过**

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Localization/IContentLoader.cs Assets/Scripts/Localization/ImageContentLoader.cs Assets/Scripts/Localization/AnchorEntity.cs Assets/Tests/EditMode/AnchorEntityTests.cs
git commit -m "feat: add AnchorEntity with dynamic image content download"
```

---

### Task 9: MarkerAnchorService(核心业务逻辑,串起 Stabilizer → Registry → AnchorEntity)

**Files:**
- Create: `Assets/Scripts/Localization/MarkerAnchorService.cs`
- Test: `Assets/Tests/EditMode/MarkerAnchorServiceTests.cs`

**Interfaces:**
- Consumes: `IMarkerTrackingProvider`(Task 6)、`MarkerStabilizer`(Task 7)、`AnchorRegistry.TryResolve`(Task 4)、`PlatformOffsetConfig`(Task 5)、`PoseMath.Compose`(Task 3)、`AnchorEntity.Create` + `IContentLoader`(Task 8)。
- Produces: `MarkerAnchorService.Initialize(IMarkerTrackingProvider, IAnchorDataSource) : Task`(生产用,内部会拉网络表);`MarkerAnchorService.SetDependenciesForTesting(...)` + `.BindProviderForTesting(IMarkerTrackingProvider)`(测试用)。`MarkerTrackingBootstrapper`(Task 11)调用 `Initialize`。

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class MarkerAnchorServiceTests
{
    private GameObject hostObject;
    private MarkerAnchorService service;
    private AnchorRegistry registry;
    private PlatformOffsetConfig offsetConfig;
    private MockMarkerProvider provider;
    private FakeContentLoader contentLoader;

    [SetUp]
    public void SetUp()
    {
        var data = new AnchorEntityData { AnchorId = "anchor_a", QuestPayload = "QR_A", PicoMarkerId = 3 };

        registry = new AnchorRegistry();
        registry.SetEntitiesForTesting(new List<AnchorEntityData> { data });

        offsetConfig = ScriptableObject.CreateInstance<PlatformOffsetConfig>();
        contentLoader = new FakeContentLoader { SpriteToReturn = null };

        hostObject = new GameObject("MarkerAnchorServiceHost");
        service = hostObject.AddComponent<MarkerAnchorService>();
        service.SetDependenciesForTesting(registry, offsetConfig, contentLoader, new MarkerStabilizer(stableFrameThreshold: 1));

        provider = new MockMarkerProvider();
        service.BindProviderForTesting(provider);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var t in GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.gameObject.name.StartsWith("AnchorEntity_"))
            {
                Object.DestroyImmediate(t.gameObject);
            }
        }
        Object.DestroyImmediate(hostObject);
    }

    [Test]
    public void MarkerResolved_WithMatchingId_CreatesAnchorEntityAtMarkerPose()
    {
        var pose = new Pose(new Vector3(1, 0, 2), Quaternion.identity);

        provider.SimulateMarkerResolved("QR_A", pose);

        var spawned = GameObject.Find("AnchorEntity_anchor_a");
        Assert.IsNotNull(spawned);
        Assert.AreEqual(pose.position, spawned.transform.position);
    }

    [Test]
    public void MarkerResolved_WithUnknownId_DoesNotCreateEntityAndLogsWarning()
    {
        LogAssert.Expect(LogType.Warning, "[MarkerAnchorService] 识别到未匹配的标记ID: UNKNOWN");

        provider.SimulateMarkerResolved("UNKNOWN", Pose.identity);

        Assert.IsNull(GameObject.Find("AnchorEntity_anchor_a"));
    }

    [Test]
    public void MarkerResolved_SameIdTwice_OnlyCreatesOnce()
    {
        provider.SimulateMarkerResolved("QR_A", Pose.identity);
        provider.SimulateMarkerResolved("QR_A", new Pose(Vector3.one, Quaternion.identity));

        int count = 0;
        foreach (var t in GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.gameObject.name == "AnchorEntity_anchor_a") count++;
        }

        Assert.AreEqual(1, count);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

预期:编译失败,找不到 `MarkerAnchorService` 及其方法。

- [ ] **Step 3: 实现 MarkerAnchorService**

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class MarkerAnchorService : MonoBehaviour
{
    [SerializeField] private PlatformOffsetConfig offsetConfig;

    private AnchorRegistry registry;
    private IContentLoader contentLoader;
    private IMarkerTrackingProvider provider;
    private MarkerStabilizer stabilizer;
    private readonly HashSet<string> activeAnchorIds = new HashSet<string>();

    public void SetDependenciesForTesting(AnchorRegistry registryOverride, PlatformOffsetConfig config, IContentLoader loader, MarkerStabilizer stabilizerOverride)
    {
        registry = registryOverride;
        offsetConfig = config;
        contentLoader = loader;
        stabilizer = stabilizerOverride;
        stabilizer.Stabilized += HandleStabilized;
    }

    public void BindProviderForTesting(IMarkerTrackingProvider trackingProvider)
    {
        provider = trackingProvider;
        provider.MarkerResolved += HandleMarkerResolved;
        provider.StartTracking();
    }

    public async Task Initialize(IMarkerTrackingProvider trackingProvider, IAnchorDataSource dataSource)
    {
        registry = new AnchorRegistry();
        await registry.LoadAsync(dataSource);

        contentLoader = new ImageContentLoader();
        stabilizer = new MarkerStabilizer();
        stabilizer.Stabilized += HandleStabilized;

        BindProviderForTesting(trackingProvider);
    }

    private void HandleMarkerResolved(string rawId, Pose rawPose)
    {
        stabilizer.Feed(rawId, rawPose, Time.deltaTime);
    }

    private void HandleStabilized(string rawId, Pose stablePose)
    {
        if (activeAnchorIds.Contains(rawId))
        {
            return;
        }

        if (!registry.TryResolve(rawId, out var data))
        {
            Debug.LogWarning($"[MarkerAnchorService] 识别到未匹配的标记ID: {rawId}");
            return;
        }

        Pose entityPose = PoseMath.Compose(stablePose, ResolvePlatformOffset());

        var entityObject = new GameObject($"AnchorEntity_{data.AnchorId}");
        entityObject.transform.SetPositionAndRotation(entityPose.position, entityPose.rotation);
        var entity = entityObject.AddComponent<AnchorEntity>();
        _ = entity.Create(data, contentLoader);

        activeAnchorIds.Add(rawId);
    }

    private Pose ResolvePlatformOffset()
    {
#if MRBASE_QUEST
        return offsetConfig.questMarkerToTargetOffset;
#elif MRBASE_PICO
        return offsetConfig.picoMarkerToTargetOffset;
#else
        return Pose.identity;
#endif
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

预期三个测试全 PASS(测试里用 `stableFrameThreshold: 1`,所以单次 `SimulateMarkerResolved` 就会立刻触发 `HandleStabilized`;`ResolvePlatformOffset` 在没有 `MRBASE_QUEST`/`MRBASE_PICO` 时走 `#else` 返回 `Pose.identity`,与测试期望一致)。

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Localization/MarkerAnchorService.cs Assets/Tests/EditMode/MarkerAnchorServiceTests.cs
git commit -m "feat: add MarkerAnchorService orchestrating stabilizer, registry, and AnchorEntity"
```

---

### Task 10: 编辑器 Demo 场景(网络数据源 + Mock provider 跑通端到端)

**Files:**
- Create: `Assets/Scenes/LocalizationDemo.unity`
- Create: `Assets/StreamingAssets/demo_anchor_registry.json`(或放 persistentDataPath 说明,见 Step 1)
- Create: `Assets/Scripts/Localization/DemoMarkerTrigger.cs`

**Interfaces:**
- Consumes: `MarkerAnchorService`(Task 9)、`MockMarkerProvider`(Task 6)、`LocalJsonAnchorDataSource`(Task 4)、`FileUtils`(Task 2)。
- Produces: 无新公共接口,是可在编辑器 Play Mode 手动验证整条链路的场景,不需要任何头显,也不需要真实网络。

- [ ] **Step 1: 准备本地模拟数据文件**

`LocalJsonAnchorDataSource` 是从 `Application.persistentDataPath` 读文件的(Task 2 里 `FileUtils.LoadJsonByUrlAsync` 的实现方式)。写一个一次性的 Editor 脚本或直接手动把以下内容写到 `Application.persistentDataPath`(可以在 Play Mode 第一次 `Start()` 里用 `File.WriteAllText` 自动写入,方便重复测试):

```json
{
    "Anchors": [
        { "AnchorId": "demo_anchor", "QuestPayload": "DEMO_QR", "PicoMarkerId": 0, "ContentImageUrl": "", "ContentVersion": "1" }
    ]
}
```

- [ ] **Step 2: 搭建场景**

新建 `Assets/Scenes/LocalizationDemo.unity`:
- 空 GameObject `FileUtilsHost`,挂 `FileUtils` 组件(`StaticInstance` 需要场景里有实例)。
- 空 GameObject `MarkerAnchorServiceHost`,挂 `MarkerAnchorService` 组件,Inspector 里 `Offset Config` 指向新建的 `PlatformOffset_Demo` 资产(`Create > MRBase/Localization/Platform Offset Config`,保持默认 identity)。
- 用 UnityMCP 的 `manage_ui` 或手动加一个 UI Button,点击后调用 `DemoMarkerTrigger.TriggerDemoMarker()`。

- [ ] **Step 3: 实现 DemoMarkerTrigger**

```csharp
using System.IO;
using UnityEngine;

public class DemoMarkerTrigger : MonoBehaviour
{
    [SerializeField] private MarkerAnchorService anchorService;

    private MockMarkerProvider mockProvider;

    private async void Start()
    {
        string demoJson = @"{ ""Anchors"": [ { ""AnchorId"": ""demo_anchor"", ""QuestPayload"": ""DEMO_QR"", ""PicoMarkerId"": 0 } ] }";
        string path = Path.Combine(Application.persistentDataPath, "demo_anchor_registry.json");
        File.WriteAllText(path, demoJson);

        mockProvider = new MockMarkerProvider();
        var dataSource = new LocalJsonAnchorDataSource("demo_anchor_registry.json");
        await anchorService.Initialize(mockProvider, dataSource);
    }

    public void TriggerDemoMarker()
    {
        var pose = new Pose(new Vector3(0, 1.5f, 2), Quaternion.identity);
        mockProvider.SimulateMarkerResolved("DEMO_QR", pose);
    }
}
```

- [ ] **Step 4: 手动验证**

Play Mode 里点按钮,等约 30 帧(`MarkerStabilizer` 默认稳定帧数,Mock 场景下 pose 每次都一样所以很快达标),确认 `AnchorEntity_demo_anchor` 在 `(0, 1.5, 2)` 位置生成(因为示例数据没填 `ContentImageUrl`,不会有可见内容,只验证空实体生成位置和流程走通;想看到内容可以把 `ContentImageUrl` 填一个可访问的图片 URL)。用 `read_console` 确认无报错。

- [ ] **Step 5: Commit**

```bash
git add Assets/Scenes/LocalizationDemo.unity Assets/Scripts/Localization/DemoMarkerTrigger.cs
git commit -m "feat: add editor demo scene wiring network-backed marker anchor flow"
```

---

### Task 11: 接入 Meta XR Core SDK / MRUK,实现 QuestMarkerProvider

**Files:**
- Modify: `Packages/manifest.json`
- Create: `Assets/Scripts/Localization/Native/MRBase.Localization.Native.asmdef`
- Create: `Assets/Scripts/Localization/Native/QuestMarkerProvider.cs`
- Create: `Assets/Scripts/Localization/Native/MarkerTrackingBootstrapper.cs`

**Interfaces:**
- Consumes: `IMarkerTrackingProvider`(Task 6)、`MarkerAnchorService.Initialize`(Task 9)、`IAnchorDataSource`(Task 4)。
- Produces: `QuestMarkerProvider : IMarkerTrackingProvider`(仅 `MRBASE_QUEST` 下编译)、`MarkerTrackingBootstrapper`(运行时按平台选 provider + 数据源)。

- [ ] **Step 1: 查询并安装 Meta XR Core SDK + MRUK**

访问 https://developers.meta.com/horizon/downloads/package/meta-xr-core-sdk/ 确认当前最新版本号(不要用本文档写作时的 v205.0)。按官方说明通过 Package Manager 添加。

- [ ] **Step 2: Quest 场景/项目配置**

- Project Settings → XR Plug-in Management → Android → 勾选 OpenXR,Meta Quest Support 打开。
- 场景里用 Building Blocks 窗口(`Meta > Tools > Building Blocks`)添加 `Camera Rig`、`Passthrough Layer`。
- 选中 `OVRManager` 所在物体,`Scene Support` 设为 `Required`,`Anchor Support` 打开。
- MRUK 设置的 `Tracker Configuration` 勾选 `QR Code Tracking Enabled`。
- Player Settings → Android → 确认 Spatial Data 相关权限已加入 Manifest(以安装的 SDK 版本文档为准)。

- [ ] **Step 3: 创建 Native 程序集**

`Assets/Scripts/Localization/Native/MRBase.Localization.Native.asmdef`:

```json
{
    "name": "MRBase.Localization.Native",
    "rootNamespace": "",
    "references": [
        "MRBase.Localization"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

**⚠️ 需要手动补一步**:在 Project 窗口找到 Meta XR Core SDK / MRUK 包里定义 `MRUKTrackable`/`OVRAnchor` 的 `.asmdef`(通常叫 `Meta.XR.MRUtilityKit` 或类似,以实际安装的包为准),把它的 `name` 加进上面 `references` 数组。

- [ ] **Step 4: 实现 QuestMarkerProvider**

```csharp
#if MRBASE_QUEST
using System;
using UnityEngine;

public class QuestMarkerProvider : IMarkerTrackingProvider
{
    public event Action<string, Pose> MarkerResolved;
    public event Action<string> MarkerLost;

    public void StartTracking()
    {
        MRUK.Instance.TrackableAdded += HandleTrackableAdded;
        MRUK.Instance.TrackableRemoved += HandleTrackableRemoved;
    }

    public void StopTracking()
    {
        MRUK.Instance.TrackableAdded -= HandleTrackableAdded;
        MRUK.Instance.TrackableRemoved -= HandleTrackableRemoved;
    }

    private void HandleTrackableAdded(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;
        if (string.IsNullOrEmpty(trackable.MarkerPayloadString)) return;

        var pose = new Pose(trackable.transform.position, trackable.transform.rotation);
        MarkerResolved?.Invoke(trackable.MarkerPayloadString, pose);
    }

    private void HandleTrackableRemoved(MRUKTrackable trackable)
    {
        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode) return;
        if (string.IsNullOrEmpty(trackable.MarkerPayloadString)) return;

        MarkerLost?.Invoke(trackable.MarkerPayloadString);
    }
}
#endif
```

**⚠️ 验证提醒**:`MRUK.Instance`、`TrackableAdded`/`TrackableRemoved` 的确切静态入口点以调研文档为准,安装 SDK 后如编译报错,以该包的 API 参考/示例场景为准调整,不要凭空猜测签名。

- [ ] **Step 5: 实现 MarkerTrackingBootstrapper**

```csharp
using UnityEngine;

public class MarkerTrackingBootstrapper : MonoBehaviour
{
    [SerializeField] private MarkerAnchorService anchorService;
    [SerializeField] private string anchorRegistryUrl = "anchor_registry.json";

    private async void Start()
    {
        await anchorService.Initialize(CreateProvider(), CreateDataSource());
    }

    private IMarkerTrackingProvider CreateProvider()
    {
#if MRBASE_QUEST
        return new QuestMarkerProvider();
#elif MRBASE_PICO
        return new PicoMarkerProvider();
#else
        Debug.LogError("[MarkerTrackingBootstrapper] 未识别到 MRBASE_QUEST 或 MRBASE_PICO 平台定义,部署配置错误。");
        throw new System.PlatformNotSupportedException("未设置平台 Scripting Define Symbol(MRBASE_QUEST / MRBASE_PICO)");
#endif
    }

    private IAnchorDataSource CreateDataSource()
    {
        // 还没有真实后端时先用本地 JSON;有后端后换成 HttpAnchorDataSource,这里是唯一要改的地方。
        return new LocalJsonAnchorDataSource(anchorRegistryUrl);
    }
}
```

- [ ] **Step 6: 真机验证(Quest)**

切到 `Quest` Build Profile 打包部署真机,按 spec 第 8 节测试计划跑一遍:贴一张只印 QR 的临时测试纸,扫描确认内容按预期 pose 生成、识别成功率、脱离视野后内容是否保留。用 `read_console` 确认无报错。

- [ ] **Step 7: Commit**

```bash
git add Packages/manifest.json Assets/Scripts/Localization/Native
git commit -m "feat: integrate Meta MRUK QR detection via QuestMarkerProvider"
```

---

### Task 12: 接入 PICO Unity Integration SDK,实现 PicoMarkerProvider

**Files:**
- Modify: `Packages/manifest.json`(或导入 `.unitypackage`)
- Create: `Assets/Scripts/Localization/Native/PicoMarkerProvider.cs`

**Interfaces:**
- Consumes: `IMarkerTrackingProvider`(Task 6)。
- Produces: `PicoMarkerProvider : IMarkerTrackingProvider`(仅 `MRBASE_PICO` 下编译),供 `MarkerTrackingBootstrapper`(Task 11 Step 5)的 `#elif MRBASE_PICO` 分支使用。

- [ ] **Step 1: 查询并安装 PICO Unity Integration SDK**

访问 https://github.com/Pico-Developer/PICO-Unity-Integration-SDK/releases 确认当前最新版本(不要用本文档写作时的 v3.4.0)。下载对应 `.unitypackage` 导入项目。

- [ ] **Step 2: PICO Enterprise Marker Tracking 配置**

按 PICO Enterprise SDK 文档(https://developer.picoxr.com/document/unity/ 与 `picoxr/ArUcoMarkerTracking` 示例仓库)完成企业权限校验、Enterprise/Marker Tracking 管理组件挂载、Android Manifest 权限补充。

- [ ] **Step 3: 实现 PicoMarkerProvider**

```csharp
#if MRBASE_PICO
using System;
using UnityEngine;
using Unity.XR.PXR;

public class PicoMarkerProvider : IMarkerTrackingProvider
{
    public event Action<string, Pose> MarkerResolved;
    public event Action<string> MarkerLost;

    public void StartTracking()
    {
        PXR_Enterprise.SetMarkerInfoCallback(HandleMarkerInfo);
    }

    public void StopTracking()
    {
        PXR_Enterprise.SetMarkerInfoCallback(null);
    }

    private void HandleMarkerInfo(int markerId, Vector3 position, Quaternion rotation, bool isTracked)
    {
        string rawId = markerId.ToString();

        if (!isTracked)
        {
            MarkerLost?.Invoke(rawId);
            return;
        }

        MarkerResolved?.Invoke(rawId, new Pose(position, rotation));
    }
}
#endif
```

**⚠️ 验证提醒**:`Unity.XR.PXR` 命名空间、`PXR_Enterprise.SetMarkerInfoCallback` 的确切签名以实际安装的 PICO SDK 版本 API 参考/`picoxr/ArUcoMarkerTracking` 示例代码为准,编译报错就照示例代码改。

- [ ] **Step 4: 把 PICO 引用加进 Native asmdef**

在 `MRBase.Localization.Native.asmdef` 的 `references` 里加入 PICO SDK 定义 `PXR_Enterprise` 所在的程序集名(通常是 `Unity.XR.PXR` 或类似,以实际安装包为准)。

- [ ] **Step 5: 真机验证(PICO)**

切到 `PICO` Build Profile 打包部署真机,贴一张只印 ArUco 的临时测试纸,扫描确认内容按预期 pose 生成、识别成功率。用 `read_console` 确认无报错。同时按 Task 7 的"待调优提醒",实测 `MarkerStabilizer` 的阈值/稳定帧数在 PICO 上是否合适,不合适就调整 `MarkerTrackingBootstrapper` 里构造 `MarkerAnchorService` 时传入的参数(目前 `Initialize` 内部用的是默认参数,如需要按平台调参,给 `MarkerAnchorService.Initialize` 加一个可选的 `MarkerStabilizer` 参数)。

- [ ] **Step 6: Commit**

```bash
git add Packages/manifest.json Assets/Scripts/Localization/Native/PicoMarkerProvider.cs
git commit -m "feat: integrate PICO Enterprise ArUco marker tracking via PicoMarkerProvider"
```

---

### Task 13: 统一面板落地验收

**Files:**
- 无新代码;更新 `PlatformOffsetConfig` 资产的两个字段;准备真实的 `anchor_registry.json`(或真实后端接口)内容。

**Interfaces:**
- 无新接口,是对 Task 11、12 成果的联调验收,对应 spec 第 5、8 节。

- [ ] **Step 1: 制作统一面板**

按 spec 第 5 节设计一版印刷面板:QR(Quest 用)与 ArUco(PICO 用)印在同一刚性面板上,记录设计稿里两个图案各自局部坐标系到面板"内容锚点"的偏移量。

- [ ] **Step 2: 回填 PlatformOffsetConfig**

把 Step 1 算出的偏移值填入 `PlatformOffsetConfig.questMarkerToTargetOffset`、`picoMarkerToTargetOffset`(Inspector 手填,不需要改代码)。

- [ ] **Step 3: 双设备联调验收**

现场把面板贴一次,Quest 和 PICO 分别扫描,核对两台设备生成的内容位置是否一致(核心验收标准)。记录实测误差,若超出可接受范围,回到 Step 1 检查面板设计坐标或印刷精度。

- [ ] **Step 4: Commit**

```bash
git add Assets/**/PlatformOffset*.asset
git commit -m "chore: calibrate platform marker offsets from unified panel design"
```

---

## 自查记录

- **Spec 覆盖**:总体架构(Task 1、6、7、9、11、12)、关键组件(Task 2-9)、物理标记物料规范(Task 13)、数据流与错误处理(Task 9 的去重/未命中/下载失败逻辑)、测试计划(每个 Task 内建 EditMode 测试 + Task 11/12/13 真机验证)、依赖与集成清单(Task 1、2、11、12)均有对应任务覆盖。第 11 节新增的两条决策(网络拉表、动态内容下载,替代 ScriptableObject 方案)已体现在 Task 4、8 里,并注明了参考 `ite-space-tour` 的具体文件。
- **占位符扫描**:已确认无 "TBD"/"待补充" 类占位符;"⚠️ 验证提醒"/"⚠️ 待调优提醒" 都附带了具体验证方法或已知需要调整的原因,不是空话。
- **类型一致性**:`IMarkerTrackingProvider` 的 `MarkerResolved`/`MarkerLost` 签名在 Task 6(接口定义)、Task 9(`MarkerAnchorService` 订阅)、Task 11/12(`QuestMarkerProvider`/`PicoMarkerProvider` 实现)保持一致;`AnchorRegistry.TryResolve(string, out AnchorEntityData)` 在 Task 4 定义、Task 9 使用处一致;`MarkerStabilizer.Feed`/`Stabilized` 签名在 Task 7 定义、Task 9 使用处一致;`IContentLoader.LoadImageAsync` 签名在 Task 8 定义、`AnchorEntity.Create` 使用处一致。
