using System;
using System.Collections.Generic;

namespace Uality.IteTour.Internal
{
    /// <summary>
    /// <see cref="ZipTopLevel.Strip"/> 语义的纯逻辑：从一组条目名里找出唯一的
    /// 公共顶层目录名。做成纯函数是为了它自己可测——不必构造真实 zip 文件。
    /// </summary>
    public static class ZipTopLevelResolver
    {
        /// <summary>
        /// macOS Finder 对文件夹右键"压缩"会额外生成 <c>__MACOSX/</c> 条目
        /// （资源分支/元数据的影子文件）。这是打包侧的工具习惯，不是内容本身，
        /// 判定公共顶层目录时必须忽略它，否则一份规规矩矩打包的内容会被
        /// 误判为"没有公共顶层"。
        /// </summary>
        public static bool IsMacosxEntry(string entryName)
        {
            if (string.IsNullOrEmpty(entryName))
            {
                return false;
            }

            string normalized = Normalize(entryName);
            return normalized == "__MACOSX" || normalized.StartsWith("__MACOSX/", StringComparison.Ordinal);
        }

        /// <summary>
        /// 找出 <paramref name="entryNames"/>（忽略目录条目与 __MACOSX/ 条目后）
        /// 唯一共有的顶层目录名。
        ///
        /// 返回 false 的两种情况：条目为空（忽略之后一个不剩），或条目并非
        /// 全部位于同一个顶层目录之下（包括某条目本身就不在任何目录之下）。
        /// 调用方在 false 时必须报错中止，不能静默按原样落盘。
        /// </summary>
        public static bool TryFindCommonTopLevel(IEnumerable<string> entryNames, out string topLevel)
        {
            topLevel = null;
            string found = null;
            bool any = false;

            foreach (string rawName in entryNames)
            {
                if (string.IsNullOrEmpty(rawName) || IsMacosxEntry(rawName))
                {
                    continue;
                }

                string normalized = Normalize(rawName);
                int slash = normalized.IndexOf('/');
                if (slash <= 0)
                {
                    // 条目不在任何目录之下（或以 '/' 开头），不存在唯一公共顶层。
                    return false;
                }

                string top = normalized.Substring(0, slash);

                if (!any)
                {
                    found = top;
                    any = true;
                }
                else if (!string.Equals(found, top, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            if (!any)
            {
                return false;
            }

            topLevel = found;
            return true;
        }

        private static string Normalize(string entryName) => entryName.Replace('\\', '/').TrimStart('/');
    }
}
