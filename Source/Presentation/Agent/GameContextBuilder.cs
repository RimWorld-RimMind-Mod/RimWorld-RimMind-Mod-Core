using System;
using System.Collections.Generic;
using System.Text;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Prompt;
using RimMind.Application.Features.Context;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Agent;
using RimWorld;
using Verse;

namespace RimMind.Presentation.Agent
{
    /// <summary>
    /// Thin facade that delegates to PawnContextBuilder and MapContextBuilder.
    /// Implements IGameContextBuilder (CollectBasicGameState) and IContextKeyProvider (Extract* methods).
    /// </summary>
    public class GameContextBuilder : IGameContextBuilder, IContextKeyProvider
    {
        private readonly PawnContextBuilder _pawnBuilder;
        private readonly MapContextBuilder _mapBuilder;
        private readonly INpcManagerAccessor _npcManagers;

        public GameContextBuilder(
            PawnContextBuilder pawnBuilder,
            MapContextBuilder mapBuilder,
            INpcManagerAccessor npcManagers)
        {
            _pawnBuilder = pawnBuilder ?? throw new ArgumentNullException(nameof(pawnBuilder));
            _mapBuilder = mapBuilder ?? throw new ArgumentNullException(nameof(mapBuilder));
            _npcManagers = npcManagers ?? throw new ArgumentNullException(nameof(npcManagers));
        }

        // --- IGameContextBuilder ---

        public string CollectBasicGameState(string npcId)
        {
            var sb = new StringBuilder();
            var pawnObj = _npcManagers.Current?.FindPawnByNpcId(npcId);
            var pawn = pawnObj as Pawn;

            if (pawn != null)
            {
                if (pawn.Map != null)
                    sb.AppendLine(_mapBuilder.BuildMapContext(pawn.Map));
                sb.AppendLine(_pawnBuilder.BuildPawnContext(pawn));
            }
            else
            {
                var map = Find.CurrentMap;
                if (map != null)
                    sb.AppendLine(_mapBuilder.BuildMapContext(map));
            }

            return sb.ToString().TrimEnd();
        }

        // --- Instance methods (replacing removed static facade) ---

        public string BuildMapContextInstance(object map, bool brief = false) => map is Map m ? _mapBuilder.BuildMapContext(m, brief) : "";

        // --- IContextKeyProvider ---

        public List<ContextEntry> BuildMapContextEntries(object map)
        {
            if (map is Map m) return _mapBuilder.BuildMapContextEntries(m);
            return new List<ContextEntry>();
        }

        public string ExtractPawnBaseInfo(object pawn) => pawn is Pawn p ? _pawnBuilder.ExtractPawnBaseInfo(p) : "";
        public string ExtractFixedRelations(object pawn) => pawn is Pawn p ? _pawnBuilder.ExtractFixedRelations(p) : "";
        public string ExtractIdeology(object pawn) => pawn is Pawn p ? _pawnBuilder.ExtractIdeology(p) : "";
        public string ExtractSkillsSummary(object pawn) => pawn is Pawn p ? _pawnBuilder.ExtractSkillsSummary(p) : "";
        public string ExtractCurrentArea(object pawn) => pawn is Pawn p ? _pawnBuilder.ExtractCurrentArea(p) : "";
        public string ExtractWeather(object pawn) => pawn is Pawn p ? _pawnBuilder.ExtractWeather(p) : "";
        public string ExtractTimeOfDay(object pawn) => pawn is Pawn p ? _pawnBuilder.ExtractTimeOfDay(p) : "";
        public string ExtractNearbyPawns(object pawn) => pawn is Pawn p ? _pawnBuilder.ExtractNearbyPawns(p) : "";
        public string ExtractSeason(object pawn) => pawn is Pawn p ? _pawnBuilder.ExtractSeason(p) : "";
        public string ExtractColonyStatus(object pawn) => pawn is Pawn p ? _pawnBuilder.ExtractColonyStatus(p) : "";
        public string ExtractHealth(object pawn) => pawn is Pawn p ? _pawnBuilder.ExtractHealth(p) : "";
        public string ExtractMood(object pawn) => pawn is Pawn p ? _pawnBuilder.ExtractMood(p) : "";
        public string ExtractCurrentJob(object pawn) => pawn is Pawn p ? _pawnBuilder.ExtractCurrentJob(p) : "";
        public string ExtractCombatStatus(object pawn) => pawn is Pawn p ? _pawnBuilder.ExtractCombatStatus(p) : "";
        public string ExtractTargetInfo(object pawn) => pawn is Pawn p ? _pawnBuilder.ExtractTargetInfo(p) : "";
        public string ExtractTaskProgress(object pawn) => "";

        // --- Cache reset (no-op after static facade removal) ---
    }
}
