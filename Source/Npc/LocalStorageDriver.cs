using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Core.Client;
using RimMind.Core.Context;
using RimMind.Core.Internal;
using RimMind.Core.Prompt;
using Verse;

namespace RimMind.Core.Npc
{
    public class LocalStorageDriver : IStorageDriver
    {
        private readonly Dictionary<string, NpcProfile> _npcRegistry = new Dictionary<string, NpcProfile>();
        private readonly Dictionary<string, string> _kvStore = new Dictionary<string, string>();
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
                Priority = AIRequestPriority.Normal,
            };

            var tcs = new TaskCompletionSource<AIResponse>();
            var queue = AIRequestQueue.Instance;
            var client = RimMindAPI.GetClient();
            if (queue != null && client != null)
                queue.Enqueue(request, resp => tcs.SetResult(resp), client);
            else
                return new NpcChatResult { Error = "AI client not configured." };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            cts.Token.Register(() => tcs.TrySetResult(AIResponse.Failure(request.RequestId, "ChatAsync timed out (120s)")));
            var response = await tcs.Task;
            if (!response.Success)
                return new NpcChatResult { Error = response.Error };

            string content = response.Content ?? "";

            // 解析命令
            var commands = ParseCommands(content);

            // 提取 user 消息和 assistant 消息用于历史记录
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
                Budget = 0.6f,
                CurrentQuery = message,
                MaxTokens = 400,
                Temperature = 0.8f,
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
                Budget = 0.6f,
                CurrentQuery = message,
                MaxTokens = 400,
                Temperature = 0.8f,
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

        public Task<bool> PutAsync(string key, string value) { _kvStore[key] = value; return Task.FromResult(true); }
        public Task<string?> GetAsync(string key) { _kvStore.TryGetValue(key, out var v); return Task.FromResult(v); }
        public Task<bool> DeleteAsync(string key) { _kvStore.Remove(key); return Task.FromResult(true); }
        public Task<Dictionary<string, string>> GetBatchAsync(IEnumerable<string> keys)
        {
            var result = new Dictionary<string, string>();
            foreach (var k in keys)
                if (_kvStore.TryGetValue(k, out var v))
                    result[k] = v;
            return Task.FromResult(result);
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
