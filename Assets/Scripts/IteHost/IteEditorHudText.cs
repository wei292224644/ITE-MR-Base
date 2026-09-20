// 桌面验收脚手架：只在 Editor 里存在，不进设备包。
//
// 为什么是 #if UNITY_EDITOR 而不是 Editor-only 的 asmdef：Unity 不允许把 Editor 程序集里的
// MonoBehaviour 挂到 GameObject 上（AddComponent 直接返回 null，场景里的引用变成 Missing）。
// 条件编译能达到同样的目的——类型在播放器构建里根本不存在——而场景与预制体的引用不受影响。
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using Uality.IteTour.Core;

namespace MRBase.Ite.Host
{
    public static class IteEditorHudText
    {
        public static string Format(
            float loadProgress,
            string spaceSceneName,
            IReadOnlyList<string> assembledTourIds,
            string activeTourId,
            ScanPrompt prompt,
            IReadOnlyList<string> pendingVolumeTourIds,
            string lastObserved,
            string lastLost)
        {
            var sb = new StringBuilder();
            sb.Append("Load: ").Append(loadProgress.ToString("F2")).Append('\n');
            sb.Append("Scene: ").Append(string.IsNullOrEmpty(spaceSceneName) ? "(none)" : spaceSceneName).Append('\n');
            sb.Append("Assembled: ").Append(Join(assembledTourIds)).Append('\n');
            sb.Append("Active: ").Append(string.IsNullOrEmpty(activeTourId) ? "(none)" : activeTourId).Append('\n');
            sb.Append("ScanPrompt: ").Append(prompt.State).Append('\n');
            sb.Append("PromptTourIds: ").Append(Join(prompt.TourIds)).Append('\n');
            sb.Append("InVolume: ").Append(Join(pendingVolumeTourIds)).Append('\n');
            sb.Append("LastObserved: ").Append(string.IsNullOrEmpty(lastObserved) ? "(none)" : lastObserved).Append('\n');
            sb.Append("LastLost: ").Append(string.IsNullOrEmpty(lastLost) ? "(none)" : lastLost);
            return sb.ToString();
        }

        private static string Join(IReadOnlyList<string> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return "(empty)";
            }

            return string.Join(", ", ids);
        }
    }
}
#endif
