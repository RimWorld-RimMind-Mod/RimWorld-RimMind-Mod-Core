using Verse;

namespace RimMind.Core.Context
{
    public class ContextDiff
    {
        public static int DefaultLifetimeTicks => RimMindCoreMod.Settings.contextDiffLifetimeTicks;

        public string Key = null!;
        public ContextLayer Layer;
        public string OldValue = null!;
        public string NewValue = null!;
        public int InsertedTick;
        public int ExpireTick;

        public bool IsExpired(int currentTick) => currentTick >= ExpireTick;

        public string Format()
        {
            return "RimMind.Core.Prompt.StateChange".Translate(Key, OldValue, NewValue);
        }
    }
}
