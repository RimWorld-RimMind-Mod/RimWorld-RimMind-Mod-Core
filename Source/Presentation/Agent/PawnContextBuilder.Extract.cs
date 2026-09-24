using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Prompt;
using RimMind.Presentation.Agent;
using UnityEngine;
using Verse;

namespace RimMind.Presentation.Agent
{
    public sealed partial class PawnContextBuilder
    {
        // --- Extract methods for IContextKeyProvider ---

        public string ExtractPawnBaseInfo(Pawn pawn)
        {
            if (pawn == null) return "";
            var data = PawnDataExtractor.Extract(pawn, _logSink);
            var parts = new List<string>();
            parts.Add(data.Name);
            parts.Add($"{data.Age}yo");
            parts.Add(data.GenderLabel);
            parts.Add(data.RaceLabel);
            if (data.ChildhoodTitle != null)
                parts.Add(data.ChildhoodTitle);
            if (data.AdulthoodTitle != null)
                parts.Add(data.AdulthoodTitle);
            if (data.TraitLabels.Length > 0)
                parts.Add($"Traits: {string.Join(", ", data.TraitLabels)}");
            return string.Join(" | ", parts);
        }

        public string ExtractFixedRelations(Pawn pawn)
        {
            if (pawn == null) return "";
            var data = PawnDataExtractor.Extract(pawn, _logSink);
            if (data.Relations.Count == 0) return "";
            return string.Join(", ", data.Relations.Select(r => $"{r.RelationLabel}({r.OtherName})"));
        }

        public string ExtractIdeology(Pawn pawn)
        {
            if (pawn == null) return "";
            var data = PawnDataExtractor.Extract(pawn, _logSink);
            if (data.IdeologyName == null) return "";
            return $"{data.IdeologyName}{data.IdeologyMemes}";
        }

        public string ExtractSkillsSummary(Pawn pawn)
        {
            if (pawn == null) return "";
            var data = PawnDataExtractor.Extract(pawn, _logSink);
            if (data.Skills.Count == 0) return "";
            var top = data.Skills
                .OrderByDescending(s => s.Value)
                .Take(MaxSkillDisplay)
                .Select(s => $"{s.Key}({s.Value})");
            return string.Join("  ", top);
        }

        public string ExtractCurrentArea(Pawn pawn)
        {
            if (pawn == null) return "";
            var data = PawnDataExtractor.Extract(pawn, _logSink);
            if (!data.HasMap) return "";
            int temp = Mathf.RoundToInt(data.Temperature);
            return $"{data.RoomLabel}, {temp}°C";
        }

        public string ExtractWeather(Pawn pawn)
        {
            if (pawn == null) return "";
            var data = PawnDataExtractor.Extract(pawn, _logSink);
            return data.WeatherLabel ?? "";
        }

        public string ExtractTimeOfDay(Pawn pawn)
        {
            if (pawn == null) return "";
            var data = PawnDataExtractor.Extract(pawn, _logSink);
            return data.TimeString ?? "";
        }

        public string ExtractNearbyPawns(Pawn pawn)
        {
            if (pawn == null) return "";
            var data = PawnDataExtractor.Extract(pawn, _logSink);
            return !string.IsNullOrEmpty(data.NearbyPawnNames) ? data.NearbyPawnNames : "";
        }

        public string ExtractSeason(Pawn pawn)
        {
            if (pawn == null) return "";
            var data = PawnDataExtractor.Extract(pawn, _logSink);
            return data.SeasonLabel ?? "";
        }

        public string ExtractColonyStatus(Pawn pawn)
        {
            if (pawn == null) return "";
            var data = PawnDataExtractor.Extract(pawn, _logSink);
            if (!data.HasMap) return "";
            return "RimMind.Prompt.Colony.Status".Translate(data.ColonistCount, $"{data.ColonyWealth:F0}", data.ThreatCount);
        }

        public string ExtractHealth(Pawn pawn)
        {
            if (pawn == null) return "";
            var data = PawnDataExtractor.Extract(pawn, _logSink);
            var notable = data.Hediffs
                .Where(h => h.Visible)
                .Select(h => h.HediffLabel)
                .Take(MaxHealthTagDisplay)
                .ToList();
            return notable.Count > 0 ? string.Join(", ", notable) : "RimMind.Prompt.Health.Healthy".Translate();
        }

        public string ExtractMood(Pawn pawn)
        {
            if (pawn == null) return "";
            var data = PawnDataExtractor.Extract(pawn, _logSink);
            if (data.MoodString == null) return "";
            if (data.InMentalState)
                return "RimMind.Prompt.Mood.MentalBreak".Translate(data.MentalStateInspectLine ?? "");
            return $"{data.MoodPercent:F0}%";
        }

        public string ExtractCurrentJob(Pawn pawn)
        {
            if (pawn == null) return "";
            var data = PawnDataExtractor.Extract(pawn, _logSink);
            return data.CurrentJobReport ?? data.CurrentJobDefLabel ?? "RimMind.Prompt.Job.Idle".Translate();
        }

        public string ExtractCombatStatus(Pawn pawn)
        {
            if (pawn == null) return "";
            var data = PawnDataExtractor.Extract(pawn, _logSink);
            var parts = new List<string>();
            if (data.Drafted) parts.Add("RimMind.Prompt.Combat.Drafted".Translate());
            if (data.EnemyTargetLabel != null)
                parts.Add("RimMind.Prompt.Combat.Fighting".Translate(data.EnemyTargetLabel));
            return parts.Count > 0 ? string.Join(" | ", parts) : "RimMind.Prompt.Combat.NotInCombat".Translate();
        }

        public string ExtractTargetInfo(Pawn pawn)
        {
            if (pawn == null) return "";
            var data = PawnDataExtractor.Extract(pawn, _logSink);
            if (data.EnemyTargetLabel == null) return "";
            string label = data.EnemyTargetHpPercent.HasValue
                ? $"{data.EnemyTargetLabel} (HP:{data.EnemyTargetHpPercent.Value:F0}%)"
                : data.EnemyTargetLabel;
            return "RimMind.Prompt.Target.Info".Translate(label);
        }
    }
}
