using System.Threading.Tasks;
using UnityEngine;

public interface IContentLoader
{
    Task<Sprite> LoadImageAsync(string url, string version);
}
