using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Debug;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.UI.DebugCenter;
using RimMind.Infrastructure.UI.DebugTables;
using RimMind.Infrastructure.Verse;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class AgentRolloutAndTraceFormattingContracts
    {
        [Fact]
        public void DebugCenterNavigation_resolves_consolidated_5_tab_routes_and_legacy_aliases()
        {
            ContractCaseRunner.Run(
                ("legacy capability routes resolve into capabilities sub-tabs", () =>
                {
                    DebugCenterNavigation.ResolveRoute("tool_calls", out string p1, out string? s1);
                    Assert.Equal("capabilities", p1);
                    Assert.Equal("tool_calls", s1);

                    DebugCenterNavigation.ResolveRoute("mechanisms", out string p2, out string? s2);
                    Assert.Equal("capabilities", p2);
                    Assert.Equal("mechanisms", s2);

                    DebugCenterNavigation.ResolveRoute("context_keys", out string p3, out string? s3);
                    Assert.Equal("capabilities", p3);
                    Assert.Equal("context_keys", s3);
                }),
                ("legacy trace and explicit colon routes resolve into ai_requests or target sub-tabs", () =>
                {
                    DebugCenterNavigation.ResolveRoute("rollouts", out string p1, out string? s1);
                    Assert.Equal("ai_requests", p1);
                    Assert.Equal("rollout", s1);

                    DebugCenterNavigation.ResolveRoute("ephemeral_requests", out string p2, out string? s2);
                    Assert.Equal("ai_requests", p2);
                    Assert.Equal("ephemeral", s2);

                    DebugCenterNavigation.ResolveRoute("capabilities:mechanisms", out string p3, out string? s3);
                    Assert.Equal("capabilities", p3);
                    Assert.Equal("mechanisms", s3);

                    var nav = new DebugCenterNavigation();
                    nav.GoTo("mechanisms");
                    Assert.Equal("capabilities", nav.ConsumeRequestedPageId());
                    Assert.Equal("mechanisms", nav.ConsumeRequestedSubTabId());
                })
            );
        }

        [Fact]
        public void AIRequestTraceLog_tracks_layer_prefix_reuse_and_deferred_action_outcomes()
        {
            ContractCaseRunner.Run(
                ("consecutive turns for same agent mark matching L0-L2 system layers as prefix-reused and L4 user layer as uncached", () =>
                {
                    var log = new AIRequestTraceLog();

                    log.StartRequest("req-1", "Decision", "deepseek-chat", "sys", "turn1-user", "");
                    log.UpdateRequestMetadata("req-1", "trace-1", "rollout-1", 0, "RimMind.Agent", "Decision", "Pawn_42", isAgentRollout: true);
                    log.UpdateRequestLayers("req-1", new[]
                    {
                        new AIRequestLayerTrace("system", "L0", "System Rules v1", 15, IsPrefixReused: false),
                        new AIRequestLayerTrace("system", "L1", "Static Identity Alice", 21, IsPrefixReused: false),
                        new AIRequestLayerTrace("system", "L2", "Tools Schema", 12, IsPrefixReused: false),
                        new AIRequestLayerTrace("user", "L4", "Perception Frame 1", 18, IsPrefixReused: false)
                    });
                    log.CompleteRequestDetailed("req-1", "<Thought>need tool</Thought>", "reasoning turn 1", 120, 90, 30, 0, 200);

                    log.StartRequest("req-2", "Decision", "deepseek-chat", "sys", "turn2-user", "");
                    log.UpdateRequestMetadata("req-2", "trace-1", "rollout-1", 1, "RimMind.Agent", "Decision", "Pawn_42", isAgentRollout: true);
                    log.UpdateRequestLayers("req-2", new[]
                    {
                        new AIRequestLayerTrace("system", "L0", "System Rules v1", 15, IsPrefixReused: false),
                        new AIRequestLayerTrace("system", "L1", "Static Identity Alice", 21, IsPrefixReused: false),
                        new AIRequestLayerTrace("system", "L2", "Tools Schema", 12, IsPrefixReused: false),
                        new AIRequestLayerTrace("user", "L4", "Perception Frame 2 + tool_results", 33, IsPrefixReused: false)
                    });
                    log.AddToolCallDetailed("req-2", "tc-decision", "Decision", succeeded: true, error: null, "{\"action\":\"assign_work\"}", null);
                    log.RecordActionOutcome("req-2", "assign_work", succeeded: false, detail: "Target unreachable", paramPreview: "Construction");

                    var entries = log.Entries;
                    Assert.Equal(2, entries.Count);

                    var turn2 = entries[1];
                    Assert.Equal(4, turn2.Layers.Count);
                    Assert.True(turn2.Layers[0].IsPrefixReused);
                    Assert.True(turn2.Layers[1].IsPrefixReused);
                    Assert.True(turn2.Layers[2].IsPrefixReused);
                    Assert.False(turn2.Layers[3].IsPrefixReused);

                    Assert.NotNull(turn2.ActionOutcome);
                    Assert.False(turn2.ActionOutcome!.Succeeded);
                    Assert.Equal("Target unreachable", turn2.ActionOutcome.Detail);
                    Assert.Single(turn2.ToolCalls);
                    Assert.False(turn2.ToolCalls[0].Succeeded);
                    Assert.Equal("Target unreachable", turn2.ToolCalls[0].Error);
                })
            );
        }

        [Fact]
        public void AgentRolloutModelBuilder_separates_rollouts_from_ephemeral_and_applies_filters()
        {
            ContractCaseRunner.Run(
                ("groups multi-turn rollout trajectories separately from ephemeral one-off requests and filters accurately", () =>
                {
                    var log = new AIRequestTraceLog();

                    log.StartRequest("r1-t0", "Decision", "deepseek-reasoner", "sys", "perc0", "");
                    log.UpdateRequestMetadata("r1-t0", "tr1", "roll-A", 0, "RimMind.Agent", "Decision", "Pawn_7", isAgentRollout: true);
                    log.UpdateRequestLayers("r1-t0", new[]
                    {
                        new AIRequestLayerTrace("system", "L0", "Rules", 5, IsPrefixReused: false),
                        new AIRequestLayerTrace("user", "L4", "Frame0", 6, IsPrefixReused: false)
                    });
                    log.CompleteRequestDetailed("r1-t0", "{\"query\":\"food\"}", "think0", 100, 80, 20, 0, 150);

                    log.StartRequest("r1-t1", "Decision", "deepseek-reasoner", "sys", "perc1", "");
                    log.UpdateRequestMetadata("r1-t1", "tr1", "roll-A", 1, "RimMind.Agent", "Decision", "Pawn_7", isAgentRollout: true);
                    log.UpdateRequestLayers("r1-t1", new[]
                    {
                        new AIRequestLayerTrace("system", "L0", "Rules", 5, IsPrefixReused: false),
                        new AIRequestLayerTrace("user", "L4", "Frame1", 6, IsPrefixReused: false)
                    });
                    log.CompleteRequestDetailed("r1-t1", "{\"action\":\"eat_food\"}", "think1", 90, 70, 20, 50, 110);
                    log.RecordActionOutcome("r1-t1", "eat_food", succeeded: true, detail: "Eating meal", paramPreview: "SimpleMeal");

                    log.StartRequest("ephem-1", "Dialogue", "gpt-4o-mini", "sys", "greet player", "");
                    log.UpdateRequestMetadata("ephem-1", "tr2", "", 0, "RimMind.Dialogue", "Chitchat", "Pawn_8", isAgentRollout: false);
                    log.CompleteRequest("ephem-1", "Hello friend!", 40, 80);

                    log.StartRequest("ephem-2", "Personality", "gpt-4o-mini", "sys", "mood shift", "");
                    log.UpdateRequestMetadata("ephem-2", "tr3", "", 0, "RimMind.Personality", "Eval", "Pawn_7", isAgentRollout: false);
                    log.FailRequest("ephem-2", "timeout", 5000);

                    var all = log.Entries;
                    IReadOnlyList<AgentRolloutEpisode> rollouts = AgentRolloutModelBuilder.BuildRollouts(all);
                    Assert.Single(rollouts);
                    Assert.Equal("roll-A", rollouts[0].RolloutId);
                    Assert.Equal(2, rollouts[0].TurnCount);
                    Assert.True(rollouts[0].PrefixReuseRatio > 0.0);
                    Assert.NotNull(rollouts[0].FinalOutcome);
                    Assert.True(rollouts[0].FinalOutcome!.Succeeded);

                    IReadOnlyList<AIRequestTraceEntry> ephemeralOnly = AgentRolloutModelBuilder.FilterEphemeral(all);
                    Assert.Equal(2, ephemeralOnly.Count);
                    Assert.DoesNotContain(ephemeralOnly, e => e.RolloutId == "roll-A");

                    IReadOnlyList<AIRequestTraceEntry> filteredModFailed = AgentRolloutModelBuilder.FilterEphemeral(
                        all,
                        new EphemeralTraceFilter(ModId: "RimMind.Personality", State: AIRequestTraceState.Failed));
                    Assert.Single(filteredModFailed);
                    Assert.Equal("ephem-2", filteredModFailed[0].RequestId);

                    IReadOnlyList<AIRequestTraceEntry> withRollouts = AgentRolloutModelBuilder.FilterEphemeral(
                        all,
                        new EphemeralTraceFilter(IncludeRolloutTurns: true, SearchQuery: "SimpleMeal"));
                    Assert.Single(withRollouts);
                    Assert.Equal("r1-t1", withRollouts[0].RequestId);
                })
            );
        }

        [Fact]
        public void TraceContentFormatter_and_ToolRegistryDebugTableModelBuilder_format_and_filter_cleanly()
        {
            ContractCaseRunner.Run(
                ("formats compact JSON and XML-tagged agent replies and filters tool definitions cleanly", () =>
                {
                    string prettyJson = TraceContentFormatter.FormatStructuredText("{\"action\":\"place_blueprint\",\"def\":\"Bed\"}");
                    Assert.Contains("\n", prettyJson);
                    Assert.Contains("\"action\": \"place_blueprint\"", prettyJson);

                    string prettyXml = TraceContentFormatter.FormatStructuredText(
                        "<Thought>build bed</Thought><Action>{\"def\":\"Bed\"}</Action>");
                    Assert.Contains("</Thought>\n<Action>", prettyXml);

                    var registry = new ToolRegistry();
                    registry.Register(new StubToolHandler("actions_place_blueprint", "Actions", AutonomyDomain.BlueprintPlacement));
                    registry.Register(new StubToolHandler("pawn_job", "Mechanisms", AutonomyDomain.EmergencySurvival));

                    IReadOnlyList<ToolDefinition> filtered = ToolRegistryDebugTableModelBuilder.FilterDefinitions(
                        registry.GetAllDefinitions(),
                        categoryFilter: "Actions",
                        searchQuery: "blueprint");
                    Assert.Single(filtered);
                    Assert.Equal("actions_place_blueprint", filtered[0].Id);

                    DebugTableModel table = ToolRegistryDebugTableModelBuilder.Build(registry, "Actions", "blueprint");
                    Assert.Single(table.Rows);
                })
            );
        }

        private sealed class StubToolHandler : IToolHandler
        {
            public StubToolHandler(string id, string category, AutonomyDomain domain)
            {
                Definition = new ToolDefinition
                {
                    Id = id,
                    Category = category,
                    Description = $"{id} description",
                    ParametersSchema = "{\"type\":\"object\"}",
                    Manifest = new ToolManifest
                    {
                        OwnerModId = "RimMind.Test",
                        Kind = ToolKind.Mutation,
                        DefaultDomain = domain,
                        RiskLevel = RiskLevel.Medium,
                        DisplayName = id
                    }
                };
            }

            public string Id => Definition.Id;
            public string OwnerModId => Definition.Manifest.OwnerModId;
            public ToolDefinition Definition { get; }

            public Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
                => Task.FromResult(Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("ok")));
        }
    }
}
