using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Core.Context;

namespace RimMind.Core.Npc
{
    public interface IStorageDriver
    {
        Task<bool> SpawnNpcAsync(NpcProfile profile);
        Task<bool> KillNpcAsync(string npcId);
        bool IsNpcAlive(string npcId);

        Task<NpcChatResult> ChatAsync(string npcId, string sender, string message, string? gameStateInfo = null, CancellationToken ct = default);
        Task<NpcChatResult> ChatAsync(ContextSnapshot snapshot, CancellationToken ct = default);
        Task<NpcChatResult> ChatStreamingAsync(string npcId, string sender, string message, Action<string>? onChunk, string? gameStateInfo = null, CancellationToken ct = default);
        Task<string> GetHistoryAsync(string npcId, int limit = 50);

        Task<bool> PutAsync(string key, string value);
        Task<string?> GetAsync(string key);
        Task<bool> DeleteAsync(string key);
        Task<Dictionary<string, string>> GetBatchAsync(IEnumerable<string> keys);

        Task<List<string>> QueryMemoriesAsync(string npcId, string query, int limit = 10);

        Task<bool> SaveAllEntriesAsync(string json);
        Task<string?> LoadAllEntriesAsync();

        bool IsRemote { get; }
        bool SupportsStreaming { get; }
        bool SupportsTts { get; }
        bool SupportsCommands { get; }
        bool SupportsStructuredOutput { get; }
    }
}
