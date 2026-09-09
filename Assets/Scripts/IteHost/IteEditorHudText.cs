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
