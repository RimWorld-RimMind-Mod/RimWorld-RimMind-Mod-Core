using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Diagnostics;
using RimMind.Application.Features.Llm;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        /// <summary>
        /// Public diagnostic hygiene helpers and trace-summarization extension registration.
        /// </summary>
        public static class Diagnostics
        {
            public static string SanitizeForLog(string? text, int maxLength = LogSecretRedactor.DefaultMaxChars)
                => LogSecretRedactor.ForLog(text, maxLength);

            public static string SanitizeMessage(string? text, string? literalSecret = null, int maxLength = 320)
                => LogSecretRedactor.ForMessageWithSecret(text, literalSecret, maxLength);

            public static void RegisterTraceSummarizer(IRequestTraceSummarizer summarizer)
                => RequestTraceSummarizerRegistry.Register(summarizer);

            public static bool UnregisterTraceSummarizer(string id)
                => RequestTraceSummarizerRegistry.Unregister(id);

            public static int UnregisterTraceSummarizersByOwner(string ownerModId)
                => RequestTraceSummarizerRegistry.UnregisterByOwner(ownerModId);

            public static IReadOnlyList<IRequestTraceSummarizer> RegisteredTraceSummarizers
                => RequestTraceSummarizerRegistry.All;
        }
    }
}
