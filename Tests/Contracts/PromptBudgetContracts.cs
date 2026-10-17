using System.Collections.Generic;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Common.Models.Prompt;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class PromptBudgetContracts
    {
        [Fact]
        public void PromptBudget_preserves_high_priority_sections_over_low_priority()
        {
            ContractCaseRunner.Run(
                ("core priority section is kept while auxiliary section is dropped when budget overflows", () =>
                {
                    // Total budget 100 tokens, 0 reserve.
                    var budget = new PromptBudget(100, 0);

                    // Core: 60 tokens (PriorityCore = 0)
                    var core = new PromptSection("core", new string('a', 210), PromptSection.PriorityCore);
                    // Auxiliary: 60 tokens (PriorityAuxiliary = 30)
                    var aux = new PromptSection("aux", new string('b', 210), PromptSection.PriorityAuxiliary);

                    var result = budget.Compose(new List<PromptSection> { aux, core });
                    Assert.NotNull(result);
                    Assert.Single(result);
                    Assert.Equal("core", result[0].Name);
                    Assert.True(budget.UsedTokens <= 100);
                    Assert.True(budget.RemainingTokens >= 0);
                }),
                ("compressible section compresses to fit when budget allows", () =>
                {
                    var budget = new PromptBudget(80, 0);
                    var core = new PromptSection("core", new string('a', 140), PromptSection.PriorityCore); // ~40 tokens
                    var aux = new PromptSection("aux", new string('b', 210), PromptSection.PriorityAuxiliary) // ~60 tokens
                    {
                        Compress = s => s.Substring(0, 70) // ~20 tokens
                    };

                    var result = budget.Compose(new List<PromptSection> { core, aux });
                    Assert.NotNull(result);
                    Assert.Equal(2, result.Count);
                    Assert.Equal("core", result[0].Name);
                    Assert.Equal("aux", result[1].Name);
                    Assert.True(budget.UsedTokens <= 80);
                }),
                ("remaining tokens accurately tracks consumed tokens and reserve", () =>
                {
                    var budget = new PromptBudget(200, 50);
                    Assert.Equal(150, budget.AvailableTokens);
                    Assert.Equal(150, budget.RemainingTokens);

                    var sec = new PromptSection("core", new string('a', 140), PromptSection.PriorityCore); // ~40 tokens
                    budget.Compose(new List<PromptSection> { sec });

                    Assert.Equal(sec.EstimatedTokens, budget.UsedTokens);
                    Assert.Equal(150 - sec.EstimatedTokens, budget.RemainingTokens);
                }));
        }

        [Fact]
        public void PerceptionBuffer_semantic_deduplication_and_FormatPerceptions_budget_cap()
        {
            ContractCaseRunner.Run(
                ("semantic deduplication updates continuous need/mood/environment changes in place", () =>
                {
                    var buffer = new RimMind.Presentation.Agent.PerceptionBuffer();
                    for (int pct = 80; pct >= 20; pct--)
                    {
                        buffer.Add(new RimMind.Application.Common.Models.Pipeline.PerceptionBufferEntry
                        {
                            PawnId = 101,
                            PerceptionType = "need",
                            Content = $"Need: Food at {pct}%",
                            Importance = 0.5f,
                            Tick = 1000 + (80 - pct)
                        });
                        buffer.Add(new RimMind.Application.Common.Models.Pipeline.PerceptionBufferEntry
                        {
                            PawnId = 101,
                            PerceptionType = "mood",
                            Content = $"Mood: {pct}% (Stressed)",
                            Importance = 0.6f,
                            Tick = 1000 + (80 - pct)
                        });
                        buffer.Add(new RimMind.Application.Common.Models.Pipeline.PerceptionBufferEntry
                        {
                            PawnId = 101,
                            PerceptionType = "environment",
                            Content = $"Environment: Clear, {pct - 50}°C",
                            Importance = 0.4f,
                            Tick = 1000 + (80 - pct)
                        });
                    }

                    // 61 updates across 3 semantic categories for the same pawn collapse into 3 latest entries
                    Assert.Equal(3, buffer.Count);
                    var flushed = buffer.Flush();
                    Assert.Contains(flushed, e => e.Content == "Need: Food at 20%");
                    Assert.Contains(flushed, e => e.Content == "Mood: 20% (Stressed)");
                    Assert.Contains(flushed, e => e.Content == "Environment: Clear, -30°C");
                }),
                ("FormatPerceptions caps output below 2800 chars even when 200 distinct perceptions are passed", () =>
                {
                    var entries = new List<RimMind.Application.Common.Models.Pipeline.PerceptionBufferEntry>();
                    for (int i = 0; i < 200; i++)
                    {
                        entries.Add(new RimMind.Application.Common.Models.Pipeline.PerceptionBufferEntry
                        {
                            PawnId = 101,
                            PerceptionType = "event",
                            Content = $"Distinct colony event #{i}: A group of raiders approached from sector {i} with long description text.",
                            Importance = 0.8f
                        });
                    }

                    string formatted = RimMind.Application.Features.Agent.Modes.ThinkStrategyHelper.FormatPerceptions(entries);
                    Assert.True(formatted.Length <= 2850, $"Expected <= 2850 chars, got {formatted.Length}");

                    var guardrail = new RimMind.Application.Features.Pipeline.Unified.BudgetOverflowGuardrail();
                    var envelope = RimMind.Application.Features.Llm.LlmRequestEnvelopeBuilder
                        .ForScenario("rimmind.decision")
                        .WithModId("RimMind.Agent")
                        .WithNpcId("NPC-101")
                        .WithGameStateInfo(new GameStateInfo().AddSection("perceptions", formatted))
                        .Build();
                    Assert.True(guardrail.Check(envelope).Passed);
                }),
                ("OutputGuardrailMiddleware strips residual XML tags, <tool_call> wrappers, and pseudo-function wrappers", () =>
                {
                    string raw = "<thinking>Let me think in Chinese</thinking><speech>这鬼地方冷得要命！</speech>";
                    string cleaned = RimMind.Application.Features.Pipeline.Unified.OutputGuardrailMiddleware.StripResidualModelXmlTags(raw);
                    Assert.Equal("这鬼地方冷得要命！", cleaned);

                    string malformedPseudo = "express_dialogue(speech)=(Stanley，外面天寒地冻的，但在这亮堂堂的屋里吃口现成的饭还算舒坦。)";
                    string unwrapped = RimMind.Application.Features.Pipeline.Unified.OutputGuardrailMiddleware.StripResidualModelXmlTags(malformedPseudo);
                    Assert.Equal("Stanley，外面天寒地冻的，但在这亮堂堂的屋里吃口现成的饭还算舒坦。", unwrapped);

                    var (_, toolName, argsJson, _) =
                        RimMind.Application.Features.Agent.Modes.ThinkStrategyHelper.TryExtractToolCallFromContent(malformedPseudo);
                    Assert.Equal("express_dialogue", toolName);
                    Assert.NotNull(argsJson);
                    Assert.Contains("Stanley，外面天寒地冻的", argsJson!);
                }));
        }
    }
}
