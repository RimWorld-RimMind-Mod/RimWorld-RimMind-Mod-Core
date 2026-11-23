using System;
using System.Collections.Generic;
using System.Linq;

namespace RimMind.Application.Common.Models.Debug
{
    public sealed record AgentRolloutEpisode(
        string RolloutId,
        string ActorId,
        string ActorLabel,
        string ModId,
        string ScenarioId,
        DateTime StartedAtUtc,
        DateTime LastActivityUtc,
        AIRequestTraceState TerminalState,
        int TurnCount,
        int TotalToolCalls,
        int FailedToolCalls,
        int TotalTokens,
        int PromptTokens,
        int CompletionTokens,
        int CachedTokens,
        int TotalElapsedMs,
        double PrefixReuseRatio,
        IReadOnlyList<AIRequestTraceEntry> Turns,
        AIRequestActionOutcomeTrace? FinalOutcome);

    public sealed record EphemeralTraceFilter(
        string? ModId = null,
        string? ScenarioId = null,
        string? Actor = null,
        AIRequestTraceState? State = null,
        string? SearchQuery = null,
        bool IncludeRolloutTurns = false);

    /// <summary>
    /// Aggregates raw trace entries into multi-turn Agent Rollout trajectories and
    /// provides multi-dimensional filtering for ephemeral one-off LLM requests.
    /// </summary>
    public static class AgentRolloutModelBuilder
    {
        public static string ResolveActorId(AIRequestTraceEntry? entry)
        {
            if (entry == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(entry.NpcId))
                return entry.NpcId!.Trim();

            string source = entry.Source ?? string.Empty;
            int npcIdx = source.IndexOf("npc:", StringComparison.OrdinalIgnoreCase);
            if (npcIdx >= 0)
            {
                int start = npcIdx + 4;
                int sep = source.IndexOf('|', start);
                return (sep >= 0 ? source.Substring(start, sep - start) : source.Substring(start)).Trim();
            }

            return string.Empty;
        }

        public static string ResolveActorLabel(
            AIRequestTraceEntry? entry,
            Func<string, string>? actorNameResolver = null)
        {
            if (entry == null)
                return "-";

            string actorId = ResolveActorId(entry);
            if (!string.IsNullOrWhiteSpace(actorId))
            {
                string? resolved = actorNameResolver?.Invoke(actorId);
                return !string.IsNullOrWhiteSpace(resolved) ? resolved! : actorId;
            }

            if (!string.IsNullOrWhiteSpace(entry.ModId))
                return entry.ModId;
            return !string.IsNullOrWhiteSpace(entry.Source) ? entry.Source : "-";
        }

        public static string ResolveModId(AIRequestTraceEntry? entry)
        {
            if (entry == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(entry.ModId)) return entry.ModId.Trim();
            return ExtractSourceTag(entry.Source, "mod:");
        }

        public static string ResolveScenarioId(AIRequestTraceEntry? entry)
        {
            if (entry == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(entry.ScenarioId)) return entry.ScenarioId.Trim();
            return ExtractSourceTag(entry.Source, "scenario:");
        }

        public static IReadOnlyList<AgentRolloutEpisode> BuildRollouts(
            IReadOnlyList<AIRequestTraceEntry>? entries,
            string? actorFilter = null,
            AIRequestTraceState? stateFilter = null,
            string? searchQuery = null,
            Func<string, string>? actorNameResolver = null)
        {
            if (entries == null || entries.Count == 0)
                return Array.Empty<AgentRolloutEpisode>();

            var groups = new Dictionary<string, List<AIRequestTraceEntry>>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry == null || !IsRolloutEntry(entry))
                    continue;

                string key = !string.IsNullOrWhiteSpace(entry.RolloutId)
                    ? entry.RolloutId
                    : (!string.IsNullOrWhiteSpace(entry.TraceId) ? entry.TraceId : entry.RequestId);

                if (string.IsNullOrWhiteSpace(key))
                    key = entry.StartedAtUtc.Ticks.ToString();

                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<AIRequestTraceEntry>();
                    groups[key] = list;
                }
                list.Add(entry);
            }

            var episodes = new List<AgentRolloutEpisode>(groups.Count);
            foreach (var kv in groups)
            {
                var orderedTurns = kv.Value
                    .OrderBy(t => t.TurnIndex)
                    .ThenBy(t => t.StartedAtUtc)
                    .ToList();

                var first = orderedTurns[0];
                var last = orderedTurns[orderedTurns.Count - 1];

                string actorId = orderedTurns.Select(ResolveActorId).FirstOrDefault(id => !string.IsNullOrEmpty(id)) ?? string.Empty;
                string actorLabel = ResolveActorLabel(first, actorNameResolver);
                string modId = ResolveModId(first);
                string scenarioId = ResolveScenarioId(first);

                AIRequestTraceState terminalState = orderedTurns.Any(t => t.State == AIRequestTraceState.Running)
                    ? AIRequestTraceState.Running
                    : last.State;

                int totalTools = orderedTurns.Sum(t => t.ToolCalls.Count);
                int failedTools = orderedTurns.Sum(t => t.ToolCalls.Count(tc => !tc.Succeeded));
                int totalTokens = orderedTurns.Sum(t => t.TokensUsed);
                int promptTokens = orderedTurns.Sum(t => t.PromptTokens);
                int completionTokens = orderedTurns.Sum(t => t.CompletionTokens);
                int cachedTokens = orderedTurns.Sum(t => t.CachedTokens);
                int totalElapsed = orderedTurns.Sum(t => Math.Max(0, t.ElapsedMs));
                double prefixReuseRatio = ComputePrefixReuseRatio(orderedTurns);
                AIRequestActionOutcomeTrace? finalOutcome = orderedTurns
                    .Select(t => t.ActionOutcome)
                    .LastOrDefault(o => o != null);

                DateTime started = orderedTurns.Min(t => t.StartedAtUtc);
                DateTime lastAct = orderedTurns.Max(t => t.FinishedAtUtc ?? t.StartedAtUtc);

                var episode = new AgentRolloutEpisode(
                    kv.Key,
                    actorId,
                    actorLabel,
                    modId,
                    scenarioId,
                    started,
                    lastAct,
                    terminalState,
                    orderedTurns.Count,
                    totalTools,
                    failedTools,
                    totalTokens,
                    promptTokens,
                    completionTokens,
                    cachedTokens,
                    totalElapsed,
                    prefixReuseRatio,
                    orderedTurns,
                    finalOutcome);

                if (MatchesRolloutFilters(episode, actorFilter, stateFilter, searchQuery))
                    episodes.Add(episode);
            }

            return episodes
                .OrderByDescending(e => e.LastActivityUtc)
                .ThenByDescending(e => e.StartedAtUtc)
                .ToList();
        }

        public static IReadOnlyList<AIRequestTraceEntry> FilterEphemeral(
            IReadOnlyList<AIRequestTraceEntry>? entries,
            EphemeralTraceFilter? filter = null,
            Func<string, string>? actorNameResolver = null)
        {
            if (entries == null || entries.Count == 0)
                return Array.Empty<AIRequestTraceEntry>();

            var f = filter ?? new EphemeralTraceFilter();
            var matched = new List<AIRequestTraceEntry>();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry == null) continue;

                if (!f.IncludeRolloutTurns && IsRolloutEntry(entry))
                    continue;

                if (!string.IsNullOrWhiteSpace(f.ModId)
                    && !string.Equals(ResolveModId(entry), f.ModId!.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(f.ScenarioId)
                    && !string.Equals(ResolveScenarioId(entry), f.ScenarioId!.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(f.Actor))
                {
                    string label = ResolveActorLabel(entry, actorNameResolver);
                    string actorId = ResolveActorId(entry);
                    if (!string.Equals(label, f.Actor!.Trim(), StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(actorId, f.Actor!.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (f.State.HasValue && entry.State != f.State.Value)
                    continue;

                if (!string.IsNullOrWhiteSpace(f.SearchQuery)
                    && !EntryMatchesSearch(entry, f.SearchQuery!.Trim(), actorNameResolver))
                    continue;

                matched.Add(entry);
            }

            return matched;
        }

        public static IReadOnlyList<string> GetDistinctMods(IReadOnlyList<AIRequestTraceEntry>? entries)
        {
            if (entries == null) return Array.Empty<string>();
            return entries.Select(ResolveModId)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static IReadOnlyList<string> GetDistinctScenarios(IReadOnlyList<AIRequestTraceEntry>? entries)
        {
            if (entries == null) return Array.Empty<string>();
            return entries.Select(ResolveScenarioId)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static IReadOnlyList<string> GetDistinctActors(
            IReadOnlyList<AIRequestTraceEntry>? entries,
            Func<string, string>? actorNameResolver = null)
        {
            if (entries == null) return Array.Empty<string>();
            return entries.Where(e => !string.IsNullOrWhiteSpace(ResolveActorId(e)))
                .Select(e => ResolveActorLabel(e, actorNameResolver))
                .Where(s => !string.IsNullOrWhiteSpace(s) && s != "-")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsRolloutEntry(AIRequestTraceEntry entry)
        {
            if (entry.IsAgentRollout)
                return true;
            if (!string.IsNullOrWhiteSpace(entry.RolloutId))
                return true;
            string scenario = ResolveScenarioId(entry);
            string mod = ResolveModId(entry);
            return Features.Context.ScenarioRegistry.IsDecisionScenario(scenario)
                || string.Equals(mod, Constants.RimMindOwnerConsts.AgentModId, StringComparison.OrdinalIgnoreCase);
        }

        private static double ComputePrefixReuseRatio(IReadOnlyList<AIRequestTraceEntry> turns)
        {
            long totalStableChars = 0;
            long reusedChars = 0;

            foreach (var turn in turns)
            {
                if (turn.Layers == null || turn.Layers.Count == 0)
                    continue;

                foreach (var layer in turn.Layers)
                {
                    if (!IsStableContextLayer(layer))
                        continue;

                    int len = Math.Max(0, layer.CharCount > 0 ? layer.CharCount : (layer.Content?.Length ?? 0));
                    totalStableChars += len;
                    if (layer.IsPrefixReused)
                        reusedChars += len;
                }
            }

            if (totalStableChars > 0)
                return (double)reusedChars / totalStableChars;

            int promptSum = turns.Sum(t => Math.Max(0, t.PromptTokens));
            int cachedSum = turns.Sum(t => Math.Max(0, t.CachedTokens));
            return promptSum > 0 ? Math.Min(1.0, (double)cachedSum / promptSum) : 0.0;
        }

        private static bool IsStableContextLayer(AIRequestLayerTrace layer)
            => string.Equals(layer.Role, "system", StringComparison.OrdinalIgnoreCase)
                || layer.LayerTag.StartsWith("L0", StringComparison.OrdinalIgnoreCase)
                || layer.LayerTag.StartsWith("L1", StringComparison.OrdinalIgnoreCase)
                || layer.LayerTag.StartsWith("L2", StringComparison.OrdinalIgnoreCase)
                || layer.LayerTag.StartsWith("L3", StringComparison.OrdinalIgnoreCase);

        private static bool MatchesRolloutFilters(
            AgentRolloutEpisode episode,
            string? actorFilter,
            AIRequestTraceState? stateFilter,
            string? searchQuery)
        {
            if (!string.IsNullOrWhiteSpace(actorFilter))
            {
                string filter = actorFilter!.Trim();
                if (!string.Equals(episode.ActorLabel, filter, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(episode.ActorId, filter, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (stateFilter.HasValue && episode.TerminalState != stateFilter.Value)
                return false;

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                string q = searchQuery!.Trim();
                bool matchEpisode = ContainsIgnoreCase(episode.RolloutId, q)
                    || ContainsIgnoreCase(episode.ActorLabel, q)
                    || ContainsIgnoreCase(episode.FinalOutcome?.ActionIntent, q)
                    || ContainsIgnoreCase(episode.FinalOutcome?.Detail, q)
                    || ContainsIgnoreCase(episode.FinalOutcome?.ParamPreview, q);

                if (!matchEpisode && !episode.Turns.Any(t => EntryMatchesSearch(t, q)))
                    return false;
            }

            return true;
        }

        private static bool EntryMatchesSearch(
            AIRequestTraceEntry entry,
            string query,
            Func<string, string>? actorNameResolver = null)
        {
            if (ContainsIgnoreCase(entry.RequestId, query)
                || ContainsIgnoreCase(entry.Source, query)
                || ContainsIgnoreCase(entry.Model, query)
                || ContainsIgnoreCase(ResolveModId(entry), query)
                || ContainsIgnoreCase(ResolveScenarioId(entry), query)
                || ContainsIgnoreCase(ResolveActorLabel(entry, actorNameResolver), query)
                || ContainsIgnoreCase(entry.UserPrompt, query)
                || ContainsIgnoreCase(entry.Response, query)
                || ContainsIgnoreCase(entry.ReasoningContent, query)
                || ContainsIgnoreCase(entry.Error, query)
                || ContainsIgnoreCase(entry.ActionOutcome?.ActionIntent, query)
                || ContainsIgnoreCase(entry.ActionOutcome?.Detail, query)
                || ContainsIgnoreCase(entry.ActionOutcome?.ParamPreview, query))
                return true;

            foreach (var tc in entry.ToolCalls)
            {
                if (ContainsIgnoreCase(tc.ToolName, query)
                    || ContainsIgnoreCase(tc.ArgumentsJson, query)
                    || ContainsIgnoreCase(tc.ResultContent, query)
                    || ContainsIgnoreCase(tc.Error, query))
                    return true;
            }

            return false;
        }

        private static bool ContainsIgnoreCase(string? source, string value)
            => !string.IsNullOrEmpty(source) && source!.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string ExtractSourceTag(string? source, string prefix)
        {
            if (string.IsNullOrWhiteSpace(source))
                return string.Empty;

            int idx = source!.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return string.Empty;

            int start = idx + prefix.Length;
            int sep = source.IndexOf('|', start);
            return (sep >= 0 ? source.Substring(start, sep - start) : source.Substring(start)).Trim();
        }
    }
}
