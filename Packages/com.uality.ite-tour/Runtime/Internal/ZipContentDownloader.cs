using System.IO;
using System.Threading.Tasks;
using Unity.SharpZipLib.Zip;
using UnityEngine;
using UnityEngine.Networking;

namespace Uality.IteTour.Internal
{
    /// <summary>
    /// ITE 内容以 zip 分发。下载、解压、缓存都归包所有——宿主不该知道
    /// tour 是以什么格式传输的（design D4）。
    /// </summary>
    public static class ZipContentDownloader
    {
        /// <summary>
        /// 下载 zip 并解压到 <c>persistentDataPath/{relativeFolder}</c>。
        /// 成功返回 true。源实现失败时静默 return，这里返回结果并记日志——
        /// 否则调用方只能等到后续读文件失败才知道出了事。
        /// </summary>
        public static async Task<bool> DownloadAndExtractAsync(string url, string relativeFolder = "")
        {
            string filename = Path.GetFileNameWithoutExtension(url);
            string zipPath = Path.Combine(Application.persistentDataPath, filename + ".zip");
            string outputFolder = Path.Combine(Application.persistentDataPath, relativeFolder);

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.downloadHandler = new DownloadHandlerBuffer();
                await www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[IteTour] 内容包下载失败: {url} — {www.error}");
                    return false;
                }

                File.WriteAllBytes(zipPath, www.downloadHandler.data);
            }

            try
            {
                await ExtractAsync(zipPath, outputFolder);
            }
            finally
            {
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
            }

            return true;
        }

        /// <summary>
        /// 解压。逃出 <paramref name="outputFolder"/> 的条目会被跳过并告警
        /// （见 <see cref="ZipEntryPath"/>）。
        /// </summary>
        public static async Task ExtractAsync(string zipFilePath, string outputFolder)
        {
            if (!File.Exists(zipFilePath))
            {
                return;
            }

            await Task.Run(() =>
            {
                using (FileStream fs = File.OpenRead(zipFilePath))
                using (ZipInputStream zipStream = new ZipInputStream(fs))
                {
                    ZipEntry entry;
                    while ((entry = zipStream.GetNextEntry()) != null)
                    {
                        if (!ZipEntryPath.TryResolve(outputFolder, entry.Name, out string entryPath))
                        {
                            Debug.LogWarning($"[IteTour] 跳过越界的 zip 条目: {entry.Name}");
                            continue;
                        }

                        string dir = Path.GetDirectoryName(entryPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        if (entry.IsDirectory)
                        {
                            continue;
                        }

                        using (FileStream streamWriter = File.Create(entryPath))
                        {
                            byte[] buffer = new byte[4096];
                            int size;
                            while ((size = zipStream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                streamWriter.Write(buffer, 0, size);
                            }
                        }
                    }
                }
            });
        }
    }
}
