using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using Verse;

namespace RimMind.Infrastructure.Mechanisms.Pawn.Draft
{
    public sealed class DraftMechanism : GameMechanismBaseNoDef
    {
        public override string MechanismId => "pawn.draft";
        public override MechanismScope Scope => MechanismScope.Pawn;
        public override MechanismRisk Risk => MechanismRisk.Moderate;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Query, MechanismOperationType.Toggle }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Toggle pawn draft status",
            QueryDescription = "Query whether the pawn is currently drafted",
            ToggleDescription = "Toggle pawn draft status. Actions: draft, undraft. Optional: urgent=true for emergency draft."
        };

        private static readonly IReadOnlyList<MechanismActionInfo> _writeActions =
            new List<MechanismActionInfo>
            {
                new MechanismActionInfo { Action = "draft", Description = "Draft the pawn into combat mode" },
                new MechanismActionInfo { Action = "undraft", Description = "Remove the pawn from combat mode" }
            }.AsReadOnly();

        public override IReadOnlyList<MechanismActionInfo>? GetWriteActions() => _writeActions;

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            var info = new
            {
                drafted = pawn.drafter?.Drafted ?? false,
                fireAtWill = pawn.drafter?.FireAtWill ?? true
            };

            return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(info)));
        }

        public override Task<Result<bool, RimMindError>> ExecuteToggleAsync(MechanismWriteArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            if (pawn.drafter == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, "drafter is null")));

            switch (args.Action)
            {
                case "draft":
                    pawn.drafter.Drafted = true;
                    return Task.FromResult(Result<bool, RimMindError>.Ok(true));
                case "undraft":
                    pawn.drafter.Drafted = false;
                    return Task.FromResult(Result<bool, RimMindError>.Ok(true));
                default:
                    return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, args.Action)));
            }
        }
    }
}
