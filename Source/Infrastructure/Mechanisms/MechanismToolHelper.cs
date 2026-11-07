using System;
using RimMind.Application.Common.Interfaces.Json;
using RimMind.Application.Common.Models.Tools;
using RimMind.Application.Features.Tools;

namespace RimMind.Infrastructure.Mechanisms
{
    internal static class MechanismToolHelper
    {
        public static string? ExtractString(IJsonExtractor jsonExtractor, string? json, string propertyName)
            => jsonExtractor.ExtractString(json ?? "{}", propertyName);

        public static bool TryParsePawnIdString(string? raw, out int parsedId)
            => PawnIdParser.TryParse(raw, out parsedId);

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
        public static PawnTargetResolution ResolveTarget(IJsonExtractor jsonExtractor, ToolCallArgs args, string propertyName = "pawn_id")
        {
            var raw = ExtractString(jsonExtractor, args.ArgumentsJson, propertyName);
            if (string.IsNullOrWhiteSpace(raw) && !string.IsNullOrWhiteSpace(args.ArgumentsJson))
            {
                try
                {
                    var root = Newtonsoft.Json.Linq.JObject.Parse(args.ArgumentsJson!);
                    raw = PawnIdParser.ReadRaw(root, propertyName);
                }
                catch (Exception)
                {
                }
            }
            return PawnIdParser.ResolveTarget(raw, args.PawnId, args.NpcId);
        }

        public static int ResolvePawnId(IJsonExtractor jsonExtractor, ToolCallArgs args, string propertyName = "pawn_id")
            => ResolveTarget(jsonExtractor, args, propertyName).PawnId;

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
                    return PawnIdParser.ReadRaw(obj, propertyName);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }
    }
}
