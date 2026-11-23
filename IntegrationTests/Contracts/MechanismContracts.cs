using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Application.Common.Models.Tools;
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
        public async Task Mechanism_tool_handlers_dispatch_operations_risk_and_declared_scopes()
        {
            await ContractCaseRunner.RunAsync(
                ("query handler dispatches through MechanismToolHandler and returns mechanism payload", async () =>
                {
                    var mechanism = new StubMechanism("pawn.work", "Core", "priorities");
                    var handler = new MechanismToolHandler(mechanism, mechanism, mechanism, mechanism, MechanismOperationType.Query);
                    var result = await handler.ExecuteAsync(
                        new ToolCallArgs { ToolCallId = "q-1", ToolName = handler.Id, ArgumentsJson = "{\"pawn_id\":7}" },
                        CancellationToken.None);
                    Assert.True(result.IsOk);
                    Assert.False(result.Value.IsError);
                    Assert.Equal("priorities", result.Value.Content);
                    Assert.Equal(ToolScope.MetaQuery, handler.Definition.Manifest!.Scopes);
                }),
                ("write handler forwards parsed arguments and exposes manifest risk and approval", async () =>
                {
                    var mechanism = new StubMechanism("pawn.social", "Core", "priorities", MechanismRisk.Dangerous, ToolScope.Social | ToolScope.ColonistSelf);
                    var handler = new MechanismToolHandler(mechanism, mechanism, mechanism, mechanism, MechanismOperationType.Set);
                    Assert.Equal(RiskLevel.High, handler.Definition.Manifest!.RiskLevel);
                    Assert.True(handler.Definition.Manifest.RequiresApproval);
                    Assert.Equal(ToolScope.Social | ToolScope.ColonistSelf, handler.Definition.Manifest.Scopes);

                    var result = await handler.ExecuteAsync(
                        new ToolCallArgs { ToolCallId = "s-1", ToolName = handler.Id, ArgumentsJson = "{\"pawn_id\":7,\"action\":\"set_priority\"}" },
                        CancellationToken.None);
                    Assert.True(result.IsOk);
                    Assert.False(result.Value.IsError);
                    Assert.Equal(7, mechanism.LastWriteArgs?.PawnId);
                    Assert.Equal("set_priority", mechanism.LastWriteArgs?.Action);
                }),
                ("unsupported operation wraps typed error as tool error result", async () =>
                {
                    var mechanism = new StubMechanism("pawn.work", "Core", "priorities");
                    var handler = new MechanismToolHandler(mechanism, mechanism, mechanism, mechanism, MechanismOperationType.Add);
                    var result = await handler.ExecuteAsync(
                        new ToolCallArgs { ToolCallId = "a-1", ToolName = handler.Id, ArgumentsJson = "{\"pawn_id\":7}" },
                        CancellationToken.None);
                    Assert.True(result.IsOk);
                    Assert.True(result.Value.IsError);
                }),
                ("list handler serializes mechanism options and MechanismListToolHandler filters by category", async () =>
                {
                    var mechanism = new StubMechanism("pawn.work", "Core", "priorities");
                    var listHandler = new MechanismToolHandler(mechanism, mechanism, mechanism, mechanism, MechanismOperationType.List);
                    var listResult = await listHandler.ExecuteAsync(
                        new ToolCallArgs { ToolCallId = "l-1", ToolName = listHandler.Id, ArgumentsJson = "{\"pawn_id\":7}" },
                        CancellationToken.None);
                    Assert.True(listResult.IsOk);
                    Assert.False(listResult.Value.IsError);
                    Assert.Contains("option", listResult.Value.Content, StringComparison.Ordinal);

                    var listToolHandler = new MechanismListToolHandler(
                        mechanism,
                        mechanism,
                        new MechanismToolHelper.FallbackJsonExtractor());
                    var catalogResult = await listToolHandler.ExecuteAsync(
                        new ToolCallArgs { ToolCallId = "cat-1", ToolName = listToolHandler.Id, ArgumentsJson = "{\"category\":\"opt\"}" },
                        CancellationToken.None);
                    Assert.True(catalogResult.IsOk);
                    Assert.False(catalogResult.Value.IsError);
                    Assert.Contains("option", catalogResult.Value.Content, StringComparison.Ordinal);
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

        private sealed class StubMechanism : IGameMechanism, IMechanismScopeMetadata
        {
            private readonly string _ownerModId;
            private readonly string _queryValue;
            private readonly ToolScope? _declaredWriteScope;

            public StubMechanism(
                string mechanismId,
                string ownerModId,
                string queryValue,
                MechanismRisk risk = MechanismRisk.Safe,
                ToolScope? declaredWriteScope = null)
            {
                MechanismId = mechanismId;
                _ownerModId = ownerModId;
                _queryValue = queryValue;
                Risk = risk;
                _declaredWriteScope = declaredWriteScope;
                SupportedOperations = new[]
                {
                    MechanismOperationType.Query,
                    MechanismOperationType.Set,
                    MechanismOperationType.List
                };
            }

            public MechanismWriteArgs? LastWriteArgs { get; private set; }
            string IExtension.Id => MechanismId;
            string IExtension.OwnerModId => _ownerModId;
            public string MechanismId { get; }
            public MechanismScope Scope => MechanismScope.Pawn;
            public MechanismRisk Risk { get; }
            public IReadOnlyList<MechanismOperationType> SupportedOperations { get; }
            public MechanismDocs Docs { get; } = new() { Summary = "contract mechanism" };

            public ToolScope? GetToolScopeForOperation(MechanismOperationType operation)
                => operation is MechanismOperationType.Query or MechanismOperationType.List
                    ? ToolScope.MetaQuery
                    : _declaredWriteScope;

            public Task<Result<string, RimMindError>> ExecuteQueryAsync(
                MechanismReadArgs args,
                CancellationToken ct) =>
                Task.FromResult(Result<string, RimMindError>.Ok(_queryValue));

            public Task<Result<bool, RimMindError>> ExecuteSetAsync(
                MechanismWriteArgs args,
                CancellationToken ct)
            {
                LastWriteArgs = args;
                return Task.FromResult(Result<bool, RimMindError>.Ok(true));
            }

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
