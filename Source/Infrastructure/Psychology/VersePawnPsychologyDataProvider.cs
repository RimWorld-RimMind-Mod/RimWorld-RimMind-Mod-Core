using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Agent.Psychology;
using RimMind.Domain.Agent.Psychology;
using Verse;
using VersePawn = Verse.Pawn;

namespace RimMind.Infrastructure.Psychology
{
    public sealed class VersePawnPsychologyDataProvider : IPawnPsychologyDataProvider
    {
        public float GetMoodLevel(int pawnId)
        {
            var pawn = FindPawn(pawnId);
            return pawn?.needs?.mood?.CurLevel ?? 0.5f;
        }

        public IReadOnlyList<NeedLevel> GetNeedLevels(int pawnId)
        {
            var pawn = FindPawn(pawnId);
            if (pawn?.needs?.AllNeeds == null)
                return new List<NeedLevel>().AsReadOnly();

            return pawn.needs.AllNeeds
                .Select(n => new NeedLevel { NeedId = n.def.defName, CurrentLevel = n.CurLevel })
                .ToList()
                .AsReadOnly();
        }

        public float GetMentalBreakThreshold(int pawnId)
        {
            var pawn = FindPawn(pawnId);
            return pawn?.mindState?.mentalBreaker?.BreakThresholdMajor ?? 0.1f;
        }

        public bool IsInMentalState(int pawnId)
        {
            var pawn = FindPawn(pawnId);
            return pawn?.MentalStateDef != null;
        }

        // Read-only queries stay lenient (any live loaded pawn) but go through the shared lookup:
        // no WorldPawns scan, cache-only off the main thread, negatively cached misses.
        private static VersePawn? FindPawn(int pawnId)
            => Presentation.Api.RimMindPawnLookup.FindPawnByNumber(pawnId);
    }
}
