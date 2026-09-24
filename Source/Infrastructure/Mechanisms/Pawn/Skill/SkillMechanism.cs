using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using Verse;
using RimWorld;

namespace RimMind.Infrastructure.Mechanisms.Pawn.Skill
{
    public sealed class SkillMechanism : GameMechanismBaseNoDef
    {
        public override string MechanismId => "pawn.skill";
        public override MechanismScope Scope => MechanismScope.Pawn;
        public override MechanismRisk Risk => MechanismRisk.Moderate;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Query, MechanismOperationType.Set, MechanismOperationType.List }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Query and modify pawn skills",
            QueryDescription = "Query pawn skill levels and passions. Optionally filter by def_name.",
            SetDescription = "Modify a pawn skill. Actions: learn_xp (add experience), set_passion (change passion level).",
            ListDescription = "List all skill definitions available for this pawn."
        };

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            if (!string.IsNullOrEmpty(args.DefName))
            {
                var skill = pawn.skills?.skills?.FirstOrDefault(s => s.def.defName == args.DefName);
                if (skill == null)
                    return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.InvalidDefName(args.DefName)));

                var info = new { def = args.DefName, level = skill.Level, passion = skill.passion.ToString() };
                return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(info)));
            }

            var skills = pawn.skills?.skills?
                .Select(s => new { def = s.def.defName, level = s.Level, passion = s.passion.ToString() })
                .ToList();

            return Task.FromResult(Result<string, RimMindError>.Ok(
                JsonConvert.SerializeObject(skills ?? new object())));
        }

        public override Task<Result<bool, RimMindError>> ExecuteSetAsync(MechanismWriteArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            if (string.IsNullOrEmpty(args.DefName))
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName("")));

            var skill = pawn.skills?.skills?.FirstOrDefault(s => s.def.defName == args.DefName);
            if (skill == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName(args.DefName)));

            if (args.Action == "learn_xp" && float.TryParse(args.ValueJson, out var xp))
            {
                skill.Learn(xp, false);
                return Task.FromResult(Result<bool, RimMindError>.Ok(true));
            }

            if (args.Action == "set_passion" && int.TryParse(args.ValueJson, out var passionLevel)
                && passionLevel >= 0 && passionLevel <= 2)
            {
                skill.passion = (Passion)passionLevel;
                return Task.FromResult(Result<bool, RimMindError>.Ok(true));
            }

            return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, args.Action)));
        }

        public override Task<Result<IReadOnlyList<MechanismEnumResult>, RimMindError>> ExecuteListAsync(int? pawnId, CancellationToken ct)
        {
            var pawn = pawnId.HasValue ? FindPawn(pawnId.Value) : null;
            var results = pawn?.skills?.skills?
                .Select(s => new MechanismEnumResult
                {
                    DefName = s.def.defName,
                    Label = s.def.label ?? s.def.defName,
                    Description = s.def.description
                })
                .ToList() ?? new List<MechanismEnumResult>();

            return Task.FromResult(Result<IReadOnlyList<MechanismEnumResult>, RimMindError>.Ok(results.AsReadOnly()));
        }
    }
}
