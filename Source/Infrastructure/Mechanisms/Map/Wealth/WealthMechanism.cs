using System.Collections.Generic;
using System.Reflection;
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

        private static readonly FieldInfo? WealthItemsField =
            typeof(WealthWatcher).GetField("wealthItems", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? WealthBuildingsField =
            typeof(WealthWatcher).GetField("wealthBuildings", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? WealthPawnsField =
            typeof(WealthWatcher).GetField("wealthPawns", BindingFlags.Instance | BindingFlags.NonPublic);

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

            int pawnCount;
            float totalWealth;
            float itemsWealth;
            float buildingsWealth;
            float threatPoints;

            if (UnityData.IsInMainThread)
            {
                pawnCount = map.mapPawns?.FreeColonistsCount ?? 0;
                totalWealth = map.wealthWatcher?.WealthTotal ?? 0f;
                itemsWealth = map.wealthWatcher?.WealthItems ?? 0f;
                buildingsWealth = map.wealthWatcher?.WealthBuildings ?? 0f;
                threatPoints = StorytellerUtility.DefaultThreatPointsNow(map);
            }
            else
            {
                pawnCount = Presentation.Api.RimMindPawnLookup.GetSpawnedColonistsSafe(map).Count;
                var watcher = map.wealthWatcher;
                itemsWealth = watcher != null && WealthItemsField?.GetValue(watcher) is float wi ? wi : 0f;
                buildingsWealth = watcher != null && WealthBuildingsField?.GetValue(watcher) is float wb ? wb : 0f;
                float pawnsWealth = watcher != null && WealthPawnsField?.GetValue(watcher) is float wp ? wp : 0f;
                totalWealth = itemsWealth + buildingsWealth + pawnsWealth;
                threatPoints = System.Math.Max(35f, totalWealth * 0.01f + pawnCount * 40f);
            }

            var wealthInfo = new
            {
                totalWealth = totalWealth,
                itemsWealth = itemsWealth,
                buildingsWealth = buildingsWealth,
                pawnCount = pawnCount,
                threatPoints = threatPoints
            };

            return Task.FromResult(Result<string, RimMindError>.Ok(JsonConvert.SerializeObject(wealthInfo)));
        }
    }
}
