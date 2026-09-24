using System.Collections.Generic;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Context
{
    public interface IContextKeyProvider
    {
        List<ContextEntry> BuildMapContextEntries(object map);
        string ExtractPawnBaseInfo(object pawn);
        string ExtractFixedRelations(object pawn);
        string ExtractIdeology(object pawn);
        string ExtractSkillsSummary(object pawn);
        string ExtractCurrentArea(object pawn);
        string ExtractWeather(object pawn);
        string ExtractTimeOfDay(object pawn);
        string ExtractNearbyPawns(object pawn);
        string ExtractSeason(object pawn);
        string ExtractColonyStatus(object pawn);
        string ExtractHealth(object pawn);
        string ExtractMood(object pawn);
        string ExtractCurrentJob(object pawn);
        string ExtractCombatStatus(object pawn);
        string ExtractTargetInfo(object pawn);
        string ExtractTaskProgress(object pawn);
    }
}
