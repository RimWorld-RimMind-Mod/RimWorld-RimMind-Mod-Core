using System;
using System.Collections.Generic;
using RimMind.Application.Common.Models.Context;
using RimMind.Domain.Llm;

namespace RimMind.Application.Features.Context
{
    /// <summary>
    /// Heuristic analyzer: checks if AI response content contains substrings
    /// from context messages, indicating the AI actually used that context.
    /// Returns the LayerTag values of messages whose content was referenced.
    /// </summary>
    public sealed class AIResponseAnalyzer
    {
        private const int MinSampleLength = 20;
        private const int SampleHeadLength = 50;

        private const int MinPhraseLength = 6;
        private const int MinTokenLength = 5;

        /// <summary>
        /// Extract the context key names (matching <see cref="ContextSnapshot.IncludedKeys"/>)
        /// whose content appears to have been referenced by the AI response.
        /// </summary>
        public IReadOnlyList<string> ExtractUsedKeys(ContextSnapshot snapshot, LlmResponse response)
        {
            var used = new List<string>();
            if (snapshot == null || response == null) return used;

            var responseText = ((response.Content ?? string.Empty) + " " + (response.ToolCallsJson ?? string.Empty)).Trim();
            if (string.IsNullOrEmpty(responseText)) return used;

            var candidateEntries = CollectKeyEntries(snapshot);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, content) in candidateEntries)
            {
                if (string.IsNullOrWhiteSpace(key) || seen.Contains(key)) continue;
                if (IsEntryReferenced(content, responseText))
                {
                    seen.Add(key);
                    used.Add(key);
                }
            }

            return used;
        }

        private static List<(string Key, string Content)> CollectKeyEntries(ContextSnapshot snapshot)
        {
            var list = new List<(string Key, string Content)>();
            if (snapshot.AllEntries != null && snapshot.AllEntries.Count > 0)
            {
                foreach (var entry in snapshot.AllEntries)
                {
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.SourceKey) && !string.IsNullOrWhiteSpace(entry.Content))
                    {
                        list.Add((entry.SourceKey!, entry.Content!));
                    }
                }
            }

            if (snapshot.Messages != null)
            {
                foreach (var msg in snapshot.Messages)
                {
                    if (msg == null || string.IsNullOrWhiteSpace(msg.Content)) continue;
                    ParseBracketedEntries(msg.Content, list);
                }
            }

            return list;
        }

        private static void ParseBracketedEntries(string messageContent, List<(string Key, string Content)> target)
        {
            var lines = messageContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            string? currentKey = null;
            var currentBuilder = new System.Text.StringBuilder();

            void FlushCurrent()
            {
                if (!string.IsNullOrWhiteSpace(currentKey) && currentBuilder.Length > 0)
                {
                    target.Add((currentKey!, currentBuilder.ToString().Trim()));
                }
                currentKey = null;
                currentBuilder.Clear();
            }

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("<layer_", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("</layer_", StringComparison.OrdinalIgnoreCase))
                {
                    FlushCurrent();
                    continue;
                }

                if (line[0] == '[')
                {
                    int closeIdx = line.IndexOf(']');
                    if (closeIdx > 1)
                    {
                        string candidateKey = line.Substring(1, closeIdx - 1).Trim();
                        if (IsPlausibleContextKey(candidateKey))
                        {
                            FlushCurrent();
                            currentKey = candidateKey;
                            string remainder = line.Substring(closeIdx + 1).Trim();
                            if (remainder.Length > 0)
                                currentBuilder.Append(remainder);
                            continue;
                        }
                    }
                }

                if (currentKey != null)
                {
                    if (currentBuilder.Length > 0) currentBuilder.Append(' ');
                    currentBuilder.Append(line);
                }
            }

            FlushCurrent();
        }

        private static bool IsPlausibleContextKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            foreach (char c in key)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-' || c == ':'))
                    return false;
            }
            return true;
        }

        private static bool IsEntryReferenced(string rawContent, string responseText)
        {
            if (string.IsNullOrWhiteSpace(rawContent)) return false;
            var content = rawContent.Trim();

            // 1. Direct head sample or full-content substring match
            var sample = content.Substring(0, Math.Min(SampleHeadLength, content.Length)).Trim();
            if (sample.Length >= MinPhraseLength && responseText.IndexOf(sample, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (content.Length >= MinPhraseLength && content.Length < MinSampleLength &&
                responseText.IndexOf(content, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // 2. Clause / phrase match (split by punctuation/newlines)
            var clauses = content.Split(new[] { '\r', '\n', ';', ',', '.', '|', ':' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawClause in clauses)
            {
                var clause = rawClause.Trim();
                if (clause.Length >= 10 && responseText.IndexOf(clause, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            // 3. Distinctive token overlap
            var tokens = ExtractDistinctiveTokens(content);
            if (tokens.Count == 0) return false;

            int matched = 0;
            foreach (var token in tokens)
            {
                if (responseText.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matched++;
                    if (matched >= 2 || tokens.Count == 1)
                        return true;
                }
            }

            return false;
        }

        private static List<string> ExtractDistinctiveTokens(string text)
        {
            var tokens = new List<string>();
            var sb = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                }
                else if (sb.Length > 0)
                {
                    AddTokenIfDistinctive(sb.ToString(), tokens);
                    sb.Clear();
                }
            }
            if (sb.Length > 0)
                AddTokenIfDistinctive(sb.ToString(), tokens);
            return tokens;
        }

        private static void AddTokenIfDistinctive(string token, List<string> tokens)
        {
            bool hasCjk = false;
            foreach (char c in token)
            {
                if (c > 0x2E80) { hasCjk = true; break; }
            }
            int minLen = hasCjk ? 2 : MinTokenLength;
            if (token.Length < minLen) return;
            if (IsCommonStopWord(token)) return;
            if (!tokens.Contains(token))
                tokens.Add(token);
        }

        private static bool IsCommonStopWord(string token)
        {
            return token.Equals("none", StringComparison.OrdinalIgnoreCase)
                || token.Equals("null", StringComparison.OrdinalIgnoreCase)
                || token.Equals("true", StringComparison.OrdinalIgnoreCase)
                || token.Equals("false", StringComparison.OrdinalIgnoreCase)
                || token.Equals("value", StringComparison.OrdinalIgnoreCase)
                || token.Equals("status", StringComparison.OrdinalIgnoreCase)
                || token.Equals("current", StringComparison.OrdinalIgnoreCase)
                || token.Equals("state", StringComparison.OrdinalIgnoreCase);
        }
    }
}
