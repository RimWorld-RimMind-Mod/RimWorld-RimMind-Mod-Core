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
        private long _revision;

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
                    return _entries.Select(CloneEntry).ToList();
                }
            }
        }

        public void StartRequest(string requestId, string source, string model, string systemPrompt, string userPrompt, string assistantPrompt)
        {
            lock (_lock)
            {
                var existing = _entries.FirstOrDefault(e => e.RequestId == requestId);
                if (existing != null)
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
                _entries.Add(new AIRequestTraceEntry
                {
                    RequestId = requestId,
                    Source = source,
                    Model = model,
                    SystemPrompt = systemPrompt,
                    UserPrompt = userPrompt,
                    AssistantPrompt = assistantPrompt,
                    StartedAtUtc = System.DateTime.UtcNow,
                    State = AIRequestTraceState.Running
                });
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
                _revision++;
            }
        }

        private AIRequestTraceEntry FindOrCreate(string requestId)
        {
            var entry = _entries.FirstOrDefault(e => e.RequestId == requestId);
            if (entry != null) return entry;

            Log.Warning($"[RimMind-Core] AIRequestTraceLog.FindOrCreate: no prior StartRequest for '{requestId}', creating orphan entry");
            TrimIfNeeded();
            entry = new AIRequestTraceEntry { RequestId = requestId };
            _entries.Add(entry);
            return entry;
        }

        private void TrimIfNeeded()
        {
            if (_entries.Count < MaxEntries) return;
            int excess = _entries.Count - MaxEntries + 1;
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
