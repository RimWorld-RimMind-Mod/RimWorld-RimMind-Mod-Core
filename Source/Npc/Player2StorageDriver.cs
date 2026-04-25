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
            catch { return false; }
        }

        public async Task<bool> KillNpcAsync(string npcId)
        {
            try
            {
                var response = await _client.SendRawAsync($"/npcs/{npcId}/kill", "{}");
                return response.Success;
            }
            catch { return false; }
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
                Log.Warning($"[RimMind] Player2StorageDriver.ChatAsync failed for '{snapshot.NpcId}': {ex.Message}");
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
                            Budget = 0.6f,
                            CurrentQuery = message,
                            MaxTokens = 400,
                            Temperature = 0.8f,
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
                MaxTokens = 400,
                Temperature = 0.8f,
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
            catch { return ""; }
        }

        public async Task<bool> PutAsync(string key, string value)
        {
            try
            {
                var body = new { value = value };
                string json = JsonConvert.SerializeObject(body);
                var response = await _client.SendRawAsync($"/games/{_gameId}/data/user/{key}", json);
                return response.Success;
            }
            catch { return false; }
        }

        public async Task<string?> GetAsync(string key)
        {
            try
            {
                var response = await _client.GetRawAsync($"/games/{_gameId}/data/user/{key}");
                return response.Success ? response.Content : null;
            }
            catch { return null; }
        }

        public async Task<bool> DeleteAsync(string key)
        {
            try
            {
                var response = await _client.DeleteRawAsync($"/games/{_gameId}/data/user/{key}");
                return response.Success;
            }
            catch { return false; }
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
            catch { return new Dictionary<string, string>(); }
        }

        public Task<List<string>> QueryMemoriesAsync(string npcId, string query, int limit = 10)
        {
            return Task.FromResult(new List<string>());
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
                Log.Warning($"[RimMind] Player2StorageDriver: auto-dispatch failed for '{npcId}' - {ex.Message}");
            }
        }
    }
}
