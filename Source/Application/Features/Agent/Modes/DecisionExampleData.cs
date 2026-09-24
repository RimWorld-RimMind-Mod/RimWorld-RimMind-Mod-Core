namespace RimMind.Application.Features.Agent.Modes
{
    internal static class DecisionExampleData
    {
        public static readonly (string User, string Assistant)[] Examples = new[]
        {
            ("[mood] Mood: 35% (importance:0.7)\n[need] Need: Food at 20% (importance:0.9)",
             "<Action>{\"action\":\"pawn.job.force_rest\",\"reason\":\"Mood and food needs are critical; rest first to stabilize mood before eating\"}</Action>"),
            ("[combat] Currently drafted for combat (importance:1.0)\n[health] Health issue: Bruise (importance:0.4)",
             "<Action>{\"action\":\"pawn.draft.toggle\",\"reason\":\"Enemy nearby; engage in combat defense despite minor injury\"}</Action>"),
            ("[environment] Environment: Clear, 22\u00b0C (importance:0.1)\n[social] Social: Alice (friend) (importance:0.3)",
             "<Action>{\"action\":\"pawn.work.set\",\"reason\":\"No urgent needs; assign to mining work with friend nearby\",\"param\":\"Mining\"}</Action>"),
            ("[combat] Enemy spotted at range 15 (importance:1.0)",
             "<Action>{\"action\":\"pawn.draft.toggle\",\"reason\":\"Need to engage enemy; drafting first to access combat tools\"}</Action>"),
            ("[tool_result] pawn.draft.toggle succeeded. Now drafted. (importance:0.8)",
             "<Action>{\"action\":\"pawn.equipment.set\",\"reason\":\"Drafted and need ranged weapon for enemy at range 15\",\"param\":\"SniperRifle\"}</Action>"),
        };
    }
}
