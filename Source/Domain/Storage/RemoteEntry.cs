namespace RimMind.Domain.Storage
{
    public sealed record RemoteEntry
    {
        public string Key { get; init; } = "";
        public string Json { get; init; } = "";
        public long Version { get; init; }
        public string? Etag { get; init; }
    }
}
