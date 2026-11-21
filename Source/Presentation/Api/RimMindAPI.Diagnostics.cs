using RimMind.Application.Features.Llm;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        /// <summary>
        /// Public diagnostic hygiene helpers for redacting credentials and truncating payloads
        /// before writing to logs, UI status messages, or error surfaces.
        /// </summary>
        public static class Diagnostics
        {
            public static string SanitizeForLog(string? text, int maxLength = LogSecretRedactor.DefaultMaxChars)
                => LogSecretRedactor.ForLog(text, maxLength);

            public static string SanitizeMessage(string? text, string? literalSecret = null, int maxLength = 320)
                => LogSecretRedactor.ForMessageWithSecret(text, literalSecret, maxLength);
        }
    }
}
