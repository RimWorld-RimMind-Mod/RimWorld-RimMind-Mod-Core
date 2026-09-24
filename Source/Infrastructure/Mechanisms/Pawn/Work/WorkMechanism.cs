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

namespace RimMind.Infrastructure.Mechanisms.Pawn.Work
{
    public sealed class WorkMechanism : GameMechanismBase<WorkTypeDef>
    {
        public override string MechanismId => "pawn.work";
        public override MechanismScope Scope => MechanismScope.Pawn;
        public override MechanismRisk Risk => MechanismRisk.Moderate;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Query, MechanismOperationType.Set, MechanismOperationType.List }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Query and modify pawn work priorities",
            QueryDescription = "Query pawn work priorities for all work types. Optionally filter by def_name.",
            SetDescription = "Set a pawn's work priority. Provide def_name and value (priority 0-4).",
            ListDescription = "List all work type definitions"
        };

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            if (pawn.workSettings == null || !pawn.workSettings.EverWork)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, "pawn cannot work")));

            if (!string.IsNullOrEmpty(args.DefName))
            {
                var workDef = FindDef(args.DefName);
                if (workDef == null)
                    return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.InvalidDefName(args.DefName)));

                var info = new { def = workDef.defName, label = workDef.label, priority = pawn.workSettings.GetPriority(workDef) };
                return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(info)));
            }

            var workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading
                .Where(w => w.visible)
                .Select(w => new { def = w.defName, label = w.label, priority = pawn.workSettings.GetPriority(w) })
                .ToList();

            return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(workTypes)));
        }

        public override Task<Result<bool, RimMindError>> ExecuteSetAsync(MechanismWriteArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            if (pawn.workSettings == null || !pawn.workSettings.EverWork)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, "pawn cannot work")));

            if (string.IsNullOrEmpty(args.DefName))
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName("")));

            var workDef = FindDef(args.DefName);
            if (workDef == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName(args.DefName)));

            if (!int.TryParse(args.ValueJson, out var priority) || priority < 0 || priority > 4)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, "priority must be 0-4")));

            pawn.workSettings.SetPriority(workDef, priority);
            return Task.FromResult(Result<bool, RimMindError>.Ok(true));
        }
    }
}
