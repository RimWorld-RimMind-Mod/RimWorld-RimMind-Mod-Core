using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RimMind.Application.Features.Json
{
    public sealed class ParsedPseudoFunctionCall
    {
        public string? Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string ArgumentsJson { get; init; } = "{}";
        public string? Reason { get; init; }
    }

    /// <summary>
    /// Unified, Verse-free LLM output scrubber, JSON payload extractor/repairer, and pseudo-function-call parser.
    /// Shared across Core and all LLM-consuming submodules via <c>RimMindAPI.Json</c>.
    /// </summary>
    public static class LlmPayloadParser
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

        private static readonly Regex ThinkingBlockRegex = new Regex(
            @"<(?:think|thinking|thought|reasoning)\b[^>]*>[\s\S]*?</(?:think|thinking|thought|reasoning)>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex UnclosedThinkingRegex = new Regex(
            @"<(?:think|thinking|thought|reasoning)\b[^>]*>[\s\S]*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex CodeFenceRegex = new Regex(
            @"```(?:[a-zA-Z0-9_+-]+)?\s*([\s\S]*?)\s*```",
            RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex PseudoFunctionCallRegex = new Regex(
            @"\b(?<fn>[a-z_][a-z0-9_.]*)\((?<inner>[\s\S]+?)\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex PseudoFunctionKeyRegex = new Regex(
            @"(?:^|,\s*)(?<key>[a-z_][a-z0-9_]*)\)?\s*[:=]\s*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex ToToolCommentaryRegex = new Regex(
            @"\bto=(?:functions\.|tools\.)?(?<fn>[a-z_][a-z0-9_.]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            RegexTimeout);

        /// <summary>
        /// Strips reasoning/thinking tags (<c>&lt;think&gt;...&lt;/think&gt;</c>, <c>&lt;thinking&gt;...&lt;/thinking&gt;</c>)
        /// and unwraps markdown code fences (```` ```json ... ``` ````).
        /// </summary>
        public static string StripThinkingAndCodeFences(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string text = raw!.Trim();
            try
            {
                text = ThinkingBlockRegex.Replace(text, string.Empty);
                text = UnclosedThinkingRegex.Replace(text, string.Empty);
                text = text.Trim();

                var fenceMatch = CodeFenceRegex.Match(text);
                if (fenceMatch.Success)
                {
                    text = fenceMatch.Groups[1].Value.Trim();
                }
                else
                {
                    if (text.StartsWith("```", StringComparison.Ordinal))
                    {
                        int nl = text.IndexOf('\n');
                        text = nl >= 0 ? text.Substring(nl + 1).Trim() : text.Substring(3).Trim();
                    }
                    if (text.EndsWith("```", StringComparison.Ordinal))
                    {
                        text = text.Substring(0, text.Length - 3).Trim();
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Return best-effort trimmed input on catastrophic regex timeout.
            }

            return text.Trim();
        }

        /// <summary>
        /// Extracts the first balanced JSON object (<c>{...}</c>) or array (<c>[...]</c>) from raw LLM output,
        /// respecting quoted strings and escape sequences. If the payload is truncated before closing,
        /// returns the substring from the first opener to the end so callers can repair it.
        /// </summary>
        public static string? ExtractJsonPayload(string? raw)
        {
            string cleaned = StripThinkingAndCodeFences(raw);
            if (string.IsNullOrEmpty(cleaned))
                return null;

            int objStart = cleaned.IndexOf('{');
            int arrStart = cleaned.IndexOf('[');
            int start;
            char openChar;
            char closeChar;

            if (objStart >= 0 && (arrStart < 0 || objStart < arrStart))
            {
                start = objStart;
                openChar = '{';
                closeChar = '}';
            }
            else if (arrStart >= 0)
            {
                start = arrStart;
                openChar = '[';
                closeChar = ']';
            }
            else
            {
                return null;
            }

            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = start; i < cleaned.Length; i++)
            {
                char c = cleaned[i];
                if (inString)
                {
                    if (escaped)
                        escaped = false;
                    else if (c == '\\')
                        escaped = true;
                    else if (c == '"')
                        inString = false;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                }
                else if (c == openChar)
                {
                    depth++;
                }
                else if (c == closeChar)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return cleaned.Substring(start, i - start + 1);
                    }
                }
            }

            // Truncated payload: return from start so JsonRepairer can balance it
            return cleaned.Substring(start);
        }

        /// <summary>
        /// Extracts and parses a JSON object from raw LLM output, applying <see cref="JsonRepairer"/> on malformed/truncated JSON.
        /// </summary>
        public static bool TryParseJObjectWithRepair(string? raw, out JObject? obj)
        {
            obj = null;
            string? candidate = ExtractJsonPayload(raw);
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            if (TryParseJObject(candidate!, out obj))
                return true;

            string repaired = JsonRepairer.Repair(candidate!);
            return TryParseJObject(repaired, out obj);
        }

        /// <summary>
        /// Extracts and deserializes <typeparamref name="T"/> from raw LLM output, automatically stripping
        /// thinking tags/code fences and attempting truncation/trailing-comma repair if direct parsing fails.
        /// </summary>
        public static bool TryDeserializeWithRepair<T>(string? raw, out T? result) where T : class
        {
            result = null;
            string? candidate = ExtractJsonPayload(raw);
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            if (JsonExtractorUtils.TryDeserialize(candidate!, out result) && result != null)
                return true;

            string repaired = JsonRepairer.Repair(candidate!);
            if (JsonExtractorUtils.TryDeserialize(repaired, out result) && result != null)
                return true;

            result = null;
            return false;
        }

        /// <summary>
        /// Extracts structured pseudo-function-calls emitted in plain text by LLMs that drop out of
        /// native tool calling: raw JSON tool objects (<c>{"name":..., "parameters":...}</c>),
        /// <c>tool_name(key=val, ...)</c>, and commentary syntax (<c>to=functions.tool_name {...}</c>).
        /// </summary>
        public static IReadOnlyList<ParsedPseudoFunctionCall> ParsePseudoFunctionCalls(string? raw)
        {
            var results = new List<ParsedPseudoFunctionCall>();
            if (string.IsNullOrWhiteSpace(raw))
                return results;

            string cleaned = StripThinkingAndCodeFences(raw);
            if (string.IsNullOrWhiteSpace(cleaned))
                return results;

            // 1. Raw JSON tool-call object
            if (TryParseJObjectWithRepair(cleaned, out var rawObj) && rawObj != null)
            {
                var fromJson = TryBuildCallFromJObject(rawObj);
                if (fromJson != null)
                {
                    results.Add(fromJson);
                    return results;
                }
            }

            // 2. Pseudo-function syntax: tool_name(key=val, ...)
            try
            {
                var matches = PseudoFunctionCallRegex.Matches(cleaned);
                foreach (Match match in matches)
                {
                    if (!match.Success) continue;
                    string fnName = match.Groups["fn"].Value;
                    if (!IsValidToolIdentifier(fnName)) continue;

                    string inner = match.Groups["inner"].Value.Trim();
                    var jObj = ParseKeyValueInner(inner);
                    if (jObj != null && jObj.HasValues)
                    {
                        results.Add(new ParsedPseudoFunctionCall
                        {
                            Name = fnName,
                            ArgumentsJson = jObj.ToString(Formatting.None),
                            Reason = ExtractReasonFromObject(jObj)
                        });
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Ignore timeout
            }

            if (results.Count > 0)
                return results;

            // 3. Commentary syntax: to=functions.tool_name {...}
            try
            {
                var toMatch = ToToolCommentaryRegex.Match(cleaned);
                if (toMatch.Success)
                {
                    string fnName = toMatch.Groups["fn"].Value;
                    if (IsValidToolIdentifier(fnName))
                    {
                        string afterMatch = cleaned.Substring(toMatch.Index + toMatch.Length).Trim();
                        string argsJson = "{}";
                        string? reason = null;
                        if (TryParseJObjectWithRepair(afterMatch, out var argObj) && argObj != null)
                        {
                            argsJson = argObj.ToString(Formatting.None);
                            reason = ExtractReasonFromObject(argObj);
                        }
                        results.Add(new ParsedPseudoFunctionCall
                        {
                            Name = fnName,
                            ArgumentsJson = argsJson,
                            Reason = reason
                        });
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Ignore timeout
            }

            return results;
        }

        /// <summary>
        /// Extracts a non-empty reason string from a tool arguments JSON object, inspecting top-level
        /// <c>reason</c>, nested <c>params.reason</c>, and nested JSON inside <c>value</c> (e.g.,
        /// <c>{"action":"force_rest","value":"{\"reason\":\"...\"}"}</c>).
        /// </summary>
        public static string? ExtractReasonFromArgumentsJson(string? argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return null;

            if (!TryParseJObjectWithRepair(argumentsJson, out var obj) || obj == null)
                return null;

            return ExtractReasonFromObject(obj);
        }

        internal static string? ExtractReasonFromObject(JObject obj)
        {
            if (obj == null) return null;

            string? direct = obj.Value<string>("reason");
            if (!string.IsNullOrWhiteSpace(direct))
                return direct!.Trim();

            if (obj["params"] is JObject paramsObj)
            {
                string? nested = paramsObj.Value<string>("reason");
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested!.Trim();
            }

            var valueTok = obj["value"];
            if (valueTok is JObject valueObj)
            {
                string? valReason = valueObj.Value<string>("reason");
                if (!string.IsNullOrWhiteSpace(valReason))
                    return valReason!.Trim();
            }
            else if (valueTok != null && valueTok.Type == JTokenType.String)
            {
                string valStr = valueTok.Value<string>() ?? string.Empty;
                if (valStr.IndexOf('{') >= 0 && TryParseJObjectWithRepair(valStr, out var parsedVal) && parsedVal != null)
                {
                    string? innerReason = parsedVal.Value<string>("reason");
                    if (!string.IsNullOrWhiteSpace(innerReason))
                        return innerReason!.Trim();
                }
            }

            return null;
        }

        private static ParsedPseudoFunctionCall? TryBuildCallFromJObject(JObject rawObj)
        {
            string? rawToolName = rawObj.Value<string>("name")
                ?? rawObj.Value<string>("tool")
                ?? (rawObj["function"] as JObject)?.Value<string>("name");
            if (string.IsNullOrWhiteSpace(rawToolName) || !IsValidToolIdentifier(rawToolName))
                return null;

            var paramsToken = rawObj["parameters"]
                ?? rawObj["arguments"]
                ?? rawObj["args"]
                ?? rawObj["param"]
                ?? (rawObj["function"] as JObject)?["arguments"];

            string? argsJson = null;
            string? reason = ExtractReasonFromObject(rawObj);

            if (paramsToken is JObject paramsObj)
            {
                argsJson = paramsObj.ToString(Formatting.None);
                reason ??= ExtractReasonFromObject(paramsObj);
            }
            else if (paramsToken != null && paramsToken.Type == JTokenType.String)
            {
                argsJson = paramsToken.ToString();
                reason ??= ExtractReasonFromArgumentsJson(argsJson);
            }
            else
            {
                var copyObj = (JObject)rawObj.DeepClone();
                copyObj.Remove("name");
                copyObj.Remove("tool");
                copyObj.Remove("function");
                copyObj.Remove("id");
                copyObj.Remove("type");
                if (copyObj.HasValues)
                    argsJson = copyObj.ToString(Formatting.None);
            }

            return new ParsedPseudoFunctionCall
            {
                Id = rawObj.Value<string>("id"),
                Name = rawToolName!,
                ArgumentsJson = string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson!,
                Reason = reason
            };
        }

        private static JObject? ParseKeyValueInner(string inner)
        {
            if (string.IsNullOrWhiteSpace(inner)) return null;
            var keyMatches = PseudoFunctionKeyRegex.Matches(inner);
            if (keyMatches.Count == 0) return null;

            var jObj = new JObject();
            for (int i = 0; i < keyMatches.Count; i++)
            {
                string key = keyMatches[i].Groups["key"].Value;
                int valStart = keyMatches[i].Index + keyMatches[i].Length;
                int valEnd = (i + 1 < keyMatches.Count) ? keyMatches[i + 1].Index : inner.Length;
                string rawVal = inner.Substring(valStart, valEnd - valStart).Trim();
                if ((rawVal.StartsWith("\"") && rawVal.EndsWith("\"") && rawVal.Length >= 2) ||
                    (rawVal.StartsWith("'") && rawVal.EndsWith("'") && rawVal.Length >= 2) ||
                    (rawVal.StartsWith("\u201C") && rawVal.EndsWith("\u201D") && rawVal.Length >= 2) ||
                    (rawVal.StartsWith("(") && rawVal.EndsWith(")") && rawVal.Length >= 2))
                {
                    rawVal = rawVal.Substring(1, rawVal.Length - 2).Replace("\\\"", "\"");
                }
                else if (rawVal.StartsWith("(") && keyMatches[i].Value.IndexOf(')') >= 0)
                {
                    rawVal = rawVal.Substring(1).TrimEnd(')').Trim();
                }

                if (int.TryParse(rawVal, out int intVal))
                    jObj[key] = intVal;
                else if (bool.TryParse(rawVal, out bool boolVal))
                    jObj[key] = boolVal;
                else
                    jObj[key] = rawVal;
            }
            return jObj;
        }

        internal static bool IsValidToolIdentifier(string? fnName)
        {
            if (string.IsNullOrWhiteSpace(fnName)) return false;
            if (fnName!.IndexOf('_') < 0 && fnName.IndexOf('.') < 0) return false;
            if (fnName[0] == '.' || fnName[fnName.Length - 1] == '.') return false;
            foreach (char c in fnName)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.'))
                    return false;
            }
            return true;
        }

        private static bool TryParseJObject(string json, out JObject? obj)
        {
            obj = null;
            try
            {
                obj = JToken.Parse(json) as JObject;
                return obj != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
