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
