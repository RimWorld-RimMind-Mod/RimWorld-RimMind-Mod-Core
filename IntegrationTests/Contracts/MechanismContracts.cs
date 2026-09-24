using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Common;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Mechanisms;
using RimMind.Testing;
using Xunit;

namespace RimMind.IntegrationTests.Contracts
{
    public sealed class MechanismContracts
    {
        [Fact]
        public void Mechanism_registry_has_replaceable_identity_semantics()
        {
            ContractCaseRunner.Run(
                ("new registry is empty", () => Assert.Empty(new GameMechanismRegistry().All)),
                ("null registration is ignored", () =>
                {
                    var registry = new GameMechanismRegistry();
                    registry.Register(null!);
                    Assert.Empty(registry.All);
                }),
                ("registered mechanism is found by identity", () =>
                {
                    var registry = new GameMechanismRegistry();
                    var mechanism = new StubMechanism("pawn.work", "Core", "first");
                    registry.Register(mechanism);
                    Assert.Same(mechanism, registry.FindById("pawn.work"));
                }),
                ("same identity replaces the previous mechanism", () =>
                {
                    var registry = new GameMechanismRegistry();
                    var first = new StubMechanism("pawn.work", "Core", "first");
                    var second = new StubMechanism("pawn.work", "Core", "second");
                    registry.Register(first);
                    registry.Register(second);
                    Assert.Same(second, registry.FindById("pawn.work"));
                    Assert.Single(registry.All);
                }),
                ("unregister reports whether identity existed", () =>
                {
                    var registry = new GameMechanismRegistry();
                    registry.Register(new StubMechanism("pawn.work", "Core", "value"));
                    Assert.True(registry.Unregister("pawn.work"));
                    Assert.False(registry.Unregister("pawn.work"));
                    Assert.Null(registry.FindById("pawn.work"));
                }));
        }

        [Fact]
        public void Mechanism_registry_retires_only_the_selected_owner()
        {
            ContractCaseRunner.Run(
                ("null owner is rejected", () =>
                    Assert.Throws<ArgumentNullException>(() => new GameMechanismRegistry().UnregisterByOwner(null!))),
                ("empty registry removes nothing", () =>
                    Assert.Equal(0, new GameMechanismRegistry().UnregisterByOwner("Core"))),
                ("every mechanism for the owner is retired", () =>
                {
                    var registry = RegistryWithOwners();
                    Assert.Equal(2, registry.UnregisterByOwner("Core"));
                    Assert.Null(registry.FindById("core.work"));
                    Assert.Null(registry.FindById("core.job"));
                }),
                ("mechanisms for another owner remain available", () =>
                {
                    var registry = RegistryWithOwners();
                    registry.UnregisterByOwner("Core");
                    Assert.NotNull(registry.FindById("Other.feature"));
                    Assert.Single(registry.All);
                }),
                ("unknown owner leaves the graph unchanged", () =>
                {
                    var registry = RegistryWithOwners();
                    string[] before = registry.All
                        .Select(mechanism => mechanism.MechanismId)
                        .OrderBy(id => id, StringComparer.Ordinal)
                        .ToArray();
                    Assert.Equal(0, registry.UnregisterByOwner("Missing"));
                    string[] after = registry.All
                        .Select(mechanism => mechanism.MechanismId)
                        .OrderBy(id => id, StringComparer.Ordinal)
                        .ToArray();
                    Assert.Equal(before, after);
                }));
        }

        [Fact]
        public async Task Mechanism_operation_boundary_preserves_results_and_risk()
        {
            await ContractCaseRunner.RunAsync(
                ("query returns the mechanism value", async () =>
                {
                    var mechanism = new StubMechanism("pawn.work", "Core", "priorities");
                    Result<string, RimMindError> result = await mechanism.ExecuteQueryAsync(
                        new MechanismReadArgs { MechanismId = "pawn.work" },
                        CancellationToken.None);
                    Assert.True(result.IsOk);
                    Assert.Equal("priorities", result.Value);
                }),
                ("supported write returns success", async () =>
                {
                    var mechanism = new StubMechanism("pawn.work", "Core", "priorities");
                    Result<bool, RimMindError> result = await mechanism.ExecuteSetAsync(
                        new MechanismWriteArgs { MechanismId = "pawn.work", Action = "set_priority" },
                        CancellationToken.None);
                    Assert.True(result.IsOk);
                    Assert.True(result.Value);
                }),
                ("unsupported operation returns a typed error", async () =>
                {
                    var mechanism = new StubMechanism("pawn.work", "Core", "priorities");
                    Result<bool, RimMindError> result = await mechanism.ExecuteAddAsync(
                        new MechanismWriteArgs { MechanismId = "pawn.work", Action = "add" },
                        CancellationToken.None);
                    Assert.True(result.IsErr);
                    Assert.Equal(RimMindErrorCode.MechanismOperationNotSupported, result.Error.Code);
                }),
                ("list returns a stable read-only option set", async () =>
                {
                    var mechanism = new StubMechanism("pawn.work", "Core", "priorities");
                    Result<IReadOnlyList<MechanismEnumResult>, RimMindError> result =
                        await mechanism.ExecuteListAsync(7, CancellationToken.None);
                    Assert.True(result.IsOk);
                    Assert.Collection(result.Value, item => Assert.Equal("option", item.DefName));
                }),
                ("operation risk is exposed through the public boundary", () =>
                {
                    var mechanism = new StubMechanism("pawn.work", "Core", "priorities", MechanismRisk.Dangerous);
                    Assert.Equal(MechanismRisk.Dangerous, mechanism.GetRiskForOperation(MechanismOperationType.Set));
                    return Task.CompletedTask;
                }));
        }

        private static GameMechanismRegistry RegistryWithOwners()
        {
            var registry = new GameMechanismRegistry();
            registry.Register(new StubMechanism("core.work", "Core", "work"));
            registry.Register(new StubMechanism("core.job", "Core", "job"));
            registry.Register(new StubMechanism("Other.feature", "Other", "other"));
            return registry;
        }

        private sealed class StubMechanism : IGameMechanism
        {
            private readonly string _ownerModId;
            private readonly string _queryValue;

            public StubMechanism(
                string mechanismId,
                string ownerModId,
                string queryValue,
                MechanismRisk risk = MechanismRisk.Safe)
            {
                MechanismId = mechanismId;
                _ownerModId = ownerModId;
                _queryValue = queryValue;
                Risk = risk;
                SupportedOperations = new[]
                {
                    MechanismOperationType.Query,
                    MechanismOperationType.Set,
                    MechanismOperationType.List
                };
            }

            string IExtension.Id => MechanismId;
            string IExtension.OwnerModId => _ownerModId;
            public string MechanismId { get; }
            public MechanismScope Scope => MechanismScope.Pawn;
            public MechanismRisk Risk { get; }
            public IReadOnlyList<MechanismOperationType> SupportedOperations { get; }
            public MechanismDocs Docs { get; } = new() { Summary = "contract mechanism" };

            public Task<Result<string, RimMindError>> ExecuteQueryAsync(
                MechanismReadArgs args,
                CancellationToken ct) =>
                Task.FromResult(Result<string, RimMindError>.Ok(_queryValue));

            public Task<Result<bool, RimMindError>> ExecuteSetAsync(
                MechanismWriteArgs args,
                CancellationToken ct) =>
                Task.FromResult(Result<bool, RimMindError>.Ok(true));

            public Task<Result<bool, RimMindError>> ExecuteAddAsync(
                MechanismWriteArgs args,
                CancellationToken ct) =>
                Unsupported(MechanismOperationType.Add);

            public Task<Result<bool, RimMindError>> ExecuteRemoveAsync(
                MechanismWriteArgs args,
                CancellationToken ct) =>
                Unsupported(MechanismOperationType.Remove);

            public Task<Result<bool, RimMindError>> ExecuteToggleAsync(
                MechanismWriteArgs args,
                CancellationToken ct) =>
                Unsupported(MechanismOperationType.Toggle);

            public Task<Result<bool, RimMindError>> ExecuteTriggerAsync(
                MechanismWriteArgs args,
                CancellationToken ct) =>
                Unsupported(MechanismOperationType.Trigger);

            public Task<Result<IReadOnlyList<MechanismEnumResult>, RimMindError>> ExecuteListAsync(
                int? pawnId,
                CancellationToken ct) =>
                Task.FromResult(Result<IReadOnlyList<MechanismEnumResult>, RimMindError>.Ok(
                    new[] { new MechanismEnumResult { DefName = "option", Label = "Option" } }));

            public Task<Result<bool, RimMindError>> ExecuteWatchAsync(
                MechanismWriteArgs args,
                CancellationToken ct) =>
                Unsupported(MechanismOperationType.Watch);

            public IReadOnlyList<MechanismActionInfo>? GetWriteActions() => null;

            public MechanismRisk GetRiskForOperation(MechanismOperationType operation) => Risk;

            private Task<Result<bool, RimMindError>> Unsupported(MechanismOperationType operation)
            {
                return Task.FromResult(Result<bool, RimMindError>.Err(
                    RimMindErrors.MechanismOperationNotSupported(MechanismId, operation.ToString())));
            }
        }
    }
}
