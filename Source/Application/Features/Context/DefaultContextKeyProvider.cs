using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Context
{
    public sealed class DefaultContextKeyProvider : IContextKeyProvider
    {
        public List<ContextEntry> BuildMapContextEntries(object map) => new List<ContextEntry>();
        public string ExtractPawnBaseInfo(object pawn) => "";
        public string ExtractFixedRelations(object pawn) => "";
        public string ExtractIdeology(object pawn) => "";
        public string ExtractSkillsSummary(object pawn) => "";
        public string ExtractCurrentArea(object pawn) => "";
        public string ExtractWeather(object pawn) => "";
        public string ExtractTimeOfDay(object pawn) => "";
        public string ExtractNearbyPawns(object pawn) => "";
        public string ExtractSeason(object pawn) => "";
        public string ExtractColonyStatus(object pawn) => "";
        public string ExtractHealth(object pawn) => "";
        public string ExtractMood(object pawn) => "";
        public string ExtractCurrentJob(object pawn) => "";
        public string ExtractCombatStatus(object pawn) => "";
        public string ExtractTargetInfo(object pawn) => "";
        public string ExtractTaskProgress(object pawn) => "";
    }
}
