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
                    existing.Error = null;
                    existing.TokensUsed = 0;
                    existing.ElapsedMs = 0;
                    existing.StartedAtUtc = System.DateTime.UtcNow;
                    existing.FinishedAtUtc = null;
                    existing.State = AIRequestTraceState.Running;
                    existing.ToolCalls.Clear();
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

        public void CompleteRequest(string requestId, string response, int tokensUsed, int elapsedMs)
        {
            lock (_lock)
            {
                var entry = FindOrCreate(requestId);
                entry.Response = response;
                entry.TokensUsed = tokensUsed;
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
        {
            lock (_lock)
            {
                var entry = FindOrCreate(requestId);
                int existingIdx = entry.ToolCalls.FindIndex(t => t.ToolCallId == toolCallId);
                if (existingIdx >= 0)
                {
                    entry.ToolCalls[existingIdx] = new AIRequestToolCallTrace(
                        toolCallId, toolName, succeeded, error);
                }
                else
                {
                    entry.ToolCalls.Add(new AIRequestToolCallTrace(
                        toolCallId, toolName, succeeded, error));
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
                Source = entry.Source,
                Model = entry.Model,
                SystemPrompt = entry.SystemPrompt,
                UserPrompt = entry.UserPrompt,
                AssistantPrompt = entry.AssistantPrompt,
                Response = entry.Response,
                Error = entry.Error,
                TokensUsed = entry.TokensUsed,
                ElapsedMs = entry.ElapsedMs,
                StartedAtUtc = entry.StartedAtUtc,
                FinishedAtUtc = entry.FinishedAtUtc,
                State = entry.State
            };

            snapshot.ToolCalls.AddRange(entry.ToolCalls.Select(toolCall => new AIRequestToolCallTrace(
                toolCall.ToolCallId,
                toolCall.ToolName,
                toolCall.Succeeded,
                toolCall.Error)));

            return snapshot;
        }
    }
}
