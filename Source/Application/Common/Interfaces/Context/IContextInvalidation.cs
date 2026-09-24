using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Context
{
    /// <summary>
    /// Invalidation notifications — removing or invalidating cached context data for NPCs.
    /// </summary>
    public interface IContextInvalidation
    {
        void RemoveL0CacheForNpc(string npcId);
        void InvalidateLayer(string npcId, ContextLayer layer);
        void InvalidateKey(string npcId, string key);
        void UpdateBaseline(string npcId);
        void InvalidateNpc(string npcId);
    }
}
