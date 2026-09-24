using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RimMind.Application.Common.Interfaces.Abstractions;

namespace RimMind.Application.Features.Prompt
{
    public sealed class TaskInstructionBuilder
    {
        private readonly StringBuilder _sb = new StringBuilder();

        public TaskInstructionBuilder AddRole(string role)
        {
            _sb.AppendLine($"You are {role}.");
            return this;
        }

        public TaskInstructionBuilder AddObjective(string objective)
        {
            _sb.AppendLine($"Objective: {objective}");
            return this;
        }

        public TaskInstructionBuilder AddConstraint(string constraint)
        {
            _sb.AppendLine($"Constraint: {constraint}");
            return this;
        }

        public TaskInstructionBuilder AddContext(string context)
        {
            _sb.AppendLine($"Context: {context}");
            return this;
        }

        public TaskInstructionBuilder AddFormat(string format)
        {
            _sb.AppendLine($"Response format: {format}");
            return this;
        }

        public string Build()
        {
            return _sb.ToString();
        }

        public void Reset() => _sb.Clear();

        public static string Build(string keyPrefix, ITranslationService? translationService, params string[] subKeys)
        {
            var parts = new List<string>();
            foreach (var subKey in subKeys)
            {
                string fullKey = $"{keyPrefix}.{subKey}";
                string translated = translationService?.Translate(fullKey) ?? fullKey;
                if (string.IsNullOrEmpty(translated)) continue;
                if (IsMissingTranslation(translated, fullKey))
                    continue;
                parts.Add(translated);
            }
            return parts.Count > 0 ? string.Join("\n\n", parts) : "";
        }

        private static bool IsMissingTranslation(string translated, string originalKey)
        {
            if (translated == originalKey) return true;
            string stripped = StripDiacritics(translated);
            return string.Equals(stripped, originalKey, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripDiacritics(string text)
        {
            string normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize();
        }
    }
}
