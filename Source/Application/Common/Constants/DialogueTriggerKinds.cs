namespace RimMind.Application.Common.Constants
{
    /// <summary>
    /// Canonical dialogue trigger kind identifiers shared between Dialogue and bridge gates
    /// (RimTalk / RimChat) so enum renames never silently break cross-module skip checks.
    /// </summary>
    public static class DialogueTriggerKinds
    {
        public const string Chitchat = "Chitchat";
        public const string Auto = "Auto";
        public const string PlayerInput = "PlayerInput";
        public const string Hediff = "Hediff";
        public const string LevelUp = "LevelUp";
        public const string Thought = "Thought";
    }
}
