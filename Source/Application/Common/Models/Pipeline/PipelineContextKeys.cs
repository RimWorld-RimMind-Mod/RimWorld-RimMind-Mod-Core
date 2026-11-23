namespace RimMind.Application.Common.Models.Pipeline
{
    /// <summary>
    /// Canonical keys used in <see cref="LlmRequestContext.Items"/> for cross-middleware communication.
    /// </summary>
    public static class PipelineContextKeys
    {
        public const string NpcProfile = "NpcProfile";
        public const string FallbackContent = "fallback_content";
        public const string RecentActionIntents = "recent_action_intents";
    }
}
