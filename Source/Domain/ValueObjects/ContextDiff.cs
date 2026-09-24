using System.Text;

namespace RimMind.Domain.ValueObjects
{
    public class ContextDiff
    {
        // Architecture limit: Domain layer cannot reference Application layer's RimMindDefaults. Value mirrors RimMindDefaults.ContextDiffLifetimeTicks.
        public const int DefaultLifetimeTicks = 36000;

        public string Key = "";
        public ContextLayer Layer;
        public string OldValue = "";
        public string NewValue = "";
        public int InsertedTick;
        public int ExpireTick;

        public bool IsExpired(int currentTick)
        {
            return currentTick > ExpireTick;
        }

        public string Format()
        {
            if (string.IsNullOrEmpty(OldValue))
                return $"[{Key}] {NewValue}";
            return $"[{Key}] {OldValue} -> {NewValue}";
        }
    }
}
