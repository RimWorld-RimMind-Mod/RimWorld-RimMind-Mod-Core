using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace RimMind.Core.Internal
{
    /// <summary>
    /// 从 AI 响应文本中提取 &lt;TagName&gt;{json}&lt;/TagName&gt; 格式的 JSON 并反序列化。
    /// 不依赖 RimWorld，可在单元测试中直接使用。
    /// </summary>
    public static class JsonTagExtractor
    {
        /// <summary>
        /// 提取并反序列化第一个匹配的标签内容。失败时返回 null（不抛出异常）。
        /// </summary>
        public static T? Extract<T>(string text, string tagName) where T : class
        {
            string? raw = ExtractRaw(text, tagName);
            if (raw == null) return null;

            try
            {
                return JsonConvert.DeserializeObject<T>(raw);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 提取并反序列化所有匹配标签的内容。解析失败的条目跳过（不抛异常）。
        /// </summary>
        public static List<T> ExtractAll<T>(string text, string tagName) where T : class
        {
            var result = new List<T>();
            foreach (var raw in ExtractAllRaw(text, tagName))
            {
                try
                {
                    var item = JsonConvert.DeserializeObject<T>(raw);
                    if (item != null) result.Add(item);
                }
                catch { }
            }
            return result;
        }

        /// <summary>
        /// 提取第一个匹配标签内的原始 JSON 字符串（已 Trim）。未找到时返回 null。
        /// </summary>
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

        /// <summary>
        /// 提取所有匹配标签内的原始字符串列表（已 Trim，空内容跳过）。
        /// </summary>
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
