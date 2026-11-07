using System;

namespace RimMind.Application.Common.Models.Psychology
{
    /// <summary>
    /// Shared specification for injecting AI-generated thoughts into RimWorld's memory thought system.
    /// </summary>
    public sealed record ThoughtSpec
    {
        public ThoughtSpec()
        {
        }

        public ThoughtSpec(
            int pawnId,
            string ownerModId,
            string? slotKey,
            string label,
            string? description,
            float moodOffset,
            int durationTicks = -1)
        {
            PawnId = pawnId;
            OwnerModId = ownerModId ?? string.Empty;
            SlotKey = slotKey;
            Label = label ?? string.Empty;
            Description = description ?? string.Empty;
            MoodOffset = moodOffset;
            DurationTicks = durationTicks;
        }

        public int PawnId { get; init; }
        public string OwnerModId { get; init; } = string.Empty;
        public string? SlotKey { get; init; }
        public string Label { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public float MoodOffset { get; init; }
        public int DurationTicks { get; init; } = -1;

        public static string FormatCleanLabelFallback(string? rawTagOrKey, string defaultPrefixToStrip = "RimMind.")
        {
            if (string.IsNullOrEmpty(rawTagOrKey)) return string.Empty;
            string key = rawTagOrKey!;
            if (!string.IsNullOrEmpty(defaultPrefixToStrip) &&
                key.StartsWith(defaultPrefixToStrip, StringComparison.Ordinal))
            {
                int lastDot = key.LastIndexOf('.');
                return lastDot >= 0 && lastDot < key.Length - 1
                    ? key.Substring(lastDot + 1)
                    : key;
            }
            return key;
        }
    }
}
