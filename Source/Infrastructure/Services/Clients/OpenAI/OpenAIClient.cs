using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Models.Npc;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Services.Clients.Shared;
using Newtonsoft.Json;
using Verse;

namespace RimMind.Infrastructure.Services.Clients.OpenAI
{
    public partial class OpenAIClient : IAIClient
    {
        private static readonly ConcurrentDictionary<string, string> _formatCapabilityCache = new();

        internal static void InvalidateFormatCache()
        {
            _formatCapabilityCache.Clear();
        }

        private string BuildCacheKey() => $"{_settings.ApiEndpoint}|{_settings.ModelName}";

        private readonly IOpenAISettings _settings;
        private readonly ILogSink? _logSink;

        public OpenAIClient(IOpenAISettings settings, ILogSink? logSink = null)
        {
            _settings = settings;
            _logSink = logSink;
        }

        public bool IsConfigured() => _settings.IsConfigured();

        public bool IsLocalEndpoint => IsLoopbackEndpoint(_settings.ApiEndpoint);

        public void Dispose()
        {
            _formatCapabilityCache.Clear();
        }

        private static bool IsLoopbackEndpoint(string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint)) return false;
            if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri)) return false;
            if (uri.IsLoopback) return true;
            string host = uri.Host;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(host, "host.docker.internal", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public bool SupportsStreaming => true;

        public bool SupportsNpcServerState => false;

        public async Task<Result<RimMind.Domain.Llm.LlmResponse, RimMindError>> SendAsync(RimMind.Domain.Llm.LlmRequestEnvelope envelope)
        {
            if (!IsConfigured())
                return Result<RimMind.Domain.Llm.LlmResponse, RimMindError>.Err(RimMindErrors.ClientNotConfigured(nameof(OpenAIClient)));

            string endpoint = FormatEndpoint(_settings.ApiEndpoint);
            string json = BuildEnvelopeRequestJson(envelope);

            if (_settings.DebugLogging)
                _logSink?.LogFromBackground($"[RimMind-Core] >> {envelope.RequestId}\n{json}");

            var sw = Stopwatch.StartNew();
            try
            {
                bool isLocal = IsLoopbackEndpoint(_settings.ApiEndpoint);
                float connectTimeout = isLocal ? 300f : 60f;
                (string responseText, long httpStatusCode) = await HttpTransport.PostAsync(
                    endpoint, json, $"Bearer {_settings.ApiKey}", connectTimeout: connectTimeout);
                var parsed = JsonConvert.DeserializeObject<OpenAIResponseDto>(responseText);
                string content = parsed?.choices?[0]?.message?.content ?? string.Empty;
                string? reasoningContent = parsed?.choices?[0]?.message?.reasoning_content;
                int tokens = parsed?.usage?.total_tokens ?? 0;
                int promptTokens = parsed?.usage?.prompt_tokens ?? 0;
                int completionTokens = parsed?.usage?.completion_tokens ?? 0;
                int cachedTokens = parsed?.usage?.prompt_tokens_details?.cached_tokens ?? 0;
                var toolCallsDto = parsed?.choices?[0]?.message?.tool_calls;
                sw.Stop();

                if (_settings.DebugLogging)
                    _logSink?.LogFromBackground($"[RimMind-Core] << {envelope.RequestId} ({tokens} tok)\n{content}");

                var response = new RimMind.Domain.Llm.LlmResponse
                {
                    RequestId = envelope.RequestId,
                    Content = content,
                    ReasoningContent = reasoningContent,
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
                return ClientExceptionMapper.MapException(ex, nameof(OpenAIClient), envelope.RequestId, "request", _logSink);
            }
        }

        public async Task<Result<RimMind.Domain.Llm.LlmResponse, RimMindError>> SendStreamAsync(RimMind.Domain.Llm.LlmRequestEnvelope envelope, Action<RimMind.Domain.Llm.LlmChunk> onChunk, CancellationToken ct)
        {
            if (!IsConfigured())
                return Result<RimMind.Domain.Llm.LlmResponse, RimMindError>.Err(RimMindErrors.ClientNotConfigured(nameof(OpenAIClient)));

            string endpoint = FormatEndpoint(_settings.ApiEndpoint);
            string json = BuildEnvelopeRequestJson(envelope, stream: true);

            if (_settings.DebugLogging)
                _logSink?.LogFromBackground($"[RimMind-Core] >> Stream {envelope.RequestId}\n{json}");

            var contentBuilder = new StringBuilder();
            var toolCallsBuilder = new StringBuilder();
            string? reasoningContent = null;
            int totalTokens = 0;
            int promptTokens = 0;
            int completionTokens = 0;
            int cachedTokens = 0;

            try
            {
                bool isLocal = IsLoopbackEndpoint(_settings.ApiEndpoint);
                float connectTimeout = isLocal ? 300f : 60f;

                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, endpoint);
                request.Content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_settings.ApiKey}");

                using var httpClient = new System.Net.Http.HttpClient();
                using var response = await httpClient.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new System.IO.StreamReader(stream);

                await SseStreamReader.ReadDataLinesAsync(reader, async data =>
                {
                    try
                    {
                        var chunk = JsonConvert.DeserializeObject<OpenAIStreamChunkDto>(data);
                        if (chunk == null) return;

                        var delta = chunk.choices?[0]?.delta;
                        if (delta == null) return;

                        if (delta.content != null)
                        {
                            contentBuilder.Append(delta.content);
                            onChunk(new RimMind.Domain.Llm.LlmChunk
                            {
                                DeltaContent = delta.content,
                            });
                        }

                        if (delta.reasoning_content != null)
                        {
                            reasoningContent += delta.reasoning_content;
                            onChunk(new RimMind.Domain.Llm.LlmChunk
                            {
                                DeltaReasoningContent = delta.reasoning_content,
                            });
                        }

                        if (delta.tool_calls != null)
                        {
                            foreach (var tc in delta.tool_calls)
                            {
                                if (tc.Function?.Arguments != null)
                                    toolCallsBuilder.Append(tc.Function.Arguments);
                            }
                        }

                        if (chunk.usage != null)
                        {
                            totalTokens = chunk.usage.total_tokens;
                            promptTokens = chunk.usage.prompt_tokens;
                            completionTokens = chunk.usage.completion_tokens;
                            cachedTokens = chunk.usage.prompt_tokens_details?.cached_tokens ?? 0;

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
                return ClientExceptionMapper.MapException(ex, nameof(OpenAIClient), envelope.RequestId, "stream", _logSink);
            }

            var finalResponse = new RimMind.Domain.Llm.LlmResponse
            {
                RequestId = envelope.RequestId,
                Content = contentBuilder.ToString(),
                ToolCallsJson = toolCallsBuilder.Length > 0 ? toolCallsBuilder.ToString() : null,
                ReasoningContent = reasoningContent,
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

        public Task<Result<bool, RimMindError>> SpawnNpcAsync(NpcProfile profile)
        {
            throw new NotSupportedException("OpenAI does not support NPC server-side state");
        }

        public Task<Result<bool, RimMindError>> KillNpcAsync(string npcId)
        {
            throw new NotSupportedException("OpenAI does not support NPC server-side state");
        }

        public Task<Result<List<string>, RimMindError>> QueryNpcMemoriesAsync(string npcId, string query, int limit)
        {
            throw new NotSupportedException("OpenAI does not support NPC server-side state");
        }

    }
}
