using System;
using System.Collections.Generic;
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
        /// <param name="topLevel">
        /// 空间场景包与 tour 包的顶层目录布局相反（design D1），调用方必须显式选择：
        /// 空间场景包传 <see cref="ZipTopLevel.Strip"/>，tour 包传 <see cref="ZipTopLevel.Preserve"/>。
        /// </param>
        public static async Task<bool> DownloadAndExtractAsync(
            string url, string relativeFolder = "", ZipTopLevel topLevel = ZipTopLevel.Preserve)
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
                await ExtractAsync(zipPath, outputFolder, topLevel);
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
        /// （见 <see cref="ZipEntryPath"/>）。<c>__MACOSX/</c> 条目（macOS Finder
        /// 压缩产物）恒被忽略。
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// <paramref name="topLevel"/> 为 <see cref="ZipTopLevel.Strip"/>，但条目并非全部
        /// 位于同一个顶层目录之下——MUST NOT 静默按原样落盘。
        /// </exception>
        public static Task ExtractAsync(
            string zipFilePath, string outputFolder, ZipTopLevel topLevel = ZipTopLevel.Preserve)
            => Task.Run(() => ExtractSync(zipFilePath, outputFolder, topLevel));

        /// <summary>
        /// <see cref="ExtractAsync"/> 的同步核心实现。刻意拆成同步方法单独公开——
        /// EditMode 测试跑在 Unity 自己的 Test Runner 里，<c>async Task</c> 测试方法
        /// 一旦内部再 <c>await Task.Run(...)</c>，续体（continuation）要抢的正是被测试
        /// 框架占着的主线程，测出过一次真实死锁（Editor 主线程卡死、CPU 却是空的）。
        /// 让测试直接调这个同步方法，从根上不涉及任何 <c>async</c>/<c>await</c>。
        /// </summary>
        public static void ExtractSync(
            string zipFilePath, string outputFolder, ZipTopLevel topLevel = ZipTopLevel.Preserve)
        {
            if (!File.Exists(zipFilePath))
            {
                return;
            }

            using (ZipFile zipFile = new ZipFile(zipFilePath))
            {
                string stripPrefix = ResolveStripPrefix(zipFile, topLevel, zipFilePath);

                foreach (ZipEntry entry in zipFile)
                {
                    if (ZipTopLevelResolver.IsMacosxEntry(entry.Name))
                    {
                        continue;
                    }

                    if (!TryStrip(entry.Name, stripPrefix, out string entryName))
                    {
                        // 顶层目录条目本身，剥掉之后已无内容，跳过。
                        continue;
                    }

                    if (!ZipEntryPath.TryResolve(outputFolder, entryName, out string entryPath))
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

                    using (Stream zipStream = zipFile.GetInputStream(entry))
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
        }

        private static string ResolveStripPrefix(ZipFile zipFile, ZipTopLevel topLevel, string zipFilePath)
        {
            if (topLevel != ZipTopLevel.Strip)
            {
                return null;
            }

            var names = new List<string>();
            foreach (ZipEntry entry in zipFile)
            {
                names.Add(entry.Name);
            }

            if (!ZipTopLevelResolver.TryFindCommonTopLevel(names, out string top))
            {
                throw new InvalidDataException(
                    $"[IteTour] zip 内容不存在唯一公共顶层目录，无法按 Strip 语义解压: {zipFilePath}");
            }

            return top + "/";
        }

        /// <summary>
        /// 剥掉 <paramref name="stripPrefix"/>。<paramref name="stripPrefix"/> 为 null 时原样返回。
        /// 条目正是顶层目录本身（剥掉后一无所剩）时返回 false。
        /// </summary>
        private static bool TryStrip(string rawEntryName, string stripPrefix, out string entryName)
        {
            entryName = rawEntryName;

            if (stripPrefix == null)
            {
                return true;
            }

            string normalized = rawEntryName.Replace('\\', '/');
            if (!normalized.StartsWith(stripPrefix, StringComparison.Ordinal))
            {
                entryName = normalized;
                return true;
            }

            string stripped = normalized.Substring(stripPrefix.Length);
            if (stripped.Length == 0)
            {
                return false;
            }

            entryName = stripped;
            return true;
        }
    }
}
