using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Client;
using RimMind.Application.Common.Models.Npc;
using RimMind.Application.Common.Helpers;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Services.Clients.Shared;
using Newtonsoft.Json;

namespace RimMind.Infrastructure.Services.Clients.Player2
{
    public partial class Player2Client : IAIClient
    {
        private const float LocalConnectTimeout = 300f;
        private const float RemoteConnectTimeout = 60f;

        public const string GameClientId = "019a8368-b00b-72bc-b367-2825079dc6fb";
        private static string LocalUrl => $"http://localhost:{RimMindDefaults.Player2LocalPort}";

        private static volatile Player2Client? _activeInstance;

        private volatile string _apiKey;
        private volatile bool _isLocalConnection;
        private readonly ISettingsProvider _settings;
        private readonly ILogSink? _logSink;

        internal Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpCompletionOption, CancellationToken, Task<System.Net.Http.HttpResponseMessage>>? StreamSendOverride { get; set; }

        private string RemoteUrl => string.IsNullOrWhiteSpace(_settings.Player2RemoteUrl)
            ? "https://api.player2.game"
            : _settings.Player2RemoteUrl.Trim().TrimEnd('/');

        private string CurrentApiUrl => _isLocalConnection ? LocalUrl : RemoteUrl;

        private Player2Client(string apiKey, bool isLocal, ISettingsProvider settings,
            ILogSink? logSink)
        {
            _apiKey = apiKey;
            _isLocalConnection = isLocal;
            _settings = settings;
            _logSink = logSink;
            _activeInstance = this;

            InitiateHealthCheckIfNeeded();
        }

        internal void UpdateDiscoveredKey(string key, bool isLocal)
        {
            _apiKey = key;
            _isLocalConnection = isLocal;
            InitiateHealthCheckIfNeeded();
        }

        public static Player2Client Create(ISettingsProvider settings, ILogSink? logSink = null)
        {
            GetDiscoveryState(out var cachedKey, out _);
            if (!string.IsNullOrEmpty(cachedKey))
            {
                return new Player2Client(cachedKey!, isLocal: true, settings, logSink);
            }

            if (!string.IsNullOrEmpty(settings?.ApiKey))
            {
                return new Player2Client(settings!.ApiKey, isLocal: false, settings, logSink);
            }

            var client = new Player2Client(string.Empty, isLocal: false, settings!, logSink);
            TriggerBackgroundDiscovery(client, settings!, logSink);
            return client;
        }

        public static async Task<Player2Client> CreateAsync(ISettingsProvider settings,
            ILogSink? logSink = null)
        {
            try
            {
                GetDiscoveryState(out var cachedKey, out _);
                if (!string.IsNullOrEmpty(cachedKey))
                {
                    return new Player2Client(cachedKey!, isLocal: true, settings, logSink);
                }

                if (!string.IsNullOrEmpty(settings?.ApiKey))
                {
                    return new Player2Client(settings!.ApiKey, isLocal: false, settings, logSink);
                }

                string? localKey = await ProbeLocalPlayer2KeyAsync(logSink).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(localKey))
                {
                    return new Player2Client(localKey!, isLocal: true, settings!, logSink);
                }

                ShowNotification("RimMind.Infrastructure.Player2.LocalNotFound");
                return new Player2Client(string.Empty, isLocal: false, settings!, logSink);
            }
            catch (Exception ex)
            {
                logSink?.LogFromBackground($"[RimMind-Core] Failed to create Player2 client: {ex.Message}", isWarning: true);
                return new Player2Client(string.Empty, isLocal: false, settings, logSink);
            }
        }

        public bool IsConfigured() => !string.IsNullOrEmpty(_apiKey);

        public bool IsLocalEndpoint => _isLocalConnection;

        public void Dispose()
        {
            // Player2Client uses a shared static health check CTS;
            // individual disposal does not stop the health check loop.
            // Use StopHealthCheck() to halt it globally.
        }

        public bool SupportsStreaming => true;

        public bool SupportsNpcServerState => true;

        public async Task<Result<RimMind.Domain.Llm.LlmResponse, RimMindError>> SendAsync(RimMind.Domain.Llm.LlmRequestEnvelope envelope)
        {
            if (!IsConfigured())
            {
                GetDiscoveryState(out var cachedKey, out var activeTask);
                if (!string.IsNullOrEmpty(cachedKey))
                {
                    UpdateDiscoveredKey(cachedKey!, isLocal: true);
                }
                else if (activeTask != null)
                {
                    try
                    {
                        string? key = await activeTask.ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(key))
                        {
                            UpdateDiscoveredKey(key!, isLocal: true);
                        }
                    }
                    catch { }
                }
            }

            if (!IsConfigured())
                return Result<RimMind.Domain.Llm.LlmResponse, RimMindError>.Err(RimMindErrors.ClientNotConfigured(nameof(Player2Client)));

            (string endpoint, string json) = BuildPayload(envelope, stream: false);

            if (_settings.DebugLogging)
                _logSink?.LogFromBackground($"[RimMind-Core] >> {envelope.RequestId} (Player2)\n{LogSanitizer.ForLog(json, LogSanitizer.DebugPayloadMaxChars)}");

            var sw = Stopwatch.StartNew();
            try
            {
                (string responseText, long httpStatusCode) = await PostAsync(endpoint, json, envelope.Ct);
                var parsed = JsonConvert.DeserializeObject<Player2ResponseDto>(responseText);
                string content = parsed?.Choices?[0]?.Message?.Content ?? string.Empty;
                int tokens = parsed?.Usage?.TotalTokens ?? 0;
                int promptTokens = parsed?.Usage?.PromptTokens ?? 0;
                int completionTokens = parsed?.Usage?.CompletionTokens ?? 0;
                int cachedTokens = parsed?.Usage?.PromptTokensDetails?.CachedTokens ?? 0;
                var toolCallsDto = parsed?.Choices?[0]?.Message?.ToolCalls;
                sw.Stop();

                if (_settings.DebugLogging)
                    _logSink?.LogFromBackground($"[RimMind-Core] << {envelope.RequestId} ({tokens} tok)\n{LogSanitizer.ForLog(content, LogSanitizer.DebugPayloadMaxChars)}");

                var response = new RimMind.Domain.Llm.LlmResponse
                {
                    RequestId = envelope.RequestId,
                    Content = content,
                    TokensUsed = tokens,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    CachedTokens = cachedTokens,
                    State = RimMind.Domain.Llm.AIRequestState.Completed,
                    Priority = envelope.Priority,
                    ProcessingMs = sw.ElapsedMilliseconds,
                    HttpStatusCode = httpStatusCode,
                };

                if (toolCallsDto != null && toolCallsDto.Count > 0)
                {
                    var toolCallsJson = ToolCallJsonNormalizer.Normalize(
                        toolCallsDto.Select(tc => new ToolCallEntry
                        {
                            Id = tc.Id,
                            Type = tc.Type,
                            FunctionName = tc.Function?.Name,
                            FunctionArguments = tc.Function?.Arguments,
                        }));
                    if (toolCallsJson != null)
                        response = response.With(toolCallsJson: toolCallsJson);
                }

                return Result<RimMind.Domain.Llm.LlmResponse, RimMindError>.Ok(response);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return ClientExceptionMapper.MapException(ex, nameof(Player2Client), envelope.RequestId, "request", _logSink, callerToken: envelope.Ct);
            }
        }

        public async Task<Result<RimMind.Domain.Llm.LlmResponse, RimMindError>> SendStreamAsync(RimMind.Domain.Llm.LlmRequestEnvelope envelope, Action<RimMind.Domain.Llm.LlmChunk> onChunk, CancellationToken ct)
        {
            if (!IsConfigured())
            {
                GetDiscoveryState(out var cachedKey, out var activeTask);
                if (!string.IsNullOrEmpty(cachedKey))
                {
                    UpdateDiscoveredKey(cachedKey!, isLocal: true);
                }
                else if (activeTask != null)
                {
                    try
                    {
                        string? key = await activeTask.ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(key))
                        {
                            UpdateDiscoveredKey(key!, isLocal: true);
                        }
                    }
                    catch { }
                }
            }

            if (!IsConfigured())
                return Result<RimMind.Domain.Llm.LlmResponse, RimMindError>.Err(RimMindErrors.ClientNotConfigured(nameof(Player2Client)));

            using var callerCts = (ct.CanBeCanceled && envelope.Ct.CanBeCanceled && ct != envelope.Ct)
                ? CancellationTokenSource.CreateLinkedTokenSource(ct, envelope.Ct)
                : null;
            CancellationToken effectiveCt = callerCts?.Token ?? (ct.CanBeCanceled ? ct : envelope.Ct);

            (string endpoint, string json) = BuildPayload(envelope, stream: true);

            if (_settings.DebugLogging)
                _logSink?.LogFromBackground($"[RimMind-Core] >> Stream {envelope.RequestId} (Player2)\n{LogSanitizer.ForLog(json, LogSanitizer.DebugPayloadMaxChars)}");

            var contentBuilder = new StringBuilder();
            var toolCallAccumulator = new StreamingToolCallAccumulator();
            int totalTokens = 0;
            int promptTokens = 0;
            int completionTokens = 0;
            int cachedTokens = 0;

            try
            {
                float connectTimeout = _isLocalConnection ? LocalConnectTimeout : RemoteConnectTimeout;

                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, endpoint);
                request.Content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");
                request.Headers.TryAddWithoutValidation("player2-game-key", GameClientId);

                using var session = StreamSendOverride != null
                    ? await HttpTransport.OpenStreamAsync(request, connectTimeout, effectiveCt, StreamSendOverride).ConfigureAwait(false)
                    : await HttpTransport.OpenStreamAsync(request, connectTimeout, effectiveCt).ConfigureAwait(false);

                await SseStreamReader.ReadDataLinesAsync(session.Reader, data =>
                {
                    try
                    {
                        var chunk = JsonConvert.DeserializeObject<Player2StreamChunkDto>(data);
                        if (chunk == null) return Task.CompletedTask;

                        var delta = chunk.Choices?[0]?.Delta;
                        if (delta != null && delta.Content != null)
                        {
                            contentBuilder.Append(delta.Content);
                            onChunk(new RimMind.Domain.Llm.LlmChunk
                            {
                                DeltaContent = delta.Content,
                            });
                        }

                        if (delta != null && delta.ToolCalls != null)
                        {
                            foreach (var tc in delta.ToolCalls)
                            {
                                if (tc == null) continue;
                                toolCallAccumulator.Append(
                                    tc.Index,
                                    tc.Id,
                                    tc.Type,
                                    tc.Function?.Name,
                                    tc.Function?.Arguments);
                            }
                        }

                        if (chunk.Usage != null)
                        {
                            totalTokens = chunk.Usage.TotalTokens;
                            promptTokens = chunk.Usage.PromptTokens;
                            completionTokens = chunk.Usage.CompletionTokens;
                            cachedTokens = chunk.Usage.PromptTokensDetails?.CachedTokens ?? 0;

                            onChunk(new RimMind.Domain.Llm.LlmChunk
                            {
                                DeltaPromptTokens = promptTokens,
                                DeltaCompletionTokens = completionTokens,
                                DeltaCachedTokens = cachedTokens,
                            });
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip malformed SSE chunks
                    }

                    return Task.CompletedTask;
                }, session.Token, session.Heartbeat).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return ClientExceptionMapper.MapException(ex, nameof(Player2Client), envelope.RequestId, "stream", _logSink,
                    useClientTransientForGeneric: true, callerToken: effectiveCt);
            }

            var finalResponse = new RimMind.Domain.Llm.LlmResponse
            {
                RequestId = envelope.RequestId,
                Content = contentBuilder.ToString(),
                ToolCallsJson = toolCallAccumulator.ToNormalizedJson(),
                TokensUsed = totalTokens,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                CachedTokens = cachedTokens,
                State = RimMind.Domain.Llm.AIRequestState.Completed,
                Priority = envelope.Priority,
            };

            onChunk(new RimMind.Domain.Llm.LlmChunk
            {
                IsLast = true,
                FinalResponse = finalResponse,
            });

            return Result<RimMind.Domain.Llm.LlmResponse, RimMindError>.Ok(finalResponse);
        }

        internal (string endpoint, string json) BuildPayload(RimMind.Domain.Llm.LlmRequestEnvelope envelope, bool stream)
        {
            int maxTokens = envelope.MaxTokens > 0 ? envelope.MaxTokens : (_settings?.MaxTokens ?? 800);

            if (!string.IsNullOrEmpty(envelope.NpcId))
            {
                string endpoint = $"{CurrentApiUrl}/npcs/{Uri.EscapeDataString(envelope.NpcId!)}/chat";
                var npcBody = new
                {
                    messages = BuildEnvelopeMessages(envelope),
                    max_tokens = maxTokens,
                    temperature = envelope.Temperature,
                    game_state_info = (string?)envelope.GameStateInfo,
                    stream = stream ? (bool?)true : null,
                };
                string json = JsonConvert.SerializeObject(npcBody, Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                return (endpoint, json);
            }
            else
            {
                string endpoint = $"{CurrentApiUrl}/v1/chat/completions";
                var body = new Player2RequestDto
                {
                    Model = "default",
                    Messages = BuildEnvelopePlayer2Messages(envelope),
                    MaxTokens = maxTokens,
                    Temperature = envelope.Temperature,
                    Stream = stream,
                };

                if (TryParseJsonSchema(envelope.JsonSchema, out var parsedSchema))
                {
                    body.ResponseFormat = new { type = "json_schema", json_schema = new { name = "response", schema = parsedSchema } };
                }

                if (envelope.Tools != null && envelope.Tools.Count > 0)
                {
                    body.Tools = new List<object>();
                    foreach (var t in envelope.Tools.OrderBy(t => t.Name, StringComparer.Ordinal))
                    {
                        body.Tools.Add(new
                        {
                            type = "function",
                            function = new
                            {
                                name = t.Name?.Replace('.', '_') ?? "",
                                description = t.Description,
                                parameters = t.Parameters != null
                                    ? JsonConvert.DeserializeObject(t.Parameters)
                                    : new { type = "object", properties = new { } },
                            },
                        });
                    }
                    body.ToolChoice = envelope.Tools.Any(t => t.ToolChoice == "required") ? "required" : "auto";
                }

                string json = JsonConvert.SerializeObject(body, Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                return (endpoint, json);
            }
        }

        private static bool TryParseJsonSchema(string? value, out object? parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string trimmed = value!.TrimStart();
            if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '[')) return false;
            try
            {
                parsed = JsonConvert.DeserializeObject(value);
                return parsed != null;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public async Task<Result<bool, RimMindError>> SpawnNpcAsync(NpcProfile profile)
        {
            if (profile == null)
                return Result<bool, RimMindError>.Err(RimMindErrors.RemoteBackendFailed("Profile is null"));
            try
            {
                var body = new
                {
                    npc_id = profile.NpcId,
                    name = profile.Name,
                    short_name = profile.ShortName,
                    character_description = profile.CharacterDescription,
                    system_prompt = profile.SystemPrompt,
                };
                string json = JsonConvert.SerializeObject(body, Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                var response = await SendRawAsync("/npcs/spawn", json);
                if (!response.IsOk)
                    return Result<bool, RimMindError>.Err(response.Error ?? RimMindErrors.RemoteBackendFailed("SpawnNpc raw request failed"));
                return Result<bool, RimMindError>.Ok(true);
            }
            catch (Exception ex)
            {
                return Result<bool, RimMindError>.Err(RimMindErrors.RemoteBackendFailed($"SpawnNpcAsync failed: {LogSanitizer.ForLog(ex.Message)}", ex));
            }
        }

        public async Task<Result<bool, RimMindError>> KillNpcAsync(string npcId)
        {
            try
            {
                string escapedNpcId = Uri.EscapeDataString(npcId ?? string.Empty);
                var response = await DeleteRawAsync($"/npcs/{escapedNpcId}");
                if (!response.IsOk)
                    return Result<bool, RimMindError>.Err(response.Error ?? RimMindErrors.RemoteBackendFailed("KillNpc raw request failed"));
                return Result<bool, RimMindError>.Ok(true);
            }
            catch (Exception ex)
            {
                return Result<bool, RimMindError>.Err(RimMindErrors.RemoteBackendFailed($"KillNpcAsync failed: {LogSanitizer.ForLog(ex.Message)}", ex));
            }
        }

        public async Task<Result<List<string>, RimMindError>> QueryNpcMemoriesAsync(string npcId, string query, int limit)
        {
            try
            {
                string escapedNpcId = Uri.EscapeDataString(npcId ?? string.Empty);
                var response = await GetRawAsync($"/npcs/{escapedNpcId}/memories?query={Uri.EscapeDataString(query ?? "")}&limit={limit}");
                if (!response.IsOk)
                    return Result<List<string>, RimMindError>.Err(response.Error ?? RimMindErrors.RemoteBackendFailed("QueryNpcMemories raw request failed"));
                var results = JsonConvert.DeserializeObject<List<string>>(response.Content ?? "[]");
                return Result<List<string>, RimMindError>.Ok(results ?? new List<string>());
            }
            catch (Exception ex)
            {
                return Result<List<string>, RimMindError>.Err(RimMindErrors.RemoteBackendFailed($"QueryNpcMemoriesAsync failed: {LogSanitizer.ForLog(ex.Message)}", ex));
            }
        }

        private List<RimMind.Domain.Llm.ChatMessage> BuildEnvelopeMessages(RimMind.Domain.Llm.LlmRequestEnvelope envelope)
        {
            return envelope.Messages ?? new List<RimMind.Domain.Llm.ChatMessage>();
        }

        private List<Player2MessageDto> BuildEnvelopePlayer2Messages(RimMind.Domain.Llm.LlmRequestEnvelope envelope)
        {
            var messages = new List<Player2MessageDto>();

            if (envelope.Messages != null && envelope.Messages.Count > 0)
            {
                foreach (var m in envelope.Messages)
                {
                    messages.Add(new Player2MessageDto { Role = m.Role, Content = m.Content });
                }
            }

            return MergeConsecutiveSameRole(messages);
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

        private async Task<(string text, long statusCode)> PostAsync(string url, string jsonBody, CancellationToken ct = default)
        {
            bool isLocal = _isLocalConnection;
            float connectTimeout = isLocal ? LocalConnectTimeout : RemoteConnectTimeout;
            return await HttpTransport.PostAsync(url, jsonBody, $"Bearer {_apiKey}",
                "player2-game-key", GameClientId, connectTimeout: connectTimeout, cancellationToken: ct);
        }
    }
}
