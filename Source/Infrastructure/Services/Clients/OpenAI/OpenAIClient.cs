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
using RimMind.Application.Common.Models.Client;
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
        public delegate Task<(string body, long statusCode)> HttpPostDelegate(
            string url,
            string jsonBody,
            string? authHeader = null,
            string? headerName = null,
            string? headerValue = null,
            float connectTimeout = 60f,
            CancellationToken cancellationToken = default);

        private const long DefaultNodeIsolationMs = 60_000L;
        private const long TransientRateLimitIsolationMs = 2_000L;
        private const long PermanentNodeIsolationMs = 1_800_000L;

        private static readonly ConcurrentDictionary<string, string> _formatCapabilityCache = new();
        private static readonly ConcurrentDictionary<string, long> _endpointIsolatedUntilMs = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, long> _endpointExhaustedUntilMs = new(StringComparer.OrdinalIgnoreCase);

        internal static void InvalidateFormatCache()
        {
            _formatCapabilityCache.Clear();
            _endpointIsolatedUntilMs.Clear();
            _endpointExhaustedUntilMs.Clear();
        }

        private string BuildCacheKey() => $"{_settings.ApiEndpoint}|{_settings.ModelName}";

        private static string BuildNodeKey(BackupEndpointNode node) =>
            $"{(node.ApiEndpoint ?? string.Empty).Trim().TrimEnd('/')}|{(node.ModelName ?? string.Empty).Trim()}|{(string.IsNullOrEmpty(node.ApiKey) ? string.Empty : (node.ApiKey.Length > 8 ? node.ApiKey.Substring(node.ApiKey.Length - 8) : node.ApiKey))}";

        internal static bool IsNodeIsolated(BackupEndpointNode node, long nowMs)
        {
            string key = BuildNodeKey(node);
            return _endpointIsolatedUntilMs.TryGetValue(key, out long untilMs) && nowMs < untilMs;
        }

        internal static bool IsNodePermanentlyExhausted(BackupEndpointNode node, long nowMs)
        {
            string key = BuildNodeKey(node);
            return _endpointExhaustedUntilMs.TryGetValue(key, out long untilMs) && nowMs < untilMs;
        }

        internal static void RecordNodeSuccess(BackupEndpointNode node)
        {
            string key = BuildNodeKey(node);
            _endpointIsolatedUntilMs.TryRemove(key, out _);
            _endpointExhaustedUntilMs.TryRemove(key, out _);
        }

        internal static void RecordNodeFailure(BackupEndpointNode node, long nowMs, long cooldownMs = DefaultNodeIsolationMs, bool isPermanentQuotaOrAuth = false)
        {
            string key = BuildNodeKey(node);
            _endpointIsolatedUntilMs[key] = nowMs + cooldownMs;
            if (isPermanentQuotaOrAuth)
            {
                _endpointExhaustedUntilMs[key] = nowMs + cooldownMs;
            }
        }

        private static string ClassificationText(Exception ex) =>
            ex is HttpTransport.HttpException httpEx ? httpEx.ClassificationText : (ex.Message ?? string.Empty);

        internal static bool IsPermanentNodeQuotaOrAuthError(Exception? ex)
        {
            if (ex == null) return false;
            if (ex is HttpTransport.HttpException httpEx &&
                (httpEx.StatusCode == 401 || httpEx.StatusCode == 402 || httpEx.StatusCode == 403))
            {
                return true;
            }

            string msg = ClassificationText(ex);
            return msg.IndexOf("Insufficient account funds", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("insufficient_quota", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("account_deactivated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("invalid_api_key", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("billing_not_active", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("model_not_found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("is not supported by any configured account", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsTransientConcurrencyOrRateLimitError(Exception? ex)
        {
            if (ex == null || IsPermanentNodeQuotaOrAuthError(ex)) return false;
            if (ex is HttpTransport.HttpException httpEx && httpEx.StatusCode == 429)
                return true;

            string msg = ClassificationText(ex);
            return msg.IndexOf("gateway_concurrency_limit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("gateway_queue_full", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("Concurrency limit exceeded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("Too many pending requests", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   msg.IndexOf("rate_limit_error", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static long ResolveNodeIsolationCooldownMs(Exception ex, out bool isPermanentQuotaOrAuth)
        {
            isPermanentQuotaOrAuth = IsPermanentNodeQuotaOrAuthError(ex);
            if (isPermanentQuotaOrAuth)
                return PermanentNodeIsolationMs;
            if (IsTransientConcurrencyOrRateLimitError(ex))
                return TransientRateLimitIsolationMs;
            return DefaultNodeIsolationMs;
        }

        private readonly IOpenAISettings _settings;
        private readonly ILogSink? _logSink;
        private readonly HttpPostDelegate _postAsync;

        internal Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpCompletionOption, CancellationToken, Task<System.Net.Http.HttpResponseMessage>>? StreamSendOverride { get; set; }

        public OpenAIClient(IOpenAISettings settings, ILogSink? logSink = null, HttpPostDelegate? postAsyncOverride = null)
        {
            _settings = settings;
            _logSink = logSink;
            _postAsync = postAsyncOverride ?? HttpTransport.PostAsync;
        }

        private static float ResolveTimeoutSeconds(bool isLocal, int candidateCount, int attempt) =>
            isLocal ? 300f : ((candidateCount > 1 && attempt < candidateCount - 1) ? 45f : 60f);

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

        internal List<BackupEndpointNode> ResolveOrderedCandidateNodes(long nowMs)
        {
            var allConfigured = new List<BackupEndpointNode>();

            if (!string.IsNullOrWhiteSpace(_settings.ApiEndpoint) && !string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                allConfigured.Add(new BackupEndpointNode
                {
                    Name = "Primary",
                    Provider = "openai",
                    ApiEndpoint = _settings.ApiEndpoint,
                    ModelName = !string.IsNullOrWhiteSpace(_settings.ModelName) ? _settings.ModelName : "gpt-4o-mini",
                    ApiKey = _settings.ApiKey,
                    Enabled = true
                });
            }

            var backups = _settings.BackupEndpoints;
            if (backups != null)
            {
                for (int i = 0; i < backups.Count; i++)
                {
                    var b = backups[i];
                    if (b == null || !b.IsConfigured)
                        continue;

                    bool isDuplicate = allConfigured.Any(existing =>
                        string.Equals(existing.ApiEndpoint?.Trim().TrimEnd('/'), b.ApiEndpoint?.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.ModelName?.Trim(), b.ModelName?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.ApiKey?.Trim(), b.ApiKey?.Trim(), StringComparison.Ordinal));
                    if (!isDuplicate)
                    {
                        allConfigured.Add(new BackupEndpointNode
                        {
                            Name = !string.IsNullOrWhiteSpace(b.Name) ? b.Name : $"Backup #{i + 1}",
                            Provider = !string.IsNullOrWhiteSpace(b.Provider) ? b.Provider : "openai",
                            ApiEndpoint = b.ApiEndpoint,
                            ModelName = b.ModelName,
                            ApiKey = b.ApiKey,
                            Enabled = b.Enabled
                        });
                    }
                }
            }

            if (allConfigured.Count <= 1)
                return allConfigured;

            var nonExhausted = allConfigured.Where(n => !IsNodePermanentlyExhausted(n, nowMs)).ToList();
            var pool = nonExhausted.Count > 0 ? nonExhausted : allConfigured;

            var healthy = new List<BackupEndpointNode>();
            var isolated = new List<(BackupEndpointNode node, long untilMs)>();

            for (int i = 0; i < pool.Count; i++)
            {
                var node = pool[i];
                string key = BuildNodeKey(node);
                if (_endpointIsolatedUntilMs.TryGetValue(key, out long untilMs) && nowMs < untilMs)
                {
                    isolated.Add((node, untilMs));
                }
                else
                {
                    healthy.Add(node);
                }
            }

            foreach (var item in isolated.OrderBy(x => x.untilMs))
            {
                healthy.Add(item.node);
            }

            return healthy;
        }

        public async Task<Result<RimMind.Domain.Llm.LlmResponse, RimMindError>> SendAsync(RimMind.Domain.Llm.LlmRequestEnvelope envelope)
        {
            if (!IsConfigured())
                return Result<RimMind.Domain.Llm.LlmResponse, RimMindError>.Err(RimMindErrors.ClientNotConfigured(nameof(OpenAIClient)));

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var candidates = ResolveOrderedCandidateNodes(nowMs);
            if (candidates.Count == 0)
                return Result<RimMind.Domain.Llm.LlmResponse, RimMindError>.Err(RimMindErrors.ClientNotConfigured(nameof(OpenAIClient)));

            Exception? lastException = null;
            BackupEndpointNode? transientRateLimitedNode = null;

            for (int attempt = 0; attempt < candidates.Count; attempt++)
            {
                var node = candidates[attempt];
                if (attempt > 0 && IsNodePermanentlyExhausted(node, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
                    continue;

                string endpoint = FormatEndpoint(node.ApiEndpoint);
                string effectiveModel = NormalizeModelNameForEndpoint(node.ApiEndpoint, node.ModelName);
                string json = BuildEnvelopeRequestJson(
                    envelope,
                    stream: false,
                    modelNameOverride: effectiveModel,
                    endpointOverride: node.ApiEndpoint);

                if (_settings.DebugLogging)
                    _logSink?.LogFromBackground($"[RimMind-Core] >> {envelope.RequestId} (node={node.Name}, model={effectiveModel})\n{LogSanitizer.ForLog(json, LogSanitizer.DebugPayloadMaxChars)}");

                var sw = Stopwatch.StartNew();
                try
                {
                    bool isLocal = IsLoopbackEndpoint(node.ApiEndpoint);
                    float connectTimeout = ResolveTimeoutSeconds(isLocal, candidates.Count, attempt);
                    bool isOpenCode = IsOpenCodeGo(node.ApiEndpoint, node.ApiKey) ||
                                      string.Equals(node.Provider, "opencode", StringComparison.OrdinalIgnoreCase);
                    string? headerName = isOpenCode ? "x-opencode-session" : null;
                    string? headerValue = isOpenCode ? ResolveSessionId(envelope) : null;

                    (string responseText, long httpStatusCode) = await _postAsync(
                        endpoint, json, $"Bearer {node.ApiKey}",
                        headerName: headerName, headerValue: headerValue,
                        connectTimeout: connectTimeout,
                        cancellationToken: envelope.Ct);

                    var parsed = JsonConvert.DeserializeObject<OpenAIResponseDto>(responseText);
                    string content = parsed?.choices?[0]?.message?.content ?? string.Empty;
                    string? reasoningContent = parsed?.choices?[0]?.message?.reasoning_content;
                    int tokens = parsed?.usage?.total_tokens ?? 0;
                    int promptTokens = parsed?.usage?.prompt_tokens ?? 0;
                    int completionTokens = parsed?.usage?.completion_tokens ?? 0;
                    int cachedTokens = parsed?.usage?.prompt_tokens_details?.cached_tokens ?? 0;
                    var toolCallsDto = parsed?.choices?[0]?.message?.tool_calls;
                    string? finishReason = parsed?.choices?[0]?.finish_reason;
                    bool expectsTools = envelope.Tools != null && envelope.Tools.Count > 0;
                    if (!expectsTools &&
                        !string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrWhiteSpace(content) &&
                        (toolCallsDto == null || toolCallsDto.Count == 0) &&
                        !string.IsNullOrWhiteSpace(reasoningContent))
                    {
                        content = reasoningContent!;
                    }
                    sw.Stop();

                    // If multiple nodes exist and this node returned an empty shell with 0 tokens and no content/tool_calls,
                    // treat it as an upstream node failure so we can fail over to the next backup node.
                    if (candidates.Count > 1 && attempt < candidates.Count - 1 &&
                        (parsed?.choices == null || parsed.choices.Count == 0 ||
                         (string.IsNullOrWhiteSpace(content) && (toolCallsDto == null || toolCallsDto.Count == 0))))
                    {
                        throw new HttpTransport.HttpException($"Node '{node.Name}' returned empty response payload.", 502);
                    }

                    RecordNodeSuccess(node);

                    if (attempt > 0 || !string.Equals(node.Name, "Primary", StringComparison.OrdinalIgnoreCase))
                    {
                        _logSink?.LogFromBackground(
                            $"[RimMind-Core] Request {envelope.RequestId} served by node '{node.Name}' ({node.ApiEndpoint}, model={effectiveModel}, {tokens} tok, {sw.ElapsedMilliseconds}ms).");
                    }
                    else if (_settings.DebugLogging)
                    {
                        _logSink?.LogFromBackground($"[RimMind-Core] << {envelope.RequestId} ({tokens} tok)\n{LogSanitizer.ForLog(content, LogSanitizer.DebugPayloadMaxChars)}");
                    }

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
                catch (OperationCanceledException ex) when (envelope.Ct.IsCancellationRequested)
                {
                    sw.Stop();
                    return ClientExceptionMapper.MapException(ex, nameof(OpenAIClient), envelope.RequestId, "request", _logSink, callerToken: envelope.Ct);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    long cooldownMs = ResolveNodeIsolationCooldownMs(ex, out bool isPermQuota);
                    RecordNodeFailure(node, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), cooldownMs, isPermQuota);

                    if (IsTransientConcurrencyOrRateLimitError(ex) && transientRateLimitedNode == null)
                    {
                        transientRateLimitedNode = node;
                    }

                    if (lastException == null || !isPermQuota || IsPermanentNodeQuotaOrAuthError(lastException))
                    {
                        lastException = ex;
                    }

                    if (attempt < candidates.Count - 1)
                    {
                        var nextNode = candidates[attempt + 1];
                        _logSink?.LogFromBackground(
                            $"[RimMind-Core] Node '{node.Name}' ({node.ApiEndpoint}, model={node.ModelName}) failed for {envelope.RequestId} ({LogSanitizer.ForLog(ex.Message)}). " +
                            $"Failing over to backup node '{nextNode.Name}' ({nextNode.ApiEndpoint}, model={nextNode.ModelName})...", isWarning: false);
                    }
                }
            }

            // If all candidate attempts failed and a node only hit a transient concurrency/queue limit
            // (while backups were absent or quota-exhausted), perform up to 2 short backoff retries on that node.
            if (transientRateLimitedNode != null && !envelope.Ct.IsCancellationRequested)
            {
                var retryNode = transientRateLimitedNode;
                string endpoint = FormatEndpoint(retryNode.ApiEndpoint);
                string effectiveModel = NormalizeModelNameForEndpoint(retryNode.ApiEndpoint, retryNode.ModelName);
                string json = BuildEnvelopeRequestJson(
                    envelope,
                    stream: false,
                    modelNameOverride: effectiveModel,
                    endpointOverride: retryNode.ApiEndpoint);
                bool isLocal = IsLoopbackEndpoint(retryNode.ApiEndpoint);
                float connectTimeout = isLocal ? 300f : 60f;
                bool isOpenCode = IsOpenCodeGo(retryNode.ApiEndpoint, retryNode.ApiKey) ||
                                  string.Equals(retryNode.Provider, "opencode", StringComparison.OrdinalIgnoreCase);
                string? headerName = isOpenCode ? "x-opencode-session" : null;
                string? headerValue = isOpenCode ? ResolveSessionId(envelope) : null;

                for (int backoffAttempt = 1; backoffAttempt <= 2; backoffAttempt++)
                {
                    try
                    {
                        await Task.Delay(1200 * backoffAttempt, envelope.Ct);
                        var sw = Stopwatch.StartNew();
                        (string responseText, long httpStatusCode) = await _postAsync(
                            endpoint, json, $"Bearer {retryNode.ApiKey}",
                            headerName: headerName, headerValue: headerValue,
                            connectTimeout: connectTimeout,
                            cancellationToken: envelope.Ct);

                        var parsed = JsonConvert.DeserializeObject<OpenAIResponseDto>(responseText);
                        string content = parsed?.choices?[0]?.message?.content ?? string.Empty;
                        string? reasoningContent = parsed?.choices?[0]?.message?.reasoning_content;
                        int tokens = parsed?.usage?.total_tokens ?? 0;
                        int promptTokens = parsed?.usage?.prompt_tokens ?? 0;
                        int completionTokens = parsed?.usage?.completion_tokens ?? 0;
                        int cachedTokens = parsed?.usage?.prompt_tokens_details?.cached_tokens ?? 0;
                        var toolCallsDto = parsed?.choices?[0]?.message?.tool_calls;
                        if (string.IsNullOrWhiteSpace(content) &&
                            (toolCallsDto == null || toolCallsDto.Count == 0) &&
                            !string.IsNullOrWhiteSpace(reasoningContent))
                        {
                            content = reasoningContent!;
                        }
                        sw.Stop();

                        RecordNodeSuccess(retryNode);

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
                    catch (OperationCanceledException ex) when (envelope.Ct.IsCancellationRequested)
                    {
                        return ClientExceptionMapper.MapException(ex, nameof(OpenAIClient), envelope.RequestId, "request", _logSink, callerToken: envelope.Ct);
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        if (!IsTransientConcurrencyOrRateLimitError(ex))
                            break;
                    }
                }
            }

            return ClientExceptionMapper.MapException(
                lastException ?? new InvalidOperationException("All configured API endpoints failed."),
                nameof(OpenAIClient),
                envelope.RequestId,
                "request",
                _logSink,
                callerToken: envelope.Ct);
        }

        public async Task<Result<RimMind.Domain.Llm.LlmResponse, RimMindError>> SendStreamAsync(RimMind.Domain.Llm.LlmRequestEnvelope envelope, Action<RimMind.Domain.Llm.LlmChunk> onChunk, CancellationToken ct)
        {
            if (!IsConfigured())
                return Result<RimMind.Domain.Llm.LlmResponse, RimMindError>.Err(RimMindErrors.ClientNotConfigured(nameof(OpenAIClient)));

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var candidates = ResolveOrderedCandidateNodes(nowMs);
            if (candidates.Count == 0)
                return Result<RimMind.Domain.Llm.LlmResponse, RimMindError>.Err(RimMindErrors.ClientNotConfigured(nameof(OpenAIClient)));

            using var callerCts = (ct.CanBeCanceled && envelope.Ct.CanBeCanceled && ct != envelope.Ct)
                ? CancellationTokenSource.CreateLinkedTokenSource(ct, envelope.Ct)
                : null;
            CancellationToken effectiveCt = callerCts?.Token ?? (ct.CanBeCanceled ? ct : envelope.Ct);

            Exception? lastException = null;

            for (int attempt = 0; attempt < candidates.Count; attempt++)
            {
                var node = candidates[attempt];
                if (attempt > 0 && IsNodePermanentlyExhausted(node, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
                    continue;

                string endpoint = FormatEndpoint(node.ApiEndpoint);
                string effectiveModel = NormalizeModelNameForEndpoint(node.ApiEndpoint, node.ModelName);
                string json = BuildEnvelopeRequestJson(
                    envelope,
                    stream: true,
                    modelNameOverride: effectiveModel,
                    endpointOverride: node.ApiEndpoint);

                if (_settings.DebugLogging)
                    _logSink?.LogFromBackground($"[RimMind-Core] >> Stream {envelope.RequestId} (node={node.Name}, model={effectiveModel})\n{LogSanitizer.ForLog(json, LogSanitizer.DebugPayloadMaxChars)}");

                var contentBuilder = new StringBuilder();
                var toolCallAccumulator = new StreamingToolCallAccumulator();
                string? reasoningContent = null;
                int totalTokens = 0;
                int promptTokens = 0;
                int completionTokens = 0;
                int cachedTokens = 0;
                bool emittedAnyChunk = false;

                try
                {
                    bool isLocal = IsLoopbackEndpoint(node.ApiEndpoint);
                    float connectTimeout = ResolveTimeoutSeconds(isLocal, candidates.Count, attempt);

                    using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, endpoint);
                    request.Content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {node.ApiKey}");
                    HttpTransport.EnsureOpenCodeSessionHeader(request, endpoint, $"Bearer {node.ApiKey}", ResolveSessionId(envelope));

                    using var session = StreamSendOverride != null
                        ? await HttpTransport.OpenStreamAsync(request, connectTimeout, effectiveCt, StreamSendOverride).ConfigureAwait(false)
                        : await HttpTransport.OpenStreamAsync(request, connectTimeout, effectiveCt).ConfigureAwait(false);

                    await SseStreamReader.ReadDataLinesAsync(session.Reader, data =>
                    {
                        try
                        {
                            var chunk = JsonConvert.DeserializeObject<OpenAIStreamChunkDto>(data);
                            if (chunk == null) return Task.CompletedTask;

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

                            var delta = chunk.choices?[0]?.delta;
                            if (delta == null) return Task.CompletedTask;

                            if (delta.content != null)
                            {
                                contentBuilder.Append(delta.content);
                                emittedAnyChunk = true;
                                onChunk(new RimMind.Domain.Llm.LlmChunk
                                {
                                    DeltaContent = delta.content,
                                });
                            }

                            if (delta.reasoning_content != null)
                            {
                                reasoningContent += delta.reasoning_content;
                                emittedAnyChunk = true;
                                onChunk(new RimMind.Domain.Llm.LlmChunk
                                {
                                    DeltaReasoningContent = delta.reasoning_content,
                                });
                            }

                            if (delta.tool_calls != null)
                            {
                                foreach (var tc in delta.tool_calls)
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
                        }
                        catch (JsonException)
                        {
                            // Skip malformed SSE chunks
                        }

                        return Task.CompletedTask;
                    }, session.Token, session.Heartbeat).ConfigureAwait(false);

                    RecordNodeSuccess(node);

                    var finalResponse = new RimMind.Domain.Llm.LlmResponse
                    {
                        RequestId = envelope.RequestId,
                        Content = contentBuilder.ToString(),
                        ToolCallsJson = toolCallAccumulator.ToNormalizedJson(),
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
                catch (OperationCanceledException ex) when (effectiveCt.IsCancellationRequested)
                {
                    return ClientExceptionMapper.MapException(ex, nameof(OpenAIClient), envelope.RequestId, "stream", _logSink, callerToken: effectiveCt);
                }
                catch (Exception ex)
                {
                    long cooldownMs = ResolveNodeIsolationCooldownMs(ex, out bool isPermQuota);
                    RecordNodeFailure(node, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), cooldownMs, isPermQuota);

                    if (lastException == null || !isPermQuota || IsPermanentNodeQuotaOrAuthError(lastException))
                    {
                        lastException = ex;
                    }

                    // Only fail over before any text/reasoning chunks have been yielded to the caller,
                    // so mid-stream failover never duplicates partial output.
                    if (emittedAnyChunk)
                    {
                        _logSink?.LogFromBackground(
                            $"[RimMind-Core] Stream node '{node.Name}' ({node.ApiEndpoint}, model={node.ModelName}) failed mid-stream for {envelope.RequestId} ({LogSanitizer.ForLog(ex.Message)}); aborting failover because partial output was already emitted.",
                            isWarning: true);
                        break;
                    }

                    if (attempt < candidates.Count - 1)
                    {
                        var nextNode = candidates[attempt + 1];
                        _logSink?.LogFromBackground(
                            $"[RimMind-Core] Stream node '{node.Name}' ({node.ApiEndpoint}, model={node.ModelName}) failed for {envelope.RequestId} ({LogSanitizer.ForLog(ex.Message)}). " +
                            $"Failing over to backup node '{nextNode.Name}' ({nextNode.ApiEndpoint}, model={nextNode.ModelName})...", isWarning: false);
                    }
                }
            }

            return ClientExceptionMapper.MapException(
                lastException ?? new InvalidOperationException("All configured API endpoints failed."),
                nameof(OpenAIClient),
                envelope.RequestId,
                "stream",
                _logSink,
                callerToken: effectiveCt);
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

        private static bool IsOpenCodeGo(string? endpoint, string? apiKey)
        {
            if (!string.IsNullOrEmpty(endpoint) && endpoint.IndexOf("opencode", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(apiKey) && apiKey.IndexOf("oc_sk_", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        private static string ResolveSessionId(RimMind.Domain.Llm.LlmRequestEnvelope envelope)
        {
            if (!string.IsNullOrEmpty(envelope.NpcId))
                return "rimmind-" + envelope.NpcId;
            if (!string.IsNullOrEmpty(envelope.RequestId))
                return "rimmind-" + envelope.RequestId;
            if (!string.IsNullOrEmpty(envelope.TraceId))
                return "rimmind-" + envelope.TraceId;
            return "rimmind-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        }
    }
}
