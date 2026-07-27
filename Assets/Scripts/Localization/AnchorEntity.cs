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
