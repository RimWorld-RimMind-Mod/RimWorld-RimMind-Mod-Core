using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Core.Client.Player2;
using RimMind.Core.Context;
using Verse;

namespace RimMind.Core.Npc
{
    public class HybridStorageDriver : IStorageDriver
    {
        private readonly LocalStorageDriver _local;
        private readonly Player2StorageDriver _remote;

        public bool IsRemote => true;
        public bool SupportsStreaming => true;
        public bool SupportsTts => true;
        public bool SupportsCommands => true;
        public bool SupportsStructuredOutput => true;

        public HybridStorageDriver(Player2Client client, HistoryManager historyManager)
        {
            _local = new LocalStorageDriver(historyManager);
            _remote = new Player2StorageDriver(client);
        }

        public async Task<bool> SpawnNpcAsync(NpcProfile profile)
        {
            var localResult = await _local.SpawnNpcAsync(profile);
            try { await _remote.SpawnNpcAsync(profile); }
            catch (Exception ex) { Log.Warning($"[RimMind] HybridDriver: remote SpawnNpc failed: {ex.Message}"); }
            return localResult;
        }

        public async Task<bool> KillNpcAsync(string npcId)
        {
            var localResult = await _local.KillNpcAsync(npcId);
            try { await _remote.KillNpcAsync(npcId); }
            catch (Exception ex) { Log.Warning($"[RimMind] HybridDriver: remote KillNpc failed: {ex.Message}"); }
            return localResult;
        }

        public bool IsNpcAlive(string npcId)
        {
            return _local.IsNpcAlive(npcId) || _remote.IsNpcAlive(npcId);
        }

        public async Task<NpcChatResult> ChatAsync(ContextSnapshot snapshot, CancellationToken ct = default)
        {
            try
            {
                return await _remote.ChatAsync(snapshot, ct);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind] HybridDriver: remote ChatAsync failed, falling back to local: {ex.Message}");
                return await _local.ChatAsync(snapshot, ct);
            }
        }

        public async Task<NpcChatResult> ChatAsync(string npcId, string sender, string message, string? gameStateInfo = null, CancellationToken ct = default)
        {
            try
            {
                return await _remote.ChatAsync(npcId, sender, message, gameStateInfo, ct);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind] HybridDriver: remote ChatAsync(legacy) failed, falling back to local: {ex.Message}");
                return await _local.ChatAsync(npcId, sender, message, gameStateInfo, ct);
            }
        }

        public async Task<NpcChatResult> ChatStreamingAsync(string npcId, string sender, string message, Action<string>? onChunk, string? gameStateInfo = null, CancellationToken ct = default)
        {
            try
            {
                return await _remote.ChatStreamingAsync(npcId, sender, message, onChunk, gameStateInfo, ct);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind] HybridDriver: remote ChatStreamingAsync failed, falling back to local: {ex.Message}");
                return await _local.ChatStreamingAsync(npcId, sender, message, onChunk, gameStateInfo, ct);
            }
        }

        public async Task<string> GetHistoryAsync(string npcId, int limit = 50)
        {
            var local = await _local.GetHistoryAsync(npcId, limit);
            if (!string.IsNullOrEmpty(local)) return local;
            try { return await _remote.GetHistoryAsync(npcId, limit); }
            catch (Exception ex) { Log.Warning($"[RimMind] HybridDriver: remote GetHistory failed: {ex.Message}"); return local; }
        }

        public async Task<bool> PutAsync(string key, string value)
        {
            var localResult = await _local.PutAsync(key, value);
            try { await _remote.PutAsync(key, value); }
            catch (Exception ex) { Log.Warning($"[RimMind] HybridDriver: remote Put failed: {ex.Message}"); }
            return localResult;
        }

        public async Task<string?> GetAsync(string key)
        {
            var local = await _local.GetAsync(key);
            if (local != null) return local;
            try { return await _remote.GetAsync(key); }
            catch (Exception ex) { Log.Warning($"[RimMind] HybridDriver: remote Get failed: {ex.Message}"); return null; }
        }

        public async Task<bool> DeleteAsync(string key)
        {
            var localResult = await _local.DeleteAsync(key);
            try { await _remote.DeleteAsync(key); }
            catch (Exception ex) { Log.Warning($"[RimMind] HybridDriver: remote Delete failed: {ex.Message}"); }
            return localResult;
        }

        public async Task<Dictionary<string, string>> GetBatchAsync(IEnumerable<string> keys)
        {
            var local = await _local.GetBatchAsync(keys);
            if (local != null && local.Count > 0) return local;
            try { return await _remote.GetBatchAsync(keys); }
            catch (Exception ex) { Log.Warning($"[RimMind] HybridDriver: remote GetBatch failed: {ex.Message}"); return local!; }
        }

        public async Task<List<string>> QueryMemoriesAsync(string npcId, string query, int limit = 10)
        {
            var local = await _local.QueryMemoriesAsync(npcId, query, limit);
            if (local != null && local.Count > 0) return local;
            try { return await _remote.QueryMemoriesAsync(npcId, query, limit); }
            catch (Exception ex) { Log.Warning($"[RimMind] HybridDriver: remote QueryMemories failed: {ex.Message}"); return local!; }
        }
    }
}
