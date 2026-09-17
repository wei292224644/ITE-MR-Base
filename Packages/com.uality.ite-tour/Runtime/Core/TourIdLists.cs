using System.Collections.Generic;

namespace Uality.IteTour.Core
{
    /// <summary>TourId 集合上的线性查找。三个策略共用，避免各写一份。</summary>
    public static class TourIdLists
    {
        public static bool Contains(IReadOnlyList<string> ids, string id)
        {
            if (ids == null || string.IsNullOrEmpty(id))
            {
                return false;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
