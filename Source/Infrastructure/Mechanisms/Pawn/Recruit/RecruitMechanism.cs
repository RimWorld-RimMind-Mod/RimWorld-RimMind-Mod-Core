using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using Verse;
using RimWorld;

namespace RimMind.Infrastructure.Mechanisms.Pawn.Recruit
{
    public sealed class RecruitMechanism : GameMechanismBaseNoDef, IMechanismScopeMetadata
    {
        public override string MechanismId => "pawn.recruit";
        public override MechanismScope Scope => MechanismScope.Pawn;
        public override MechanismRisk Risk => MechanismRisk.Dangerous;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        public ToolScope? GetToolScopeForOperation(MechanismOperationType operation)
            => operation is MechanismOperationType.Query or MechanismOperationType.List
                ? ToolScope.MetaQuery
                : ToolScope.Social | ToolScope.ColonistSelf;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Trigger }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Recruit a prisoner or factionless pawn into the colony",
            TriggerDescription = "Recruit the target pawn into the player's faction. This is a dangerous operation that permanently changes faction allegiance."
        };

        public override Task<Result<bool, RimMindError>> ExecuteTriggerAsync(MechanismWriteArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            if (pawn.Faction == Faction.OfPlayer)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, "pawn is already a colonist")));

            pawn.SetFaction(Faction.OfPlayer, null);
            return Task.FromResult(Result<bool, RimMindError>.Ok(true));
        }
    }
}
