using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace RimMind.Core.Prompt
{
    public class PromptBudget
    {
        public int TotalBudget { get; set; } = 4000;
        public int ReserveForOutput { get; set; } = 800;

        public int AvailableForInput => TotalBudget - ReserveForOutput;

        public PromptBudget() { }

        public PromptBudget(int totalBudget, int reserveForOutput = 800)
        {
            TotalBudget = totalBudget;
            ReserveForOutput = reserveForOutput;
        }

        public List<PromptSection> Compose(List<PromptSection> sections)
        {
            if (sections == null || sections.Count == 0) return new List<PromptSection>();

            var working = sections.Where(s => !string.IsNullOrEmpty(s.Content)).ToList();

            int used = working.Sum(s => s.EstimatedTokens);
            if (used <= AvailableForInput)
                return ContextComposer.Reorder(working);

            var result = new List<PromptSection>(working);

            var compressible = result
                .Where(s => s.IsCompressible)
                .OrderByDescending(s => s.Priority)
                .ToList();

            foreach (var section in compressible)
            {
                if (used <= AvailableForInput) break;
                try
                {
                    string compressed = section.Compress!(section.Content);
                    if (string.IsNullOrEmpty(compressed))
                    {
                        result.Remove(section);
                        used -= section.EstimatedTokens;
                        continue;
                    }
                    int oldTokens = section.EstimatedTokens;
                    section.Content = compressed;
                    section.EstimatedTokens = PromptSection.EstimateTokens(compressed);
                    used -= oldTokens - section.EstimatedTokens;
                    if (RimMindCoreMod.Settings?.debugLogging == true)
                        Log.Message($"[RimMind] PromptBudget: compressed '{section.Tag}' (~{oldTokens}tok → ~{section.EstimatedTokens}tok)");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimMind] PromptBudget: compress failed for '{section.Tag}': {ex.Message}");
                }
            }

            if (used > AvailableForInput)
            {
                var trimmable = result
                    .Where(s => s.IsTrimable)
                    .OrderByDescending(s => s.Priority)
                    .ToList();

                foreach (var section in trimmable)
                {
                    if (used <= AvailableForInput) break;
                    result.Remove(section);
                    used -= section.EstimatedTokens;
                    if (RimMindCoreMod.Settings?.debugLogging == true)
                        Log.Message($"[RimMind] PromptBudget: trimmed '{section.Tag}' (~{section.EstimatedTokens}tok) to fit budget");
                }
            }

            return ContextComposer.Reorder(result);
        }

        public string ComposeToString(List<PromptSection> sections)
        {
            var composed = Compose(sections);
            var sb = new StringBuilder();
            foreach (var section in composed)
                sb.AppendLine(section.Content);
            return sb.ToString().TrimEnd();
        }
    }
}
