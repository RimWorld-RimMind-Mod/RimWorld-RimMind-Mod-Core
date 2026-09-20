using System;
using RimWorld;
using Verse;

namespace RimMind.Presentation.Api
{
    /// <summary>
    /// Base class for in-game behavioral autotest suites across RimMind modules.
    /// Encapsulates standard execution logic, Dev menu runner, and result logging.
    /// </summary>
    public abstract class BehaviorAutotestSuiteBase : IInGameBehaviorSuite
    {
        public abstract string ModId { get; }
        public abstract string SuiteId { get; }

        public abstract void RunSuite(IInGameBehaviorSuiteContext context);

        /// <summary>
        /// Shared static helper for running an autotest suite from the RimWorld Dev menu.
        /// Resolves the current map and active colonist, creates context, executes the suite,
        /// and logs pass/fail details.
        /// </summary>
        /// <typeparam name="TSuite">The autotest suite type.</typeparam>
        public static void RunSuiteFromDevMenu<TSuite>() where TSuite : IInGameBehaviorSuite, new()
        {
            var colonist = Find.CurrentMap?.mapPawns?.FreeColonists?.Count > 0
                ? Find.CurrentMap.mapPawns.FreeColonists[0]
                : Find.Selector?.SingleSelectedThing as Pawn;

            var ctx = new InGameBehaviorSuiteContext(colonist, Find.CurrentMap);
            var suite = new TSuite();

            Verse.Log.Message($"[RimMind-{suite.ModId}] Starting in-game behavior autotest ({suite.SuiteId})...");
            suite.RunSuite(ctx);

            string status = ctx.FailCount == 0 ? "PASS" : "FAIL";
            Verse.Log.Message($"[RimMind-{suite.ModId}] Suite completed with status {status}: {ctx.PassCount} passed, {ctx.FailCount} failed.");
            foreach (var detail in ctx.Details)
            {
                Verse.Log.Message($"  {detail}");
            }
        }
    }
}
