using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimMind.Presentation.Api
{
    /// <summary>
    /// Contract for in-game behavioral autotest suites across RimMind modules.
    /// Discovered dynamically at runtime by BehaviorAutotestRunner.
    /// </summary>
    public interface IInGameBehaviorSuite
    {
        string ModId { get; }
        string SuiteId { get; }
        void RunSuite(IInGameBehaviorSuiteContext context);
    }

    /// <summary>
    /// Context provided to in-game autotest suites containing map/pawn entities and assertion recorders.
    /// </summary>
    public interface IInGameBehaviorSuiteContext
    {
        Pawn? ActiveColonist { get; }
        Map? CurrentMap { get; }
        int PassCount { get; }
        int FailCount { get; }
        void Assert(bool condition, string checkDescription);
        void LogDetail(string detail);
        void Warn(string warning);
    }

    /// <summary>
    /// Default standalone context implementation for in-game execution and logging.
    /// </summary>
    public sealed class InGameBehaviorSuiteContext : IInGameBehaviorSuiteContext
    {
        public Pawn? ActiveColonist { get; }
        public Map? CurrentMap { get; }
        public int PassCount { get; private set; }
        public int FailCount { get; private set; }
        public List<string> Details { get; } = new();

        public InGameBehaviorSuiteContext(Pawn? colonist, Map? map)
        {
            ActiveColonist = colonist;
            CurrentMap = map;
        }

        public void Assert(bool condition, string checkDescription)
        {
            if (condition)
            {
                PassCount++;
                Details.Add($"[PASS] {checkDescription}");
            }
            else
            {
                FailCount++;
                Details.Add($"[FAIL] {checkDescription}");
            }
        }

        public void LogDetail(string detail)
        {
            Details.Add(detail);
        }

        public void Warn(string warning)
        {
            Details.Add($"[WARN] {warning}");
        }
    }
}
