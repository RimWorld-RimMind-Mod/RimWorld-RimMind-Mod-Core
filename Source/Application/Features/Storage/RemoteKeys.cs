using System;

namespace RimMind.Application.Features.Storage
{
    /// <summary>
    /// Centralized key naming for IRemoteBackend operations.
    /// All keys MUST start with "rimmind:" prefix.
    /// </summary>
    public static class RemoteKeys
    {
        public const string Prefix = "rimmind:";

        public static string PawnMemory(int pawnId) => $"{Prefix}memory:pawn:{pawnId}";
        public static string NarratorMemory() => $"{Prefix}memory:narrator";
        public static string MemoryFullSnapshot() => $"{Prefix}memory:full";
        public static string ContextSettings() => $"{Prefix}settings:context";
        public static string FlywheelParams() => $"{Prefix}settings:flywheel";
        public static string AgentIdentity(int pawnId) => $"{Prefix}agent:identity:{pawnId}";

        public static bool IsValid(string key) => key != null && key.StartsWith(Prefix, StringComparison.Ordinal);
    }
}
