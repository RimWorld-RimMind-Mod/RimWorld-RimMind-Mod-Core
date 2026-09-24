namespace RimMind.Domain.Storage
{
    public sealed record MemoryEntry
    {
        public string Key { get; init; } = "";
        public string Content { get; init; } = "";
        public string? Metadata { get; init; }
        public float[]? Embedding { get; init; }
    }
}
