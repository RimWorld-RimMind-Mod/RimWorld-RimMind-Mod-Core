using System;
using RimMind.Application.Common.Interfaces.Json;

namespace RimMind.Infrastructure.Mechanisms
{
    internal static class MechanismToolHelper
    {
        public static string? ExtractString(IJsonExtractor jsonExtractor, string? json, string propertyName)
            => jsonExtractor.ExtractString(json ?? "{}", propertyName);

        public static int ExtractInt(IJsonExtractor jsonExtractor, string? json, string propertyName)
        {
            var str = jsonExtractor.ExtractString(json ?? "{}", propertyName);
            return int.TryParse(str, out var val) ? val : 0;
        }

        public static int? ExtractNullableInt(IJsonExtractor jsonExtractor, string? json, string propertyName)
        {
            var str = jsonExtractor.ExtractString(json ?? "{}", propertyName);
            return int.TryParse(str, out var val) ? val : (int?)null;
        }

        public sealed class FallbackJsonExtractor : IJsonExtractor
        {
            public string? ExtractString(string json, string propertyName)
            {
                if (string.IsNullOrEmpty(json)) return null;
                try
                {
                    var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                    return obj[propertyName]?.ToString();
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }
    }
}
