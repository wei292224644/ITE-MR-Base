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
