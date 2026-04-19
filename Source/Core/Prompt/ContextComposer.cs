using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace RimMind.Core.Prompt
{
    public static class ContextComposer
    {
        public static List<PromptSection> Reorder(List<PromptSection> sections)
        {
            if (sections == null || sections.Count <= 2) return sections ?? new List<PromptSection>();

            var core = sections.Where(s => s.Priority == PromptSection.PriorityCore).ToList();
            var currentInput = sections.Where(s => s.Priority == PromptSection.PriorityCurrentInput).ToList();
            var keyState = sections.Where(s => s.Priority == PromptSection.PriorityKeyState).ToList();
            var memory = sections.Where(s => s.Priority == PromptSection.PriorityMemory).ToList();
            var auxiliary = sections.Where(s => s.Priority == PromptSection.PriorityAuxiliary).ToList();
            var custom = sections.Where(s => s.Priority == PromptSection.PriorityCustom).ToList();

            var result = new List<PromptSection>();

            result.AddRange(core);
            result.AddRange(currentInput);
            result.AddRange(keyState);
            result.AddRange(memory);
            result.AddRange(auxiliary);
            result.AddRange(custom);

            var accounted = new HashSet<PromptSection>(result);
            var remaining = sections.Where(s => !accounted.Contains(s))
                .OrderBy(s => s.Priority)
                .ToList();
            result.AddRange(remaining);

            return result;
        }

        public static string BuildFromSections(List<PromptSection> sections)
        {
            if (sections == null || sections.Count == 0) return string.Empty;

            var ordered = Reorder(sections);
            var sb = new StringBuilder();
            foreach (var section in ordered)
            {
                if (!string.IsNullOrEmpty(section.Content))
                    sb.AppendLine(section.Content);
            }
            return sb.ToString().TrimEnd();
        }

        public static string CompressHistory(string historyText, int maxLines = 6, string summaryLine = "")
        {
            if (string.IsNullOrEmpty(historyText)) return string.Empty;

            var lines = historyText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length <= maxLines) return historyText;

            var recent = lines.Skip(lines.Length - maxLines).ToArray();
            string header = string.IsNullOrEmpty(summaryLine)
                ? "RimMind.Core.Prompt.HistoryOmitted".Translate(lines.Length - maxLines)
                : summaryLine;

            var sb = new StringBuilder();
            sb.AppendLine(header);
            foreach (var line in recent)
                sb.AppendLine(line);
            return sb.ToString().TrimEnd();
        }
    }
}
