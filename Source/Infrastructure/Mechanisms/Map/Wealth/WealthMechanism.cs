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

namespace RimMind.Infrastructure.Mechanisms.Map.Wealth
{
    public sealed class WealthMechanism : GameMechanismBaseNoDef
    {
        public override string MechanismId => "map.wealth";
        public override MechanismScope Scope => MechanismScope.Map;
        public override MechanismRisk Risk => MechanismRisk.Safe;
        public override IReadOnlyList<MechanismOperationType> SupportedOperations => _supportedOps;
        public override MechanismDocs Docs => _docs;

        private static readonly IReadOnlyList<MechanismOperationType> _supportedOps =
            new List<MechanismOperationType> { MechanismOperationType.Query }.AsReadOnly();

        private static readonly MechanismDocs _docs = new MechanismDocs
        {
            Summary = "Query colony wealth and threat points",
            QueryDescription = "Query the colony's total wealth, item wealth, building wealth, and threat points."
        };

        public override Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
        {
            var map = ResolveMap(args);
            if (map == null)
                return Task.FromResult(Result<string, RimMindError>.Err(RimMindErrors.MapNotFound(args.MapId ?? 0)));

            var wealthInfo = new
            {
                totalWealth = map.wealthWatcher?.WealthTotal ?? 0f,
                itemsWealth = map.wealthWatcher?.WealthItems ?? 0f,
                buildingsWealth = map.wealthWatcher?.WealthBuildings ?? 0f,
                pawnCount = map.mapPawns?.FreeColonistsCount ?? 0,
                threatPoints = StorytellerUtility.DefaultThreatPointsNow(map)
            };

            return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(wealthInfo)));
        }
    }
}
