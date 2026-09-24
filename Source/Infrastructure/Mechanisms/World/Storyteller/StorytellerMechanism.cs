using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using Verse;
using RimWorld;

namespace RimMind.Infrastructure.Mechanisms.World.Storyteller
{
    public sealed class StorytellerMechanism : GameMechanismBase<IncidentDef>
    {
        public override string MechanismId => "world.storyteller";
        public override MechanismScope Scope => MechanismScope.World;
        public override MechanismRisk Risk => MechanismRisk.Dangerous;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Query, MechanismOperationType.Trigger, MechanismOperationType.List }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Query storyteller status and trigger incidents",
            QueryDescription = "Query current storyteller settings and threat level",
            TriggerDescription = "Trigger an incident. DANGEROUS - can cause raids, diseases, and other events.",
            ListDescription = "List all incident definitions"
        };

        public override MechanismRisk GetRiskForOperation(MechanismOperationType operation)
        {
            return operation switch
            {
                MechanismOperationType.Trigger => MechanismRisk.Dangerous,
                MechanismOperationType.Query => MechanismRisk.Safe,
                MechanismOperationType.List => MechanismRisk.Safe,
                _ => Risk
            };
        }

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var storyteller = Find.Storyteller;
            if (storyteller == null)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.Internal("No storyteller found")));

            var info = new
            {
                def = storyteller.def.defName,
                label = storyteller.def.label,
                difficulty = Find.Storyteller?.difficultyDef?.defName,
                threatPoints = Find.CurrentMap != null ? StorytellerUtility.DefaultThreatPointsNow(Find.CurrentMap) : 0
            };

            return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(info)));
        }

        public override Task<Result<bool, RimMindError>> ExecuteTriggerAsync(MechanismWriteArgs args, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(args.DefName))
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName("")));

            var incidentDef = FindDef(args.DefName);
            if (incidentDef == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.InvalidDefName(args.DefName)));

            var map = Find.AnyPlayerHomeMap;
            if (map == null)
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.MapNotFound(0)));

            var parms = StorytellerUtility.DefaultParmsNow(incidentDef.category, map);
            parms.forced = true;

            if (!incidentDef.Worker.TryExecute(parms))
                return Task.FromResult(Result<bool, RimMindError>.Err(RimMindErrors.Internal($"Failed to execute incident '{args.DefName}'")));

            return Task.FromResult(Result<bool, RimMindError>.Ok(true));
        }
    }
}
