using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using Verse;

namespace RimMind.Infrastructure.UI.DebugTables
{
    public static class DebugTableEnumLabels
    {
        public static string For(MechanismScope value)
            => TranslateEnum("RimMind.UI.Enum.MechanismScope.", value.ToString());

        public static string For(MechanismRisk value)
            => TranslateEnum("RimMind.UI.Enum.MechanismRisk.", value.ToString());

        public static string For(MechanismOperationType value)
            => TranslateEnum("RimMind.UI.Enum.MechanismOperation.", value.ToString());

        public static string For(ContextLayer value)
            => TranslateEnum("RimMind.UI.Enum.ContextLayer.", value.ToString());

        public static string For(CacheScope value)
            => TranslateEnum("RimMind.UI.Enum.CacheScope.", value.ToString());

        private static string TranslateEnum(string prefix, string name)
        {
            string key = prefix + name;
            return key.CanTranslate() ? (string)key.Translate() : name;
        }
    }
}
