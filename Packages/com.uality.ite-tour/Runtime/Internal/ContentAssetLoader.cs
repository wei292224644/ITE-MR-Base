using System.IO;
using System.Threading.Tasks;
using GLTFast;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Uality.IteTour.Internal
{
    /// <summary>
    /// 从缓存目录读取 ITE 内容资源。全部走 Unity 内置模块与包依赖，
    /// 不向宿主索取任何 IO 能力（design D4）。
    ///
    /// 沿用源实现的约定：**文件不存在时静默返回 null/default，不抛异常**。
    /// tour 里缺某类可选资源（比如没有音频）是正常情况，抛异常会让整个
    /// tour 加载不出来。调用方据返回值决定跳过还是报错。
    /// </summary>
    public static class ContentAssetLoader
    {
        /// <summary>把相对路径解析成缓存目录下的绝对路径。</summary>
        public static string Resolve(string relativePath)
            => Path.Combine(Application.persistentDataPath, relativePath);

        public static async Task<T> LoadJsonAsync<T>(string relativePath, JsonSerializerSettings settings = null)
        {
            string uri = Resolve(relativePath);

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

        public static async Task<Texture2D> LoadTextureAsync(string relativePath)
        {
            string uri = Resolve(relativePath);
            if (!File.Exists(uri))
            {
                return null;
            }

            using (UnityWebRequest www = UnityWebRequestTexture.GetTexture("file://" + uri))
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

        public static async Task<Sprite> LoadSpriteAsync(string relativePath)
        {
            Texture2D texture = await LoadTextureAsync(relativePath);
            if (texture == null)
            {
                return null;
            }

            return Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));
        }

        public static async Task<AudioClip> LoadAudioClipAsync(string relativePath)
        {
            string uri = Resolve(relativePath);
            if (!File.Exists(uri))
            {
                return null;
            }

            uri = "file://" + uri;

            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG))
            {
                www.downloadHandler = new DownloadHandlerAudioClip(uri, AudioType.MPEG);
                await www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    return null;
                }

                return DownloadHandlerAudioClip.GetContent(www);
            }
        }

        public static async Task<GltfImport> LoadGlbAsync(string relativePath)
        {
            string uri = Resolve(relativePath);
            if (!File.Exists(uri))
            {
                return null;
            }

            var gltf = new GltfImport();
            bool success = await gltf.Load(uri);

            return success ? gltf : null;
        }

        /// <summary>从远端拉 JSON。用于版本查询等不落盘的请求（原宿主 <c>FetchUtils</c>）。</summary>
        public static async Task<T> FetchJsonAsync<T>(string absoluteUrl)
        {
            using (UnityWebRequest www = UnityWebRequest.Get(absoluteUrl))
            {
                www.downloadHandler = new DownloadHandlerBuffer();
                await www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[IteTour] 请求失败: {absoluteUrl} — {www.error}");
                    return default;
                }

                return JsonConvert.DeserializeObject<T>(www.downloadHandler.text);
            }
        }
    }
}
