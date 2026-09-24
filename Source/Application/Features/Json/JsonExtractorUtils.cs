using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace RimMind.Application.Features.Json
{
    public static class JsonExtractorUtils
    {
        private static readonly JsonSerializerSettings DefaultSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        private static readonly JsonSerializerSettings PrettySettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        public static string Serialize(object obj, bool pretty = false)
        {
            return JsonConvert.SerializeObject(obj, pretty ? PrettySettings : DefaultSettings);
        }

        public static T? Deserialize<T>(string json) where T : class
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(json, DefaultSettings);
            }
            catch
            {
                return null;
            }
        }

        public static T? Deserialize<T>(string json, T defaultValue) where T : class
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(json, DefaultSettings) ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        public static bool TryDeserialize<T>(string json, out T? result) where T : class
        {
            try
            {
                result = JsonConvert.DeserializeObject<T>(json, DefaultSettings);
                return result != null;
            }
            catch
            {
                result = null;
                return false;
            }
        }

        public static string? SafeGetString(JObject obj, string key)
        {
            return obj.TryGetValue(key, out var token) ? token.Value<string>() : null;
        }

        public static int SafeGetInt(JObject obj, string key, int defaultValue = 0)
        {
            return obj.TryGetValue(key, out var token) ? token.Value<int>() : defaultValue;
        }

        public static bool SafeGetBool(JObject obj, string key, bool defaultValue = false)
        {
            return obj.TryGetValue(key, out var token) ? token.Value<bool>() : defaultValue;
        }

        public static string? ExtractString(string json, string propertyName)
        {
            try
            {
                var obj = JObject.Parse(json);
                return obj.TryGetValue(propertyName, out var token) ? token.Value<string>() : null;
            }
            catch
            {
                return null;
            }
        }

        public static int? ExtractNullableInt(string json, string propertyName)
        {
            try
            {
                var obj = JObject.Parse(json);
                if (!obj.TryGetValue(propertyName, out var token)) return null;
                return token.Value<int>();
            }
            catch
            {
                return null;
            }
        }
    }
}
