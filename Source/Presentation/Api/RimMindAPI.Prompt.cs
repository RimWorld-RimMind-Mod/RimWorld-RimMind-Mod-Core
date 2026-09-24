using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Features.Json;
using RimMind.Application.Features.Prompt;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        /// <summary>
        /// Facade for prompt-related operations: PromptSanitizer, TaskInstructionBuilder.
        /// Sub-mods should use this instead of directly referencing
        /// RimMind.Application.Features.Prompt.
        /// </summary>
        public static class Prompt
        {
            // ── PromptSanitizer ──

            public static string Sanitize(string input)
                => PromptSanitizer.Sanitize(input);

            public static string SanitizeUserInput(string input)
                => PromptSanitizer.SanitizeUserInput(input);

            public static string Truncate(string input, int maxLength)
                => PromptSanitizer.Truncate(input, maxLength);

            public static string RemoveDuplicateLines(string input)
                => PromptSanitizer.RemoveDuplicateLines(input);

            // ── TaskInstructionBuilder ──

            public static string BuildTaskInstruction(string keyPrefix, ITranslationService? translationService, params string[] subKeys)
                => TaskInstructionBuilder.Build(keyPrefix, translationService, subKeys);

            public static TaskInstructionBuilder CreateInstructionBuilder()
                => new TaskInstructionBuilder();
        }

        /// <summary>
        /// Facade for JSON-related operations: JsonExtractorUtils, JsonRepairer, JsonTagExtractor.
        /// Sub-mods should use this instead of directly referencing
        /// RimMind.Application.Features.Json.
        /// </summary>
        public static class Json
        {
            // ── JsonExtractorUtils ──

            public static string Serialize(object obj, bool pretty = false)
                => JsonExtractorUtils.Serialize(obj, pretty);

            public static T? Deserialize<T>(string json) where T : class
                => JsonExtractorUtils.Deserialize<T>(json);

            public static T? Deserialize<T>(string json, T defaultValue) where T : class
                => JsonExtractorUtils.Deserialize<T>(json, defaultValue);

            public static bool TryDeserialize<T>(string json, out T? result) where T : class
                => JsonExtractorUtils.TryDeserialize<T>(json, out result);

            public static string? ExtractString(string json, string propertyName)
                => JsonExtractorUtils.ExtractString(json, propertyName);

            public static int? ExtractNullableInt(string json, string propertyName)
                => JsonExtractorUtils.ExtractNullableInt(json, propertyName);

            // ── JsonRepairer ──

            public static string? TryRepairJson(string input)
                => JsonRepairer.TryRepair(input);

            public static string? TryRepairTruncatedJson(string input)
                => JsonRepairer.TryRepairTruncatedJson(input);

            // ── JsonTagExtractor ──

            public static T? ExtractTag<T>(string text, string tagName) where T : class
                => JsonTagExtractor.Extract<T>(text, tagName);

            public static List<T> ExtractAllTags<T>(string text, string tagName) where T : class
                => JsonTagExtractor.ExtractAll<T>(text, tagName);

            public static string? ExtractTagRaw(string text, string tagName)
                => JsonTagExtractor.ExtractRaw(text, tagName);

            public static List<string> ExtractAllTagRaw(string text, string tagName)
                => JsonTagExtractor.ExtractAllRaw(text, tagName);
        }
    }
}
