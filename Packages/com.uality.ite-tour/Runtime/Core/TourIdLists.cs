using System.Collections.Generic;

namespace Uality.IteTour.Core
{
    /// <summary>TourId 列表上的线性查找。</summary>
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
