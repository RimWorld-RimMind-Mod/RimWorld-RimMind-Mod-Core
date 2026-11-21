using System;

namespace RimMind.Application.Common.Models.Context
{
    public static class ScenarioIds
    {
        public const string Dialogue = "Dialogue";
        public const string Decision = "Decision";
        public const string AgentDecision = "agent_decision";
        public const string AdvisorDecision = "advisor_decision";
        public const string Personality = "Personality";
        public const string Storyteller = "Storyteller";
        public const string Memory = "Memory";
        public const string CrisisDialogue = "CrisisDialogue";

        /// <summary>
        /// Returns true for any autonomous/advisor decision scenario whose mutation tool calls
        /// must be executed by a main-thread coordinator rather than auto-dispatched on background threads.
        /// </summary>
        public static bool IsDecisionScenario(string? scenarioId)
            => string.Equals(scenarioId, Decision, StringComparison.OrdinalIgnoreCase)
            || string.Equals(scenarioId, AgentDecision, StringComparison.OrdinalIgnoreCase)
            || string.Equals(scenarioId, AdvisorDecision, StringComparison.OrdinalIgnoreCase);
    }
}
