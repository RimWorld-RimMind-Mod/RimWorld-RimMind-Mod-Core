using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RimMind.Application.Features.Prompt
{
    public static class PromptSanitizer
    {
        private static readonly Regex ControlCharRegex = new Regex(
            @"[\x00-\x08\x0B\x0C\x0E-\x1F]",
            RegexOptions.Compiled);

        private static readonly Regex PromptOverrideRegex = new Regex(
            @"\bignore\s+(?:all\s+)?previous\s+instructions\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var result = ControlCharRegex.Replace(input, "");
            result = result.Replace("{{", "{").Replace("}}", "}");
            result = result.Trim();
            return result;
        }

        public static string SanitizeUserInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            string normalized;
            try
            {
                normalized = input.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                normalized = input;
            }

            normalized = RemoveFormatCharacters(normalized);
            normalized = PromptOverrideRegex.Replace(normalized, "[filtered]");
            return Sanitize(normalized);
        }

        private static string RemoveFormatCharacters(string input)
        {
            StringBuilder? sanitized = null;
            var copyStart = 0;

            for (var index = 0; index < input.Length;)
            {
                var charLength = char.IsSurrogatePair(input, index) ? 2 : 1;
                if (CharUnicodeInfo.GetUnicodeCategory(input, index) == UnicodeCategory.Format)
                {
                    sanitized ??= new StringBuilder(input.Length);
                    sanitized.Append(input, copyStart, index - copyStart);
                    copyStart = index + charLength;
                }

                index += charLength;
            }

            if (sanitized == null)
            {
                return input;
            }

            sanitized.Append(input, copyStart, input.Length - copyStart);
            return sanitized.ToString();
        }

        public static string Truncate(string input, int maxLength)
        {
            if (string.IsNullOrEmpty(input) || input.Length <= maxLength) return input;
            return input.Substring(0, maxLength) + "...";
        }

        public static string RemoveDuplicateLines(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var seen = new HashSet<string>();
            var sb = new StringBuilder();
            foreach (var line in input.Split('\n'))
            {
                if (seen.Add(line))
                {
                    sb.AppendLine(line);
                }
            }
            return sb.ToString();
        }
    }
}
