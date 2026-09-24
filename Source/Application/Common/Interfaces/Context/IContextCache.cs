namespace RimMind.Application.Common.Interfaces.Context
{
    /// <summary>
    /// Cache management — read-only cache queries and cache reset/touch operations.
    /// </summary>
    public interface IContextCache
    {
        int GetL0CacheCount();
        int GetL1BlockCacheCount();
        int GetDiffStoreCount();
        int GetEmbedCacheCount();
        void ResetCaches();
        void TouchCache(string cacheKey);
    }
}
