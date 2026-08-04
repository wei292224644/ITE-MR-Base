using System;
using System.IO;

namespace Uality.IteTour.Internal
{
    /// <summary>
    /// 解压时把 zip 条目名解析成落盘路径，并确保它不会逃出解压目录。
    ///
    /// zip 内容来自远端服务器，是信任边界。源实现直接
    /// <c>Path.Combine(outputFolder, entry.Name)</c> 且不做任何校验，
    /// 一个名为 <c>../../x</c> 的条目就能覆盖解压目录之外的文件（zip slip）。
    ///
    /// 这是本次**有意偏离**「行为等价优先」的一处：安全防护不在 D12 的豁免范围内。
    /// 做成纯函数是为了它自己可测——直接在解压流程里断言的话，需要构造恶意 zip。
    /// </summary>
    public static class ZipEntryPath
    {
        /// <summary>
        /// 解析成功返回 true 并给出绝对路径；条目名逃出 <paramref name="outputFolder"/> 时返回 false。
        /// </summary>
        public static bool TryResolve(string outputFolder, string entryName, out string fullPath)
        {
            fullPath = null;

            if (string.IsNullOrEmpty(outputFolder) || string.IsNullOrEmpty(entryName))
            {
                return false;
            }

            string root = Path.GetFullPath(outputFolder);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                root += Path.DirectorySeparatorChar;
            }

            string candidate;
            try
            {
                // 条目名若是绝对路径，Path.Combine 会直接返回它 —— 下面的前缀检查照样拦得住。
                candidate = Path.GetFullPath(Path.Combine(root, entryName));
            }
            catch (ArgumentException)
            {
                // 条目名含非法字符
                return false;
            }

            if (!candidate.StartsWith(root, StringComparison.Ordinal))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }
    }
}
