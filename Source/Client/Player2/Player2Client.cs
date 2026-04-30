using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using RimMind.Core.Internal;
using RimMind.Core.Settings;
using Newtonsoft.Json;
using RimWorld;
using UnityEngine.Networking;
using Verse;

namespace RimMind.Core.Client.Player2
{
    public partial class Player2Client : IAIClient
    {
        public const string GameClientId = "019a8368-b00b-72bc-b367-2825079dc6fb";
        private const string LocalUrl = "http://localhost:4315";

        private readonly string _apiKey;
        private readonly bool _isLocalConnection;
        private readonly RimMindCoreSettings _settings;

        private static DateTime _lastHealthCheck = DateTime.MinValue;
        private static bool _healthCheckActive;

        private string RemoteUrl => string.IsNullOrWhiteSpace(_settings.player2RemoteUrl)
            ? "https://api.player2.game"
            : _settings.player2RemoteUrl.Trim().TrimEnd('/');

        private string CurrentApiUrl => _isLocalConnection ? LocalUrl : RemoteUrl;

        private Player2Client(string apiKey, bool isLocal, RimMindCoreSettings settings)
        {
            _apiKey = apiKey;
            _isLocalConnection = isLocal;
            _settings = settings;

            if (!_healthCheckActive && !string.IsNullOrEmpty(apiKey) && !isLocal)
            {
                _healthCheckActive = true;
                StartHealthCheckLoop();
            }
        }

        public static async Task<Player2Client> CreateAsync(RimMindCoreSettings settings)
        {
            try
            {
                string? localKey = await TryGetLocalPlayer2Key();
                if (!string.IsNullOrEmpty(localKey))
                {
                    AIRequestQueue.LogFromBackground("[RimMind] Player2 local app detected.");
                    ShowNotification("RimMind.Core.Player2.LocalDetected");
                    return new Player2Client(localKey!, isLocal: true, settings);
                }

                if (!string.IsNullOrEmpty(settings.apiKey))
                {
                    AIRequestQueue.LogFromBackground("[RimMind] Using manual Player2 API key.");
                    return new Player2Client(settings.apiKey, isLocal: false, settings);
                }

                ShowNotification("RimMind.Core.Player2.LocalNotFound");
                return new Player2Client(string.Empty, isLocal: false, settings);
            }
            catch (Exception ex)
            {
                AIRequestQueue.LogFromBackground($"[RimMind] Failed to create Player2 client: {ex.Message}", isWarning: true);
                return new Player2Client(string.Empty, isLocal: false, settings);
            }
        }

        public bool IsConfigured() => !string.IsNullOrEmpty(_apiKey);

        public bool IsLocalEndpoint => _isLocalConnection;

        public async Task<AIResponse> SendAsync(AIRequest request)
        {
            string endpoint = $"{CurrentApiUrl}/v1/chat/completions";
            string json = BuildRequestJson(request);

            if (_settings.debugLogging)
                AIRequestQueue.LogFromBackground($"[RimMind] → {request.RequestId} (Player2)\n{json}");

            var sw = Stopwatch.StartNew();
            try
            {
                await EnsureHealthCheck();

                (string responseText, long httpStatusCode) = await PostAsync(endpoint, json);
                var parsed = JsonConvert.DeserializeObject<Player2ResponseDto>(responseText);
                string content = parsed?.Choices?[0]?.Message?.Content ?? string.Empty;
                int tokens = parsed?.Usage?.TotalTokens ?? 0;
                int promptTokens = parsed?.Usage?.PromptTokens ?? 0;
                int completionTokens = parsed?.Usage?.CompletionTokens ?? 0;
                int cachedTokens = parsed?.Usage?.PromptTokensDetails?.CachedTokens ?? 0;
                sw.Stop();

                if (_settings.debugLogging)
                    AIRequestQueue.LogFromBackground($"[RimMind] ← {request.RequestId} ({tokens} tok)\n{content}");

                var response = AIResponse.Ok(request.RequestId, content, tokens);
                response.PromptTokens = promptTokens;
                response.CompletionTokens = completionTokens;
                response.CachedTokens = cachedTokens;
                response.ProcessingMs = sw.ElapsedMilliseconds;
                response.HttpStatusCode = httpStatusCode;
                response.RequestPayloadBytes = Encoding.UTF8.GetByteCount(json);
                response.Priority = request.Priority;
                AIDebugLog.Record(request, response, (int)sw.ElapsedMilliseconds);
                return response;
            }
            catch (Exception ex)
            {
                sw.Stop();
                AIRequestQueue.LogFromBackground($"[RimMind] Player2 request failed ({request.RequestId}): {ex.Message}", isWarning: true);
                var response = AIResponse.Failure(request.RequestId, ex.Message);
                response.ProcessingMs = sw.ElapsedMilliseconds;
                response.RequestPayloadBytes = Encoding.UTF8.GetByteCount(json);
                response.Priority = request.Priority;
                AIDebugLog.Record(request, response, (int)sw.ElapsedMilliseconds);
                return response;
            }
        }

        private string BuildRequestJson(AIRequest request)
        {
            var messages = new List<Player2MessageDto>();

            if (request.Messages != null && request.Messages.Count > 0)
            {
                messages = request.Messages
                    .Select(m => new Player2MessageDto { Role = m.Role, Content = m.Content })
                    .ToList();
            }
            else
            {
                if (!string.IsNullOrEmpty(request.SystemPrompt))
                    messages.Add(new Player2MessageDto { Role = "system", Content = request.SystemPrompt });
                messages.Add(new Player2MessageDto { Role = "user", Content = request.UserPrompt });
            }

            messages = MergeConsecutiveSameRole(messages);

            var body = new Player2RequestDto
            {
                Model = "default",
                Messages = messages,
                MaxTokens = request.MaxTokens > 0 ? request.MaxTokens : _settings.maxTokens,
                Temperature = request.Temperature,
                Stream = false,
            };

            return JsonConvert.SerializeObject(body, Formatting.None,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        private static List<Player2MessageDto> MergeConsecutiveSameRole(List<Player2MessageDto> messages)
        {
            if (messages == null || messages.Count <= 1) return messages!;

            var merged = new List<Player2MessageDto>(messages.Count);
            var current = messages[0];

            for (int i = 1; i < messages.Count; i++)
            {
                if (string.Equals(current.Role, messages[i].Role, StringComparison.OrdinalIgnoreCase))
                {
                    current = new Player2MessageDto
                    {
                        Role = current.Role,
                        Content = current.Content + "\n" + messages[i].Content
                    };
                }
                else
                {
                    merged.Add(current);
                    current = messages[i];
                }
            }
            merged.Add(current);

            return merged;
        }

        private async Task<(string text, long statusCode)> PostAsync(string url, string jsonBody)
        {
            bool isLocal = _isLocalConnection;
            float connectTimeout = isLocal ? 300f : 60f;
            float readTimeout = 60f;

            using var webRequest = new UnityWebRequest(url, "POST");
            webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
            webRequest.SetRequestHeader("player2-game-key", GameClientId);

            var asyncOp = webRequest.SendWebRequest();

            float inactivity = 0f;
            ulong lastBytes = 0;

            while (!asyncOp.isDone)
            {
                if (Current.Game == null)
                    throw new OperationCanceledException("Game unloaded during AI request.");

                await Task.Delay(100);
                ulong currentBytes = webRequest.downloadedBytes;

                if (currentBytes != lastBytes) { inactivity = 0f; lastBytes = currentBytes; }
                else inactivity += 0.1f;

                if (currentBytes == 0 && inactivity > connectTimeout)
                {
                    webRequest.Abort();
                    throw new TimeoutException($"Connection timeout after {connectTimeout}s");
                }
                if (currentBytes > 0 && inactivity > readTimeout)
                {
                    webRequest.Abort();
                    throw new TimeoutException($"Read timeout after {readTimeout}s");
                }
            }

            if (webRequest.result == UnityWebRequest.Result.ConnectionError ||
                webRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                string body = webRequest.downloadHandler.text;
                string unityErr = webRequest.error ?? "";
                string detail = body.Length > 0 ? body : unityErr;
                throw new Exception($"HTTP {webRequest.responseCode}: {detail}");
            }

            return (webRequest.downloadHandler.text, webRequest.responseCode);
        }

        private static async Task<string?> TryGetLocalPlayer2Key()
        {
            try
            {
                using (var healthRequest = UnityWebRequest.Get($"{LocalUrl}/v1/health"))
                {
                    healthRequest.timeout = 2;
                    var healthOp = healthRequest.SendWebRequest();
                    while (!healthOp.isDone)
                    {
                        if (Current.Game == null) return null;
                        await Task.Delay(50);
                    }
                    if (healthRequest.result != UnityWebRequest.Result.Success)
                        return null!;
                }

                using (var loginRequest = new UnityWebRequest($"{LocalUrl}/v1/login/web/{GameClientId}", "POST"))
                {
                    loginRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
                    loginRequest.downloadHandler = new DownloadHandlerBuffer();
                    loginRequest.SetRequestHeader("Content-Type", "application/json");
                    loginRequest.timeout = 3;

                    var loginOp = loginRequest.SendWebRequest();
                    while (!loginOp.isDone)
                    {
                        if (Current.Game == null) return null;
                        await Task.Delay(50);
                    }
                    if (loginRequest.result != UnityWebRequest.Result.Success)
                        return null!;

                    var response = JsonConvert.DeserializeObject<Player2LocalLoginResponse>(
                        loginRequest.downloadHandler.text);
                    if (response != null && !string.IsNullOrEmpty(response.P2Key))
                    {
                        AIRequestQueue.LogFromBackground("[RimMind] Player2 local app authenticated successfully.");
                        return response.P2Key;
                    }
                    return null!;
                }
            }
            catch (Exception ex)
            {
                AIRequestQueue.LogFromBackground($"[RimMind] Local Player2 detection failed: {ex.Message}");
                return null!;
            }
        }

        private static void ShowNotification(string messageKey)
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    string text = messageKey.Translate();
                    Messages.Message(text,
                        messageKey.Contains("LocalDetected")
                            ? MessageTypeDefOf.PositiveEvent
                            : MessageTypeDefOf.CautionInput);
                }
                catch (Exception ex) { Log.Warning($"[RimMind] Failed to show notification: {ex.Message}"); }
            });
        }

        private async Task StartHealthCheckLoopAsync()
        {
            try
            {
                while (_healthCheckActive && Current.Game != null)
                {
                    await Task.Delay(60000);
                    if (_healthCheckActive) await EnsureHealthCheck(force: true);
                }
            }
            catch (Exception ex)
            {
                AIRequestQueue.LogFromBackground($"[RimMind] Player2 health check loop crashed: {ex.Message}", isWarning: true);
                _healthCheckActive = false;
            }
        }

        private void StartHealthCheckLoop()
        {
            _ = StartHealthCheckLoopAsync();
        }

        private async Task EnsureHealthCheck(bool force = false)
        {
            if (_isLocalConnection || string.IsNullOrEmpty(_apiKey)) return;
            if (!force && (DateTime.Now - _lastHealthCheck).TotalSeconds < 60) return;

            try
            {
                using var webRequest = UnityWebRequest.Get($"{CurrentApiUrl}/v1/health");
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                webRequest.SetRequestHeader("player2-game-key", GameClientId);

                var asyncOp = webRequest.SendWebRequest();
                while (!asyncOp.isDone)
                {
                    if (Current.Game == null) return;
                    await Task.Delay(100);
                }

                _lastHealthCheck = DateTime.Now;
                if (webRequest.responseCode != 200)
                    AIRequestQueue.LogFromBackground(
                        $"[RimMind] Player2 health check failed: {webRequest.responseCode}", isWarning: true);
            }
            catch (Exception ex)
            {
                AIRequestQueue.LogFromBackground(
                    $"[RimMind] Player2 health check exception: {ex.Message}", isWarning: true);
            }
        }

        public static void StopHealthCheck() => _healthCheckActive = false;

        public static void CheckPlayer2StatusAndNotify()
        {
            Task.Run(async () =>
            {
                bool isAvailable = await IsPlayer2LocalAppAvailableAsync();
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    Messages.Message(
                        isAvailable
                            ? "RimMind.Core.Player2.LocalDetected".Translate()
                            : "RimMind.Core.Player2.LocalNotFound".Translate(),
                        isAvailable ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.CautionInput);
                });
            });
        }

        private static async Task<bool> IsPlayer2LocalAppAvailableAsync()
        {
            try
            {
                using var webRequest = UnityWebRequest.Get($"{LocalUrl}/v1/health");
                webRequest.timeout = 2;
                var asyncOp = webRequest.SendWebRequest();
                while (!asyncOp.isDone)
                {
                    if (Current.Game == null) return false;
                    await Task.Delay(50);
                }
                return webRequest.responseCode == 200;
            }
            catch (Exception ex) { Log.Warning($"[RimMind] Player2 local availability check failed: {ex.Message}"); return false; }
        }

        public async Task<float> GetJoulesBalanceAsync()
        {
            if (string.IsNullOrEmpty(_apiKey)) return -1f;

            try
            {
                string endpoint = $"{CurrentApiUrl}/v1/balance";
                using var webRequest = UnityWebRequest.Get(endpoint);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                webRequest.SetRequestHeader("player2-game-key", GameClientId);
                webRequest.timeout = 10;

                var asyncOp = webRequest.SendWebRequest();
                while (!asyncOp.isDone)
                {
                    if (Current.Game == null) return -1f;
                    await Task.Delay(100);
                }

                if (webRequest.result == UnityWebRequest.Result.ConnectionError ||
                    webRequest.result == UnityWebRequest.Result.ProtocolError)
                    return -1f;

                var balance = JsonConvert.DeserializeObject<Player2JoulesBalance>(
                    webRequest.downloadHandler.text);
                return balance?.Balance ?? -1f;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind] GetJoulesBalanceAsync failed: {ex.Message}");
                return -1f;
            }
        }

        private static float _cachedJoulesBalance = -1f;
        private static DateTime _lastBalanceCheck = DateTime.MinValue;

        public static float CachedJoulesBalance => _cachedJoulesBalance;

        public static void RefreshJoulesBalance()
        {
            var s = RimMindCoreMod.Settings;
            if (s == null || s.provider != AIProvider.Player2) return;

            Task.Run(async () =>
            {
                var client = await CreateAsync(s);
                if (client?.IsConfigured() == true)
                {
                    float balance = await client.GetJoulesBalanceAsync();
                    _cachedJoulesBalance = balance;
                    _lastBalanceCheck = DateTime.Now;
                }
            });
        }

        public async Task<RawResponse> SendRawAsync(string path, string jsonBody)
        {
            string endpoint = $"{CurrentApiUrl}{path}";
            var result = new RawResponse();
            try
            {
                using var webRequest = new UnityWebRequest(endpoint, "POST");
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                webRequest.SetRequestHeader("player2-game-key", GameClientId);
                webRequest.timeout = 30;

                var asyncOp = webRequest.SendWebRequest();
                while (!asyncOp.isDone) { if (Current.Game == null) { result.Error = "Game exiting"; return result; } await Task.Delay(50); }

                result.Content = webRequest.downloadHandler?.text;
                result.Success = webRequest.result != UnityWebRequest.Result.ConnectionError
                              && webRequest.result != UnityWebRequest.Result.ProtocolError;
                if (!result.Success) result.Error = webRequest.error;
            }
            catch (System.Exception ex) { result.Error = ex.Message; }
            return result;
        }

        public async Task<RawResponse> GetRawAsync(string path)
        {
            string endpoint = $"{CurrentApiUrl}{path}";
            var result = new RawResponse();
            try
            {
                using var webRequest = UnityWebRequest.Get(endpoint);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                webRequest.SetRequestHeader("player2-game-key", GameClientId);
                webRequest.timeout = 30;

                var asyncOp = webRequest.SendWebRequest();
                while (!asyncOp.isDone) { if (Current.Game == null) { result.Error = "Game exiting"; return result; } await Task.Delay(50); }

                result.Content = webRequest.downloadHandler?.text;
                result.Success = webRequest.result != UnityWebRequest.Result.ConnectionError
                              && webRequest.result != UnityWebRequest.Result.ProtocolError;
                if (!result.Success) result.Error = webRequest.error;
            }
            catch (System.Exception ex) { result.Error = ex.Message; }
            return result;
        }

        public async Task<RawResponse> DeleteRawAsync(string path)
        {
            string endpoint = $"{CurrentApiUrl}{path}";
            var result = new RawResponse();
            try
            {
                using var webRequest = UnityWebRequest.Delete(endpoint);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                webRequest.SetRequestHeader("player2-game-key", GameClientId);
                webRequest.timeout = 30;

                var asyncOp = webRequest.SendWebRequest();
                while (!asyncOp.isDone) { if (Current.Game == null) { result.Error = "Game exiting"; return result; } await Task.Delay(50); }

                result.Content = webRequest.downloadHandler?.text;
                result.Success = webRequest.result != UnityWebRequest.Result.ConnectionError
                              && webRequest.result != UnityWebRequest.Result.ProtocolError;
                if (!result.Success) result.Error = webRequest.error;
            }
            catch (System.Exception ex) { result.Error = ex.Message; }
            return result;
        }
    }

    public class RawResponse
    {
        public bool Success;
        public string? Content;
        public string? Error;
    }

    public partial class Player2Client
    {
        public async Task<AIResponse> SendStructuredAsync(AIRequest request, string? jsonSchema, List<StructuredTool>? tools)
        {
            try
            {
                var messages = new List<Player2MessageDto>();
                if (request.Messages != null && request.Messages.Count > 0)
                    messages = request.Messages.Select(m => new Player2MessageDto { Role = m.Role, Content = m.Content }).ToList();
                else
                {
                    if (!string.IsNullOrEmpty(request.SystemPrompt))
                        messages.Add(new Player2MessageDto { Role = "system", Content = request.SystemPrompt });
                    messages.Add(new Player2MessageDto { Role = "user", Content = request.UserPrompt });
                }

                messages = MergeConsecutiveSameRole(messages);

                var body = new Dictionary<string, object?>
                {
                    ["model"] = "default",
                    ["messages"] = messages,
                    ["max_tokens"] = request.MaxTokens > 0 ? request.MaxTokens : _settings.maxTokens,
                    ["temperature"] = request.Temperature,
                };

                if (!string.IsNullOrEmpty(jsonSchema))
                {
                    body["response_format"] = new
                    {
                        type = "json_schema",
                        json_schema = new { name = "response", schema = JsonConvert.DeserializeObject(jsonSchema!) },
                    };
                }

                if (tools != null && tools.Count > 0)
                {
                    var toolList = new List<object>();
                    foreach (var t in tools)
                    {
                        toolList.Add(new
                        {
                            type = "function",
                            function = new
                            {
                                name = t.Name,
                                description = t.Description,
                                parameters = t.Parameters != null ? JsonConvert.DeserializeObject(t.Parameters) : new { type = "object", properties = new { } },
                            },
                        });
                    }
                    body["tools"] = toolList;
                    if (tools.Any(t => t.ToolChoice == "required"))
                        body["tool_choice"] = "required";
                    else
                        body["tool_choice"] = "auto";
                }

                string json = JsonConvert.SerializeObject(body, Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                string endpoint = $"{CurrentApiUrl}/v1/chat/completions";
                using var webRequest = new UnityWebRequest(endpoint, "POST");
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                webRequest.SetRequestHeader("player2-game-key", GameClientId);
                webRequest.timeout = _isLocalConnection ? 300 : 60;

                var asyncOp = webRequest.SendWebRequest();
                while (!asyncOp.isDone)
                {
                    if (Current.Game == null)
                        return AIResponse.Failure(request.RequestId, "Game exiting");
                    await Task.Delay(100);
                }

                if (webRequest.result == UnityWebRequest.Result.ConnectionError ||
                    webRequest.result == UnityWebRequest.Result.ProtocolError)
                {
                    string errBody = webRequest.downloadHandler?.text ?? webRequest.error ?? "Unknown error";
                    return AIResponse.Failure(request.RequestId, errBody);
                }

                var dto = JsonConvert.DeserializeObject<Player2ResponseDto>(webRequest.downloadHandler.text);
                string? content = dto?.Choices?.FirstOrDefault()?.Message?.Content;
                int tokens = dto?.Usage?.TotalTokens ?? 0;
                int promptTokens = dto?.Usage?.PromptTokens ?? 0;
                int completionTokens = dto?.Usage?.CompletionTokens ?? 0;
                int cachedTokens = dto?.Usage?.PromptTokensDetails?.CachedTokens ?? 0;
                var toolCalls = dto?.Choices?.FirstOrDefault()?.Message?.ToolCalls;
                var response = new AIResponse
                {
                    Success = true,
                    Content = content ?? "",
                    RequestId = request.RequestId,
                    TokensUsed = tokens,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    CachedTokens = cachedTokens,
                };
                if (toolCalls != null && toolCalls.Count > 0)
                {
                    response.ToolCallsJson = JsonConvert.SerializeObject(toolCalls);
                }
                return response;
            }
            catch (System.Exception ex)
            {
                return AIResponse.Failure(request.RequestId, ex.Message);
            }
        }
    }
}
