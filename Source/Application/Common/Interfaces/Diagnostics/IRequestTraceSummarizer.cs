using System;
using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Models.Debug;

namespace RimMind.Application.Common.Interfaces.Diagnostics
{
    /// <summary>
    /// Summary produced by a submodule <see cref="IRequestTraceSummarizer"/> for an AI request trace row.
    /// </summary>
    public sealed record RequestTraceSummary(
        string ContentSummary,
        IReadOnlyList<string>? SuppressToolNames = null);

    /// <summary>
    /// Extensible trace-summarization seam allowing submodules (e.g. Dialogue) to format their own
    /// tool responses, prompts, and tool-badge suppressions in Core's trace UI without Core hardcoding
    /// submodule tool names or translation keys.
    /// </summary>
    public interface IRequestTraceSummarizer : IExtension
    {
        int Order { get; }
        bool TrySummarizeResponse(AIRequestTraceEntry entry, out RequestTraceSummary? summary);
        bool TrySummarizePrompt(string rawPrompt, out string? summary);
        bool ShouldSuppressToolCall(string toolName);
    }

    /// <summary>
    /// Thread-safe registry of <see cref="IRequestTraceSummarizer"/> extensions queried by trace UI builders.
    /// </summary>
    public static class RequestTraceSummarizerRegistry
    {
        private static readonly object SyncRoot = new object();
        private static readonly List<IRequestTraceSummarizer> Registered = new List<IRequestTraceSummarizer>();

        public static void Register(IRequestTraceSummarizer summarizer)
        {
            if (summarizer == null) throw new ArgumentNullException(nameof(summarizer));
            if (string.IsNullOrWhiteSpace(summarizer.Id))
                throw new ArgumentException("Summarizer Id cannot be null or whitespace.", nameof(summarizer));

            lock (SyncRoot)
            {
                int idx = Registered.FindIndex(s => string.Equals(s.Id, summarizer.Id, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) Registered[idx] = summarizer;
                else Registered.Add(summarizer);
                Registered.Sort((a, b) => a.Order.CompareTo(b.Order));
            }
        }

        public static bool Unregister(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            lock (SyncRoot)
            {
                return Registered.RemoveAll(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            }
        }

        public static int UnregisterByOwner(string ownerModId)
        {
            if (ownerModId == null) throw new ArgumentNullException(nameof(ownerModId));
            lock (SyncRoot)
            {
                return Registered.RemoveAll(s => string.Equals(s.OwnerModId, ownerModId, StringComparison.Ordinal));
            }
        }

        public static bool TrySummarizeResponse(AIRequestTraceEntry entry, out RequestTraceSummary? summary)
        {
            summary = null;
            if (entry == null) return false;
            var snapshot = Snapshot();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i].TrySummarizeResponse(entry, out summary) && summary != null)
                    return true;
            }
            return false;
        }

        public static bool TrySummarizePrompt(string rawPrompt, out string? summary)
        {
            summary = null;
            if (string.IsNullOrWhiteSpace(rawPrompt)) return false;
            var snapshot = Snapshot();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i].TrySummarizePrompt(rawPrompt, out summary) && !string.IsNullOrWhiteSpace(summary))
                    return true;
            }
            return false;
        }

        public static bool ShouldSuppressToolCall(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName)) return false;
            var snapshot = Snapshot();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i].ShouldSuppressToolCall(toolName))
                    return true;
            }
            return false;
        }

        public static IReadOnlyList<IRequestTraceSummarizer> All => Snapshot();

        public static void Clear()
        {
            lock (SyncRoot)
            {
                Registered.Clear();
            }
        }

        private static IRequestTraceSummarizer[] Snapshot()
        {
            lock (SyncRoot)
            {
                return Registered.Count == 0 ? Array.Empty<IRequestTraceSummarizer>() : Registered.ToArray();
            }
        }
    }
}
