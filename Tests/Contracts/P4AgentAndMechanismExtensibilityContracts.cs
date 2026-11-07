using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Agent;
using RimMind.Application.Features.AgentBus;
using RimMind.Application.Features.Registry;
using RimMind.Domain.Enums;
using RimMind.Domain.Events;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Mechanisms;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class P4AgentAndMechanismExtensibilityContracts
    {
        [Fact]
        public void ProactiveBehaviorOrchestrator_AndThinkContextEnricher_ExecuteRegisteredExtensionsInOrder()
        {
            var bus = new AgentBusImpl();
            var behaviorRegistry = new ExtensionRegistry<IProactiveBehavior>();
            var executionOrder = new List<string>();

            behaviorRegistry.Register(new RecordingProactiveBehavior("behavior.late", order: 30, shouldRun: true, executionOrder));
            behaviorRegistry.Register(new RecordingProactiveBehavior("behavior.skipped", order: 5, shouldRun: false, executionOrder));
            behaviorRegistry.Register(new RecordingProactiveBehavior("behavior.early", order: 10, shouldRun: true, executionOrder));
            behaviorRegistry.Register(new RecordingProactiveBehavior("behavior.middle", order: 20, shouldRun: true, executionOrder));

            var orchestrator = new ProactiveBehaviorOrchestrator(
                agentBus: bus,
                pawnId: 42,
                behaviorRegistry: behaviorRegistry);

            var agentInfo = new StubAgentInfo("NPC-42");
            orchestrator.ExecuteAll(agentInfo, currentTick: 2500);

            Assert.Equal(new[] { "behavior.early@2500", "behavior.middle@2500", "behavior.late@2500" }, executionOrder);

            var contributorRegistry = new ExtensionRegistry<IThinkContributor>();
            var contributorOrder = new List<string>();
            contributorRegistry.Register(new RecordingThinkContributor(
                "contrib.second",
                order: 20,
                (npcId, pawnId, sections) =>
                {
                    contributorOrder.Add($"second:{npcId}:{pawnId}");
                    sections["second_section"] = "Second contributor content";
                }));
            contributorRegistry.Register(new RecordingThinkContributor(
                "contrib.first",
                order: 10,
                (npcId, pawnId, sections) =>
                {
                    contributorOrder.Add($"first:{npcId}:{pawnId}");
                    sections["first_section"] = "First contributor content";
                }));

            var enricher = new ThinkContextEnricher(contributorRegistry: contributorRegistry);
            var envelope = new LlmRequestEnvelope { RequestId = "req-ext" };
            enricher.EnrichEnvelope(envelope, "NPC-42", "Stay focused.", pawnId: 42);

            Assert.Equal(new[] { "first:NPC-42:42", "second:NPC-42:42" }, contributorOrder);
            Assert.NotNull(envelope.GameStateInfo);
            Assert.True(envelope.GameStateInfo!.ContainsSection("inner_voice"));
            Assert.True(envelope.GameStateInfo.ContainsSection("first_section"));
            Assert.True(envelope.GameStateInfo.ContainsSection("second_section"));

            AgentActionOutcomeEvent? receivedOutcome = null;
            bus.RegisterEventType(nameof(AgentActionOutcomeEvent), typeof(AgentActionOutcomeEvent));
            var subKey = bus.SubscribeByName(nameof(AgentActionOutcomeEvent), evt => receivedOutcome = evt as AgentActionOutcomeEvent);
            try
            {
                bus.Publish(new AgentActionOutcomeEvent("NPC-42", 42, "pawn.job.set", succeeded: true, summary: "[Action Executed] pawn.job.set", timestamp: 2500));
                Assert.NotNull(receivedOutcome);
                Assert.Equal("pawn.job.set", receivedOutcome!.ActionIntent);
                Assert.True(receivedOutcome.Succeeded);
                Assert.Equal("[Action Executed] pawn.job.set", receivedOutcome.Summary);
            }
            finally
            {
                bus.Unsubscribe(subKey);
            }
        }

        [Fact]
        public void GameMechanismRegistry_DoesNotDoubleRegisterList_AndPropagatesCustomOwnerModId()
        {
            var toolRegistry = new RecordingToolRegistry();
            var mechanismRegistry = new GameMechanismRegistry(toolRegistry);

            const string customOwner = "custom.submod.mechanisms";
            var mechanism = new StubCustomMechanism(
                mechanismId: "colony.custom_scanner",
                ownerModId: customOwner,
                operations: new[]
                {
                    MechanismOperationType.Query,
                    MechanismOperationType.Set,
                    MechanismOperationType.List
                });

            mechanismRegistry.Register(mechanism);

            // List must only be registered once (via MechanismListToolHandler), never registered as MechanismToolHandler and overwritten
            Assert.Equal(1, toolRegistry.GetRegistrationCount("colony.custom_scanner.query"));
            Assert.Equal(1, toolRegistry.GetRegistrationCount("colony.custom_scanner.set"));
            Assert.Equal(1, toolRegistry.GetRegistrationCount("colony.custom_scanner.list"));

            var queryHandler = toolRegistry.FindById("colony.custom_scanner.query");
            var setHandler = toolRegistry.FindById("colony.custom_scanner.set");
            var listHandler = toolRegistry.FindById("colony.custom_scanner.list");

            Assert.IsType<MechanismToolHandler>(queryHandler);
            Assert.IsType<MechanismToolHandler>(setHandler);
            Assert.IsType<MechanismListToolHandler>(listHandler);

            Assert.Equal(customOwner, queryHandler!.OwnerModId);
            Assert.Equal(customOwner, setHandler!.OwnerModId);
            Assert.Equal(customOwner, listHandler!.OwnerModId);

            // UnregisterByOwner on GameMechanismRegistry removes the mechanism and all its tool handlers
            int removedMechanisms = mechanismRegistry.UnregisterByOwner(customOwner);
            Assert.Equal(1, removedMechanisms);
            Assert.Null(mechanismRegistry.FindById("colony.custom_scanner"));
            Assert.Null(toolRegistry.FindById("colony.custom_scanner.query"));
            Assert.Null(toolRegistry.FindById("colony.custom_scanner.set"));
            Assert.Null(toolRegistry.FindById("colony.custom_scanner.list"));

            // Re-register and verify ToolRegistry.UnregisterByOwner also matches the propagated OwnerModId
            mechanismRegistry.Register(mechanism);
            int removedTools = toolRegistry.UnregisterByOwner(customOwner);
            Assert.Equal(3, removedTools);
        }

        [Fact]
        public void MechanismToolHandler_GeneratesParameterSchemasAndExtractsAliases_FromParameterDescriptors()
        {
            var writeActions = new[]
            {
                new MechanismActionInfo(
                    action: "scan_sector",
                    description: "Scan a map sector with custom radius and sensitivity",
                    defNameHint: "ScannerDef",
                    requiredParams: new[] { "legacy_note" },
                    parameterDescriptors: new[]
                    {
                        new MechanismParamDescriptor(
                            name: "radius",
                            jsonType: "integer",
                            description: "Scan radius in cells",
                            required: true,
                            aliases: new[] { "r", "scan_radius" }),
                        new MechanismParamDescriptor(
                            name: "deep_mode",
                            jsonType: "boolean",
                            description: "Enable deep underground scanning",
                            required: false,
                            aliases: new[] { "deep" })
                    })
            };

            var mechanism = new StubCustomMechanism(
                mechanismId: "map.scanner",
                ownerModId: RimMindOwnerConsts.CoreModId,
                operations: new[] { MechanismOperationType.Set },
                writeActions: writeActions);

            var handler = new MechanismToolHandler(
                mechanism,
                mechanism,
                mechanism,
                mechanism,
                MechanismOperationType.Set);

            var schemaJson = JObject.Parse(handler.Definition.ParametersSchema);
            var properties = (JObject)schemaJson["properties"]!;
            var required = schemaJson["required"]!.Values<string>().ToList();

            Assert.Equal("integer", properties["radius"]?["type"]?.Value<string>());
            Assert.Equal("Scan radius in cells", properties["radius"]?["description"]?.Value<string>());
            Assert.Contains("radius", required);

            Assert.Equal("boolean", properties["deep_mode"]?["type"]?.Value<string>());
            Assert.Equal("Enable deep underground scanning", properties["deep_mode"]?["description"]?.Value<string>());
            Assert.DoesNotContain("deep_mode", required);

            Assert.Equal("string", properties["legacy_note"]?["type"]?.Value<string>());

            var writeArgs = handler.BuildWriteArgs(new ToolCallArgs
            {
                ToolCallId = "call-scan",
                ToolName = "map.scanner.set",
                ArgumentsJson = "{\"action\":\"scan_sector\",\"r\":18,\"deep\":true}",
                PawnId = 7
            });

            Assert.Equal("scan_sector", writeArgs.Action);
            Assert.NotNull(writeArgs.Params);
            Assert.Equal("18", writeArgs.Params!["radius"]);
            Assert.Equal("true", writeArgs.Params["deep_mode"]);
        }

        private sealed class RecordingProactiveBehavior : IProactiveBehavior
        {
            private readonly bool _shouldRun;
            private readonly List<string> _executionOrder;

            public RecordingProactiveBehavior(string behaviorId, int order, bool shouldRun, List<string> executionOrder)
            {
                BehaviorId = behaviorId;
                Order = order;
                _shouldRun = shouldRun;
                _executionOrder = executionOrder;
            }

            public string Id => BehaviorId;
            public string OwnerModId => "test.mod";
            public string BehaviorId { get; }
            public int Order { get; }

            public bool ShouldExecute(IAgentInfo agent, int currentTick) => _shouldRun;

            public Task ExecuteAsync(IAgentInfo agent, CancellationToken ct)
            {
                _executionOrder.Add($"{BehaviorId}@{(agent != null ? 2500 : 0)}");
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingThinkContributor : IThinkContributor
        {
            private readonly Action<string, int, IDictionary<string, string>> _contribute;

            public RecordingThinkContributor(
                string contributorId,
                int order,
                Action<string, int, IDictionary<string, string>> contribute)
            {
                ContributorId = contributorId;
                Order = order;
                _contribute = contribute;
            }

            public string Id => ContributorId;
            public string OwnerModId => "test.mod";
            public string ContributorId { get; }
            public int Order { get; }

            public void Contribute(string npcId, int pawnId, IDictionary<string, string> sections)
                => _contribute(npcId, pawnId, sections);
        }

        private sealed class StubAgentInfo : IAgentInfo
        {
            public StubAgentInfo(string npcId)
            {
                NpcId = npcId;
                Label = npcId;
            }

            public AgentState State => AgentState.Active;
            public string NpcId { get; }
            public string Label { get; }
            public int? LastThinkTick { get; set; }
            public int GoalCount => 0;
            public IReadOnlyList<BehaviorRecordDto> GetRecentHistory(int count = 10) => Array.Empty<BehaviorRecordDto>();
            public float GetRecentSuccessRate(int count = 10) => 1f;
        }

        private sealed class RecordingToolRegistry : IToolRegistry
        {
            private readonly Dictionary<string, IToolHandler> _handlers = new(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _registrationCounts = new(StringComparer.Ordinal);

            public void Register(IToolHandler handler)
            {
                _handlers[handler.Id] = handler;
                _registrationCounts.TryGetValue(handler.Id, out int count);
                _registrationCounts[handler.Id] = count + 1;
            }

            public bool Unregister(string toolId) => _handlers.Remove(toolId);

            public int UnregisterByOwner(string ownerModId)
            {
                var ids = _handlers.Values
                    .Where(h => h.OwnerModId == ownerModId)
                    .Select(h => h.Id)
                    .ToList();
                foreach (var id in ids)
                {
                    _handlers.Remove(id);
                }
                return ids.Count;
            }

            public IToolHandler? FindById(string toolId)
                => _handlers.TryGetValue(toolId, out var handler) ? handler : null;

            public IReadOnlyList<IToolHandler> All => _handlers.Values.ToList();

            public IReadOnlyList<ToolDefinition> GetAllDefinitions()
                => _handlers.Values.Select(h => h.Definition).ToList();

            public IReadOnlyList<IToolHandler> GetHandlersForScope(AgentScopeKind scopeKind)
                => _handlers.Values.ToList();

            public IReadOnlyList<ToolDefinition> GetDefinitionsForScope(AgentScopeKind scopeKind)
                => _handlers.Values.Select(h => h.Definition).ToList();

            public int GetRegistrationCount(string toolId)
                => _registrationCounts.TryGetValue(toolId, out int count) ? count : 0;
        }

        private sealed class StubCustomMechanism : IGameMechanism
        {
            private readonly IReadOnlyList<MechanismActionInfo>? _writeActions;

            public StubCustomMechanism(
                string mechanismId,
                string ownerModId,
                IReadOnlyList<MechanismOperationType> operations,
                IReadOnlyList<MechanismActionInfo>? writeActions = null)
            {
                MechanismId = mechanismId;
                OwnerModId = ownerModId;
                SupportedOperations = operations;
                _writeActions = writeActions;
            }

            public string Id => MechanismId;
            public string OwnerModId { get; }
            public string MechanismId { get; }
            public MechanismScope Scope => MechanismScope.Map;
            public MechanismRisk Risk => MechanismRisk.Safe;
            public IReadOnlyList<MechanismOperationType> SupportedOperations { get; }
            public MechanismDocs Docs { get; } = new MechanismDocs { Summary = "Custom test mechanism" };

            public IReadOnlyList<MechanismActionInfo>? GetWriteActions() => _writeActions;
            public MechanismRisk GetRiskForOperation(MechanismOperationType operation) => Risk;

            public Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
                => Task.FromResult(Result<string, RimMindError>.Ok("{}"));

            public Task<Result<IReadOnlyList<MechanismEnumResult>, RimMindError>> ExecuteListAsync(int? pawnId, CancellationToken ct)
                => Task.FromResult(Result<IReadOnlyList<MechanismEnumResult>, RimMindError>.Ok(Array.Empty<MechanismEnumResult>()));

            public Task<Result<bool, RimMindError>> ExecuteSetAsync(MechanismWriteArgs args, CancellationToken ct)
                => Task.FromResult(Result<bool, RimMindError>.Ok(true));

            public Task<Result<bool, RimMindError>> ExecuteAddAsync(MechanismWriteArgs args, CancellationToken ct)
                => Task.FromResult(Result<bool, RimMindError>.Ok(true));

            public Task<Result<bool, RimMindError>> ExecuteRemoveAsync(MechanismWriteArgs args, CancellationToken ct)
                => Task.FromResult(Result<bool, RimMindError>.Ok(true));

            public Task<Result<bool, RimMindError>> ExecuteToggleAsync(MechanismWriteArgs args, CancellationToken ct)
                => Task.FromResult(Result<bool, RimMindError>.Ok(true));

            public Task<Result<bool, RimMindError>> ExecuteTriggerAsync(MechanismWriteArgs args, CancellationToken ct)
                => Task.FromResult(Result<bool, RimMindError>.Ok(true));

            public Task<Result<bool, RimMindError>> ExecuteWatchAsync(MechanismWriteArgs args, CancellationToken ct)
                => Task.FromResult(Result<bool, RimMindError>.Ok(true));
        }
    }
}
