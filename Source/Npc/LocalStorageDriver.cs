using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Core.Client;
using RimMind.Core.Context;
using RimMind.Core.Prompt;
using RimMind.Core.Settings;
using Verse;

namespace RimMind.Core.Npc
{
    public class LocalStorageDriver : IStorageDriver
    {
        private readonly Dictionary<string, NpcProfile> _npcRegistry = new Dictionary<string, NpcProfile>();
        internal static readonly Dictionary<string, string> KvStore = new Dictionary<string, string>();
        private readonly HistoryManager _historyManager;

        public bool IsRemote => false;
        public bool SupportsStreaming => true;
        public bool SupportsTts => false;
        public bool SupportsCommands => true;
        public bool SupportsStructuredOutput => true;

        public LocalStorageDriver(HistoryManager historyManager)
        {
            _historyManager = historyManager;
        }

        public Task<bool> SpawnNpcAsync(NpcProfile profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.NpcId)) return Task.FromResult(false);
            _npcRegistry[profile.NpcId] = profile;
            return Task.FromResult(true);
        }

        public Task<bool> KillNpcAsync(string npcId)
        {
            _npcRegistry.Remove(npcId);
            _historyManager.ClearHistory(npcId);
            return Task.FromResult(true);
        }

        public bool IsNpcAlive(string npcId) => _npcRegistry.ContainsKey(npcId);

        public async Task<NpcChatResult> ChatAsync(ContextSnapshot snapshot, CancellationToken ct = default)
        {
            if (!_npcRegistry.TryGetValue(snapshot.NpcId, out var profile))
                return new NpcChatResult { Error = $"NPC {snapshot.NpcId} not found" };

            var request = new AIRequest
            {
                Messages = snapshot.Messages,
                MaxTokens = snapshot.MaxTokens,
                Temperature = snapshot.Temperature,
                RequestId = $"NpcChat_{snapshot.NpcId}_{Find.TickManager.TicksGame}",
                ModId = "NpcChat",
                ExpireAtTicks = Find.TickManager.TicksGame + 30000,
                UseJsonMode = true,
                Priority = AIRequestPriority.Normal,
            };

            var client = RimMindAPI.GetClient();
            if (client == null)
                return new NpcChatResult { Error = "AI client not configured." };

            AIResponse response;
            try
            {
                response = await client.SendAsync(request);
            }
            catch (Exception ex)
            {
                return new NpcChatResult { Error = $"AI request failed: {ex.Message}" };
            }

            if (!response.Success)
                return new NpcChatResult { Error = response.Error };

            string content = response.Content ?? "";

            content = ExtractMessageFromJson(content);

            var commands = ParseCommands(content);

            string? userMessage = snapshot.CurrentQuery;
            _historyManager.AddTurn(snapshot.NpcId, userMessage ?? "", content, snapshot.Scenario);
            _historyManager.CompressIfNeeded(snapshot.NpcId);

            return new NpcChatResult { Message = content, Commands = commands };
        }

        public async Task<NpcChatResult> ChatAsync(string npcId, string sender, string message, string? gameStateInfo = null, CancellationToken ct = default)
        {
            var request = new ContextRequest
            {
                NpcId = npcId,
                Scenario = ScenarioIds.Dialogue,
                Budget = RimMindCoreMod.Settings?.Context?.ContextBudget ?? 0.6f,
                CurrentQuery = message,
                MaxTokens = RimMindCoreMod.Settings?.maxTokens ?? 400,
                Temperature = RimMindCoreMod.Settings?.defaultTemperature ?? 0.7f,
            };
            var engine = RimMindAPI.GetContextEngine();
            var snapshot = engine.BuildSnapshot(request);
            return await ChatAsync(snapshot, ct);
        }

        public async Task<NpcChatResult> ChatStreamingAsync(string npcId, string sender, string message, Action<string>? onChunk, string? gameStateInfo = null, CancellationToken ct = default)
        {
            var request = new ContextRequest
            {
                NpcId = npcId,
                Scenario = ScenarioIds.Dialogue,
                Budget = RimMindCoreMod.Settings?.Context?.ContextBudget ?? 0.6f,
                CurrentQuery = message,
                MaxTokens = RimMindCoreMod.Settings?.maxTokens ?? 400,
                Temperature = RimMindCoreMod.Settings?.defaultTemperature ?? 0.7f,
            };
            var engine = RimMindAPI.GetContextEngine();
            var snapshot = engine.BuildSnapshot(request);
            var result = await ChatAsync(snapshot, ct);
            if (result.Message != null)
                onChunk?.Invoke(result.Message);
            return result;
        }

        public Task<string> GetHistoryAsync(string npcId, int limit = 50)
        {
            var history = _historyManager.GetHistory(npcId, limit);
            if (history == null || history.Count == 0)
                return Task.FromResult("");

            var sb = new System.Text.StringBuilder();
            foreach (var (role, content) in history)
                sb.AppendLine($"[{role}] {content}");
            return Task.FromResult(sb.ToString().TrimEnd());
        }

        public Task<bool> PutAsync(string key, string value) { KvStore[key] = value; return Task.FromResult(true); }
        public Task<string?> GetAsync(string key) { KvStore.TryGetValue(key, out var v); return Task.FromResult<string?>(v); }
        public Task<bool> DeleteAsync(string key) { KvStore.Remove(key); return Task.FromResult(true); }
        public Task<Dictionary<string, string>> GetBatchAsync(IEnumerable<string> keys)
        {
            var result = new Dictionary<string, string>();
            foreach (var k in keys)
                if (KvStore.TryGetValue(k, out var v))
                    result[k] = v;
            return Task.FromResult(result);
        }

        private const string AllEntriesKey = "rimmind:all_memory_entries";

        public Task<bool> SaveAllEntriesAsync(string json)
        {
            KvStore[AllEntriesKey] = json ?? string.Empty;
            return Task.FromResult(true);
        }

        public Task<string?> LoadAllEntriesAsync()
        {
            KvStore.TryGetValue(AllEntriesKey, out var json);
            return Task.FromResult<string?>(json);
        }

        public Task<List<string>> QueryMemoriesAsync(string npcId, string query, int limit = 10)
        {
            var results = new List<string>();
            var history = _historyManager.GetHistory(npcId, 20);
            if (history == null || history.Count == 0)
                return Task.FromResult(results);

            var queryLower = query?.ToLowerInvariant() ?? "";
            foreach (var (role, content) in history)
            {
                if (results.Count >= limit) break;
                if (string.IsNullOrEmpty(content)) continue;
                if (!string.IsNullOrEmpty(queryLower) && content.ToLowerInvariant().Contains(queryLower))
                    results.Add($"[{role}] {content}");
            }
            return Task.FromResult(results);
        }

        /// <summary>
        /// 解析响应中的命令格式 [CMD:action_name:params] 或 [CMD:action_name]
        /// </summary>
        private static string ExtractMessageFromJson(string content)
        {
            if (string.IsNullOrEmpty(content) || !content.TrimStart().StartsWith("{")) return content;
            try
            {
                var obj = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, object>>(content);
                if (obj != null && obj.TryGetValue("message", out var msg) && msg != null)
                {
                    string? extracted = msg.ToString();
                    if (!string.IsNullOrEmpty(extracted)) return extracted;
                }
            }
            catch (Exception ex) { Log.Warning($"[RimMind] Failed to extract message from JSON: {ex.Message}"); }
            return content;
        }

        private static List<NpcCommandResult> ParseCommands(string content)
        {
            var commands = new List<NpcCommandResult>();
            if (string.IsNullOrEmpty(content)) return commands;

            var regex = new Regex(@"\[CMD:(\w+)(?::([^\]]+))?\]");
            var matches = regex.Matches(content);
            foreach (Match match in matches)
            {
                commands.Add(new NpcCommandResult
                {
                    Name = match.Groups[1].Value,
                    Arguments = match.Groups[2].Success ? match.Groups[2].Value : "",
                });
            }
            return commands;
        }
    }
}
