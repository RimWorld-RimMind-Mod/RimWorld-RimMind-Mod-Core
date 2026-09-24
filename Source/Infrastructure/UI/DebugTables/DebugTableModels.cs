using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RimMind.Infrastructure.UI.DebugTables
{
    public enum DebugTableStatus
    {
        Waiting,
        Streaming,
        Completed,
        Failed,
        Cancelled
    }

    public sealed class DebugTableRow
    {
        private DebugTableRow(
            string id,
            DebugTableStatus status,
            string time,
            string scope,
            string actor,
            string channel,
            string model,
            string summary,
            string duration)
        {
            Id = id;
            Status = status;
            Time = time;
            Scope = scope;
            Actor = actor;
            Channel = channel;
            Model = model;
            Summary = summary;
            Duration = duration;
            StatusColorName = ResolveStatusColorName(status);
        }

        public string Id { get; }

        public DebugTableStatus Status { get; }

        public string Time { get; }

        public string Scope { get; }

        public string Actor { get; }

        public string Channel { get; }

        public string Model { get; }

        public string Summary { get; }

        public string Duration { get; }

        public string StatusColorName { get; }

        public static DebugTableRow Create(
            string id,
            DebugTableStatus status,
            string time,
            string scope,
            string actor,
            string channel,
            string model,
            string summary,
            string duration)
            => new(id, status, time, scope, actor, channel, model, summary, duration);

        private static string ResolveStatusColorName(DebugTableStatus status)
        {
            return status switch
            {
                DebugTableStatus.Waiting => "orange",
                DebugTableStatus.Streaming => "blue",
                DebugTableStatus.Completed => "green",
                DebugTableStatus.Failed => "red",
                DebugTableStatus.Cancelled => "gray",
                _ => "gray"
            };
        }
    }

    public sealed class DebugTableModel
    {
        public DebugTableModel(string title, IEnumerable<DebugTableRow> rows)
        {
            Title = title;
            Rows = new ReadOnlyCollection<DebugTableRow>(new List<DebugTableRow>(rows ?? Array.Empty<DebugTableRow>()));
        }

        public string Title { get; }

        public ReadOnlyCollection<DebugTableRow> Rows { get; }
    }

    public static class DebugTableText
    {
        public const int PreviewChars = 160;

        public static string Preview(string? value, int maxChars = PreviewChars)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            int safeMaxChars = System.Math.Max(1, maxChars);

            string oneLine = value
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("<", "[")
                .Replace(">", "]");
            return oneLine.Length <= safeMaxChars
                ? oneLine
                : oneLine.Substring(0, safeMaxChars) + "...";
        }
    }

    public static class DebugTableFixtures
    {
        public static DebugTableModel MixedRequests()
        {
            return new DebugTableModel(
                "AI Requests",
                new[]
                {
                    DebugTableRow.Create("req-001", DebugTableStatus.Waiting, "10:00", "Pawn", "Nickie", "chat", "deepseek-v4-flash", "Queued for context build", "0ms"),
                    DebugTableRow.Create("req-002", DebugTableStatus.Streaming, "10:01", "Pawn", "Tynan", "advisor", "gpt-4.1-mini", "Streaming 3 chunks", "1.2s"),
                    DebugTableRow.Create("req-003", DebugTableStatus.Completed, "10:02", "Colony", "Workbench", "memory", "deepseek-v4-flash", "Completed ToolCall summary: choose_repair_target", "2.4s"),
                    DebugTableRow.Create("req-004", DebugTableStatus.Failed, "10:03", "Pawn", "Maya", "chat", "deepseek-v4-flash", "HTTP timeout after retry", "5s")
                });
        }

        public static DebugTableModel MixedToolCalls()
        {
            return new DebugTableModel(
                "ToolCalls",
                new[]
                {
                    DebugTableRow.Create("tool-001", DebugTableStatus.Completed, "10:04", "Pawn", "Nickie", "move_to", "mechanism", "Moved to stockpile", "180ms"),
                    DebugTableRow.Create("tool-002", DebugTableStatus.Streaming, "10:04", "Pawn", "Tynan", "inspect_need", "mechanism", "Reading hunger state", "80ms"),
                    DebugTableRow.Create("tool-003", DebugTableStatus.Failed, "10:05", "Pawn", "Maya", "reserve_target", "mechanism", "Target reservation denied", "220ms")
                });
        }

        public static DebugTableModel MixedMechanisms()
        {
            return new DebugTableModel(
                "Mechanisms",
                new[]
                {
                    DebugTableRow.Create("mech-001", DebugTableStatus.Completed, "10:06", "Core", "Registry", "movement", "mechanism", "12 handlers registered", "ready"),
                    DebugTableRow.Create("mech-002", DebugTableStatus.Waiting, "10:06", "Core", "Registry", "social", "mechanism", "Waiting for bridge provider", "pending"),
                    DebugTableRow.Create("mech-003", DebugTableStatus.Failed, "10:07", "Core", "Registry", "crafting", "mechanism", "Schema validation failed", "error")
                });
        }

        public static DebugTableModel DenseContextKeys()
        {
            return new DebugTableModel(
                "Context Keys",
                new[]
                {
                    DebugTableRow.Create("ctx-001", DebugTableStatus.Completed, "10:08", "L0", "Core", "pawn.identity", "context", "Pawn identity snapshot", "120 tokens"),
                    DebugTableRow.Create("ctx-002", DebugTableStatus.Completed, "10:08", "L1", "Core", "pawn.needs", "context", "Need and mood summary", "220 tokens"),
                    DebugTableRow.Create("ctx-003", DebugTableStatus.Completed, "10:08", "L1", "Memory", "memory.recent", "context", "Recent memories compacted", "310 tokens"),
                    DebugTableRow.Create("ctx-004", DebugTableStatus.Completed, "10:08", "L2", "Advisor", "advisor.options", "context", "Available actions and risks", "260 tokens"),
                    DebugTableRow.Create("ctx-005", DebugTableStatus.Waiting, "10:09", "L3", "Storyteller", "world.threats", "context", "Awaiting threat scan", "0 tokens"),
                    DebugTableRow.Create("ctx-006", DebugTableStatus.Cancelled, "10:09", "L4", "Dialogue", "dialogue.history", "context", "Skipped by budget", "0 tokens")
                });
        }
    }
}
