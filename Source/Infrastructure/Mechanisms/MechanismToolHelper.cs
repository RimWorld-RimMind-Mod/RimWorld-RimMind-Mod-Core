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

        public static int ResolvePawnId(IJsonExtractor jsonExtractor, ToolCallArgs args, string propertyName = "pawn_id")
        {
            int id = ExtractInt(jsonExtractor, args.ArgumentsJson, propertyName);
            int fallbackId = 0;
            if (args.PawnId.HasValue && args.PawnId.Value > 0)
            {
                fallbackId = args.PawnId.Value;
            }
            else if (TryParsePawnIdString(args.NpcId, out int npcId) && npcId > 0)
            {
                fallbackId = npcId;
            }

            if (id <= 0)
            {
                return fallbackId;
            }

#if NET48
            if (fallbackId > 0 && id != fallbackId && global::Verse.UnityData.IsInMainThread && global::Verse.Find.CurrentMap != null)
            {
                if (RimMind.Presentation.Api.RimMindPawnLookup.FindPawnByNumber(id) == null &&
                    RimMind.Presentation.Api.RimMindPawnLookup.FindPawnByNumber(fallbackId) != null)
                {
                    return fallbackId;
                }
            }
#endif

            return id;
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
