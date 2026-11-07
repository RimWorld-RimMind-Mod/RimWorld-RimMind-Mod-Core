using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Agent.Perception;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Client;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Agent;
using RimMind.Application.Features.AgentBus;
using RimMind.Application.Features.Registry;
using RimMind.Application.Features.Tools;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Common;
using RimMind.Domain.Enums;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Agent;
using RimMind.Infrastructure.Mechanisms;
using RimMind.Infrastructure.Services.Clients.OpenAI;
using RimMind.Infrastructure.Services.Clients.Player2;
using RimMind.Presentation.Agent;
using RimMind.Presentation.Agent.Perception;
using Verse;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class BatchBCoreAgentMechanismContracts
    {
        [Fact]
        public void B2_BuiltInPerceptionSources_MatchInlineSense_AndSaveLoadWiresRegistry()
        {
            Find.TickManager.TicksGame = 1000;
            GenTemperature.CurrentTemperature = -18f;

            var otherPawn = new Pawn { thingIDNumber = 77, Position = new IntVec3(3, 0, 4) };
            var map = new Map
            {
                uniqueID = 1,
                weatherManager = new WeatherManager
                {
                    curWeather = new WeatherDef { defName = "Rain", label = "rain" }
                }
            };

            var pawn = new Pawn
            {
                thingIDNumber = 42,
                Drafted = true,
                Position = new IntVec3(0, 0, 0),
                Map = map,
                needs = new Pawn_NeedsTracker
                {
                    mood = new Need { CurLevel = 0.65f, def = new NeedDef { defName = "Mood", label = "mood" } },
                    AllNeeds = new List<Need>
                    {
                        new Need { CurLevel = 0.20f, def = new NeedDef { defName = "Food", label = "food" } }
                    }
                },
                health = new Pawn_HealthTracker
                {
                    hediffSet = new HediffSet
                    {
                        hediffs = new List<Hediff>
                        {
                            new Hediff { Visible = true, Severity = 0.8f, Label = "gunshot", def = new HediffDef { defName = "Gunshot" } },
                            new Hediff { Visible = true, Severity = 0.9f, Label = "gunshot duplicate", def = new HediffDef { defName = "Gunshot" } }
                        }
                    }
                },
                relations = new Pawn_RelationsTracker
                {
                    DirectRelations = new List<DirectPawnRelation>
                    {
                        new DirectPawnRelation { otherPawn = otherPawn, def = new PawnRelationDef { defName = "Friend", label = "friend" } }
                    }
                }
            };

            var bus = new AgentBusImpl();

            // 1. Inline fallback (no registry)
            var inlineAgent = new StubPawnAgentVerse(pawn);
            var inlinePerceiver = new PawnPerceiver(inlineAgent, bus);
            inlinePerceiver.Tick();

            // 2. Registered built-in sources + custom extension source
            var registry = new ExtensionRegistry<IPerceptionSource>();
            registry.Register(new MoodPerceptionSource());
            registry.Register(new HealthPerceptionSource());
            registry.Register(new CombatPerceptionSource());
            registry.Register(new NeedPerceptionSource());
            registry.Register(new SocialPerceptionSource());
            registry.Register(new EnvironmentPerceptionSource());

            var registryAgent = new StubPawnAgentVerse(pawn);
            var registryPerceiver = new PawnPerceiver(registryAgent, bus, registry);
            registryPerceiver.Tick();

            Assert.Equal(6, inlineAgent.PerceptionBuffer.Count);
            Assert.Equal(inlineAgent.PerceptionBuffer.Count, registryAgent.PerceptionBuffer.Count);

            var inlineEntries = inlineAgent.PerceptionBuffer.Entries;
            var registryEntries = registryAgent.PerceptionBuffer.Entries;
            Assert.Equal(
                inlineEntries.Select(e => (e.PerceptionType, e.Content, e.Importance)).ToList(),
                registryEntries.Select(e => (e.PerceptionType, e.Content, e.Importance)).ToList());

            // Registering a custom perception source preserves all 6 built-in sources
            registry.Register(new CustomTestPerceptionSource());
            Assert.Equal(7, registryPerceiver.GetActiveSources().Count);

            Find.TickManager.TicksGame = 2000;
            registryAgent.PerceptionBuffer.Clear();
            registryPerceiver.Tick();
            Assert.Contains(registryAgent.PerceptionBuffer.Entries, e => e.PerceptionType == "custom.alert");
            Assert.Contains(registryAgent.PerceptionBuffer.Entries, e => e.PerceptionType == "mood");

            // 3. Static contract: PawnAgentFactory.WireCollaborators is the single path for Create and Serialize,
            // and AgentComposition registers all 6 built-in perception sources.
            string factorySource = ReadCoreSource("Presentation/Agent/PawnAgentFactory.cs");
            string agentSource = ReadCoreSource("Presentation/Agent/PawnAgent.cs");
            string compositionSource = ReadCoreSource("Presentation/Runtime/Composition/AgentComposition.cs");

            Assert.Contains("internal void WireCollaborators(PawnAgent agent", factorySource, StringComparison.Ordinal);
            Assert.Contains("new PawnPerceiver(agent, bus, _perceptionSourceRegistry)", factorySource, StringComparison.Ordinal);
            Assert.Contains("factory.WireCollaborators(concrete", agentSource, StringComparison.Ordinal);
            Assert.DoesNotContain("new PawnPerceiver(concrete", agentSource, StringComparison.Ordinal);
            Assert.Contains("RegisterBuiltInPerceptionSources(perceptionRegistry)", compositionSource, StringComparison.Ordinal);
            Assert.Contains("new MoodPerceptionSource()", compositionSource, StringComparison.Ordinal);
            Assert.Contains("new HealthPerceptionSource()", compositionSource, StringComparison.Ordinal);
            Assert.Contains("new CombatPerceptionSource()", compositionSource, StringComparison.Ordinal);
            Assert.Contains("new NeedPerceptionSource()", compositionSource, StringComparison.Ordinal);
            Assert.Contains("new SocialPerceptionSource()", compositionSource, StringComparison.Ordinal);
            Assert.Contains("new EnvironmentPerceptionSource()", compositionSource, StringComparison.Ordinal);
        }

        [Fact]
        public void B3_ToolManifestRiskResolver_UsesManifestAndMechanismMetadata_InsteadOfSubstrings()
        {
            var toolRegistry = new ToolRegistry();
            // A benign read-only tool whose name contains "attack", "kill", and "execute" must NOT be flagged Critical/High
            toolRegistry.Register(new StubToolHandler(
                "query_attack_kill_execute_log",
                new ToolManifest { RiskLevel = RiskLevel.Low, RequiresApproval = false }));
            toolRegistry.Register(new StubToolHandler(
                "actions_place_blueprint",
                new ToolManifest { RiskLevel = RiskLevel.Medium, RequiresApproval = false }));
            toolRegistry.Register(new StubToolHandler(
                "colony_self_destruct",
                new ToolManifest { RiskLevel = RiskLevel.High, RequiresApproval = true }));

            var mechanismRegistry = new GameMechanismRegistry();
            var jobMechanism = new StubJobMechanismWithActionRisk();
            mechanismRegistry.Register(jobMechanism);

            var resolver = new ToolManifestRiskResolver(
                () => toolRegistry,
                () => mechanismRegistry,
                MechanismActionExecutor.ResolveToolId);

            // 1. Direct tool manifest risk & alias resolution
            Assert.Equal(RiskLevel.Low, resolver.Resolve("query_attack_kill_execute_log"));
            Assert.Equal(RiskLevel.Medium, resolver.Resolve("place_blueprint"));
            Assert.Equal(RiskLevel.Critical, resolver.Resolve("colony_self_destruct"));
            Assert.Equal(RiskLevel.Low, resolver.Resolve("dialogue.free"));
            Assert.Equal(RiskLevel.Low, resolver.Resolve(""));

            // 2. Mechanism operation & per-action risk escalation (pawn.job.force_rest vs pawn.job.arrest_pawn)
            Assert.Equal(RiskLevel.Medium, resolver.Resolve("pawn.job.force_rest"));
            Assert.Equal(RiskLevel.Critical, resolver.Resolve("pawn.job.arrest_pawn"));

            // 3. Unknown intent defaults to UnknownRisk (Medium)
            Assert.Equal(ToolManifestRiskResolver.UnknownRisk, resolver.Resolve("nonexistent.intent"));

            // 4. PawnAgent.AssessRiskLevel delegates to ToolManifestRiskResolver with no substring heuristics
            string agentSource = ReadCoreSource("Presentation/Agent/PawnAgent.cs");
            Assert.Contains("(_riskResolver ?? DefaultRiskResolver).Resolve(decision.ActionIntent)", agentSource, StringComparison.Ordinal);
            Assert.DoesNotContain("intent.Contains(\"attack\")", agentSource, StringComparison.Ordinal);
            Assert.DoesNotContain("intent.Contains(\"surgery\")", agentSource, StringComparison.Ordinal);
        }

        [Fact]
        public async Task B9_PawnIdParser_AndMechanismHandlers_ParseAllPawnIdForms_AndRejectInvalidTarget()
        {
            Assert.True(PawnIdParser.TryParse("12", out int id1) && id1 == 12);
            Assert.True(PawnIdParser.TryParse("Pawn_12", out int id2) && id2 == 12);
            Assert.True(PawnIdParser.TryParse("NPC-12", out int id3) && id3 == 12);
            Assert.True(PawnIdParser.TryParse("pawn-12", out int id4) && id4 == 12);
            Assert.True(PawnIdParser.TryParse("12.0", out int id5) && id5 == 12);
            Assert.False(PawnIdParser.TryParse("ColonistBob", out _));

            var writer = new RecordingMechanismWriter();
            var reader = new RecordingMechanismReader();
            var handler = new MechanismToolHandler(
                reader,
                writer,
                new NoOpMechanismTrigger(),
                new StubJobMechanismWithActionRisk(),
                MechanismOperationType.Set);

            // 1. "Pawn_12" at root resolves to 12 (not caller 42)
            var resPawnPrefix = await handler.ExecuteAsync(new ToolCallArgs
            {
                ToolCallId = "call-pawn-prefix",
                ToolName = "pawn.job.set",
                ArgumentsJson = "{\"pawn_id\":\"Pawn_12\",\"action\":\"force_rest\"}",
                PawnId = 42,
                NpcId = "NPC-42"
            }, CancellationToken.None);
            Assert.True(resPawnPrefix.IsOk);
            Assert.False(resPawnPrefix.Value.IsError);
            Assert.Equal(12, writer.LastWriteArgs?.PawnId);

            // 2. Nested params.pawn_id = "NPC-19" resolves to 19
            var resNested = await handler.ExecuteAsync(new ToolCallArgs
            {
                ToolCallId = "call-nested",
                ToolName = "pawn.job.set",
                ArgumentsJson = "{\"action\":\"force_rest\",\"params\":{\"pawn_id\":\"NPC-19\"}}",
                PawnId = 42
            }, CancellationToken.None);
            Assert.True(resNested.IsOk);
            Assert.False(resNested.Value.IsError);
            Assert.Equal(19, writer.LastWriteArgs?.PawnId);

            // 3. Missing pawn_id falls back to caller NpcId when PawnId is null
            var resNpcFallback = await handler.ExecuteAsync(new ToolCallArgs
            {
                ToolCallId = "call-npc-fallback",
                ToolName = "pawn.job.set",
                ArgumentsJson = "{\"action\":\"force_rest\"}",
                PawnId = null,
                NpcId = "Pawn_55"
            }, CancellationToken.None);
            Assert.True(resNpcFallback.IsOk);
            Assert.False(resNpcFallback.Value.IsError);
            Assert.Equal(55, writer.LastWriteArgs?.PawnId);

            // 4. Explicit but unparseable pawn_id ("Bob") MUST fail with IsError=true and NEVER execute on caller 42
            writer.LastWriteArgs = null;
            var resInvalid = await handler.ExecuteAsync(new ToolCallArgs
            {
                ToolCallId = "call-invalid",
                ToolName = "pawn.job.set",
                ArgumentsJson = "{\"pawn_id\":\"Bob\",\"action\":\"arrest_pawn\"}",
                PawnId = 42,
                NpcId = "NPC-42"
            }, CancellationToken.None);
            Assert.True(resInvalid.IsOk);
            Assert.True(resInvalid.Value.IsError);
            Assert.Contains("Bob", resInvalid.Value.Content, StringComparison.Ordinal);
            Assert.Null(writer.LastWriteArgs);

            // 5. MechanismListToolHandler also rejects invalid explicit pawn_id
            var listHandler = new MechanismListToolHandler(
                reader,
                new StubJobMechanismWithActionRisk(),
                new MechanismToolHelper.FallbackJsonExtractor());
            var listInvalid = await listHandler.ExecuteAsync(new ToolCallArgs
            {
                ToolCallId = "call-list-invalid",
                ToolName = "pawn.job.list",
                ArgumentsJson = "{\"pawn_id\":\"NotAPawnId\"}",
                PawnId = 42
            }, CancellationToken.None);
            Assert.True(listInvalid.IsOk);
            Assert.True(listInvalid.Value.IsError);
            Assert.Equal(0, reader.ListCallCount);
        }

        [Fact]
        public void B13_Player2Client_PreservesToolLoopFields_MatchingOpenAIRequestSerializer()
        {
            var envelope = new LlmRequestEnvelope
            {
                RequestId = "req-tool-loop",
                Temperature = 0.4f,
                MaxTokens = 600,
                Examples = new List<ChatMessage>
                {
                    new ChatMessage { Role = "user", Content = "Example question" },
                    new ChatMessage { Role = "assistant", Content = "Example answer" }
                },
                Messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "system", Content = "You are a colony agent." },
                    new ChatMessage { Role = "user", Content = "Inspect and tend colonists." },
                    new ChatMessage
                    {
                        Role = "assistant",
                        Content = "",
                        ReasoningContent = "Checking health of pawn 12 and 15 first.",
                        ToolCalls = new List<ChatToolCall>
                        {
                            new ChatToolCall { Id = "call_1", Name = "pawn_health_query", Arguments = "{\"pawn_id\":12}" },
                            new ChatToolCall { Id = "call_2", Name = "pawn_health_query", Arguments = "{\"pawn_id\":15}" }
                        }
                    },
                    new ChatMessage { Role = "tool", ToolCallId = "call_1", Content = "{\"bleeding\":true}" },
                    new ChatMessage { Role = "tool", ToolCallId = "call_2", Content = "{\"bleeding\":false}" }
                },
                Tools = new List<StructuredTool>
                {
                    new StructuredTool
                    {
                        Name = "pawn.health.query",
                        Description = "Query pawn health",
                        Parameters = "{\"type\":\"object\",\"properties\":{\"pawn_id\":{\"type\":\"integer\"}}}",
                        ToolChoice = "required"
                    }
                }
            };

            var openAiJson = JObject.Parse(OpenAIRequestSerializer.BuildRequestJson(envelope, "default", 600, stream: false));
            var (_, player2RawJson) = Player2Client.BuildPayload(envelope, stream: false, "https://api.player2.game", 600);
            var player2Json = JObject.Parse(player2RawJson);

            Assert.Equal(openAiJson["messages"]!.ToString(), player2Json["messages"]!.ToString());
            Assert.Equal(openAiJson["tools"]!.ToString(), player2Json["tools"]!.ToString());
            Assert.Equal(openAiJson["tool_choice"]!.ToString(), player2Json["tool_choice"]!.ToString());

            var messages = (JArray)player2Json["messages"]!;
            // system + 2 examples + user + assistant(tool_calls) + 2 distinct tool messages = 7 messages
            Assert.Equal(7, messages.Count);
            Assert.Equal("Checking health of pawn 12 and 15 first.", messages[4]!["reasoning_content"]?.ToString());
            Assert.Equal(2, ((JArray)messages[4]!["tool_calls"]!).Count);
            Assert.Equal("call_1", messages[5]!["tool_call_id"]?.ToString());
            Assert.Equal("call_2", messages[6]!["tool_call_id"]?.ToString());
        }

        [Fact]
        public async Task B15_OpenAIClient_AndSerializer_HonorForceJsonMode_WhenNoSchemaOnEnvelope()
        {
            var envelopeWithoutJsonWord = new LlmRequestEnvelope
            {
                RequestId = "req-json-mode",
                Messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "system", Content = "You are a RimWorld advisor." },
                    new ChatMessage { Role = "user", Content = "Recommend next colony priority." }
                }
            };

            // 1. ForceJsonMode = false -> no response_format
            var disabledJson = JObject.Parse(OpenAIRequestSerializer.BuildRequestJson(
                envelopeWithoutJsonWord, "gpt-4o-mini", 500, stream: false, forceJsonMode: false));
            Assert.Null(disabledJson["response_format"]);

            // 2. ForceJsonMode = true via OpenAIClient -> emits response_format=json_object and injects JSON keyword guard
            string? capturedBody = null;
            var settings = new StubOpenAISettings
            {
                ApiEndpoint = "https://api.example.com/v1",
                ModelName = "gpt-4o-mini",
                ApiKey = "sk-test-12345678",
                ForceJsonMode = true,
                MaxTokens = 500
            };
            var client = new OpenAIClient(settings, postAsyncOverride: (url, body, auth, hName, hVal, timeout, ct) =>
            {
                capturedBody = body;
                return Task.FromResult(("{\"choices\":[{\"message\":{\"content\":\"{\\\"ok\\\":true}\"}}],\"usage\":{\"total_tokens\":10}}", 200L));
            });

            var sendRes = await client.SendAsync(envelopeWithoutJsonWord);
            Assert.True(sendRes.IsOk);
            Assert.NotNull(capturedBody);

            var enabledJson = JObject.Parse(capturedBody!);
            Assert.Equal("json_object", enabledJson["response_format"]?["type"]?.ToString());
            Assert.Contains("JSON", enabledJson["messages"]?[0]?["content"]?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

            // 3. Explicit JsonSchema on envelope takes precedence over ForceJsonMode
            var schemaEnvelope = new LlmRequestEnvelope
            {
                RequestId = "req-schema-mode",
                JsonSchema = "{\"type\":\"object\",\"properties\":{\"ok\":{\"type\":\"boolean\"}}}",
                Messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "user", Content = "Return status json." }
                }
            };
            var schemaJson = JObject.Parse(OpenAIRequestSerializer.BuildRequestJson(
                schemaEnvelope, "gpt-4o-mini", 500, stream: false, forceJsonMode: true));
            Assert.Equal("json_schema", schemaJson["response_format"]?["type"]?.ToString());
        }

        private static string ReadCoreSource(string relativePath)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Core", "Source")))
                directory = directory.Parent;
            string root = Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repo root not found"), "RimMind-Core", "Source");
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private sealed class CustomTestPerceptionSource : IPerceptionSource
        {
            public string Id => "test.perception.custom";
            public string OwnerModId => "test.mod";
            public int Priority => 15;
            public bool ShouldSense(IAgentInfo agent) => true;
            public IReadOnlyList<PerceptionBufferEntry> Sense(IAgentInfo agent) => new[]
            {
                new PerceptionBufferEntry
                {
                    PerceptionType = "custom.alert",
                    Content = "Custom perception alert",
                    Importance = 0.9f,
                    Tick = Find.TickManager.TicksGame
                }
            };
        }

        private sealed class StubPawnAgentVerse : IPawnAgentVerse
        {
            public StubPawnAgentVerse(Pawn pawn)
            {
                Pawn = pawn;
                Identity = new AgentIdentity($"NPC-{pawn.thingIDNumber}", pawn.thingIDNumber, pawn.LabelShort);
            }

            public Pawn Pawn { get; }
            public AgentState State { get; set; } = AgentState.Active;
            public AgentWorkflowPhase WorkflowPhase => AgentWorkflowPhase.Idle;
            public AgentIdentity Identity { get; }
            public AgentGoalStack GoalStack { get; } = new AgentGoalStack();
            public IStrategyOptimizer StrategyOptimizer { get; } = null!;
            public IPerceptionBuffer PerceptionBuffer { get; } = new PerceptionBuffer();
            public bool IsActive => State == AgentState.Active;
            public bool IsPawnValid => !Pawn.Dead;
            public AgentModeId CurrentModeId => AgentModeId.Reactive;
            public IAgentMode CurrentMode => null!;
            public int? LastThinkTick { get; set; }
            public AgentAutonomyLevel AutonomyLevel { get; set; } = AgentAutonomyLevel.Autonomous;
            public IReadOnlyList<BehaviorRecord> BehaviorHistory => Array.Empty<BehaviorRecord>();
            string IAgentInfo.NpcId => Identity.NpcId;
            string IAgentInfo.Label => Pawn.LabelShort;
            int IAgentInfo.GoalCount => 0;
            public RimMind.Infrastructure.Verse.AgentIntentSession? ActiveIntentSession => null;
            public void StartIntentSession(RimMind.Infrastructure.Verse.AgentIntentSession session) { }
            public void ClearIntentSession() { }
            public Verse.AI.Job? ConsumePendingJob() => null;
            object? IJobProvider.ConsumePendingJob() => null;
            public void SetPendingJob(Verse.AI.Job job) { }
            public void Tick() { }
            public void TransitionWorkflow(AgentWorkflowPhase target) { }
            public bool TransitionTo(AgentState newState) { State = newState; return true; }
            public void AddGoal(AgentGoal goal) { }
            public bool RemoveGoal(string goalDescription) => false;
            public void ForceThink() { }
            public Result<Unit, RimMindError> ExecuteDecision(AgentDecision decision) => Result<Unit, RimMindError>.Ok(Unit.Value);
            public void RecordBehavior(BehaviorRecordDto record) { }
            IReadOnlyList<BehaviorRecordDto> IAgentInfo.GetRecentHistory(int count) => Array.Empty<BehaviorRecordDto>();
            public IReadOnlyList<BehaviorRecord> GetRecentHistory(int count = 10) => Array.Empty<BehaviorRecord>();
            public float GetRecentSuccessRate(int count = 10) => 1f;
            public void SwitchMode(AgentModeId modeId) { }
            public void ResubscribeEvents() { }
            public void Cleanup() { }
            public void Destroy() { }
            public string GetDebugInfo() => "";
        }

        private sealed class StubToolHandler : IToolHandler
        {
            public StubToolHandler(string id, ToolManifest manifest)
            {
                Id = id;
                Definition = new ToolDefinition
                {
                    Id = id,
                    Description = id,
                    ParametersSchema = "{}",
                    Manifest = manifest
                };
            }

            public string Id { get; }
            public string OwnerModId => "test";
            public ToolDefinition Definition { get; }
            public Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
                => Task.FromResult(Result<ToolResult, RimMindError>.Ok(new ToolResult { ToolCallId = args.ToolCallId, Content = "ok" }));
        }

        private sealed class StubJobMechanismWithActionRisk : IGameMechanism, IMechanismActionRiskProvider
        {
            public string Id => MechanismId;
            public string OwnerModId => "test";
            public string MechanismId => "pawn.job";
            public MechanismScope Scope => MechanismScope.Pawn;
            public MechanismRisk Risk => MechanismRisk.Moderate;
            public IReadOnlyList<MechanismOperationType> SupportedOperations { get; } =
                new[] { MechanismOperationType.Query, MechanismOperationType.Set, MechanismOperationType.List };
            public MechanismDocs Docs { get; } = new MechanismDocs { Summary = "Job mechanism" };
            public MechanismRisk GetRiskForOperation(MechanismOperationType operation) => Risk;
            public IReadOnlyList<MechanismActionInfo>? GetWriteActions() => new[]
            {
                new MechanismActionInfo { Action = "force_rest", Description = "Rest" },
                new MechanismActionInfo { Action = "arrest_pawn", Description = "Arrest" }
            };
            public MechanismRisk? GetRiskForAction(string action)
                => string.Equals(action, "arrest_pawn", StringComparison.OrdinalIgnoreCase)
                    ? MechanismRisk.Dangerous
                    : null;

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

        private sealed class RecordingMechanismWriter : IMechanismWriter
        {
            public MechanismWriteArgs? LastWriteArgs { get; set; }
            public Task<Result<bool, RimMindError>> ExecuteSetAsync(MechanismWriteArgs args, CancellationToken ct)
            {
                LastWriteArgs = args;
                return Task.FromResult(Result<bool, RimMindError>.Ok(true));
            }
            public Task<Result<bool, RimMindError>> ExecuteAddAsync(MechanismWriteArgs args, CancellationToken ct)
            {
                LastWriteArgs = args;
                return Task.FromResult(Result<bool, RimMindError>.Ok(true));
            }
            public Task<Result<bool, RimMindError>> ExecuteRemoveAsync(MechanismWriteArgs args, CancellationToken ct)
            {
                LastWriteArgs = args;
                return Task.FromResult(Result<bool, RimMindError>.Ok(true));
            }
        }

        private sealed class RecordingMechanismReader : IMechanismReader
        {
            public int ListCallCount { get; private set; }
            public Task<Result<string, RimMindError>> ExecuteQueryAsync(MechanismReadArgs args, CancellationToken ct)
                => Task.FromResult(Result<string, RimMindError>.Ok("{}"));
            public Task<Result<IReadOnlyList<MechanismEnumResult>, RimMindError>> ExecuteListAsync(int? pawnId, CancellationToken ct)
            {
                ListCallCount++;
                return Task.FromResult(Result<IReadOnlyList<MechanismEnumResult>, RimMindError>.Ok(Array.Empty<MechanismEnumResult>()));
            }
        }

        private sealed class NoOpMechanismTrigger : IMechanismTrigger
        {
            public Task<Result<bool, RimMindError>> ExecuteToggleAsync(MechanismWriteArgs args, CancellationToken ct)
                => Task.FromResult(Result<bool, RimMindError>.Ok(true));
            public Task<Result<bool, RimMindError>> ExecuteTriggerAsync(MechanismWriteArgs args, CancellationToken ct)
                => Task.FromResult(Result<bool, RimMindError>.Ok(true));
            public Task<Result<bool, RimMindError>> ExecuteWatchAsync(MechanismWriteArgs args, CancellationToken ct)
                => Task.FromResult(Result<bool, RimMindError>.Ok(true));
        }

        private sealed class StubOpenAISettings : IOpenAISettings
        {
            public string ApiEndpoint { get; set; } = "";
            public string ModelName { get; set; } = "";
            public string ApiKey { get; set; } = "";
            public bool ForceJsonMode { get; set; }
            public int MaxTokens { get; set; } = 500;
            public float DefaultTemperature { get; set; } = 0.7f;
            public bool DebugLogging { get; set; }
            public IReadOnlyList<BackupEndpointNode>? BackupEndpoints { get; set; }
            public bool IsConfigured() => !string.IsNullOrWhiteSpace(ApiEndpoint) && !string.IsNullOrWhiteSpace(ApiKey);
        }
    }
}
