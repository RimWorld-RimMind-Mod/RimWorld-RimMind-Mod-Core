namespace RimMind.Domain.Interfaces
{
    public interface IEmbedCache
    {
        float[]? GetOrComputeQueryEmbedding(string query);
        void StoreEntryEmbedding(string key, float[] embedding);
        float[]? GetEntryEmbedding(string key);

        // Cache lifecycle management - needed by ContextCacheManager (Application layer)
        // which cannot reference the concrete EmbedCache in Infrastructure.
        int Count { get; }
        void Clear();
    }
}
