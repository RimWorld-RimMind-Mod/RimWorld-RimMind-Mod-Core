using System;
using System.Text.RegularExpressions;

namespace RimMind.Application.Features.Llm
{
    /// <summary>
    /// Engine-free credential redaction and log-payload truncation helper.
    /// Shared across Core and extension modules (e.g. ModelService) without touching Infrastructure.
    /// </summary>
    public static class LogSecretRedactor
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
            @"\b(?:(?:[a-z]{2,4}_sk|sk-ant|sk-proj|sk|pk|rk|xai|gsk)[\-_][A-Za-z0-9\-_]{8,}|AIza[A-Za-z0-9\-_]{16,})",
            RegexOptions.Compiled);

        public static string Redact(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            string result = BearerPattern.Replace(text!, m => m.Groups[1].Value + " " + Mask);
            result = KeyValuePattern.Replace(result, m => m.Groups[1].Value + Mask);
            result = KeyPrefixPattern.Replace(result, Mask);
            return result;
        }

        public static string Truncate(string? text, int maxChars = DefaultMaxChars)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            if (maxChars <= 0 || text!.Length <= maxChars) return text!;
            return text.Substring(0, maxChars) + $"…[truncated {text.Length - maxChars} chars]";
        }

        public static string ForLog(string? text, int maxChars = DefaultMaxChars)
            => Truncate(Redact(text), maxChars);

        public static string ForMessage(string? text, int maxChars = DefaultMaxChars)
            => ForLog(text, maxChars);

        public static string ForMessageWithSecret(string? text, string? literalSecret, int maxLength = 320)
        {
            string value = text ?? string.Empty;
            if (!string.IsNullOrEmpty(literalSecret) && literalSecret!.Length >= 4)
                value = value.Replace(literalSecret, Mask);
            return ForLog(value, maxLength);
        }
    }
}
