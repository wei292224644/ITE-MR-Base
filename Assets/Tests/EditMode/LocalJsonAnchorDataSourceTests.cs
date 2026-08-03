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
        if (FileUtils.Instance == null)
        {
            var fileUtils = new GameObject("FileUtils").AddComponent<FileUtils>();
            FileUtils.BindInstanceForTesting(fileUtils);
        }

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
