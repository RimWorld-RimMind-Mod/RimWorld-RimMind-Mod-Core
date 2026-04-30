using System.Collections.Generic;
using Verse;

namespace RimMind.Core.Prompt
{
    public static class TaskInstructionBuilder
    {
        public static string Build(string keyPrefix, params string[] subKeys)
        {
            var parts = new List<string>();
            foreach (var subKey in subKeys)
            {
                string fullKey = $"{keyPrefix}.{subKey}";
                string translated = fullKey.Translate();
                if (!string.IsNullOrEmpty(translated) && translated != fullKey)
                    parts.Add(translated);
            }
            return parts.Count > 0 ? string.Join("\n\n", parts) : "";
        }
    }
}
