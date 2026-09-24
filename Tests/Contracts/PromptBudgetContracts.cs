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
    }
}
