using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace RimMind.Application.Features.Json
{
    public static class JsonTagExtractor
    {
        public static Action<string>? OnWarning;

        private static void Warn(string message)
        {
            OnWarning?.Invoke(message);
        }

        public static T? Extract<T>(string text, string tagName) where T : class
        {
            string? raw = ExtractRaw(text, tagName);
            if (raw != null)
            {
                return DeserializeWithRepair<T>(raw);
            }

            // Fallback: if tag is absent, check if the entire text contains JSON
            string cleanText = SanitizeJsonContent(text);
            if ((cleanText.StartsWith("{") && cleanText.EndsWith("}")) ||
                (cleanText.StartsWith("[") && cleanText.EndsWith("]")))
            {
                return DeserializeWithRepair<T>(cleanText);
            }

            return null;
        }

        public static List<T> ExtractAll<T>(string text, string tagName) where T : class
        {
            var result = new List<T>();
            foreach (var raw in ExtractAllRaw(text, tagName))
            {
                var item = DeserializeWithRepair<T>(raw);
                if (item != null) result.Add(item);
            }

            if (result.Count == 0)
            {
                var fallback = Extract<T>(text, tagName);
                if (fallback != null) result.Add(fallback);
            }

            return result;
        }

        public static string SanitizeJsonContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return "";
            string trimmed = content.Trim();

            if (trimmed.StartsWith("```"))
            {
                int firstNewline = trimmed.IndexOf('\n');
                if (firstNewline >= 0)
                    trimmed = trimmed.Substring(firstNewline + 1);
                else
                    trimmed = trimmed.TrimStart('`');

                if (trimmed.EndsWith("```"))
                    trimmed = trimmed.Substring(0, trimmed.Length - 3);
            }

            return trimmed.Trim();
        }

        private static T? DeserializeWithRepair<T>(string raw) where T : class
        {
            string clean = SanitizeJsonContent(raw);
            if (string.IsNullOrEmpty(clean)) return null;

            try
            {
                return JsonConvert.DeserializeObject<T>(clean);
            }
            catch
            {
                try
                {
                    string repaired = JsonRepairer.Repair(clean);
                    return JsonConvert.DeserializeObject<T>(repaired);
                }
                catch (Exception ex)
                {
                    Warn($"[RimMind-Core] JsonTagExtractor deserialization failed after repair: {ex.Message}");
                    return null;
                }
            }
        }

        public static string? ExtractRaw(string text, string tagName)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(tagName))
                return null;

            var pattern = $@"<{Regex.Escape(tagName)}>([\s\S]*?)</{Regex.Escape(tagName)}>";
            var match = Regex.Match(text, pattern, RegexOptions.Singleline);
            if (!match.Success) return null;

            string content = match.Groups[1].Value.Trim();
            return string.IsNullOrEmpty(content) ? null : content;
        }

        public static List<string> ExtractAllRaw(string text, string tagName)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(tagName))
                return result;

            var pattern = $@"<{Regex.Escape(tagName)}>([\s\S]*?)</{Regex.Escape(tagName)}>";
            foreach (Match match in Regex.Matches(text, pattern, RegexOptions.Singleline))
            {
                string content = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(content))
                    result.Add(content);
            }
            return result;
        }
    }
}
