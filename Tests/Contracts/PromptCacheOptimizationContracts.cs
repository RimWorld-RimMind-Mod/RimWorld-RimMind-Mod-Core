using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Prompt;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Services.Clients.OpenAI;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class PromptCacheOptimizationContracts
    {
        [Fact]
        public async Task ContextOrchestrator_assembles_messages_in_strict_4zone_order()
        {
            var runtime = new AsyncContextBuildContracts.ContextRuntime();

            // Register all layers
            runtime.Keys.Register(new ContextProviderDef("system_rule", ContextLayer.L0_Static, 1,
                (_, _) => Task.FromResult<string?>("Directive: Colony Survival")));
            runtime.Keys.Register(new ContextProviderDef("pawn_bio", ContextLayer.L1_Baseline, 1,
                (_, _) => Task.FromResult<string?>("Name: John, Traits: HardWorker")));
            runtime.Keys.Register(new ContextProviderDef("weather_time", ContextLayer.L2_Environment, 1,
                (_, _) => Task.FromResult<string?>("Weather: Clear, Time: 08:00")));
            runtime.Keys.Register(new ContextProviderDef("mood_health", ContextLayer.L3_State, 1,
                (_, _) => Task.FromResult<string?>("Mood: 75, Health: Good")));
            runtime.Keys.Register(new ContextProviderDef("proximity", ContextLayer.L5_Sensor, 1,
                (_, _) => Task.FromResult<string?>("Sensors: No threat nearby")));

            // Add history
            runtime.History.AddTurn("pawn-1", "How is the colony?", "Everything is calm.", "dialogue");

            var snapshot = await runtime.Engine.BuildSnapshotFromEnvelopeAsync("pawn-1", "Let's build a shelter.",
                maxTokens: 500, temperature: 0.3f, scenarioId: "dialogue");

            Assert.NotNull(snapshot);
            var msgs = snapshot.Messages;

            // Strict 4-Zone sequence:
            // Zone 1: L0 Static
            // Zone 2: L1 Baseline
            // Zone 3: History (User -> Assistant)
            // Zone 4: Volatile Tail (L2 -> L3 -> L5 -> Current Query)
            Assert.Equal("L0", msgs[0].LayerTag);
            Assert.Equal("L1", msgs[1].LayerTag);
            Assert.Equal("L4", msgs[2].LayerTag); // History user
            Assert.Equal("L4", msgs[3].LayerTag); // History assistant
            Assert.Equal("L2", msgs[4].LayerTag);
            Assert.Equal("L3", msgs[5].LayerTag);
            Assert.Equal("L5", msgs[6].LayerTag);
            Assert.Equal("L4", msgs[7].LayerTag); // Current query

            Assert.Equal("user", msgs[2].Role);
            Assert.Equal("assistant", msgs[3].Role);
            Assert.Equal("user", msgs[7].Role);
            Assert.Equal("Let's build a shelter.", msgs[7].Content);
        }

        [Fact]
        public async Task PromptCache_prefix_remains_100_percent_byte_identical_across_ticks_when_volatile_data_changes()
        {
            var runtime = new AsyncContextBuildContracts.ContextRuntime();

            // Zone 1 & 2 (Static / Semi-static)
            runtime.Keys.Register(new ContextProviderDef("system_rule", ContextLayer.L0_Static, 1,
                (_, _) => Task.FromResult<string?>("Static Core Directives")));
            runtime.Keys.Register(new ContextProviderDef("pawn_bio", ContextLayer.L1_Baseline, 1,
                (_, _) => Task.FromResult<string?>("Pawn: Alice, Passion: Mining")));

            // Zone 4 (Volatile - changes with game ticks)
            int clockMinute = 0;
            runtime.Keys.Register(new ContextProviderDef("time_clock", ContextLayer.L2_Environment, 1,
                (_, _) => Task.FromResult<string?>($"GameTime: 12:{clockMinute:D2}")));

            // Zone 3 (History)
            runtime.History.AddTurn("alice", "Hello Alice", "Hello Administrator", "dialogue");

            // Turn 1 at 12:00
            clockMinute = 0;
            var snap1 = await runtime.Engine.BuildSnapshotFromEnvelopeAsync("alice", "Report status.",
                maxTokens: 500, temperature: 0.3f, scenarioId: "dialogue");

            // Turn 2 at 12:05 (time changed, but history and static profile did not change)
            clockMinute = 5;
            var snap2 = await runtime.Engine.BuildSnapshotFromEnvelopeAsync("alice", "Report status.",
                maxTokens: 500, temperature: 0.3f, scenarioId: "dialogue");

            Assert.NotNull(snap1);
            Assert.NotNull(snap2);

            // Prefix = Zone 1 (L0) + Zone 2 (L1) + Zone 3 (History User + Assistant)
            string prefix1 = string.Join("||", snap1!.Messages.Take(4).Select(m => $"{m.Role}:{m.Content}"));
            string prefix2 = string.Join("||", snap2!.Messages.Take(4).Select(m => $"{m.Role}:{m.Content}"));

            // Must be 100% byte-for-byte identical! This guarantees 100% KV-Cache hit on DeepSeek / OpenAI / Anthropic!
            Assert.Equal(prefix1, prefix2);

            // Volatile tail differs
            string tail1 = snap1.Messages[4].Content ?? "";
            string tail2 = snap2.Messages[4].Content ?? "";
            Assert.Contains("12:00", tail1);
            Assert.Contains("12:05", tail2);
            Assert.NotEqual(tail1, tail2);
        }

        [Fact]
        public void OpenAIRequestSerializer_sorts_tools_deterministically_by_name()
        {
            var envelope = new LlmRequestEnvelope
            {
                Messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "system", Content = "Test" },
                    new ChatMessage { Role = "user", Content = "Run" }
                },
                Tools = new List<StructuredTool>
                {
                    new StructuredTool { Name = "zeta_tool", Description = "Zeta" },
                    new StructuredTool { Name = "alpha_tool", Description = "Alpha" },
                    new StructuredTool { Name = "beta_tool", Description = "Beta" }
                }
            };

            string json = OpenAIRequestSerializer.BuildRequestJson(envelope, "gpt-4o-mini", 500);
            var parsed = JObject.Parse(json);
            var tools = parsed["tools"] as JArray;

            Assert.NotNull(tools);
            Assert.Equal(3, tools.Count);
            Assert.Equal("alpha_tool", tools[0]["function"]?["name"]?.ToString());
            Assert.Equal("beta_tool", tools[1]["function"]?["name"]?.ToString());
            Assert.Equal("zeta_tool", tools[2]["function"]?["name"]?.ToString());
        }

        [Fact]
        public void PromptBudget_compose_preserves_chronological_sequence_when_trimming()
        {
            var budget = new PromptBudget(totalTokens: 120, reserveForOutput: 0);

            // Sections passed in chronological 4-zone sequence
            var s0 = new PromptSection("system", new string('a', 70), PromptSection.PriorityCore); // ~20 tokens, Priority 0
            var s1 = new PromptSection("user", new string('b', 70), 15); // ~20 tokens, Priority 15 (History User)
            var s2 = new PromptSection("assistant", new string('c', 70), 15); // ~20 tokens, Priority 15 (History Assistant)
            var s3 = new PromptSection("system", new string('d', 175), 25); // ~50 tokens, Priority 25 (Volatile Environment - will be dropped)
            var s4 = new PromptSection("user", new string('e', 70), 2); // ~20 tokens, Priority 2 (Current Query)

            var input = new List<PromptSection> { s0, s1, s2, s3, s4 };
            var result = budget.Compose(input);

            Assert.NotNull(result);
            // Budget allows 120 tokens. s0(20) + s1(20) + s2(20) + s4(20) = 80 tokens fit; s3 (50 tokens) overflows and is dropped.
            Assert.Equal(4, result.Count);

            // Assert original chronological sequence is strictly preserved (never grouped by priority!)
            Assert.Equal("system", result[0].Name);
            Assert.Equal(s0.Content, result[0].Content);

            Assert.Equal("user", result[1].Name);
            Assert.Equal(s1.Content, result[1].Content);

            Assert.Equal("assistant", result[2].Name);
            Assert.Equal(s2.Content, result[2].Content);

            Assert.Equal("user", result[3].Name);
            Assert.Equal(s4.Content, result[3].Content);
        }
    }
}
