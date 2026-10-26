using System;
using RimMind.Application.Common.Interfaces.Json;
using RimMind.Application.Common.Models.Tools;

namespace RimMind.Infrastructure.Mechanisms
{
    internal static class MechanismToolHelper
    {
        public static string? ExtractString(IJsonExtractor jsonExtractor, string? json, string propertyName)
            => jsonExtractor.ExtractString(json ?? "{}", propertyName);

        public static bool TryParsePawnIdString(string? raw, out int parsedId)
        {
            parsedId = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var s = raw!.Trim();
            if (s.StartsWith("NPC-", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("pawn-", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(s.IndexOf('-') + 1).Trim();
            }
            else if (s.StartsWith("Pawn_", StringComparison.OrdinalIgnoreCase))
            {
                int idx = s.Length - 1;
                while (idx >= 0 && char.IsDigit(s[idx])) idx--;
                s = s.Substring(idx + 1);
            }
            return int.TryParse(s, out parsedId);
        }

        public static int ExtractInt(IJsonExtractor jsonExtractor, string? json, string propertyName)
        {
            var str = jsonExtractor.ExtractString(json ?? "{}", propertyName);
            return TryParsePawnIdString(str, out var val) ? val : 0;
        }

        public static int? ExtractNullableInt(IJsonExtractor jsonExtractor, string? json, string propertyName)
        {
            var str = jsonExtractor.ExtractString(json ?? "{}", propertyName);
            return TryParsePawnIdString(str, out var val) ? val : (int?)null;
        }

        /// <summary>
        /// Resolves the pawn id a mechanism tool call targets.
        /// <list type="bullet">
        /// <item>An explicit, parseable id &gt; 0 in <paramref name="propertyName"/> is returned as-is, even if it
        /// does not resolve to a live pawn. The mechanism then fails with a clear PawnNotFound error instead of
        /// silently acting on the calling pawn (a hallucinated id must never redirect a write to the caller).</item>
        /// <item>Only when the argument is missing, unparseable or &lt;= 0 does it fall back to the caller's own
        /// pawn (<see cref="ToolCallArgs.PawnId"/>, then the numeric part of <see cref="ToolCallArgs.NpcId"/>).</item>
        /// </list>
        /// </summary>
        public static int ResolvePawnId(IJsonExtractor jsonExtractor, ToolCallArgs args, string propertyName = "pawn_id")
        {
            int id = ExtractInt(jsonExtractor, args.ArgumentsJson, propertyName);
            if (id > 0)
            {
                return id;
            }

            if (args.PawnId.HasValue && args.PawnId.Value > 0)
            {
                return args.PawnId.Value;
            }
            if (TryParsePawnIdString(args.NpcId, out int npcId) && npcId > 0)
            {
                return npcId;
            }
            return 0;
        }

        public static int? ResolveNullablePawnId(IJsonExtractor jsonExtractor, ToolCallArgs args, string propertyName = "pawn_id")
        {
            int resolved = ResolvePawnId(jsonExtractor, args, propertyName);
            return resolved > 0 ? resolved : (int?)null;
        }

        public sealed class FallbackJsonExtractor : IJsonExtractor
        {
            public string? ExtractString(string json, string propertyName)
            {
                if (string.IsNullOrEmpty(json)) return null;
                try
                {
                    var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                    return obj[propertyName]?.ToString();
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }
    }
}
