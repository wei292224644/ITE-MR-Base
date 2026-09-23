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

        /// <summary>
        /// 两个集合的成员是否相同，不看顺序；null 视为空集。集合里的元素本就不重复
        /// （<see cref="TourRegionPolicy.Apply"/> 保证），所以比较数量与逐个包含就够了。
        /// </summary>
        public static bool SameSet(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            int countA = a?.Count ?? 0;
            int countB = b?.Count ?? 0;
            if (countA != countB)
            {
                return false;
            }

            for (int i = 0; i < countA; i++)
            {
                if (!Contains(b, a[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
