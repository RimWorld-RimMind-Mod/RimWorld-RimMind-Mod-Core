namespace RimMind.Domain.Storage
{
    public sealed record MemoryHit
    {
        public string Key { get; init; } = "";
        public string Content { get; init; } = "";
        public float Score { get; init; }
        public string? Source { get; init; }
    }
}
