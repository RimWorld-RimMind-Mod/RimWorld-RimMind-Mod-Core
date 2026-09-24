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

namespace RimMind.Infrastructure.Mechanisms.Pawn.Equipment
{
    public sealed class EquipmentMechanism : GameMechanismBaseNoDef
    {
        public override string MechanismId => "pawn.equipment";
        public override MechanismScope Scope => MechanismScope.Pawn;
        public override MechanismRisk Risk => MechanismRisk.Moderate;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Query, MechanismOperationType.Set }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Query and manage pawn equipment",
            QueryDescription = "Query pawn's currently equipped items",
            SetDescription = "Manage pawn equipment. Action: drop_weapon."
        };

        private static readonly IReadOnlyList<MechanismActionInfo> _writeActions =
            new List<MechanismActionInfo>
            {
                new MechanismActionInfo { Action = "drop_weapon", Description = "Drop the pawn's currently equipped weapon" }
            }.AsReadOnly();

        public override IReadOnlyList<MechanismActionInfo>? GetWriteActions() => _writeActions;

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            var equipment = pawn.equipment?.AllEquipmentListForReading?
                .Select(e => new
                {
                    def = e.def.defName,
                    label = e.LabelCap,
                    hitPoints = e.HitPoints,
                    maxHitPoints = e.MaxHitPoints
                })
                .ToList();

            return Task.FromResult(Result<string, RimMindError>.Ok(
                JsonConvert.SerializeObject(equipment ?? new object())));
        }

        public override Task<Result<bool, RimMindError>> ExecuteSetAsync(MechanismWriteArgs args, CancellationToken ct)
        {
            var pawn = FindPawn(args.PawnId);
            if (pawn == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.PawnNotFound(args.PawnId)));

            if (args.Action == "drop_weapon")
            {
                var weapon = pawn.equipment?.Primary;
                if (weapon == null)
                    return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, "no weapon equipped")));

                pawn.equipment?.Remove(weapon);
                GenPlace.TryPlaceThing(weapon, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                return Task.FromResult(Result<bool, RimMindError>.Ok(true));
            }

            return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidAction(MechanismId, args.Action)));
        }
    }
}
