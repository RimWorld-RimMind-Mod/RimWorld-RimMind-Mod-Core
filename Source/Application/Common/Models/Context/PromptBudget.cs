using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Models.Prompt;

namespace RimMind.Application.Common.Models.Context
{
    public class PromptBudget
    {
        public int TotalTokens { get; set; }
        public int ReserveForOutput { get; set; }
        public int UsedTokens { get; private set; }
        public int AvailableTokens => Math.Max(0, TotalTokens - ReserveForOutput);
        public int RemainingTokens => Math.Max(0, AvailableTokens - UsedTokens);

        public PromptBudget(int totalTokens, int reserveForOutput = 0)
        {
            TotalTokens = totalTokens;
            ReserveForOutput = reserveForOutput;
        }

        public List<PromptSection>? Compose(List<PromptSection> sections)
        {
            if (sections == null) return null;
            var withIndex = sections.Select((s, idx) => (Section: s, Index: idx)).ToList();
            var sorted = withIndex.OrderBy(x => x.Section.Priority).ToList();
            var result = new List<(PromptSection Section, int Index)>();
            int used = 0;
            int maxAllowed = AvailableTokens;
            foreach (var item in sorted)
            {
                var sec = item.Section;
                if (used + sec.EstimatedTokens > maxAllowed)
                {
                    if (sec.IsCompressible && sec.Compress != null)
                    {
                        var compressed = sec.Clone();
                        compressed.Content = sec.Compress(sec.Content);
                        compressed.EstimatedTokens = PromptSection.EstimateTokens(compressed.Content);
                        if (used + compressed.EstimatedTokens <= maxAllowed)
                        {
                            result.Add((compressed, item.Index));
                            used += compressed.EstimatedTokens;
                            continue;
                        }
                    }
                    continue;
                }
                result.Add(item);
                used += sec.EstimatedTokens;
            }
            UsedTokens = used;
            return result.OrderBy(x => x.Index).Select(x => x.Section).ToList();
        }
    }
}
