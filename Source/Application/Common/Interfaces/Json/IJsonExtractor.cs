namespace RimMind.Application.Common.Interfaces.Json
{
    public interface IJsonExtractor
    {
        string? ExtractString(string json, string propertyName);
    }
}
