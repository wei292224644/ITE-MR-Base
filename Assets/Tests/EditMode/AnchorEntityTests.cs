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
