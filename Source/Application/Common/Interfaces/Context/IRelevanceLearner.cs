using System.Collections.Generic;

namespace RimMind.Application.Common.Interfaces.Context
{
    /// <summary>
    /// Records which context keys were included in prompts and which were actually
    /// referenced by the AI response, providing feedback scores for BudgetScheduler.
    /// </summary>
    public interface IRelevanceLearner
    {
        /// <summary>Record which keys were included in a prompt.</summary>
        void RecordInclusion(string traceId, string scenario, IReadOnlyList<string> includedKeys);

        /// <summary>Record which keys the AI response actually referenced.</summary>
        void RecordUsage(string traceId, IReadOnlyList<string> usedKeys);

        /// <summary>Get the feedback score for a key in a scenario (0-1, default 0.5).</summary>
        float GetFeedbackScore(string scenario, string key);
    }
}
