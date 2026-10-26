using System;
using System.Text.RegularExpressions;

namespace RimMind.Infrastructure.Services.Clients.Shared
{
    /// <summary>
    /// Log hygiene for transport diagnostics: truncates provider payloads and
    /// redacts anything that looks like a credential before it reaches a log sink
    /// or an exception message.
    /// </summary>
    internal static class LogSanitizer
    {
        /// <summary>Default cap for provider error bodies embedded in messages and logs.</summary>
        public const int DefaultMaxChars = 500;

        /// <summary>Cap for request/response JSON echoed when DebugLogging is enabled.</summary>
        public const int DebugPayloadMaxChars = 2000;

        private const string Mask = "***";

        private static readonly Regex BearerPattern = new Regex(
            @"(?i)\b(Bearer|Basic)\s+[A-Za-z0-9\-\._~\+\/=:]{4,}",
            RegexOptions.Compiled);

        private static readonly Regex KeyValuePattern = new Regex(
            @"(?i)(""?(?<![A-Za-z0-9_\-])(?:api[_\-]?key|x-api-key|authorization|access[_\-]?token|refresh[_\-]?token|p2_key|secret|password|token)""?\s*[:=]\s*""?)([^""\s,;}&]+)",
            RegexOptions.Compiled);

        private static readonly Regex KeyPrefixPattern = new Regex(
            @"\b(?:sk|oc_sk|sk-ant|sk-proj|pk|rk|xai|gsk|AIza)[\-_][A-Za-z0-9\-_]{8,}",
            RegexOptions.Compiled);

        /// <summary>Redacts bearer tokens, key/value secrets and well-known API-key shapes.</summary>
        public static string Redact(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            string result = BearerPattern.Replace(text, m => m.Groups[1].Value + " " + Mask);
            result = KeyValuePattern.Replace(result, m => m.Groups[1].Value + Mask);
            result = KeyPrefixPattern.Replace(result, Mask);
            return result;
        }

        /// <summary>Truncates to <paramref name="maxChars"/> characters, appending a marker with the original length.</summary>
        public static string Truncate(string? text, int maxChars = DefaultMaxChars)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            if (maxChars <= 0 || text!.Length <= maxChars) return text!;
            return text.Substring(0, maxChars) + $"…[truncated {text.Length - maxChars} chars]";
        }

        /// <summary>Redacts then truncates; the standard treatment for anything that may be logged.</summary>
        public static string ForLog(string? text, int maxChars = DefaultMaxChars)
        {
            return Truncate(Redact(text), maxChars);
        }
    }
}
