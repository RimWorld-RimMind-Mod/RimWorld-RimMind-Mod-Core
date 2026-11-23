using System;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.Api
{
    /// <summary>
    /// Formats raw tool/action identifiers into human-readable titles for UI overlays, motes,
    /// and approval cards, preferring registered <see cref="Application.Common.Models.Tools.ToolManifest.DisplayName"/>.
    /// </summary>
    public static class ToolActionDisplayFormatter
    {
        private static readonly string[] StripPrefixes =
        {
            "pawn.job.",
            "pawn.",
            "map.",
            "world.",
            "pawn_",
            "rimmind_"
        };

        public static string FormatHumanReadableActionName(string? rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return "Action";

            var trimmed = rawName!.Trim();
            try
            {
                var registry = RuntimeServiceHub.Shared.Capture().GetOptional<IToolRegistry>();
                var displayName = registry?.FindById(trimmed)?.Definition?.Manifest?.DisplayName;
                if (!string.IsNullOrWhiteSpace(displayName))
                    return displayName!;
            }
            catch
            {
                // Safe fallback when runtime is not booted (e.g. unit tests).
            }

            foreach (var prefix in StripPrefixes)
            {
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    trimmed.Length > prefix.Length)
                {
                    trimmed = trimmed.Substring(prefix.Length);
                    break;
                }
            }

            var tokens = trimmed.Split(new[] { '_', '.', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return trimmed;

            var formatted = new string[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                formatted[i] = token.Length == 1
                    ? token.ToUpperInvariant()
                    : char.ToUpperInvariant(token[0]) + token.Substring(1);
            }

            return string.Join(" ", formatted);
        }
    }
}
