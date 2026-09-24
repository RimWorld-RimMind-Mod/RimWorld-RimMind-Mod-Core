using System;
using System.Collections.Generic;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Features.Agent;
using RimMind.Application.Features.Agent.Modes;
using RimMind.Application.Features.Context;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Common;
using RimMind.Domain.Enums;
using RimMind.Domain.Llm;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class DomainApplicationContracts
    {
        [Fact]
        public void Unit_preserves_value_semantics()
        {
            ContractCaseRunner.Run(
                ("all values compare equal", () => Assert.Equal(Unit.Value, new Unit())),
                ("equality operator agrees with Equals", () => Assert.True(Unit.Value == new Unit())),
                ("inequality operator rejects no unit value", () => Assert.False(Unit.Value != new Unit())),
                ("non-unit values are different", () => Assert.False(Unit.Value.Equals("unit"))),
                ("text and hash are deterministic", () =>
                {
                    Assert.Equal("()", Unit.Value.ToString());
                    Assert.Equal(0, Unit.Value.GetHashCode());
                }));
        }

        [Fact]
        public void Request_envelope_builder_enforces_identity_and_defaults()
        {
            ContractCaseRunner.Run(
                ("scenario identity is required", () =>
                    Assert.Throws<InvalidOperationException>(() => new LlmRequestEnvelopeBuilder().Build())),
                ("scenario supplies stable defaults", () =>
                {
                    LlmRequestEnvelope envelope = new LlmRequestEnvelopeBuilder()
                        .ForScenarioId("decision")
                        .Build();
                    Assert.Equal("decision", envelope.ScenarioId);
                    Assert.Equal("decision", envelope.ModId);
                    Assert.Equal(800, envelope.MaxTokens);
                    Assert.Equal(0.7f, envelope.Temperature);
                    Assert.Equal(AIRequestPriority.Normal, envelope.Priority);
                    Assert.Equal(ToolCallDispatchMode.Auto, envelope.ToolDispatchMode);
                }),
                ("caller overrides request policy", () =>
                {
                    LlmRequestEnvelope envelope = new LlmRequestEnvelopeBuilder()
                        .ForScenarioId("advisor")
                        .WithModId("RimMindAdvisor")
                        .WithMaxTokens(1200)
                        .WithTemperature(0.2f)
                        .WithPriority(AIRequestPriority.High)
                        .WithToolDispatchMode(ToolCallDispatchMode.Manual)
                        .Build();
                    Assert.Equal("RimMindAdvisor", envelope.ModId);
                    Assert.Equal(1200, envelope.MaxTokens);
                    Assert.Equal(0.2f, envelope.Temperature);
                    Assert.Equal(AIRequestPriority.High, envelope.Priority);
                    Assert.Equal(ToolCallDispatchMode.Manual, envelope.ToolDispatchMode);
                }),
                ("npc request carries game state", () =>
                {
                    LlmRequestEnvelope envelope = LlmRequestEnvelopeBuilder
                        .ForNpc("pawn-7", new GameStateInfo().AddSection("mood", "content"))
                        .ForScenarioId("dialogue")
                        .Build();
                    Assert.Equal("pawn-7", envelope.NpcId);
                    Assert.Contains("content", envelope.GameStateInfo!.ToXml(), StringComparison.Ordinal);
                }),
                ("streaming and correlation identity are published", () =>
                {
                    LlmRequestEnvelope envelope = new LlmRequestEnvelopeBuilder()
                        .ForScenarioId("stream")
                        .Streaming(_ => { })
                        .Build();
                    Assert.True(envelope.IsStreaming);
                    Assert.False(string.IsNullOrWhiteSpace(envelope.RequestId));
                    Assert.False(string.IsNullOrWhiteSpace(envelope.TraceId));
                }));
        }

        [Fact]
        public void Llm_response_copy_is_non_destructive()
        {
            ContractCaseRunner.Run(
                ("copy preserves original values", () =>
                {
                    LlmResponse original = SampleResponse();
                    LlmResponse copy = original.With();
                    Assert.NotSame(original, copy);
                    Assert.Equal(original.Content, copy.Content);
                    Assert.Equal(original.TokensUsed, copy.TokensUsed);
                }),
                ("string override changes only the copy", () =>
                {
                    LlmResponse original = SampleResponse();
                    LlmResponse copy = original.With(content: "updated");
                    Assert.Equal("updated", copy.Content);
                    Assert.Equal("hello", original.Content);
                    Assert.Equal(original.TokensUsed, copy.TokensUsed);
                }),
                ("numeric override keeps unrelated metrics", () =>
                {
                    LlmResponse original = SampleResponse();
                    LlmResponse copy = original.With(tokensUsed: 500);
                    Assert.Equal(500, copy.TokensUsed);
                    Assert.Equal(original.ProcessingMs, copy.ProcessingMs);
                }),
                ("state override keeps priority", () =>
                {
                    LlmResponse original = SampleResponse();
                    LlmResponse copy = original.With(state: AIRequestState.Error);
                    Assert.Equal(AIRequestState.Error, copy.State);
                    Assert.Equal(original.Priority, copy.Priority);
                }),
                ("chained copies accumulate changes", () =>
                {
                    LlmResponse copy = SampleResponse()
                        .With(content: "step-one")
                        .With(tokensUsed: 200)
                        .With(state: AIRequestState.Processing);
                    Assert.Equal("step-one", copy.Content);
                    Assert.Equal(200, copy.TokensUsed);
                    Assert.Equal(AIRequestState.Processing, copy.State);
                }));
        }

        [Fact]
        public void Prompt_augmentation_is_ordered_and_non_destructive()
        {
            ContractCaseRunner.Run(
                ("augmentations follow the final system turn", () =>
                {
                    var messages = new List<ChatMessage>
                    {
                        new() { Role = "system", Content = "system" },
                        new() { Role = "user", Content = "question" }
                    };
                    PromptAugmentation.InsertAfterLastSystem(
                        messages,
                        new[] { new PromptAugmentation("extra", "context", 1) });
                    Assert.Equal(new[] { "system", "context", "question" }, messages.ConvertAll(message => message.Content));
                }),
                ("missing system turn inserts at the beginning", () =>
                {
                    var messages = new List<ChatMessage> { new() { Role = "user", Content = "question" } };
                    PromptAugmentation.InsertAfterLastSystem(
                        messages,
                        new[] { new PromptAugmentation("extra", "context", 1) });
                    Assert.Equal("context", messages[0].Content);
                }),
                ("priority sorts before identity", () =>
                {
                    var messages = new List<ChatMessage>();
                    PromptAugmentation.InsertAfterLastSystem(messages, new[]
                    {
                        new PromptAugmentation("z", "last", 20),
                        new PromptAugmentation("b", "second", 10),
                        new PromptAugmentation("a", "first", 10)
                    });
                    Assert.Equal(new[] { "first", "second", "last" }, messages.ConvertAll(message => message.Content));
                }),
                ("blank content is ignored", () =>
                {
                    var messages = new List<ChatMessage>();
                    PromptAugmentation.InsertAfterLastSystem(
                        messages,
                        new[] { new PromptAugmentation("blank", " ", 1) });
                    Assert.Empty(messages);
                }),
                ("existing conversation objects are retained", () =>
                {
                    var user = new ChatMessage { Role = "user", Content = "question" };
                    var messages = new List<ChatMessage> { user };
                    PromptAugmentation.InsertAfterLastSystem(
                        messages,
                        new[] { new PromptAugmentation("extra", "context", 1) });
                    Assert.Same(user, messages[1]);
                }));
        }

        [Fact]
        public void Decision_intent_mapping_preserves_mechanism_and_parameters()
        {
            ContractCaseRunner.Run(
                ("qualified action splits at the final separator", () =>
                {
                    var parsed = DecisionMapper.ParseActionIntent("pawn.interaction.social_chat");
                    Assert.Equal("pawn.interaction", parsed.mechanismId);
                    Assert.Equal("social_chat", parsed.action);
                }),
                ("unqualified action uses pawn job", () =>
                {
                    var parsed = DecisionMapper.ParseActionIntent("force_rest");
                    Assert.Equal("pawn.job", parsed.mechanismId);
                    Assert.Equal("force_rest", parsed.action);
                }),
                ("empty action retains a safe default", () =>
                {
                    var parsed = DecisionMapper.ParseActionIntent(null!);
                    Assert.Equal("pawn.job", parsed.mechanismId);
                    Assert.Equal(string.Empty, parsed.action);
                }),
                ("object parameter becomes named arguments", () =>
                {
                    AgentDecision decision = new(
                        "pawn.job.move_to",
                        "move",
                        null,
                        "{\"cell_x\":10,\"cell_z\":20}");
                    var args = DecisionMapper.ToWriteArgs(decision, 42);
                    Assert.Equal("10", args.Params!["cell_x"]);
                    Assert.Equal("20", args.Params["cell_z"]);
                    Assert.NotNull(args.ValueJson);
                }),
                ("target and trace identities are propagated", () =>
                {
                    AgentDecision decision = new(
                        "pawn.job.tend_pawn",
                        "help",
                        "99",
                        null,
                        "call-7");
                    var args = DecisionMapper.ToWriteArgs(decision, 42);
                    Assert.Equal(42, args.PawnId);
                    Assert.Equal("99", args.Params!["target_pawn_id"]);
                    Assert.Equal("call-7", args.TraceId);
                }));
        }

        [Fact]
        public void Decision_operation_inference_uses_stable_intent_prefixes()
        {
            ContractCaseRunner.Run(
                ("force intent triggers", () => Assert.Equal(MechanismOperationType.Trigger, DecisionMapper.InferOperationType("force_rest"))),
                ("set intent sets", () => Assert.Equal(MechanismOperationType.Set, DecisionMapper.InferOperationType("set_priority"))),
                ("grant intent adds", () => Assert.Equal(MechanismOperationType.Add, DecisionMapper.InferOperationType("grant_permission"))),
                ("toggle intent toggles", () => Assert.Equal(MechanismOperationType.Toggle, DecisionMapper.InferOperationType("toggle_power"))),
                ("clear intent removes", () => Assert.Equal(MechanismOperationType.Remove, DecisionMapper.InferOperationType("clear_cache"))),
                ("unknown intent safely defaults to set", () => Assert.Equal(MechanismOperationType.Set, DecisionMapper.InferOperationType("wander"))));
        }

        [Fact]
        public void Context_diff_formats_change_and_expiry_semantics()
        {
            ContractCaseRunner.Run(
                ("expiry boundary remains valid", () => Assert.False(new ContextDiff { ExpireTick = 100 }.IsExpired(100))),
                ("tick after boundary expires", () => Assert.True(new ContextDiff { ExpireTick = 100 }.IsExpired(101))),
                ("tick before boundary remains valid", () => Assert.False(new ContextDiff { ExpireTick = 100 }.IsExpired(99))),
                ("new value omits an empty old value", () =>
                    Assert.Equal("[health] 80", new ContextDiff { Key = "health", OldValue = "", NewValue = "80" }.Format())),
                ("changed value exposes the transition", () =>
                    Assert.Equal("[health] 100 -> 80", new ContextDiff { Key = "health", OldValue = "100", NewValue = "80" }.Format())),
                ("scenario registry uses only operation-scoped services", ScenarioRegistryUsesOperationScopedServices));
        }

        private static void ScenarioRegistryUsesOperationScopedServices()
        {
            ScenarioRegistry.Clear();
            var oldLog = new CapturingLogSink();
            var currentLog = new CapturingLogSink();
            var oldTranslation = new PrefixTranslationService("old:");

            ScenarioRegistry.RegisterCoreScenarios(oldTranslation, oldLog);
            Assert.StartsWith("old:", ScenarioRegistry.Get(ScenarioIds.Dialogue)!.Description, StringComparison.Ordinal);
            ScenarioRegistry.Clear();
            ScenarioRegistry.RegisterCoreScenarios();
            Assert.Equal("RimMind.Application.Scenario.Dialogue", ScenarioRegistry.Get(ScenarioIds.Dialogue)!.Description);

            ScenarioRegistry.Register("duplicate", 1, "first", logSink: oldLog);
            ScenarioRegistry.Register("duplicate", 2, "second", logSink: currentLog);
            ScenarioRegistry.Register("duplicate", 3, "third");

            Assert.Empty(oldLog.Warnings);
            Assert.Single(currentLog.Warnings);
            ScenarioRegistry.Clear();
        }

        [Fact]
        public void Decision_response_parsing_degrades_to_dialogue()
        {
            ContractCaseRunner.Run(
                ("valid action payload maps action and reason", () =>
                {
                    var parsed = ThinkStrategyHelper.ParseDecisionCore(new LlmResponse
                    {
                        Content = "<Action>{\"action\":\"force_rest\",\"reason\":\"tired\"}</Action>"
                    });
                    Assert.True(parsed.IsOk);
                    Assert.Equal("force_rest", parsed.Value.ActionIntent);
                    Assert.Equal("tired", parsed.Value.Reason);
                }),
                ("plain text becomes free dialogue", () =>
                {
                    var parsed = ThinkStrategyHelper.ParseDecisionCore(new LlmResponse { Content = "hello" });
                    Assert.True(parsed.IsOk);
                    Assert.Equal("dialogue.free", parsed.Value.ActionIntent);
                    Assert.Equal("hello", parsed.Value.Reason);
                }),
                ("empty text remains a valid free dialogue decision", () =>
                {
                    var parsed = ThinkStrategyHelper.ParseDecisionCore(new LlmResponse { Content = "" });
                    Assert.True(parsed.IsOk);
                    Assert.Equal("dialogue.free", parsed.Value.ActionIntent);
                }),
                ("missing reason defaults to empty", () =>
                {
                    var parsed = ThinkStrategyHelper.ParseDecisionCore(new LlmResponse
                    {
                        Content = "<Action>{\"action\":\"force_rest\"}</Action>"
                    });
                    Assert.True(parsed.IsOk);
                    Assert.Equal(string.Empty, parsed.Value.Reason);
                }),
                ("optional target and parameter survive parsing", () =>
                {
                    var parsed = ThinkStrategyHelper.ParseDecisionCore(new LlmResponse
                    {
                        Content = "<Action>{\"action\":\"tend_pawn\",\"target\":\"42\",\"param\":\"urgent\"}</Action>"
                    });
                    Assert.True(parsed.IsOk);
                    Assert.Equal("42", parsed.Value.TargetPawnId);
                    Assert.Equal("urgent", parsed.Value.Param);
                }));
        }

        private static LlmResponse SampleResponse()
        {
            return new LlmResponse
            {
                RequestId = "request-1",
                Content = "hello",
                TokensUsed = 100,
                PromptTokens = 60,
                CompletionTokens = 40,
                State = AIRequestState.Completed,
                Priority = AIRequestPriority.Normal,
                ProcessingMs = 200
            };
        }

        private sealed class CapturingLogSink : ILogSink
        {
            public List<string> Warnings { get; } = new();
            public void Message(string msg) { }
            public void Warning(string msg) => Warnings.Add(msg);
            public void Error(string msg) { }
            public void LogFromBackground(string msg, bool isWarning = false) { }
        }

        private sealed class PrefixTranslationService : ITranslationService
        {
            private readonly string _prefix;
            public PrefixTranslationService(string prefix) => _prefix = prefix;
            public string Translate(string key) => _prefix + key;
            public string Translate(string key, object arg0) => Translate(key);
            public string Translate(string key, object arg0, object arg1) => Translate(key);
            public string Translate(string key, object arg0, object arg1, object arg2) => Translate(key);
        }
    }
}
