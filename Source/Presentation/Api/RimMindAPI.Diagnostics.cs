using RimMind.Infrastructure.Services.Clients.Shared;

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
            public static string SanitizeForLog(string? text, int maxLength = LogSanitizer.DefaultMaxChars)
                => LogSanitizer.ForLog(text, maxLength);

            public static string SanitizeMessage(string? text, string? literalSecret = null, int maxLength = 320)
                => LogSanitizer.ForMessageWithSecret(text, literalSecret, maxLength);
        }
    }
}
