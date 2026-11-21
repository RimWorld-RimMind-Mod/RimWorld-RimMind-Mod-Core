using RimMind.Application.Features.Llm;

namespace RimMind.Infrastructure.Services.Clients.Shared
{
    /// <summary>
    /// Backwards-compatible forwarding facade delegating to <see cref="LogSecretRedactor"/>.
    /// Extension modules should call <see cref="LogSecretRedactor"/> or <c>RimMindAPI.Diagnostics</c>.
    /// </summary>
    public static class LogSanitizer
    {
        public const int DefaultMaxChars = LogSecretRedactor.DefaultMaxChars;
        public const int DebugPayloadMaxChars = LogSecretRedactor.DebugPayloadMaxChars;

        public static string Redact(string? text) => LogSecretRedactor.Redact(text);
        public static string Truncate(string? text, int maxChars = DefaultMaxChars) => LogSecretRedactor.Truncate(text, maxChars);
        public static string ForLog(string? text, int maxChars = DefaultMaxChars) => LogSecretRedactor.ForLog(text, maxChars);
        public static string ForMessage(string? text, int maxChars = DefaultMaxChars) => LogSecretRedactor.ForMessage(text, maxChars);
        public static string ForMessageWithSecret(string? text, string? literalSecret, int maxLength = 320)
            => LogSecretRedactor.ForMessageWithSecret(text, literalSecret, maxLength);
    }
}
