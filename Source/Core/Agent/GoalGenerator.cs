using System;
using System.Collections.Generic;
using RimMind.Core.Context;
using Verse;

namespace RimMind.Core.Agent
{
    public static class GoalGenerator
    {
        private static readonly Dictionary<string, Func<string, List<AgentGoal>>> _customGenerators
            = new Dictionary<string, Func<string, List<AgentGoal>>>();

        public static void RegisterGoalGenerator(string eventType, Func<string, List<AgentGoal>> generator)
        {
            if (string.IsNullOrEmpty(eventType) || generator == null) return;
            _customGenerators[eventType] = generator;
        }

        public static void UnregisterGoalGenerator(string eventType)
        {
            _customGenerators.Remove(eventType);
        }

        public static List<AgentGoal> GenerateFromIdentity(Pawn pawn)
        {
            var goals = new List<AgentGoal>();
            var identity = RimMindAPI.GetAgentIdentity(pawn);
            if (identity == null) return goals;

            foreach (var motivation in identity.Motivations)
            {
                var category = InferCategory(motivation);
                goals.Add(new AgentGoal
                {
                    Description = motivation,
                    Category = category,
                    Priority = 3,
                    Status = GoalStatus.Proposed,
                    DeadlineTick = Find.TickManager?.TicksGame + 60000 ?? 0
                });
            }
            return goals;
        }

        public static List<AgentGoal> GenerateFromState(Pawn pawn)
        {
            var goals = new List<AgentGoal>();
            if (pawn == null || pawn.Dead) return goals;

            var mood = pawn.needs?.mood?.CurLevelPercentage ?? 1f;
            if (mood < 0.3f)
            {
                goals.Add(new AgentGoal
                {
                    Description = "Improve mood - find joy or rest",
                    Category = GoalCategory.Survival,
                    Priority = 8,
                    Status = GoalStatus.Proposed,
                    DeadlineTick = Find.TickManager?.TicksGame + 30000 ?? 0
                });
            }

            var food = pawn.needs?.food?.CurLevelPercentage ?? 1f;
            if (food < 0.25f)
            {
                goals.Add(new AgentGoal
                {
                    Description = "Find food - hunger is critical",
                    Category = GoalCategory.Survival,
                    Priority = 9,
                    Status = GoalStatus.Proposed,
                    DeadlineTick = Find.TickManager?.TicksGame + 15000 ?? 0
                });
            }

            if (pawn.health?.HasHediffsNeedingTend() == true)
            {
                goals.Add(new AgentGoal
                {
                    Description = "Get medical treatment",
                    Category = GoalCategory.Survival,
                    Priority = 7,
                    Status = GoalStatus.Proposed,
                    DeadlineTick = Find.TickManager?.TicksGame + 20000 ?? 0
                });
            }

            return goals;
        }

        public static List<AgentGoal> GenerateFromEvent(string eventType, string content)
        {
            if (_customGenerators.TryGetValue(eventType, out var customGen))
            {
                try { return customGen(content) ?? new List<AgentGoal>(); }
                catch (Exception ex) { Log.Warning($"[RimMind] Custom goal generator for '{eventType}' failed: {ex.Message}"); return new List<AgentGoal>(); }
            }
            var goals = new List<AgentGoal>();
            switch (eventType)
            {
                case "raid":
                case "threat":
                    goals.Add(new AgentGoal
                    {
                        Description = "Defend the colony",
                        Category = GoalCategory.Colony,
                        Priority = 10,
                        Status = GoalStatus.Proposed,
                        DeadlineTick = Find.TickManager?.TicksGame + 15000 ?? 0
                    });
                    break;
                case "new_colonist":
                    goals.Add(new AgentGoal
                    {
                        Description = "Welcome new colonist",
                        Category = GoalCategory.Social,
                        Priority = 4,
                        Status = GoalStatus.Proposed,
                        DeadlineTick = Find.TickManager?.TicksGame + 40000 ?? 0
                    });
                    break;
                case "death":
                    goals.Add(new AgentGoal
                    {
                        Description = "Mourn and support others",
                        Category = GoalCategory.Social,
                        Priority = 6,
                        Status = GoalStatus.Proposed,
                        DeadlineTick = Find.TickManager?.TicksGame + 50000 ?? 0
                    });
                    break;
            }
            return goals;
        }

        private static GoalCategory InferCategory(string motivation)
        {
            var lower = motivation.ToLowerInvariant();
            if (lower.Contains("surviv") || lower.Contains("food") || lower.Contains("health"))
                return GoalCategory.Survival;
            if (lower.Contains("social") || lower.Contains("friend") || lower.Contains("love"))
                return GoalCategory.Social;
            if (lower.Contains("work") || lower.Contains("craft") || lower.Contains("build"))
                return GoalCategory.Work;
            if (lower.Contains("self") || lower.Contains("learn") || lower.Contains("improve"))
                return GoalCategory.Self;
            return GoalCategory.Colony;
        }
    }
}
