using System.Collections.Generic;
using UnityEngine;

namespace MRBase.SacredRelic
{
    /// <summary>
    /// Turns a shard's disappearing surface into drifting dust. Two implementations exist so the
    /// look and the cost can be compared side by side; see <see cref="RelicDustBakedPoints"/> and
    /// <see cref="RelicDustVfx"/>.
    /// </summary>
    public abstract class RelicDustSource : MonoBehaviour
    {
        /// <summary>Called once the shard list and its rest transforms are known.</summary>
        public abstract void Prepare(IReadOnlyList<SacredRelicFracture.Shard> shards);

        /// <summary>
        /// The shard's dissolve moved from <paramref name="previous"/> to
        /// <paramref name="current"/>; shed whatever surface that band uncovered.
        /// </summary>
        public abstract void Advance(int shardIndex, float previous, float current);

        /// <summary>Back to sealed: drop every live particle.</summary>
        public abstract void ClearAll();
    }
}
