using System;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RimMind.Application.Common.Models.Debug
{
    /// <summary>
    /// Formats raw LLM trace payloads (JSON objects/arrays, XML agent tags, and tool results)
    /// into readable indented blocks and concise single-line previews for debug UI views.
    /// </summary>
    public static class TraceContentFormatter
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

        private static readonly Regex TagBreakRegex = new Regex(
            @"(?<close></(?:Thought|Action|Param|tool_call_results|perceptions|behavior_history|scope)>)(?!\r|\n)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex InlineToolResultJsonRegex = new Regex(
            @"(?<prefix>\[Tool\s+[^\]]+?result\]:\s*)(?<json>[\{\[][\s\S]*?)(?=\r?\n\[Tool\s|\r?\n</tool_call_results>|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            RegexTimeout);

        public static string FormatStructuredText(string? raw, int maxChars = 16000)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "-";

            string trimmed = raw!.Trim();
            if (TryFormatJson(trimmed, out string formattedJson))
                return TruncateSafe(formattedJson, maxChars);

            string normalized = trimmed.Replace("\r\n", "\n");
            try
            {
                normalized = TagBreakRegex.Replace(normalized, m => m.Groups["close"].Value + "\n");
                normalized = InlineToolResultJsonRegex.Replace(normalized, m =>
                {
                    string candidate = m.Groups["json"].Value.Trim();
                    if (TryFormatJson(candidate, out string pretty))
                        return m.Groups["prefix"].Value + "\n" + IndentLines(pretty, "  ");
                    return m.Value;
                });
            }
            catch (RegexMatchTimeoutException)
            {
                // Fall back to un-normalized text if input is pathological
            }

            // Format JSON inside <Action>{...}</Action> or <Param>{...}</Param> if single-line
            normalized = FormatTagInnerJson(normalized, "Action");
            normalized = FormatTagInnerJson(normalized, "Param");

            return TruncateSafe(normalized, maxChars);
        }

        public static string FormatToolArguments(string? argumentsJson, int maxChars = 4000)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return "{}";
            return FormatStructuredText(argumentsJson, maxChars);
        }

        public static string BuildCompactPreview(string? raw, int maxLen = 80)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var sb = new StringBuilder(Math.Min(raw!.Length, maxLen + 8));
            bool lastWasSpace = false;
            foreach (char ch in raw)
            {
                char mapped = (ch == '\r' || ch == '\n' || ch == '\t') ? ' ' : ch;
                if (mapped == ' ')
                {
                    if (sb.Length == 0 || lastWasSpace)
                        continue;
                    lastWasSpace = true;
                    sb.Append(' ');
                }
                else
                {
                    lastWasSpace = false;
                    sb.Append(mapped);
                }

                if (sb.Length >= maxLen)
                    break;
            }

            string compact = sb.ToString().Trim();
            if (compact.Length > maxLen && maxLen > 3)
                return compact.Substring(0, maxLen - 3) + "...";
            if (raw.Trim().Length > maxLen && compact.Length == maxLen && maxLen > 3)
                return compact.Substring(0, maxLen - 3) + "...";
            return compact;
        }

        private static bool TryFormatJson(string candidate, out string indented)
        {
            indented = string.Empty;
            if (candidate.Length < 2)
                return false;

            char first = candidate[0];
            char last = candidate[candidate.Length - 1];
            if (!((first == '{' && last == '}') || (first == '[' && last == ']')))
                return false;

            try
            {
                var token = JToken.Parse(candidate);
                indented = token.ToString(Formatting.Indented);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string FormatTagInnerJson(string text, string tagName)
        {
            string open = $"<{tagName}>";
            string close = $"</{tagName}>";
            int openIdx = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (openIdx < 0)
                return text;

            int contentStart = openIdx + open.Length;
            int closeIdx = text.IndexOf(close, contentStart, StringComparison.OrdinalIgnoreCase);
            if (closeIdx <= contentStart)
                return text;

            string inner = text.Substring(contentStart, closeIdx - contentStart).Trim();
            if (!TryFormatJson(inner, out string pretty))
                return text;

            return text.Substring(0, contentStart)
                + "\n" + IndentLines(pretty, "  ") + "\n"
                + text.Substring(closeIdx);
        }

        private static string IndentLines(string text, string indent)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder(text.Length + lines.Length * indent.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(indent).Append(lines[i]);
            }
            return sb.ToString();
        }

        private static string TruncateSafe(string text, int maxChars)
        {
            if (maxChars <= 0 || text.Length <= maxChars)
                return text;
            return text.Substring(0, maxChars) + "\n... [truncated]";
        }
    }
}
