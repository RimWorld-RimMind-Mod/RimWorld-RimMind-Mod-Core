using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RimMind.Core.Client.Player2;
using RimMind.Core.Context;
using RimMind.Core.Internal;
using RimMind.Core.Settings;
using Verse;

namespace RimMind.Core.Npc
{
    public class Player2StorageDriver : IStorageDriver
    {
        private readonly Player2Client _client;
        private readonly string _gameId;

        private readonly List<LocalMemoryEntry> _localMemoryIndex = new List<LocalMemoryEntry>();
        private readonly object _indexLock = new object();

        private struct LocalMemoryEntry
        {
            public string Key;
            public string Value;
        }

        public bool AutoDispatch { get; set; } = false;

        public bool IsRemote => true;
        public bool SupportsStreaming => true;
        public bool SupportsTts => true;
        public bool SupportsCommands => true;
        public bool SupportsStructuredOutput => true;

        public Player2StorageDriver(Player2Client client)
        {
            _client = client;
            _gameId = Player2Client.GameClientId;
        }

        public async Task<bool> SpawnNpcAsync(NpcProfile profile)
        {
            if (profile == null) return false;
            try
            {
                var body = new
                {
                    npc_id = profile.NpcId,
                    name = profile.Name,
                    short_name = profile.ShortName,
                    character_description = profile.CharacterDescription,
                    system_prompt = profile.SystemPrompt,
                    commands = ConvertCommands(profile.Commands),
                    tts = profile.TtsConfig != null ? new
                    {
                        voice_ids = profile.TtsConfig.VoiceIds,
                        speed = profile.TtsConfig.Speed,
                        audio_format = profile.TtsConfig.AudioFormat,
                    } : null,
                };
                string json = JsonConvert.SerializeObject(body, Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                var response = await _client.SendRawAsync("/npcs/spawn", json);
                return response.Success;
            }
            catch (Exception ex) { Log.Warning($"[RimMind] Player2StorageDriver.SpawnNpcAsync failed: {ex.Message}"); return false; }
        }

        public async Task<bool> KillNpcAsync(string npcId)
        {
            try
            {
                var response = await _client.SendRawAsync($"/npcs/{npcId}/kill", "{}");
                return response.Success;
            }
            catch (Exception ex) { Log.Warning($"[RimMind] Player2StorageDriver.KillNpcAsync failed: {ex.Message}"); return false; }
        }

        public bool IsNpcAlive(string npcId)
        {
            return NpcManager.Instance?.IsNpcAlive(npcId) == true;
        }

        public async Task<NpcChatResult> ChatAsync(ContextSnapshot snapshot, CancellationToken ct = default)
        {
            try
            {
                if (_client == null || !_client.IsConfigured())
                    return new NpcChatResult { Error = "Player2 client not configured." };

                var body = new
                {
                    npc_id = snapshot.NpcId,
                    scenario = snapshot.Scenario,
                    messages = snapshot.Messages,
                    max_tokens = snapshot.MaxTokens,
                    temperature = snapshot.Temperature,
                    current_query = snapshot.CurrentQuery,
                };
                string json = JsonConvert.SerializeObject(body, Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                var response = await _client.SendRawAsync($"/npcs/{snapshot.NpcId}/chat", json);

                if (!response.Success)
                    return new NpcChatResult { Error = response.Error };

                var result = JsonConvert.DeserializeObject<NpcChatResult>(response.Content!);
                result ??= new NpcChatResult { Message = response.Content ?? "" };
                MaybeDispatch(result, snapshot.NpcId);
                return result;
            }
            catch (System.Exception ex)
            {
                AIRequestQueue.LogFromBackground($"[RimMind] Player2StorageDriver.ChatAsync failed for '{snapshot.NpcId}': {ex.Message}", isWarning: true);
                return new NpcChatResult { Error = ex.Message };
            }
        }

        public async Task<NpcChatResult> ChatAsync(string npcId, string sender, string message, string? gameStateInfo = null, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrEmpty(gameStateInfo))
                {
                    var engine = RimMindAPI.GetContextEngine();
                    if (engine != null)
                    {
                        var request = new ContextRequest
                        {
                            NpcId = npcId,
                            Scenario = ScenarioIds.Dialogue,
                            Budget = RimMindCoreMod.Settings?.Context?.ContextBudget ?? 0.6f,
                            CurrentQuery = message,
                            MaxTokens = RimMindCoreMod.Settings?.maxTokens ?? 400,
                            Temperature = RimMindCoreMod.Settings?.defaultTemperature ?? 0.7f,
                            Map = Find.CurrentMap,
                        };
                        var snapshot = engine.BuildSnapshot(request);
                        var sb = new StringBuilder();
                        foreach (var msg in snapshot.Messages)
                        {
                            if (msg.Role == "system" && !string.IsNullOrEmpty(msg.Content))
                                sb.AppendLine(msg.Content);
                        }
                        gameStateInfo = sb.ToString().TrimEnd();
                    }
                    else
                    {
                        gameStateInfo = GameContextBuilder.CollectBasicGameState(npcId);
                    }
                }

                var body = new
                {
                    sender_name = sender,
                    message = message,
                    game_state_info = gameStateInfo,
                };
                string json = JsonConvert.SerializeObject(body, Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                var response = await _client.SendRawAsync($"/npcs/{npcId}/chat", json);

                if (!response.Success)
                    return new NpcChatResult { Error = response.Error };

                var result = JsonConvert.DeserializeObject<NpcChatResult>(response.Content!);
                result ??= new NpcChatResult { Message = response.Content ?? "" };
                MaybeDispatch(result, npcId);
                return result;
            }
            catch (System.Exception ex)
            {
                return new NpcChatResult { Error = ex.Message };
            }
        }

        public async Task<NpcChatResult> ChatStreamingAsync(string npcId, string sender, string message, Action<string>? onChunk, string? gameStateInfo = null, CancellationToken ct = default)
        {
            // 兼容旧接口：构建 ContextSnapshot 委托到新 ChatAsync
            var snapshot = new ContextSnapshot
            {
                NpcId = npcId,
                Scenario = ScenarioIds.Dialogue,
                CurrentQuery = gameStateInfo != null ? $"{message}\n\n[Game State]\n{gameStateInfo}" : message,
                MaxTokens = RimMindCoreMod.Settings?.maxTokens ?? 400,
                Temperature = RimMindCoreMod.Settings?.defaultTemperature ?? 0.7f,
            };
            snapshot.Messages.Add(new Client.ChatMessage { Role = "user", Content = snapshot.CurrentQuery });

            return await ChatAsync(snapshot, ct);
        }

        public async Task<string> GetHistoryAsync(string npcId, int limit = 50)
        {
            try
            {
                var response = await _client.GetRawAsync($"/npcs/{npcId}/history?limit={limit}");
                return response.Success ? response.Content ?? "" : "";
            }
            catch (Exception ex) { Log.Warning($"[RimMind] Player2StorageDriver.GetHistoryAsync failed: {ex.Message}"); return ""; }
        }

        public async Task<bool> PutAsync(string key, string value)
        {
            lock (_indexLock)
            {
                int idx = _localMemoryIndex.FindIndex(e => e.Key == key);
                if (idx >= 0)
                    _localMemoryIndex[idx] = new LocalMemoryEntry { Key = key, Value = value };
                else
                    _localMemoryIndex.Add(new LocalMemoryEntry { Key = key, Value = value });
            }

            try
            {
                var body = new { value = value };
                string json = JsonConvert.SerializeObject(body);
                var response = await _client.SendRawAsync($"/games/{_gameId}/data/user/{key}", json);
                return response.Success;
            }
            catch (Exception ex) { Log.Warning($"[RimMind] Player2StorageDriver.PutAsync failed: {ex.Message}"); return false; }
        }

        public async Task<string?> GetAsync(string key)
        {
            try
            {
                var response = await _client.GetRawAsync($"/games/{_gameId}/data/user/{key}");
                return response.Success ? response.Content : null;
            }
            catch (Exception ex) { Log.Warning($"[RimMind] Player2StorageDriver.GetAsync failed: {ex.Message}"); return null; }
        }

        public async Task<bool> DeleteAsync(string key)
        {
            try
            {
                var response = await _client.DeleteRawAsync($"/games/{_gameId}/data/user/{key}");
                return response.Success;
            }
            catch (Exception ex) { Log.Warning($"[RimMind] Player2StorageDriver.DeleteAsync failed: {ex.Message}"); return false; }
        }

        public async Task<Dictionary<string, string>> GetBatchAsync(IEnumerable<string> keys)
        {
            try
            {
                var body = new { keys = keys };
                string json = JsonConvert.SerializeObject(body);
                var response = await _client.SendRawAsync($"/games/{_gameId}/data/user/batch", json);
                if (!response.Success) return new Dictionary<string, string>();
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content!)
                    ?? new Dictionary<string, string>();
            }
            catch (Exception ex) { Log.Warning($"[RimMind] Player2StorageDriver.GetBatchAsync failed: {ex.Message}"); return new Dictionary<string, string>(); }
        }

        public Task<bool> SaveAllEntriesAsync(string json)
        {
            return PutAsync("rimmind:all_memory_entries", json ?? string.Empty);
        }

        public Task<string?> LoadAllEntriesAsync()
        {
            return GetAsync("rimmind:all_memory_entries");
        }

        public Task<List<string>> QueryMemoriesAsync(string npcId, string query, int limit = 10)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(query)) return Task.FromResult(results);

            var tokens = query.ToLowerInvariant().Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return Task.FromResult(results);

            var scored = new List<(string value, int score)>();

            lock (_indexLock)
            {
                foreach (var entry in _localMemoryIndex)
                {
                    if (string.IsNullOrEmpty(entry.Value)) continue;
                    int score = 0;
                    var lowerValue = entry.Value.ToLowerInvariant();
                    foreach (var token in tokens)
                    {
                        if (lowerValue.Contains(token))
                            score++;
                    }
                    if (score > 0)
                        scored.Add((entry.Value, score));
                }
            }

            results = scored
                .OrderByDescending(s => s.score)
                .Take(limit)
                .Select(s => s.value)
                .ToList();

            return Task.FromResult(results);
        }

        private static List<object> ConvertCommands(List<NpcCommand> commands)
        {
            var result = new List<object>();
            if (commands == null) return result;
            foreach (var cmd in commands)
            {
                result.Add(new
                {
                    name = cmd.Name,
                    description = cmd.Description,
                    parameters = cmd.Parameters != null ? JsonConvert.DeserializeObject(cmd.Parameters) : null,
                    never_respond_with_message = cmd.NeverRespondWithMessage,
                });
            }
            return result;
        }

        private void MaybeDispatch(NpcChatResult result, string npcId)
        {
            if (!AutoDispatch) return;
            try
            {
                ResponseDispatcher.Dispatch(result);
            }
            catch (System.Exception ex)
            {
                AIRequestQueue.LogFromBackground($"[RimMind] Player2StorageDriver: auto-dispatch failed for '{npcId}' - {ex.Message}", isWarning: true);
            }
        }
    }
}
