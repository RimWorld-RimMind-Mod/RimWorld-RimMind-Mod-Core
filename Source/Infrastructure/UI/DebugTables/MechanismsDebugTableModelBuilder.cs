using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Mechanisms;
using Verse;

namespace RimMind.Infrastructure.UI.DebugTables
{
    public sealed class MechanismsDebugTableModelBuilder : IDebugTableModelBuilder
    {
        private IGameMechanismRegistry? _registry;
        private long _cachedGeneration = long.MinValue;
        private DebugTableModel? _cachedModel;

        public MechanismsDebugTableModelBuilder(IGameMechanismRegistry? registry = null)
        {
            _registry = registry;
        }

        public void Bind(IGameMechanismRegistry? registry, long generation)
        {
            if (_cachedGeneration == generation && ReferenceEquals(_registry, registry))
                return;
            _registry = registry;
            _cachedGeneration = generation;
            _cachedModel = null;
        }

        public DebugTableModel Build()
            => _cachedModel ??= Build(_registry?.All ?? System.Array.Empty<IGameMechanism>());

        public static DebugTableModel Build(IReadOnlyList<IGameMechanism> mechanisms)
        {
            return new DebugTableModel(
                "RimMind.UI.Hub.Tab.Mechanisms".Translate(),
                mechanisms.Select(ToRow));
        }

        private static DebugTableRow ToRow(IGameMechanism mechanism)
        {
            string operations = mechanism.SupportedOperations.Count > 0
                ? string.Join(", ", mechanism.SupportedOperations.Select(DebugTableEnumLabels.For))
                : "RimMind.UI.DebugTable.Mechanisms.NoOperations".Translate().ToString();

            return DebugTableRow.Create(
                mechanism.MechanismId,
                DebugTableStatus.Completed,
                string.Empty,
                DebugTableEnumLabels.For(mechanism.Scope),
                mechanism.OwnerModId ?? string.Empty,
                operations,
                DebugTableEnumLabels.For(mechanism.Risk),
                mechanism.Docs.Summary,
                "RimMind.UI.DebugTable.Mechanisms.OperationCount".Translate(mechanism.SupportedOperations.Count).ToString());
        }
    }
}
