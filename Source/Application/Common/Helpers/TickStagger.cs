namespace RimMind.Application.Common.Helpers
{
    /// <summary>
    /// Deterministic per-entity tick phase so periodic work for many pawns/agents is spread across
    /// ticks instead of all firing on the same tick (e.g. right after a save is loaded).
    /// </summary>
    public static class TickStagger
    {
        /// <summary>Stable phase in <c>[0, interval)</c> derived from an entity id (negative ids allowed).</summary>
        public static int PhaseOffset(int stableId, int interval)
        {
            if (interval <= 1) return 0;
            int offset = stableId % interval;
            return offset < 0 ? offset + interval : offset;
        }

        /// <summary>True on the ticks where <c>(now + phase) % interval == 0</c>; always true for interval &lt;= 1.</summary>
        public static bool IsDue(int now, int stableId, int interval)
        {
            if (interval <= 1) return true;
            long phased = (long)now + PhaseOffset(stableId, interval);
            return phased % interval == 0;
        }

        /// <summary>
        /// Latest tick <c>t &lt;= now</c> that lies on the entity's phase. Using it as a "last run" anchor makes
        /// the next run happen at <c>t + interval</c>, i.e. staggered by entity id.
        /// </summary>
        public static int AlignedTickAtOrBefore(int now, int stableId, int interval)
        {
            if (interval <= 1) return now;
            long phased = (long)now + PhaseOffset(stableId, interval);
            long remainder = phased % interval;
            if (remainder < 0) remainder += interval;
            return (int)(now - remainder);
        }

        /// <summary>
        /// Computes an initial <c>_lastTick</c> anchor for <paramref name="stableId"/> so entities created on the
        /// same tick stagger their first execution across <c>[1, interval]</c>.
        /// </summary>
        public static int InitialLastTick(int stableId, int interval, int now = 0)
            => AlignedTickAtOrBefore(now, stableId, interval);
    }
}
