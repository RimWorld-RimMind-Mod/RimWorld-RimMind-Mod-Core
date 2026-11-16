using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Debug;
using Verse;

namespace RimMind.Infrastructure.Verse
{
    public sealed class AIRequestTraceLog : IAIRequestTraceLog
    {
        private const int MaxEntries = RimMindDefaults.DebugMaxEntries;
        private readonly object _lock = new();
        private readonly List<AIRequestTraceEntry> _entries = new();
        private readonly Dictionary<string, AIRequestTraceEntry> _byId = new(System.StringComparer.Ordinal);
        private readonly Dictionary<string, (int Length, int Hash)> _lastLayerHashByScopeAndTag = new(System.StringComparer.Ordinal);
        private long _revision;
        private long _entriesSnapshotCount;

        internal long EntriesSnapshotCount
        {
            get
            {
                lock (_lock)
                {
                    return _entriesSnapshotCount;
                }
            }
        }

        public long Revision
        {
            get
            {
                lock (_lock)
                {
                    return _revision;
                }
            }
        }

        public IReadOnlyList<AIRequestTraceEntry> Entries
        {
            get
            {
                lock (_lock)
                {
                    _entriesSnapshotCount++;
                    return _entries.Select(CloneEntry).ToList();
                }
            }
        }

        public IReadOnlyList<AIRequestTraceEntry> GetRecent(
            int limit,
            string? npcId = null,
            string? fallbackPawnId = null)
        {
            if (limit <= 0)
                return System.Array.Empty<AIRequestTraceEntry>();

            lock (_lock)
            {
                var matched = new List<AIRequestTraceEntry>(System.Math.Min(limit, _entries.Count));
                for (int i = _entries.Count - 1; i >= 0 && matched.Count < limit; i--)
                {
                    var entry = _entries[i];
                    if (MatchesActor(entry, npcId, fallbackPawnId))
                        matched.Add(CloneEntry(entry));
                }
                matched.Reverse();
                return matched;
            }
        }

        public static bool MatchesActor(
            AIRequestTraceEntry? entry,
            string? npcId,
            string? fallbackPawnId = null)
        {
            if (entry == null)
                return false;
            if (string.IsNullOrWhiteSpace(npcId) && string.IsNullOrWhiteSpace(fallbackPawnId))
                return true;

            if (!string.IsNullOrWhiteSpace(entry.NpcId))
                return MatchesId(entry.NpcId!.Trim(), npcId, fallbackPawnId);

            string source = entry.Source ?? string.Empty;
            int npcIdx = source.IndexOf("npc:", System.StringComparison.OrdinalIgnoreCase);
            if (npcIdx >= 0)
            {
                int valueStart = npcIdx + 4;
                int sepIdx = source.IndexOf('|', valueStart);
                string entryNpcId = (sepIdx >= 0
                    ? source.Substring(valueStart, sepIdx - valueStart)
                    : source.Substring(valueStart)).Trim();
                return MatchesId(entryNpcId, npcId, fallbackPawnId);
            }

            string trimmed = source.Trim();
            if (trimmed.StartsWith("Pawn_", System.StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("NPC-", System.StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("npc_", System.StringComparison.OrdinalIgnoreCase))
            {
                return MatchesId(trimmed, npcId, fallbackPawnId);
            }

            return true;
        }

        private static bool MatchesId(string candidate, string? npcId, string? fallbackPawnId)
        {
            if (!string.IsNullOrWhiteSpace(npcId)
                && string.Equals(candidate, npcId!.Trim(), System.StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrWhiteSpace(fallbackPawnId)
                && string.Equals(candidate, fallbackPawnId!.Trim(), System.StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        public void StartRequest(string requestId, string source, string model, string systemPrompt, string userPrompt, string assistantPrompt)
        {
            lock (_lock)
            {
                string key = requestId ?? string.Empty;
                if (!string.IsNullOrEmpty(key) && _byId.TryGetValue(key, out var existing))
                {
                    existing.Source = source;
                    existing.Model = model;
                    existing.SystemPrompt = systemPrompt;
                    existing.UserPrompt = userPrompt;
                    existing.AssistantPrompt = assistantPrompt;
                    existing.Response = string.Empty;
                    existing.ReasoningContent = null;
                    existing.Error = null;
                    existing.TokensUsed = 0;
                    existing.PromptTokens = 0;
                    existing.CompletionTokens = 0;
                    existing.CachedTokens = 0;
                    existing.ElapsedMs = 0;
                    existing.StartedAtUtc = System.DateTime.UtcNow;
                    existing.FinishedAtUtc = null;
                    existing.State = AIRequestTraceState.Running;
                    existing.ToolCalls.Clear();
                    existing.Layers.Clear();
                    existing.ActionOutcome = null;
                    _revision++;
                    return;
                }

                TrimIfNeeded();
                var created = new AIRequestTraceEntry
                {
                    RequestId = key,
                    Source = source,
                    Model = model,
                    SystemPrompt = systemPrompt,
                    UserPrompt = userPrompt,
                    AssistantPrompt = assistantPrompt,
                    StartedAtUtc = System.DateTime.UtcNow,
                    State = AIRequestTraceState.Running
                };
                _entries.Add(created);
                if (!string.IsNullOrEmpty(key))
                    _byId[key] = created;
                _revision++;
            }
        }

        public void UpdateRequestMetadata(
            string requestId,
            string traceId,
            string rolloutId,
            int turnIndex,
            string modId,
            string scenarioId,
            string? npcId,
            bool isAgentRollout)
        {
            lock (_lock)
            {
                var entry = FindOrCreate(requestId);
                entry.TraceId = traceId ?? string.Empty;
                entry.RolloutId = rolloutId ?? string.Empty;
                entry.TurnIndex = turnIndex;
                entry.ModId = modId ?? string.Empty;
                entry.ScenarioId = scenarioId ?? string.Empty;
                entry.NpcId = string.IsNullOrWhiteSpace(npcId) ? null : npcId;
                entry.IsAgentRollout = isAgentRollout;
                _revision++;
            }
        }

        public void UpdateRequestLayers(string requestId, IReadOnlyList<AIRequestLayerTrace> layers)
        {
            if (layers == null)
                return;

            lock (_lock)
            {
                var entry = FindOrCreate(requestId);
                entry.Layers.Clear();
                string scope = !string.IsNullOrWhiteSpace(entry.NpcId)
                    ? entry.NpcId!
                    : (!string.IsNullOrWhiteSpace(entry.ModId) ? entry.ModId : entry.Source);

                foreach (var layer in layers)
                {
                    if (layer == null) continue;
                    string content = layer.Content ?? string.Empty;
                    string hashKey = $"{scope}|{layer.Role}|{layer.LayerTag}";
                    var sig = (content.Length, System.StringComparer.Ordinal.GetHashCode(content));
                    bool isStableSystemLayer = string.Equals(layer.Role, "system", System.StringComparison.OrdinalIgnoreCase)
                        || layer.LayerTag.StartsWith("L0", System.StringComparison.OrdinalIgnoreCase)
                        || layer.LayerTag.StartsWith("L1", System.StringComparison.OrdinalIgnoreCase)
                        || layer.LayerTag.StartsWith("L2", System.StringComparison.OrdinalIgnoreCase)
                        || layer.LayerTag.StartsWith("L3", System.StringComparison.OrdinalIgnoreCase);

                    bool reused = layer.IsPrefixReused;
                    if (isStableSystemLayer
                        && _lastLayerHashByScopeAndTag.TryGetValue(hashKey, out var prev)
                        && prev.Length == sig.Length
                        && prev.Hash == sig.Item2)
                    {
                        reused = true;
                    }
                    if (isStableSystemLayer)
                        _lastLayerHashByScopeAndTag[hashKey] = sig;

                    entry.Layers.Add(new AIRequestLayerTrace(
                        layer.Role,
                        layer.LayerTag,
                        content,
                        content.Length,
                        reused));
                }
                _revision++;
            }
        }

        public void CompleteRequest(string requestId, string response, int tokensUsed, int elapsedMs)
            => CompleteRequestDetailed(requestId, response, reasoningContent: null, tokensUsed, promptTokens: 0, completionTokens: 0, cachedTokens: 0, elapsedMs);

        public void CompleteRequestDetailed(
            string requestId,
            string response,
            string? reasoningContent,
            int tokensUsed,
            int promptTokens,
            int completionTokens,
            int cachedTokens,
            int elapsedMs)
        {
            lock (_lock)
            {
                var entry = FindOrCreate(requestId);
                entry.Response = response;
                if (!string.IsNullOrWhiteSpace(reasoningContent))
                    entry.ReasoningContent = reasoningContent;
                entry.TokensUsed = tokensUsed;
                entry.PromptTokens = promptTokens;
                entry.CompletionTokens = completionTokens;
                entry.CachedTokens = cachedTokens;
                entry.ElapsedMs = elapsedMs;
                entry.FinishedAtUtc = System.DateTime.UtcNow;
                entry.State = AIRequestTraceState.Completed;
                _revision++;
            }
        }

        public void FailRequest(string requestId, string error)
            => FailRequest(requestId, error, elapsedMs: 0);

        public void FailRequest(string requestId, string error, int elapsedMs)
        {
            lock (_lock)
            {
                var entry = FindOrCreate(requestId);
                entry.Error = error;
                entry.ElapsedMs = elapsedMs;
                entry.FinishedAtUtc = System.DateTime.UtcNow;
                entry.State = AIRequestTraceState.Failed;
                _revision++;
            }
        }

        public void UpdateRequestPrompts(string requestId, string systemPrompt, string userPrompt, string assistantPrompt)
        {
            lock (_lock)
            {
                var entry = FindOrCreate(requestId);
                entry.SystemPrompt = systemPrompt;
                entry.UserPrompt = userPrompt;
                entry.AssistantPrompt = assistantPrompt;
                _revision++;
            }
        }

        public void AddToolCall(string requestId, string toolCallId, string toolName, bool succeeded, string? error)
            => AddToolCallDetailed(requestId, toolCallId, toolName, succeeded, error, argumentsJson: null, resultContent: null);

        public void AddToolCallDetailed(
            string requestId,
            string toolCallId,
            string toolName,
            bool succeeded,
            string? error,
            string? argumentsJson,
            string? resultContent)
        {
            lock (_lock)
            {
                var entry = FindOrCreate(requestId);
                int existingIdx = entry.ToolCalls.FindIndex(t => t.ToolCallId == toolCallId);
                if (existingIdx >= 0)
                {
                    var existing = entry.ToolCalls[existingIdx];
                    entry.ToolCalls[existingIdx] = new AIRequestToolCallTrace(
                        toolCallId,
                        toolName,
                        succeeded,
                        error,
                        argumentsJson ?? existing.ArgumentsJson,
                        resultContent ?? existing.ResultContent);
                }
                else
                {
                    entry.ToolCalls.Add(new AIRequestToolCallTrace(
                        toolCallId,
                        toolName,
                        succeeded,
                        error,
                        argumentsJson,
                        resultContent));
                }
                _revision++;
            }
        }

        internal bool HasToolCall(string requestId, string toolCallId)
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(requestId) || !_byId.TryGetValue(requestId, out var entry))
                    return false;
                return entry.ToolCalls.Any(t => t.ToolCallId == toolCallId);
            }
        }

        public void RecordActionOutcome(
            string requestId,
            string actionIntent,
            bool succeeded,
            string? detail = null,
            string? paramPreview = null)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                return;

            lock (_lock)
            {
                if (!_byId.TryGetValue(requestId, out var entry))
                    return;

                entry.ActionOutcome = new AIRequestActionOutcomeTrace(
                    actionIntent ?? string.Empty,
                    succeeded,
                    detail,
                    paramPreview);

                // Sync final terminal action outcome back onto any matching declared write-tool trace
                // that had ResultContent == null (i.e. deferred from ToolCallDispatchMiddleware to DecisionProcessor).
                for (int i = entry.ToolCalls.Count - 1; i >= 0; i--)
                {
                    var tc = entry.ToolCalls[i];
                    if (tc.ResultContent == null
                        && (string.Equals(tc.ToolName, actionIntent, System.StringComparison.OrdinalIgnoreCase)
                            || entry.ToolCalls.Count == 1))
                    {
                        entry.ToolCalls[i] = tc with
                        {
                            Succeeded = succeeded,
                            Error = succeeded ? null : (detail ?? tc.Error ?? "Action failed")
                        };
                        break;
                    }
                }
                _revision++;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
                _byId.Clear();
                _lastLayerHashByScopeAndTag.Clear();
                _revision++;
            }
        }

        private AIRequestTraceEntry FindOrCreate(string requestId)
        {
            string key = requestId ?? string.Empty;
            if (!string.IsNullOrEmpty(key) && _byId.TryGetValue(key, out var entry))
                return entry;

            Log.Warning($"[RimMind-Core] AIRequestTraceLog.FindOrCreate: no prior StartRequest for '{requestId}', creating orphan entry");
            TrimIfNeeded();
            entry = new AIRequestTraceEntry { RequestId = key };
            _entries.Add(entry);
            if (!string.IsNullOrEmpty(key))
                _byId[key] = entry;
            return entry;
        }

        private void TrimIfNeeded()
        {
            if (_entries.Count < MaxEntries) return;
            int excess = _entries.Count - MaxEntries + 1;
            for (int i = 0; i < excess; i++)
            {
                string removedKey = _entries[i].RequestId;
                if (!string.IsNullOrEmpty(removedKey)
                    && _byId.TryGetValue(removedKey, out var mapped)
                    && ReferenceEquals(mapped, _entries[i]))
                {
                    _byId.Remove(removedKey);
                }
            }
            _entries.RemoveRange(0, excess);
        }

        private static AIRequestTraceEntry CloneEntry(AIRequestTraceEntry entry)
        {
            var snapshot = new AIRequestTraceEntry
            {
                RequestId = entry.RequestId,
                TraceId = entry.TraceId,
                RolloutId = entry.RolloutId,
                TurnIndex = entry.TurnIndex,
                ModId = entry.ModId,
                ScenarioId = entry.ScenarioId,
                NpcId = entry.NpcId,
                IsAgentRollout = entry.IsAgentRollout,
                Source = entry.Source,
                Model = entry.Model,
                SystemPrompt = entry.SystemPrompt,
                UserPrompt = entry.UserPrompt,
                AssistantPrompt = entry.AssistantPrompt,
                Response = entry.Response,
                ReasoningContent = entry.ReasoningContent,
                Error = entry.Error,
                TokensUsed = entry.TokensUsed,
                PromptTokens = entry.PromptTokens,
                CompletionTokens = entry.CompletionTokens,
                CachedTokens = entry.CachedTokens,
                ElapsedMs = entry.ElapsedMs,
                StartedAtUtc = entry.StartedAtUtc,
                FinishedAtUtc = entry.FinishedAtUtc,
                State = entry.State,
                ActionOutcome = entry.ActionOutcome
            };

            snapshot.ToolCalls.AddRange(entry.ToolCalls.Select(toolCall => new AIRequestToolCallTrace(
                toolCall.ToolCallId,
                toolCall.ToolName,
                toolCall.Succeeded,
                toolCall.Error,
                toolCall.ArgumentsJson,
                toolCall.ResultContent)));

            snapshot.Layers.AddRange(entry.Layers);

            return snapshot;
        }
    }
}
