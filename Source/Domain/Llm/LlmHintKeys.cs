namespace RimMind.Domain.Llm
{
    /// <summary>
    /// Canonical keys used in <see cref="LlmRequestEnvelope.Hints"/>.
    /// Centralizes request-hint keys so pipeline stages, clients, and submodules do not rely on magic strings.
    /// </summary>
    public static class LlmHintKeys
    {
        public const string ProviderId = "ProviderId";
        public const string RolloutId = "rollout_id";
        public const string TurnIndex = "turn_index";
        public const string IsMonologue = "IsMonologue";
        public const string SessionId = "session_id";
    }
}
