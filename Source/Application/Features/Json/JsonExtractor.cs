using RimMind.Application.Common.Interfaces.Json;

namespace RimMind.Application.Features.Json
{
    public sealed class JsonExtractor : IJsonExtractor
    {
        public string? ExtractString(string json, string propertyName)
        {
            return JsonExtractorUtils.ExtractString(json, propertyName);
        }
    }
}
