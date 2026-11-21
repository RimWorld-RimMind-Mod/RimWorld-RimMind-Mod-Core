using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Helpers;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Agent.Modes;
using RimMind.Application.Features.Json;
using RimMind.Application.Features.Llm;
using RimMind.Application.Features.Prompt;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Application.Features.Tools;
using RimMind.Domain.Agent.Policies;
using RimMind.Domain.Common;
using RimMind.Domain.Enums;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Mechanisms;
using RimMind.Presentation.Api;
using RimMind.Presentation.UI;
using RimMind.Presentation.UI.Framework;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class Phase2CoreBuildingBlockContracts
    {
        private sealed class FakeToolHandler : IToolHandler
        {
            public FakeToolHandler(ToolDefinition definition, string ownerModId = "RimMindCore")
            {
                Definition = definition;
                OwnerModId = ownerModId;
            }

            public string Id => Definition.Id;
            public string OwnerModId { get; }
            public ToolDefinition Definition { get; }

            public Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
                => Task.FromResult(Result<ToolResult, RimMindError>.Ok(ToolResult.Ok("ok", args.ToolCallId, Id)));
        }

        private sealed class FakeTickProvider : ITickProvider
        {
            public int TicksGame { get; set; } = 1000;
            public long TicksAbs => TicksGame;
        }

        private sealed class FakeAgentInfo : IAgentInfo
        {
            public AgentState State => AgentState.Active;
            public string NpcId => "NPC-42";
            public string Label => "Alice";
            public int? LastThinkTick { get; set; } = 0;
            public int GoalCount => 0;
            public IReadOnlyList<BehaviorRecordDto> GetRecentHistory(int count = 10) => Array.Empty<BehaviorRecordDto>();
            public float GetRecentSuccessRate(int count = 10) => 1f;
        }

        private sealed class FakeAutonomyMechanism : IMechanismReader, IMechanismWriter, IMechanismTrigger, IMechanismAutonomyMetadata
        {
            public string Id => MechanismId;
            public string OwnerModId => "TestMod";
            public string MechanismId { get; init; } = "pawn.custom_schedule";
            public MechanismScope Scope { get; init; } = MechanismScope.Pawn;
            public MechanismRisk Risk { get; init; } = MechanismRisk.Moderate;
            public IReadOnlyList<MechanismOperationType> SupportedOperations { get; } =
                new[] { MechanismOperationType.Query, MechanismOperationType.Set };
            public MechanismDocs Docs { get; } = new MechanismDocs { Summary = "Custom test mechanism" };
            public IReadOnlyList<MechanismActionInfo>? GetWriteActions() => null;
            public MechanismRisk GetRiskForOperation(MechanismOperationType operation) => Risk;
            public AutonomyDomain? GetAutonomyDomainForOperation(MechanismOperationType operation)
                => operation == MechanismOperationType.Set ? AutonomyDomain.WorkPriorityModification : null;

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

        private sealed class FakeBioBadgeProvider : IBioTabBadgeProvider
        {
            public FakeBioBadgeProvider(string id, string ownerModId, int order)
            {
                Id = id;
                OwnerModId = ownerModId;
                Order = order;
            }

            public string Id { get; }
            public string OwnerModId { get; }
            public int Order { get; }
            public BioTabBadgeDescriptor? TryGetBadge(Verse.Pawn pawn) => new BioTabBadgeDescriptor(Id);
        }

        private sealed class SampleDto
        {
            public string? Action { get; set; }
            public int Priority { get; set; }
        }

        [Fact]
        public void ToolScope_projection_scenario_separation_and_declarative_mechanisms()
        {
            ContractCaseRunner.Run(
                ("AgentDecision and AdvisorDecision scenarios are distinct from Decision and wired into modes", () =>
                {
                    Assert.NotEqual(ScenarioIds.AgentDecision, ScenarioIds.AdvisorDecision);
                    Assert.NotEqual(ScenarioIds.AgentDecision, ScenarioIds.Decision);

                    var tickProvider = new FakeTickProvider();
                    var agentInfo = new FakeAgentInfo();
                    var proactive = new ProactiveAgentMode(tickProvider);
                    var reactive = new ReactiveAgentMode();
                    var proactiveEnv = proactive.GetThinkStrategy().BuildEnvelope(agentInfo, Array.Empty<PerceptionBufferEntry>(), Array.Empty<ToolDefinition>());
                    var reactiveEnv = reactive.GetThinkStrategy().BuildEnvelope(agentInfo, Array.Empty<PerceptionBufferEntry>(), Array.Empty<ToolDefinition>());
                    Assert.Equal(ScenarioIds.AgentDecision, proactiveEnv.ScenarioId);
                    Assert.Equal(ScenarioIds.AgentDecision, reactiveEnv.ScenarioId);
                }),
                ("GetDefinitionsForScope filters WorldAdmin and MetaQuery and compacts schemas", () =>
                {
                    var registry = new ToolRegistry();
                    string verboseSchema = "{\"type\":\"object\",\"properties\":{\"pawn_id\":{\"type\":\"integer\",\"description\":\"A very long verbose description explaining pawn_id in extreme detail for humans and models alike beyond 72 chars.\"},\"map_id\":{\"type\":\"integer\",\"description\":\"Optional map ID\"},\"def_name\":{\"type\":\"string\",\"enum\":[\"WoodLog\",\"Steel\"],\"description\":\"Long description\"}},\"required\":[\"pawn_id\"]}";

                    registry.Register(new FakeToolHandler(new ToolDefinition
                    {
                        Id = "pawn.job.set",
                        Description = "Assign a concrete job to the colonist.\nExtra verbose line 2.",
                        ParametersSchema = verboseSchema,
                        Category = "pawn"
                    }));
                    registry.Register(new FakeToolHandler(new ToolDefinition
                    {
                        Id = "world.storyteller.trigger",
                        Description = "Trigger storyteller incident",
                        ParametersSchema = verboseSchema,
                        Category = "world"
                    }));
                    registry.Register(new FakeToolHandler(new ToolDefinition
                    {
                        Id = "pawn.job.list",
                        Description = "List all job defs",
                        ParametersSchema = verboseSchema,
                        Category = "pawn"
                    }));
                    registry.Register(new FakeToolHandler(new ToolDefinition
                    {
                        Id = "actions_place_blueprint",
                        Description = "Place blueprint",
                        ParametersSchema = verboseSchema,
                        Category = "actions",
                        Manifest = new ToolManifest { Scopes = ToolScope.ColonyBuild }
                    }));

                    var scoped = registry.GetDefinitionsForScope(ToolScopeProjection.ColonistAgentDefaultScope, compactSchema: true);
                    var ids = scoped.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
                    Assert.Contains("pawn.job.set", ids);
                    Assert.Contains("actions_place_blueprint", ids);
                    Assert.DoesNotContain("world.storyteller.trigger", ids);
                    Assert.DoesNotContain("pawn.job.list", ids);

                    var jobDef = scoped.Single(d => d.Id == "pawn.job.set");
                    Assert.True(jobDef.ParametersSchema.Length < verboseSchema.Length);
                    var schemaObj = JObject.Parse(jobDef.ParametersSchema);
                    Assert.Equal("integer", schemaObj["properties"]?["pawn_id"]?["type"]?.ToString());
                    Assert.NotNull(schemaObj["properties"]?["def_name"]?["enum"]);
                    string? compactedPawnDesc = schemaObj["properties"]?["pawn_id"]?["description"]?.ToString();
                    Assert.True(compactedPawnDesc == null || compactedPawnDesc.Length <= 72);
                    Assert.Null(schemaObj["properties"]?["map_id"]);

                    var proactiveAllowed = new ProactiveAgentMode(new FakeTickProvider()).AllowedToolIds(registry);
                    Assert.Contains("pawn.job.set", proactiveAllowed);
                    Assert.DoesNotContain("world.storyteller.trigger", proactiveAllowed);
                    Assert.DoesNotContain("pawn.job.list", proactiveAllowed);
                }),
                ("MechanismToolHandler resolves DefaultDomain declaratively via IMechanismAutonomyMetadata and populates Kind/Scopes", () =>
                {
                    var mech = new FakeAutonomyMechanism();
                    var setHandler = new MechanismToolHandler(mech, mech, mech, mech, MechanismOperationType.Set);
                    var queryHandler = new MechanismToolHandler(mech, mech, mech, mech, MechanismOperationType.Query);

                    Assert.Equal(AutonomyDomain.WorkPriorityModification, setHandler.Definition.Manifest.DefaultDomain);
                    Assert.Equal(ToolKind.Mutation, setHandler.Definition.Manifest.Kind);
                    Assert.True((setHandler.Definition.Manifest.Scopes!.Value & ToolScope.ColonyManage) != 0);

                    Assert.Null(queryHandler.Definition.Manifest.DefaultDomain);
                    Assert.Equal(ToolKind.Query, queryHandler.Definition.Manifest.Kind);
                    Assert.Equal(ToolScope.MetaQuery, queryHandler.Definition.Manifest.Scopes);
                }),
                ("AutonomyDomainRiskMapper provides canonical risk levels", () =>
                {
                    Assert.Equal(RiskLevel.Low, AutonomyDomainRiskMapper.MapToRisk(AutonomyDomain.EmergencySurvival));
                    Assert.Equal(RiskLevel.Low, AutonomyDomainRiskMapper.MapToRisk(AutonomyDomain.ResourceMarking));
                    Assert.Equal(RiskLevel.Medium, AutonomyDomainRiskMapper.MapToRisk(AutonomyDomain.BlueprintPlacement));
                    Assert.Equal(RiskLevel.Medium, AutonomyDomainRiskMapper.MapToRisk(AutonomyDomain.WorkPriorityModification));
                    Assert.Equal(RiskLevel.High, AutonomyDomainRiskMapper.MapToRisk(AutonomyDomain.HighRiskOperations));
                    Assert.Equal(RiskLevel.Low, AutonomyDomainRiskMapper.MapToRisk(null, ToolKind.Query));
                    Assert.Equal(RiskLevel.Medium, AutonomyDomainRiskMapper.MapToRisk(null, ToolKind.Mutation));
                }));
        }

        [Fact]
        public void RequestQueue_enforces_liveness_gate_npc_cancel_coalescing_and_ttl()
        {
            ContractCaseRunner.Run(
                ("pre-dispatch PawnLivenessChecker blocks dead NPC requests by default and respects opt-out", () =>
                {
                    var queue = new RequestQueue();
                    queue.PawnLivenessChecker = npcId => npcId == "NPC-10";
                    int executedCount = 0;
                    Result<LlmResponse, RimMindError>? deadResult = null;
                    Result<LlmResponse, RimMindError>? optOutResult = null;

                    var deadEnv = new LlmRequestEnvelope
                    {
                        RequestId = "req-dead",
                        ModId = "RimMind.Test",
                        NpcId = "NPC-99",
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "hi" } }
                    };
                    var optOutEnv = new LlmRequestEnvelope
                    {
                        RequestId = "req-optout",
                        ModId = "RimMind.Test",
                        NpcId = "NPC-99",
                        RequireAliveColonist = false,
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "memorial" } }
                    };

                    using var optOutDone = new ManualResetEventSlim(false);

                    queue.Enqueue(deadEnv, r => deadResult = r, _ =>
                    {
                        Interlocked.Increment(ref executedCount);
                        return Task.FromResult(Result<LlmResponse, RimMindError>.Ok(new LlmResponse { Content = "ok" }));
                    });

                    queue.Enqueue(optOutEnv, r =>
                    {
                        optOutResult = r;
                    }, _ =>
                    {
                        Interlocked.Increment(ref executedCount);
                        optOutDone.Set();
                        return Task.FromResult(Result<LlmResponse, RimMindError>.Ok(new LlmResponse { Content = "ok" }));
                    });

                    queue.CurrentTick = 100;
                    queue.Tick();
                    Assert.True(optOutDone.Wait(TimeSpan.FromSeconds(5)), "Opt-out executor did not run within 5s");
                    for (int i = 0; i < 50 && (!optOutResult.HasValue || !deadResult.HasValue); i++)
                    {
                        queue.CurrentTick = 101 + i;
                        queue.Tick();
                        if (!optOutResult.HasValue || !deadResult.HasValue)
                            Thread.Sleep(5);
                    }

                    Assert.Equal(1, executedCount);
                    Assert.True(deadResult.HasValue && deadResult.Value.IsErr);
                    Assert.Equal(RimMindErrorCode.Cancelled, deadResult!.Value.Error.Code);
                    Assert.True(optOutResult.HasValue && optOutResult.Value.IsOk);
                }),
                ("CoalesceKey supersedes stale pending requests so backlogs cannot accumulate", () =>
                {
                    var queue = new RequestQueue();
                    queue.PauseQueue();
                    Result<LlmResponse, RimMindError>? firstResult = null;
                    Result<LlmResponse, RimMindError>? secondResult = null;
                    int executedCount = 0;
                    string? executedId = null;
                    using var secondDone = new ManualResetEventSlim(false);

                    var env1 = new LlmRequestEnvelope
                    {
                        RequestId = "mem-1",
                        ModId = "RimMind.Memory",
                        CoalesceKey = "mem:NPC-5",
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "old" } }
                    };
                    var env2 = new LlmRequestEnvelope
                    {
                        RequestId = "mem-2",
                        ModId = "RimMind.Memory",
                        CoalesceKey = "mem:NPC-5",
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "new" } }
                    };

                    queue.Enqueue(env1, r => firstResult = r, _ =>
                    {
                        Interlocked.Increment(ref executedCount);
                        executedId = env1.RequestId;
                        return Task.FromResult(Result<LlmResponse, RimMindError>.Ok(new LlmResponse { Content = "old" }));
                    });

                    queue.Enqueue(env2, r => secondResult = r, _ =>
                    {
                        Interlocked.Increment(ref executedCount);
                        executedId = env2.RequestId;
                        secondDone.Set();
                        return Task.FromResult(Result<LlmResponse, RimMindError>.Ok(new LlmResponse { Content = "new" }));
                    });

                    Assert.Equal(1, queue.TotalQueuedCount);
                    queue.ResumeQueue();
                    queue.CurrentTick = 200;
                    queue.Tick();
                    Assert.True(secondDone.Wait(TimeSpan.FromSeconds(5)), "Coalesced request executor did not run within 5s");
                    for (int i = 0; i < 50 && (!firstResult.HasValue || !secondResult.HasValue); i++)
                    {
                        queue.CurrentTick = 201 + i;
                        queue.Tick();
                        if (!firstResult.HasValue || !secondResult.HasValue)
                            Thread.Sleep(5);
                    }

                    Assert.Equal(1, executedCount);
                    Assert.Equal("mem-2", executedId);
                    Assert.True(firstResult.HasValue && firstResult.Value.IsErr);
                    Assert.Equal(RimMindErrorCode.Cancelled, firstResult!.Value.Error.Code);
                    Assert.True(secondResult.HasValue && secondResult.Value.IsOk);
                }),
                ("CancelByNpcId removes queued entries for that NPC only and TTL drops expired entries", () =>
                {
                    var queue = new RequestQueue();
                    var fakeNow = new DateTime(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);
                    queue.UtcClock = () => fakeNow;
                    queue.PauseQueue();
                    queue.CurrentTick = 1000;

                    Result<LlmResponse, RimMindError>? npc1Result = null;
                    Result<LlmResponse, RimMindError>? expiredResult = null;

                    queue.Enqueue(new LlmRequestEnvelope
                    {
                        RequestId = "npc1-req",
                        ModId = "ModA",
                        NpcId = "NPC-1"
                    }, r => npc1Result = r, _ => Task.FromResult(Result<LlmResponse, RimMindError>.Ok(new LlmResponse())));

                    queue.Enqueue(new LlmRequestEnvelope
                    {
                        RequestId = "ttl-req",
                        ModId = "ModA",
                        NpcId = "NPC-2",
                        MaxQueueWaitTicks = 50,
                        ExpiresAtUtc = fakeNow.AddSeconds(5)
                    }, r => expiredResult = r, _ => Task.FromResult(Result<LlmResponse, RimMindError>.Ok(new LlmResponse())));

                    int cancelled = queue.CancelByNpcId("NPC-1", "Colonist died");
                    Assert.Equal(1, cancelled);
                    Assert.Equal(1, queue.TotalQueuedCount);

                    queue.ResumeQueue();
                    queue.CurrentTick = 1200; // Exceeds MaxQueueWaitTicks = 50
                    queue.Tick();
                    queue.Tick();

                    Assert.True(npc1Result.HasValue && npc1Result.Value.IsErr);
                    Assert.Equal(RimMindErrorCode.Cancelled, npc1Result!.Value.Error.Code);
                    Assert.True(expiredResult.HasValue && expiredResult.Value.IsErr);
                    Assert.Equal(RimMindErrorCode.Timeout, expiredResult!.Value.Error.Code);
                }));
        }

        [Fact]
        public void LlmPayloadParser_reason_extraction_and_MoteTextFormatter_contracts()
        {
            ContractCaseRunner.Run(
                ("StripThinkingAndCodeFences and ExtractJsonPayload handle thinking tags, fences, and quoted braces", () =>
                {
                    string raw = "<think>let me plan {not json}</think>\n```json\n{\"action\":\"eat\",\"note\":\"a {b} c\"}\n```";
                    string cleaned = LlmPayloadParser.StripThinkingAndCodeFences(raw);
                    Assert.Equal("{\"action\":\"eat\",\"note\":\"a {b} c\"}", cleaned);

                    string? extracted = LlmPayloadParser.ExtractJsonPayload("Sure! Here is the result: " + raw + " thanks!");
                    Assert.Equal("{\"action\":\"eat\",\"note\":\"a {b} c\"}", extracted);
                }),
                ("TryDeserializeWithRepair repairs trailing commas and extracts JSON cleanly", () =>
                {
                    string raw = "<thinking>hmm</thinking>\n```json\n{\"Action\":\"build\",\"Priority\":2,}\n```";
                    Assert.True(LlmPayloadParser.TryDeserializeWithRepair<SampleDto>(raw, out var dto));
                    Assert.NotNull(dto);
                    Assert.Equal("build", dto!.Action);
                    Assert.Equal(2, dto.Priority);
                }),
                ("ExtractReasonFromArgumentsJson finds reason nested inside value JSON string (pawn_job_set fix)", () =>
                {
                    string nestedArgs = "{\"pawn_id\":42,\"action\":\"take_job\",\"value\":\"{\\\"job_def\\\":\\\"HaulToCell\\\",\\\"reason\\\":\\\"Hauling wood before rain ruins it\\\"}\"}";
                    string? reason = LlmPayloadParser.ExtractReasonFromArgumentsJson(nestedArgs);
                    Assert.Equal("Hauling wood before rain ruins it", reason);

                    var resp = new LlmResponse
                    {
                        ToolCallsJson = "[{\"id\":\"call_1\",\"function\":{\"name\":\"pawn.job.set\",\"arguments\":" +
                                        Newtonsoft.Json.JsonConvert.SerializeObject(nestedArgs) + "}}]"
                    };
                    var parsedDecision = ThinkStrategyHelper.ParseDecisionCore(resp);
                    Assert.True(parsedDecision.IsOk);
                    Assert.Equal("pawn.job.set", parsedDecision.Value.ActionIntent);
                    Assert.Equal("Hauling wood before rain ruins it", parsedDecision.Value.Reason);
                }),
                ("ParsePseudoFunctionCalls parses textual function calls", () =>
                {
                    string text = "I will haul now. pawn_job_set(pawn_id=12, action=\"take_job\", reason=\"Keep food dry\")";
                    var calls = LlmPayloadParser.ParsePseudoFunctionCalls(text);
                    Assert.Single(calls);
                    Assert.Equal("pawn_job_set", calls[0].Name);
                    Assert.Equal("Keep food dry", calls[0].Reason);
                }),
                ("MoteTextFormatter wraps English and CJK text without overflowing line budget", () =>
                {
                    string en = MoteTextFormatter.WrapForMote("Colonists should haul the harvested rice before the storm arrives", 20);
                    Assert.Contains("\n", en);
                    Assert.DoesNotContain("  ", en);

                    string zh = MoteTextFormatter.WrapForMote("我们必须赶紧把室外的木材搬回仓库里避免淋雨腐烂", 16);
                    Assert.Contains("\n", zh);
                }));
        }

        [Fact]
        public void Public_protocol_failover_redactor_and_pool_building_blocks()
        {
            ContractCaseRunner.Run(
                ("HttpErrorClassifier separates moderation, quota/auth, transient, and permanent errors", () =>
                {
                    Assert.Equal(RimMindErrorCode.ContentFiltered, HttpErrorClassifier.Classify(400, "Content Exists Risk").Code);
                    Assert.Equal(RimMindErrorCode.ClientQuotaOrAuthFailure, HttpErrorClassifier.Classify(401, "invalid_api_key").Code);
                    Assert.Equal(RimMindErrorCode.ClientQuotaOrAuthFailure, HttpErrorClassifier.Classify(400, "Insufficient Balance").Code);
                    Assert.Equal(RimMindErrorCode.ClientTransientFailure, HttpErrorClassifier.Classify(429, "rate_limit").Code);
                    Assert.Equal(RimMindErrorCode.ClientTransientFailure, HttpErrorClassifier.Classify(503, "unavailable").Code);
                    Assert.Equal(RimMindErrorCode.ClientPermanentFailure, HttpErrorClassifier.Classify(400, "bad schema").Code);
                }),
                ("EndpointFailoverPolicy isolates failing nodes and restores on success or cooldown expiry", () =>
                {
                    long now = 10_000L;
                    var policy = new EndpointFailoverPolicy(() => now, defaultIsolationMs: 60_000L, permanentIsolationMs: 600_000L);
                    var nodes = new[] { "primary", "backup-1", "backup-2" };

                    policy.RecordFailure("primary", RimMindErrors.ClientTransient("503"));
                    Assert.True(policy.IsIsolated("primary"));
                    var ordered = policy.OrderCandidates(nodes, n => n);
                    Assert.Equal("backup-1", ordered[0]);

                    now += 61_000L;
                    Assert.False(policy.IsIsolated("primary"));

                    policy.RecordFailure("primary", RimMindErrors.ClientQuotaOrAuth("402"));
                    Assert.True(policy.IsPermanentlyExhausted("primary"));
                    policy.RecordSuccess("primary");
                    Assert.False(policy.IsPermanentlyExhausted("primary"));
                }),
                ("PromptZonePartitioner separates L0/L1 static prefix from L2/L3/L5 volatile observations", () =>
                {
                    var env = new LlmRequestEnvelope
                    {
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "system", LayerTag = "L0", Content = "Rules" },
                            new ChatMessage { Role = "system", LayerTag = "L1", Content = "MapTopology" },
                            new ChatMessage { Role = "system", LayerTag = "L2", Content = "WeatherRain" },
                            new ChatMessage { Role = "system", LayerTag = "L3", Content = "MoodLow" },
                            new ChatMessage { Role = "user", Content = "Decide" }
                        }
                    };
                    var part = PromptZonePartitioner.Partition(env);
                    Assert.Equal(2, part.StaticSystemPrompts.Count);
                    Assert.Equal(2, part.VolatileObservations.Count);
                    Assert.Single(part.LiveNonSystemMessages);
                    Assert.Equal("Decide", part.LastUserMessage?.Content);
                }),
                ("OpenAIWireCodec supports single-pass customizePayload and parses cached tokens", () =>
                {
                    var env = new LlmRequestEnvelope
                    {
                        Messages = new List<ChatMessage>
                        {
                            new ChatMessage { Role = "system", LayerTag = "L0", Content = "System" },
                            new ChatMessage { Role = "user", Content = "Hello" }
                        },
                        MaxTokens = 256
                    };

                    string json = OpenAIWireCodec.BuildRequestJson(
                        env,
                        "test-model",
                        defaultMaxTokens: 256,
                        customizePayload: root => root["custom_dialect_flag"] = true);

                    var parsedReq = JObject.Parse(json);
                    Assert.Equal(true, parsedReq["custom_dialect_flag"]?.Value<bool>());

                    string respJson = "{\"choices\":[{\"message\":{\"content\":\"hi\",\"reasoning_content\":\"thought\"}}],\"usage\":{\"total_tokens\":120,\"prompt_tokens\":100,\"completion_tokens\":20,\"prompt_tokens_details\":{\"cached_tokens\":80}}}";
                    var parsedResp = OpenAIWireCodec.ParseResponse(respJson, env);
                    Assert.True(parsedResp.IsOk);
                    Assert.Equal("hi", parsedResp.Value.Content);
                    Assert.Equal("thought", parsedResp.Value.ReasoningContent);
                    Assert.Equal(100, parsedResp.Value.PromptTokens);
                    Assert.Equal(80, parsedResp.Value.CachedTokens);
                }),
                ("LogSecretRedactor redacts generic credentials and RimMindHttpClientPool classifies loopback hosts", () =>
                {
                    string redacted = LogSecretRedactor.ForMessage("Authorization: Bearer sk-abc123456789012345 api_key=my_sk_9999999999999");
                    Assert.DoesNotContain("sk-abc123456789012345", redacted);
                    Assert.DoesNotContain("my_sk_9999999999999", redacted);

                    Assert.True(RimMindHttpClientPool.IsLoopbackEndpoint("http://localhost:11434/v1"));
                    Assert.True(RimMindHttpClientPool.IsLoopbackEndpoint("http://127.0.0.1:8080/v1"));
                    Assert.False(RimMindHttpClientPool.IsLoopbackEndpoint("https://api.openai.com/v1"));
                }));
        }

        [Fact]
        public void Generic_ui_overlay_biotab_provider_preset_and_action_formatting_contracts()
        {
            ContractCaseRunner.Run(
                ("RequestOverlayLayoutEvaluator.ShouldHideCompletely hides idle empty overlay when auto-hide enabled", () =>
                {
                    Assert.True(RequestOverlayLayoutEvaluator.ShouldHideCompletely(0, autoHideWhenEmpty: true, isExpanded: false, isDragging: false, isResizing: false));
                    Assert.False(RequestOverlayLayoutEvaluator.ShouldHideCompletely(1, autoHideWhenEmpty: true, isExpanded: false, isDragging: false, isResizing: false));
                    Assert.False(RequestOverlayLayoutEvaluator.ShouldHideCompletely(0, autoHideWhenEmpty: false, isExpanded: false, isDragging: false, isResizing: false));
                    Assert.False(RequestOverlayLayoutEvaluator.ShouldHideCompletely(0, autoHideWhenEmpty: true, isExpanded: true, isDragging: false, isResizing: false));
                }),
                ("BioTabBadgeRegistry orders providers and cleans up by owner", () =>
                {
                    BioTabBadgeRegistry.Clear();
                    try
                    {
                        BioTabBadgeRegistry.Register(new FakeBioBadgeProvider("b2", "ModB", 20));
                        BioTabBadgeRegistry.Register(new FakeBioBadgeProvider("a1", "ModA", 10));
                        var all = BioTabBadgeRegistry.GetAll();
                        Assert.Equal(2, all.Count);
                        Assert.Equal("a1", all[0].Id);
                        Assert.Equal("b2", all[1].Id);

                        Assert.Equal(1, BioTabBadgeRegistry.UnregisterByOwner("ModA"));
                        Assert.Single(BioTabBadgeRegistry.GetAll());
                    }
                    finally
                    {
                        BioTabBadgeRegistry.Clear();
                    }
                }),
                ("AIProviderRegistry supports preset registration, endpoint matching, and owner cleanup", () =>
                {
                    AIProviderRegistry.ResetToDefaults();
                    try
                    {
                        AIProviderRegistry.RegisterPreset(new AIProviderPreset(
                            Id: "test_preset",
                            ProviderId: AIProviders.OpenAI,
                            Endpoint: "https://api.example.invalid/v1",
                            ModelName: "example-model",
                            FallbackLabel: "Example AI",
                            HostMatchSubstring: "api.example.invalid",
                            OwnerModId: "TestMod"));

                        var matched = AIProviderRegistry.MatchPresetByEndpoint("https://api.example.invalid/v1/chat/completions");
                        Assert.NotNull(matched);
                        Assert.Equal("test_preset", matched!.Id);
                        Assert.NotEmpty(AIProviderRegistry.GetPresets(AIProviders.OpenAI));

                        AIProviderRegistry.UnregisterByOwner("TestMod");
                        Assert.Null(AIProviderRegistry.MatchPresetByEndpoint("https://api.example.invalid/v1"));
                    }
                    finally
                    {
                        AIProviderRegistry.ResetToDefaults();
                    }
                }),
                ("RimMindPawnLookup.FormatHumanReadableActionName strips generic prefixes cleanly without module hardcoding", () =>
                {
                    Assert.Equal("Take Job", RimMindPawnLookup.FormatHumanReadableActionName("pawn.job.take_job"));
                    Assert.Equal("Place Blueprint", RimMindPawnLookup.FormatHumanReadableActionName("place_blueprint"));
                    Assert.Equal("Action", RimMindPawnLookup.FormatHumanReadableActionName(""));
                }));
        }
    }
}
