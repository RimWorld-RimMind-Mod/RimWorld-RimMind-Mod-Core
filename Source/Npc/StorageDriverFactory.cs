using RimMind.Core.Client.Player2;
using RimMind.Core.Context;
using RimMind.Core.Settings;
using RimMind.Core;
using Verse;

namespace RimMind.Core.Npc
{
    public static class StorageDriverFactory
    {
        private static IStorageDriver? _cachedDriver;
        private static AIProvider _cachedProvider;

        public static IStorageDriver GetDriver()
        {
            var s = RimMindCoreMod.Settings;
            var historyManager = RimMindAPI.GetHistoryManager();
            if (s == null) return new LocalStorageDriver(historyManager);

            if (_cachedDriver != null && _cachedProvider == s.provider)
                return _cachedDriver;

            _cachedProvider = s.provider;

            if (s.provider == AIProvider.Player2)
            {
                var client = RimMindAPI.GetPlayer2Client();
                if (client != null && client.IsConfigured())
                {
                    _cachedDriver = new HybridStorageDriver(client, historyManager);
                    return _cachedDriver;
                }
                Log.Warning("[RimMind] Player2 client not available, falling back to LocalStorageDriver");
            }

            _cachedDriver = new LocalStorageDriver(historyManager);
            return _cachedDriver;
        }

        public static void InvalidateCache()
        {
            _cachedDriver = null;
            _cachedProvider = default;
        }
    }
}
