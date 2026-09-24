using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using Verse;

namespace RimMind.Infrastructure.UI.DebugTables
{
    public static class DebugTableEnumLabels
    {
        public static string For(MechanismScope value)
            => ("RimMind.UI.Enum.MechanismScope." + value).Translate();

        public static string For(MechanismRisk value)
            => ("RimMind.UI.Enum.MechanismRisk." + value).Translate();

        public static string For(MechanismOperationType value)
            => ("RimMind.UI.Enum.MechanismOperation." + value).Translate();

        public static string For(ContextLayer value)
            => ("RimMind.UI.Enum.ContextLayer." + value).Translate();

        public static string For(CacheScope value)
            => ("RimMind.UI.Enum.CacheScope." + value).Translate();
    }
}
