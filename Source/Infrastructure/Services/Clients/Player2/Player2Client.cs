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

        private readonly string _apiKey;
        private readonly bool _isLocalConnection;
        private readonly ISettingsProvider _settings;
        private readonly ILogSink? _logSink;

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

            InitiateHealthCheckIfNeeded();
        }

        public static async Task<Player2Client> CreateAsync(ISettingsProvider settings,
            ILogSink? logSink = null)
        {
            try
            {
                string? localKey = await TryGetLocalPlayer2Key(logSink);
                if (!string.IsNullOrEmpty(localKey))
                {
                    logSink?.LogFromBackground("[RimMind-Core] Player2 local app detected.");
                    ShowNotification("RimMind.Infrastructure.Player2.LocalDetected");
                    return new Player2Client(localKey!, isLocal: true, settings, logSink);
                }

                if (!string.IsNullOrEmpty(settings.ApiKey))
                {
                    logSink?.LogFromBackground("[RimMind-Core] Using manual Player2 API key.");
                    return new Player2Client(settings.ApiKey, isLocal: false, settings, logSink);
                }

                ShowNotification("RimMind.Infrastructure.Player2.LocalNotFound");
                return new Player2Client(string.Empty, isLocal: false, settings, logSink);
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
                return Result<RimMind.Domain.Llm.LlmResponse, RimMindError>.Err(RimMindErrors.ClientNotConfigured(nameof(Player2Client)));

            string endpoint;
            string json;

            if (!string.IsNullOrEmpty(envelope.NpcId))
            {
                endpoint = $"{CurrentApiUrl}/npcs/{envelope.NpcId}/chat";
                var npcBody = new
                {
                    messages = BuildEnvelopeMessages(envelope),
                    max_tokens = envelope.MaxTokens > 0 ? envelope.MaxTokens : _settings.MaxTokens,
                    temperature = envelope.Temperature,
                    game_state_info = (string?)envelope.GameStateInfo,
                };
                json = JsonConvert.SerializeObject(npcBody, Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            }
            else
            {
                endpoint = $"{CurrentApiUrl}/v1/chat/completions";
                var body = new Player2RequestDto
                {
                    Model = "default",
                    Messages = BuildEnvelopePlayer2Messages(envelope),
                    MaxTokens = envelope.MaxTokens > 0 ? envelope.MaxTokens : _settings.MaxTokens,
                    Temperature = envelope.Temperature,
                    Stream = false,
                };

                if (!string.IsNullOrEmpty(envelope.JsonSchema))
                {
                    body.ResponseFormat = new { type = "json_schema", json_schema = new { name = "response", schema = JsonConvert.DeserializeObject(envelope.JsonSchema!) } };
                }

                if (envelope.Tools != null && envelope.Tools.Count > 0)
                {
                    body.Tools = new List<object>();
                    foreach (var t in envelope.Tools)
                    {
                        body.Tools.Add(new
                        {
                            type = "function",
                            function = new
                            {
                                name = t.Name,
                                description = t.Description,
                                parameters = t.Parameters != null
                                    ? JsonConvert.DeserializeObject(t.Parameters)
                                    : new { type = "object", properties = new { } },
                            },
                        });
                    }
                    body.ToolChoice = "auto";
                }

                json = JsonConvert.SerializeObject(body, Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            }

            if (_settings.DebugLogging)
                _logSink?.LogFromBackground($"[RimMind-Core] >> {envelope.RequestId} (Player2)\n{json}");

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
                var toolCallsDto = parsed?.Choices?[0]?.Message?.ToolCalls;
                sw.Stop();

                if (_settings.DebugLogging)
                    _logSink?.LogFromBackground($"[RimMind-Core] << {envelope.RequestId} ({tokens} tok)\n{content}");

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
                return ClientExceptionMapper.MapException(ex, nameof(Player2Client), envelope.RequestId, "request", _logSink);
            }
        }

        public async Task<Result<RimMind.Domain.Llm.LlmResponse, RimMindError>> SendStreamAsync(RimMind.Domain.Llm.LlmRequestEnvelope envelope, Action<RimMind.Domain.Llm.LlmChunk> onChunk, CancellationToken ct)
        {
            if (!IsConfigured())
                return Result<RimMind.Domain.Llm.LlmResponse, RimMindError>.Err(RimMindErrors.ClientNotConfigured(nameof(Player2Client)));

            string endpoint;
            string json;

            if (!string.IsNullOrEmpty(envelope.NpcId))
            {
                endpoint = $"{CurrentApiUrl}/npcs/{envelope.NpcId}/chat";
                var npcBody = new
                {
                    messages = BuildEnvelopeMessages(envelope),
                    max_tokens = envelope.MaxTokens > 0 ? envelope.MaxTokens : _settings.MaxTokens,
                    temperature = envelope.Temperature,
                    game_state_info = (string?)envelope.GameStateInfo,
                    stream = true,
                };
                json = JsonConvert.SerializeObject(npcBody, Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            }
            else
            {
                endpoint = $"{CurrentApiUrl}/v1/chat/completions";
                var body = new Player2RequestDto
                {
                    Model = "default",
                    Messages = BuildEnvelopePlayer2Messages(envelope),
                    MaxTokens = envelope.MaxTokens > 0 ? envelope.MaxTokens : _settings.MaxTokens,
                    Temperature = envelope.Temperature,
                    Stream = true,
                };
                json = JsonConvert.SerializeObject(body, Formatting.None,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            }

            if (_settings.DebugLogging)
                _logSink?.LogFromBackground($"[RimMind-Core] >> Stream {envelope.RequestId} (Player2)\n{json}");

            var contentBuilder = new StringBuilder();
            var toolCallsBuilder = new StringBuilder();
            int totalTokens = 0;
            int promptTokens = 0;
            int completionTokens = 0;
            int cachedTokens = 0;

            try
            {
                await EnsureHealthCheck();

                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, endpoint);
                request.Content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");
                request.Headers.TryAddWithoutValidation("player2-game-key", GameClientId);

                using var httpClient = new System.Net.Http.HttpClient();
                using var response = await httpClient.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new System.IO.StreamReader(stream);

                await SseStreamReader.ReadDataLinesAsync(reader, async data =>
                {
                    try
                    {
                        var chunk = JsonConvert.DeserializeObject<Player2StreamChunkDto>(data);
                        if (chunk == null) return;

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
                                if (tc.Function?.Arguments != null)
                                    toolCallsBuilder.Append(tc.Function.Arguments);
                            }

                            var toolCallsJson = JsonConvert.SerializeObject(delta.ToolCalls.Select(tc => new
                            {
                                index = tc.Index,
                                id = tc.Id,
                                type = tc.Type,
                                function = new { name = tc.Function?.Name, arguments = tc.Function?.Arguments }
                            }));

                            onChunk(new RimMind.Domain.Llm.LlmChunk
                            {
                                DeltaToolCallsJson = toolCallsJson,
                            });
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
                }, ct);
            }
            catch (Exception ex)
            {
                return ClientExceptionMapper.MapException(ex, nameof(Player2Client), envelope.RequestId, "stream", _logSink,
                    useClientTransientForGeneric: true);
            }

            var finalResponse = new RimMind.Domain.Llm.LlmResponse
            {
                RequestId = envelope.RequestId,
                Content = contentBuilder.ToString(),
                ToolCallsJson = toolCallsBuilder.Length > 0 ? toolCallsBuilder.ToString() : null,
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
                return Result<bool, RimMindError>.Err(RimMindErrors.RemoteBackendFailed($"SpawnNpcAsync failed: {ex.Message}", ex));
            }
        }

        public async Task<Result<bool, RimMindError>> KillNpcAsync(string npcId)
        {
            try
            {
                var response = await DeleteRawAsync($"/npcs/{npcId}");
                if (!response.IsOk)
                    return Result<bool, RimMindError>.Err(response.Error ?? RimMindErrors.RemoteBackendFailed("KillNpc raw request failed"));
                return Result<bool, RimMindError>.Ok(true);
            }
            catch (Exception ex)
            {
                return Result<bool, RimMindError>.Err(RimMindErrors.RemoteBackendFailed($"KillNpcAsync failed: {ex.Message}", ex));
            }
        }

        public async Task<Result<List<string>, RimMindError>> QueryNpcMemoriesAsync(string npcId, string query, int limit)
        {
            try
            {
                var response = await GetRawAsync($"/npcs/{npcId}/memories?query={Uri.EscapeDataString(query ?? "")}&limit={limit}");
                if (!response.IsOk)
                    return Result<List<string>, RimMindError>.Err(response.Error ?? RimMindErrors.RemoteBackendFailed("QueryNpcMemories raw request failed"));
                var results = JsonConvert.DeserializeObject<List<string>>(response.Content ?? "[]");
                return Result<List<string>, RimMindError>.Ok(results ?? new List<string>());
            }
            catch (Exception ex)
            {
                return Result<List<string>, RimMindError>.Err(RimMindErrors.RemoteBackendFailed($"QueryNpcMemoriesAsync failed: {ex.Message}", ex));
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

        private async Task<(string text, long statusCode)> PostAsync(string url, string jsonBody)
        {
            bool isLocal = _isLocalConnection;
            float connectTimeout = isLocal ? LocalConnectTimeout : RemoteConnectTimeout;
            return await HttpTransport.PostAsync(url, jsonBody, $"Bearer {_apiKey}",
                "player2-game-key", GameClientId, connectTimeout: connectTimeout);
        }
    }
}
