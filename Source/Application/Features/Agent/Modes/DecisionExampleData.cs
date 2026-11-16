namespace RimMind.Application.Features.Agent.Modes
{
    internal static class DecisionExampleData
    {
        public static readonly (string User, string Assistant)[] Examples = new[]
        {
            ("[mood] Mood: 35% (importance:0.7)\n[need] Need: Food at 20% (importance:0.9)",
             "<Action>{\"action\":\"pawn.job.eat_food\",\"reason\":\"Food is critically low; eating comes first and also lifts mood\"}</Action>"),
            ("[combat] Enemy spotted at range 15 (importance:1.0)\n[health] Health issue: Bruise (importance:0.4)",
             "<Action>{\"action\":\"pawn.draft.toggle\",\"reason\":\"Enemy nearby; draft to defend despite the minor injury\",\"param\":\"{\\\"action\\\":\\\"draft\\\"}\"}</Action>"),
            ("[environment] Environment: Clear, 22\u00b0C (importance:0.1)\n[social] Social: Alice (friend) (importance:0.3)",
             "<Action>{\"action\":\"pawn.work.set\",\"reason\":\"No urgent needs; prioritize mining to gather resources\",\"param\":\"{\\\"work_type\\\":\\\"Mining\\\",\\\"priority\\\":1}\"}</Action>"),
            ("[combat] No enemies remain on the map (importance:0.8)\n[status] Currently drafted (importance:0.5)",
             "<Action>{\"action\":\"pawn.draft.toggle\",\"reason\":\"Threat is gone; undraft and return to normal work\",\"param\":\"{\\\"action\\\":\\\"undraft\\\"}\"}</Action>"),
        };
    }
}
