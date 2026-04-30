using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RimMind.Core.Prompt
{
    public static class ContextComposer
    {
        public static List<PromptSection> Reorder(List<PromptSection> sections)
        {
            if (sections == null || sections.Count <= 1)
                return sections!;

            return sections
                .OrderBy(s => s.Priority)
                .ThenBy(s => s.Tag, StringComparer.Ordinal)
                .ToList();
        }

        public static string BuildFromSections(List<PromptSection> sections)
        {
            var ordered = Reorder(sections);
            var sb = new StringBuilder();
            foreach (var section in ordered)
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.Append(section.Content);
            }
            return sb.ToString();
        }

        public static string CompressHistory(string historyText, int maxLines = 6, string summaryLine = "")
        {
            if (string.IsNullOrEmpty(historyText))
                return historyText;

            var lines = historyText.Split(new[] { '\n' }, StringSplitOptions.None);
            if (lines.Length <= maxLines)
                return historyText;

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(summaryLine))
                sb.AppendLine(summaryLine);

            for (int i = lines.Length - maxLines; i < lines.Length; i++)
            {
                sb.AppendLine(lines[i]);
            }

            return sb.ToString().TrimEnd();
        }
    }
}
