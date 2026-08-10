using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace MRBase.GsplatBench
{
    /// <summary>
    /// 落盘日志。刻意**不走 Debug.Log**：
    /// 编辑器里每条 Debug.Log 都要收集栈、进 Console 列表、触发重绘，
    /// 而这套装置本来就要每隔几秒吐一整份报告 —— 那点开销会直接混进被测数字里。
    /// 只有生命周期里程碑（就绪、sweep 起止、文件路径）才走 Console。
    /// </summary>
    public static class BenchLog
    {
        public const string Tag = "GSPLAT_BENCH";

        static string s_path;
        static bool s_failed;

        public static string Path
        {
            get
            {
                if (string.IsNullOrEmpty(s_path))
                {
                    var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    s_path = System.IO.Path.Combine(Application.persistentDataPath, $"gsplat-bench-{stamp}.log");
                }

                return s_path;
            }
        }

        /// <summary>写一行，带毫秒时间戳。IO 失败只警告一次，绝不让日志把测量拖死。</summary>
        public static void Write(string text)
        {
            if (s_failed)
                return;

            try
            {
                File.AppendAllText(Path,
                    DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + text + "\n");
            }
            catch (Exception e)
            {
                s_failed = true;
                Debug.LogWarning($"{Tag}|log write failed, further writes disabled: {e.Message}");
            }
        }

        /// <summary>里程碑：既进文件也进 Console，方便在 logcat 里找到文件路径。</summary>
        public static void Milestone(string text)
        {
            Write(text);
            Debug.Log(Tag + "|" + text);
        }
    }
}
