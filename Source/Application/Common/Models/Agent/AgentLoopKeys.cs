using System;

namespace RimMind.Application.Common.Models.Agent
{
    public static class AgentLoopKeys
    {
        public static string ForPawn(int id) => $"pawn:{id}";

        public static string ForScoped(string compositeKey)
        {
            if (compositeKey == null)
                throw new ArgumentNullException(nameof(compositeKey));
            if (string.IsNullOrWhiteSpace(compositeKey))
                throw new ArgumentException("Scoped agent composite key cannot be blank.", nameof(compositeKey));

            return $"scope:{compositeKey}";
        }
    }
}
