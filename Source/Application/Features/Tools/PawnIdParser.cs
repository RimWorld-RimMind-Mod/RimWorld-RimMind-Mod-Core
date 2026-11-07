using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RimMind.Application.Features.Tools
{
    /// <summary>
    /// Outcome of resolving which pawn a tool call targets.
    /// </summary>
    public readonly struct PawnTargetResolution
    {
        private PawnTargetResolution(bool isValid, int pawnId, bool isExplicit, string? invalidValue)
        {
            IsValid = isValid;
            PawnId = pawnId;
            IsExplicit = isExplicit;
            InvalidValue = invalidValue;
        }

        /// <summary>False when the caller supplied a target that cannot be parsed as a pawn id.</summary>
        public bool IsValid { get; }

        /// <summary>Resolved pawn id; 0 when no target and no caller fallback exist.</summary>
        public int PawnId { get; }

        /// <summary>True when <see cref="PawnId"/> came from the explicit argument, not the caller fallback.</summary>
        public bool IsExplicit { get; }

        /// <summary>The raw explicit value that failed to parse (only when <see cref="IsValid"/> is false).</summary>
        public string? InvalidValue { get; }

        public static PawnTargetResolution Explicit(int pawnId) => new PawnTargetResolution(true, pawnId, true, null);
        public static PawnTargetResolution Fallback(int pawnId) => new PawnTargetResolution(true, pawnId, false, null);
        public static PawnTargetResolution Invalid(string raw) => new PawnTargetResolution(false, 0, true, raw);
    }

    /// <summary>
    /// Single pawn-id parser shared by every tool handler that accepts a pawn target
    /// (mechanism tools, list tools, composite tools).
    /// Accepted forms: <c>12</c>, <c>"12"</c>, <c>"NPC-12"</c>, <c>"pawn-12"</c>, <c>"Pawn_12"</c>, <c>12.0</c>.
    /// </summary>
    public static class PawnIdParser
    {
        /// <summary>
        /// Parses one raw pawn id string. Returns false when the value is empty or not a recognised id form.
        /// </summary>
        public static bool TryParse(string? raw, out int pawnId)
        {
            pawnId = 0;
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

            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out pawnId))
                return true;

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                && d >= int.MinValue && d <= int.MaxValue && Math.Abs(d - Math.Round(d)) < double.Epsilon)
            {
                pawnId = (int)Math.Round(d);
                return true;
            }

            pawnId = 0;
            return false;
        }

        /// <summary>
        /// Resolves the target pawn of a tool call.
        /// <list type="bullet">
        /// <item>Explicit, parseable id &gt; 0: returned as-is, even if no such pawn exists (the handler then
        /// fails with PawnNotFound instead of acting on the caller).</item>
        /// <item>Explicit value present but unparseable (e.g. <c>"Bob"</c>): <see cref="PawnTargetResolution.IsValid"/>
        /// is false. Callers must return an error; a hallucinated target must never redirect a write to the caller.</item>
        /// <item>Missing, empty or &lt;= 0: falls back to <paramref name="callerPawnId"/>, then to the numeric part of
        /// <paramref name="callerNpcId"/>.</item>
        /// </list>
        /// </summary>
        public static PawnTargetResolution ResolveTarget(string? explicitRaw, int? callerPawnId, string? callerNpcId)
        {
            if (!string.IsNullOrWhiteSpace(explicitRaw))
            {
                if (!TryParse(explicitRaw, out var explicitId))
                    return PawnTargetResolution.Invalid(explicitRaw!);
                if (explicitId > 0)
                    return PawnTargetResolution.Explicit(explicitId);
            }

            return PawnTargetResolution.Fallback(ResolveCaller(callerPawnId, callerNpcId));
        }

        /// <summary>
        /// Resolves the target pawn from a JSON argument object, looking at <paramref name="key"/> on the root
        /// and then inside a nested <c>params</c> object.
        /// </summary>
        public static PawnTargetResolution ResolveTarget(JObject? root, string key, int? callerPawnId, string? callerNpcId)
            => ResolveTarget(ReadRaw(root, key), callerPawnId, callerNpcId);

        /// <summary>
        /// Reads the raw string form of <paramref name="key"/> from the root or nested <c>params</c> object.
        /// </summary>
        public static string? ReadRaw(JObject? root, string key)
        {
            if (root == null || string.IsNullOrEmpty(key)) return null;

            if (!root.TryGetValue(key, StringComparison.Ordinal, out var token) || token == null || token.Type == JTokenType.Null)
            {
                if (!(root["params"] is JObject nested)
                    || !nested.TryGetValue(key, StringComparison.Ordinal, out token)
                    || token == null
                    || token.Type == JTokenType.Null)
                {
                    return null;
                }
            }

            return token.Type == JTokenType.String
                ? token.Value<string>()
                : token.ToString(Formatting.None);
        }

        private static int ResolveCaller(int? callerPawnId, string? callerNpcId)
        {
            if (callerPawnId.HasValue && callerPawnId.Value > 0)
                return callerPawnId.Value;
            if (TryParse(callerNpcId, out var npcId) && npcId > 0)
                return npcId;
            return 0;
        }
    }
}
