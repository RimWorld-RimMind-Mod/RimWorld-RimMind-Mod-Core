using System;
using System.Text;

namespace RimMind.Application.Features.Prompt
{
    /// <summary>
    /// CJK-aware and word-boundary-aware text wrapper for overhead speech bubbles / motes.
    /// </summary>
    public static class MoteTextFormatter
    {
        public const int DefaultMaxCharsPerLine = 26;

        public static string WrapForMote(string? text, int maxCharsPerLine = DefaultMaxCharsPerLine)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (maxCharsPerLine < 4)
                maxCharsPerLine = 4;

            string normalized = text!.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            var sb = new StringBuilder(normalized.Length + 16);
            string[] paragraphs = normalized.Split('\n');

            for (int p = 0; p < paragraphs.Length; p++)
            {
                if (p > 0) sb.Append('\n');
                WrapParagraph(paragraphs[p].Trim(), maxCharsPerLine, sb);
            }

            return sb.ToString();
        }

        private static void WrapParagraph(string line, int maxUnits, StringBuilder sb)
        {
            if (string.IsNullOrEmpty(line))
                return;

            int lineStart = 0;
            int currentUnits = 0;
            int lastBreakIndex = -1;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                int charUnits = IsWideCjkChar(ch) ? 2 : 1;

                if (currentUnits + charUnits > maxUnits && i > lineStart)
                {
                    int cut = lastBreakIndex > lineStart ? lastBreakIndex : i;
                    string segment = line.Substring(lineStart, cut - lineStart).TrimEnd();
                    if (segment.Length > 0)
                    {
                        sb.Append(segment);
                        sb.Append('\n');
                    }

                    lineStart = cut;
                    while (lineStart < line.Length && line[lineStart] == ' ')
                        lineStart++;

                    i = lineStart - 1;
                    currentUnits = 0;
                    lastBreakIndex = -1;
                    continue;
                }

                currentUnits += charUnits;
                if (IsPreferredBreakAfter(ch))
                {
                    lastBreakIndex = i + 1;
                }
                else if (ch == ' ')
                {
                    lastBreakIndex = i;
                }
            }

            if (lineStart < line.Length)
            {
                string tail = line.Substring(lineStart).Trim();
                if (tail.Length > 0)
                    sb.Append(tail);
            }
        }

        private static bool IsWideCjkChar(char ch)
        {
            return (ch >= '\u2E80' && ch <= '\u9FFF')
                || (ch >= '\uF900' && ch <= '\uFAFF')
                || (ch >= '\uFF00' && ch <= '\uFFEF')
                || (ch >= '\u3000' && ch <= '\u303F');
        }

        private static bool IsPreferredBreakAfter(char ch)
        {
            return ch == '，' || ch == '。' || ch == '！' || ch == '？' || ch == '、' || ch == '；'
                || ch == ',' || ch == '.' || ch == '!' || ch == '?' || ch == ';'
                || IsWideCjkChar(ch);
        }
    }
}
