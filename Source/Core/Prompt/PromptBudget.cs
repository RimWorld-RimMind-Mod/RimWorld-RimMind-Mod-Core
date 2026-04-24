using System;
using System.Collections.Generic;
using System.Linq;

namespace RimMind.Core.Prompt
{
    public class PromptBudget
    {
        public int TotalBudget { get; }
        public int ReserveForOutput { get; }
        public int AvailableForInput { get; }

        public PromptBudget(int totalBudget = 4000, int reserveForOutput = 800)
        {
            TotalBudget = totalBudget;
            ReserveForOutput = reserveForOutput;
            AvailableForInput = totalBudget - reserveForOutput;
        }

        public List<PromptSection> Compose(List<PromptSection> sections)
        {
            if (sections == null || sections.Count == 0)
                return sections;

            var result = sections.Select(s => s.Clone()).ToList();
            int totalTokens = result.Sum(s => s.EstimatedTokens);

            if (totalTokens <= AvailableForInput)
                return result;

            var compressible = result
                .Where(s => s.IsCompressible)
                .OrderByDescending(s => s.Priority)
                .ToList();

            foreach (var section in compressible)
            {
                if (totalTokens <= AvailableForInput) break;

                if (section.Compress == null) continue;

                int beforeTokens = section.EstimatedTokens;
                section.Content = section.Compress(section.Content);
                section.EstimatedTokens = PromptSection.EstimateTokens(section.Content);
                totalTokens -= beforeTokens - section.EstimatedTokens;
            }

            if (totalTokens <= AvailableForInput)
                return result;

            var trimmable = result
                .Where(s => s.IsTrimable)
                .OrderByDescending(s => s.Priority)
                .ToList();

            foreach (var section in trimmable)
            {
                if (totalTokens <= AvailableForInput) break;

                totalTokens -= section.EstimatedTokens;
                result.Remove(section);
            }

            return result;
        }

        public string ComposeToString(List<PromptSection> sections)
        {
            var trimmed = Compose(sections);
            return string.Join("\n\n", trimmed.Select(s => s.Content));
        }
    }
}
