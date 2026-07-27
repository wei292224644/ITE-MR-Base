using System.Threading.Tasks;
using UnityEngine;

public class ImageContentLoader : IContentLoader
{
    public async Task<Sprite> LoadImageAsync(string url, string version)
    {
        return await FileUtils.Instance.LoadSpriteByUrl(url, isLocal: false);
    }
}
