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
using RimMind.Application.Features.Llm;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Services.Clients.Shared;
using Newtonsoft.Json;
using Verse;

namespace RimMind.Infrastructure.Services.Clients.OpenAI
{
    public partial class OpenAIClient : IAIClient
    {
        static OpenAIClient()
        {
            HttpTransport.RegisterDecorator(OpenCodeHeaderDecorator.Instance);
        }

        public delegate Task<(string body, long statusCode)> HttpPostDelegate(
            string url,
            string jsonBody,
            string? authHeader = null,
            string? headerName = null,
            string? headerValue = null,
            float connectTimeout = 60f,
            CancellationToken cancellationToken = default);

        private const long DefaultNodeIsolationMs = EndpointFailoverPolicy.DefaultIsolationMs;
        private const long TransientRateLimitIsolationMs = EndpointFailoverPolicy.DefaultTransientRateLimitIsolationMs;
        private const long PermanentNodeIsolationMs = EndpointFailoverPolicy.DefaultPermanentIsolationMs;

        private static readonly ConcurrentDictionary<string, string> _formatCapabilityCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly EndpointFailoverPolicy FailoverPolicy = new EndpointFailoverPolicy();

        internal static void InvalidateFormatCache()
        {
            _formatCapabilityCache.Clear();
            FailoverPolicy.Reset();
        }

        internal static string? GetCachedFormatCapability(string? endpoint, string? model)
        {
            return _formatCapabilityCache.TryGetValue(BuildFormatCacheKey(endpoint, model), out string? mode)
                ? mode
                : null;
        }

        private string BuildCacheKey() => BuildFormatCacheKey(_settings.ApiEndpoint, _settings.ModelName);

        private static string BuildNodeKey(BackupEndpointNode node) =>
            $"{(node.ApiEndpoint ?? string.Empty).Trim().TrimEnd('/')}|{(node.ModelName ?? string.Empty).Trim()}|{(string.IsNullOrEmpty(node.ApiKey) ? string.Empty : (node.ApiKey.Length > 8 ? node.ApiKey.Substring(node.ApiKey.Length - 8) : node.ApiKey))}";

        internal static bool IsNodeIsolated(BackupEndpointNode node, long nowMs)
            => FailoverPolicy.IsIsolated(BuildNodeKey(node), nowMs);

        internal static bool IsNodePermanentlyExhausted(BackupEndpointNode node, long nowMs)
            => FailoverPolicy.IsPermanentlyExhausted(BuildNodeKey(node), nowMs);

        internal static void RecordNodeSuccess(BackupEndpointNode node)
            => FailoverPolicy.RecordSuccess(BuildNodeKey(node));

        internal static void RecordNodeFailure(BackupEndpointNode node, long nowMs, long cooldownMs = DefaultNodeIsolationMs, bool isPermanentQuotaOrAuth = false)
            => FailoverPolicy.RecordFailure(BuildNodeKey(node), cooldownMs, isPermanentQuotaOrAuth, nowMs);

        private static string ClassificationText(Exception ex) =>
            ex is HttpTransport.HttpException httpEx ? httpEx.ClassificationText : (ex.Message ?? string.Empty);

        internal static bool IsPermanentNodeQuotaOrAuthError(Exception? ex)
        {
            if (ex == null) return false;
            int statusCode = ex is HttpTransport.HttpException httpEx ? httpEx.StatusCode : 0;
            return HttpErrorClassifier.IsQuotaOrAuthError(statusCode, ClassificationText(ex));
        }

        internal static bool IsTransientConcurrencyOrRateLimitError(Exception? ex)
        {
            if (ex == null) return false;
            int statusCode = ex is HttpTransport.HttpException httpEx ? httpEx.StatusCode : 0;
            return HttpErrorClassifier.IsTransientRateLimitOrQueueError(statusCode, ClassificationText(ex));
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
        private readonly HttpPostDelegate? _postAsyncOverride;

        internal Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpCompletionOption, CancellationToken, Task<System.Net.Http.HttpResponseMessage>>? StreamSendOverride { get; set; }

        public OpenAIClient(IOpenAISettings settings, ILogSink? logSink = null, HttpPostDelegate? postAsyncOverride = null)
        {
            _settings = settings;
            _logSink = logSink;
            _postAsyncOverride = postAsyncOverride;
        }

        private static float ResolveTimeoutSeconds(bool isLocal, int candidateCount, int attempt) =>
            isLocal ? 300f : ((candidateCount > 1 && attempt < candidateCount - 1) ? 40f : 60f);

        public bool IsConfigured() => _settings.IsConfigured();

        public bool IsLocalEndpoint => IsLoopbackEndpoint(_settings.ApiEndpoint);

        public void Dispose()
        {
            _formatCapabilityCache.Clear();
        }

        private static bool IsLoopbackEndpoint(string endpoint)
            => RimMindHttpClientPool.IsLoopbackEndpoint(endpoint);

        public bool SupportsStreaming => true;

        public bool SupportsNpcServerState => false;

        internal List<BackupEndpointNode> ResolveOrderedCandidateNodes(long nowMs)
        {
            var allConfigured = new List<BackupEndpointNode>();

            if (!string.IsNullOrWhiteSpace(_settings.ApiEndpoint)
                && (!string.IsNullOrWhiteSpace(_settings.ApiKey) || IsLoopbackEndpoint(_settings.ApiEndpoint)))
            {
                allConfigured.Add(new BackupEndpointNode
                {
                    Name = "Primary",
                    Provider = AIProviders.OpenAI,
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

            return FailoverPolicy.OrderCandidates(allConfigured, BuildNodeKey, nowMs);
        }

        private async Task<(string responseText, long httpStatusCode)> PostWithFormatFallbackAsync(
            BackupEndpointNode node,
            string endpoint,
            string effectiveModel,
            RimMind.Domain.Llm.LlmRequestEnvelope envelope,
            string initialJson,
            float connectTimeout)
        {
            string currentJson = initialJson;
            string cacheKey = BuildFormatCacheKey(node.ApiEndpoint, effectiveModel);
            string authHeader = $"Bearer {node.ApiKey}";
            var decorationContext = new HttpRequestDecorationContext(
                endpoint,
                authHeader,
                CustomSessionId: null,
                RequestId: envelope.RequestId,
                NpcId: envelope.NpcId,
                Provider: node.Provider);

            while (true)
            {
                try
                {
                    (string responseText, long httpStatusCode) = _postAsyncOverride != null
                        ? await _postAsyncOverride(
                            endpoint, currentJson, authHeader,
                            headerName: null, headerValue: null,
                            connectTimeout: connectTimeout,
                            cancellationToken: envelope.Ct)
                        : await HttpTransport.PostAsync(
                            endpoint, currentJson, authHeader,
                            headerName: null, headerValue: null,
                            connectTimeout: connectTimeout,
                            cancellationToken: envelope.Ct,
                            decorationContext: decorationContext);

                    bool isModeration = ClientExceptionMapper.IsContentModerationResponse((int)httpStatusCode, responseText);
                    if (httpStatusCode == 400 && !isModeration && currentJson.IndexOf("\"response_format\"", StringComparison.Ordinal) >= 0)
                    {
                        throw HttpTransport.HttpException.FromResponse((int)httpStatusCode, responseText, null);
                    }

                    if (httpStatusCode >= 400)
                    {
                        throw HttpTransport.HttpException.FromResponse((int)httpStatusCode, responseText, null);
                    }

                    return (responseText, httpStatusCode);
                }
                catch (HttpTransport.HttpException httpEx) when (
                    httpEx.StatusCode == 400 &&
                    !ClientExceptionMapper.IsContentFilteredHttpFailure(httpEx) &&
                    currentJson.IndexOf("\"response_format\"", StringComparison.Ordinal) >= 0)
                {
                    string nextFormat = currentJson.IndexOf("\"json_schema\"", StringComparison.Ordinal) >= 0
                        ? "json_object"
                        : "none";

                    _formatCapabilityCache[cacheKey] = nextFormat;
                    _logSink?.LogFromBackground(
                        $"[RimMind-Core] Node '{node.Name}' ({effectiveModel}) rejected response_format with HTTP 400; downgrading format capability to '{nextFormat}' and retrying on same node.");

                    currentJson = BuildEnvelopeRequestJson(
                        envelope,
                        stream: false,
                        modelNameOverride: effectiveModel,
                        endpointOverride: node.ApiEndpoint,
                        formatModeOverride: nextFormat);
                }
            }
        }

        private Result<RimMind.Domain.Llm.LlmResponse, RimMindError> BuildCompletedResponse(
            string responseText,
            long httpStatusCode,
            RimMind.Domain.Llm.LlmRequestEnvelope envelope,
            BackupEndpointNode node,
            bool allowEmptyFailover,
            Stopwatch sw,
            string effectiveModel,
            int attempt,
            bool logSuccessDetails)
        {
            var parsed = OpenAIRequestSerializer.ParseResponse(responseText, envelope);
            if (sw.IsRunning) sw.Stop();

            if (parsed.IsErr)
            {
                var err = parsed.Error;
                if (err.Code == RimMindErrorCode.ContentFiltered)
                {
                    return Result<RimMind.Domain.Llm.LlmResponse, RimMindError>.Err(err);
                }
                if (err.Code == RimMindErrorCode.ClientTransientFailure)
                {
                    throw new HttpTransport.HttpException(err.Message, 503);
                }
                if (allowEmptyFailover)
                {
                    throw new HttpTransport.HttpException($"Node '{node.Name}' returned empty or invalid response payload: {err.Message}", 502);
                }
                return Result<RimMind.Domain.Llm.LlmResponse, RimMindError>.Err(err);
            }

            var val = parsed.Value;
            if (allowEmptyFailover &&
                string.IsNullOrWhiteSpace(val.Content) &&
                string.IsNullOrEmpty(val.ToolCallsJson))
            {
                throw new HttpTransport.HttpException($"Node '{node.Name}' returned empty response payload.", 502);
            }

            RecordNodeSuccess(node);

            if (logSuccessDetails)
            {
                string cacheSuffix = val.PromptTokens > 0
                    ? $", cached={val.CachedTokens}/{val.PromptTokens}"
                    : string.Empty;
                if (attempt > 0 || !string.Equals(node.Name, "Primary", StringComparison.OrdinalIgnoreCase))
                {
                    _logSink?.LogFromBackground(
                        $"[RimMind-Core] Request {envelope.RequestId} served by node '{node.Name}' ({node.ApiEndpoint}, model={effectiveModel}, {val.TokensUsed} tok{cacheSuffix}, {sw.ElapsedMilliseconds}ms).");
                }
                else if (_settings.DebugLogging)
                {
                    _logSink?.LogFromBackground($"[RimMind-Core] << {envelope.RequestId} ({val.TokensUsed} tok{cacheSuffix})\n{LogSanitizer.ForLog(val.Content, LogSanitizer.DebugPayloadMaxChars)}");
                }
            }

            return Result<RimMind.Domain.Llm.LlmResponse, RimMindError>.Ok(
                val.With(
                    requestId: envelope.RequestId,
                    state: RimMind.Domain.Llm.AIRequestState.Completed,
                    priority: envelope.Priority,
                    processingMs: sw.ElapsedMilliseconds,
                    httpStatusCode: httpStatusCode));
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

                    (string responseText, long httpStatusCode) = await PostWithFormatFallbackAsync(
                        node,
                        endpoint,
                        effectiveModel,
                        envelope,
                        json,
                        connectTimeout);

                    return BuildCompletedResponse(
                        responseText,
                        httpStatusCode,
                        envelope,
                        node,
                        allowEmptyFailover: candidates.Count > 1 && attempt < candidates.Count - 1,
                        sw,
                        effectiveModel,
                        attempt,
                        logSuccessDetails: true);
                }
                catch (OperationCanceledException ex) when (envelope.Ct.IsCancellationRequested)
                {
                    sw.Stop();
                    return ClientExceptionMapper.MapException(ex, nameof(OpenAIClient), envelope.RequestId, "request", _logSink, callerToken: envelope.Ct);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    if (ClientExceptionMapper.IsContentFilteredHttpFailure(ex))
                    {
                        return ClientExceptionMapper.MapException(ex, nameof(OpenAIClient), envelope.RequestId, "request", _logSink, callerToken: envelope.Ct);
                    }

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

                for (int backoffAttempt = 1; backoffAttempt <= 2; backoffAttempt++)
                {
                    try
                    {
                        await Task.Delay(1200 * backoffAttempt, envelope.Ct);
                        var sw = Stopwatch.StartNew();
                        (string responseText, long httpStatusCode) = await PostWithFormatFallbackAsync(
                            retryNode,
                            endpoint,
                            effectiveModel,
                            envelope,
                            json,
                            connectTimeout);

                        return BuildCompletedResponse(
                            responseText,
                            httpStatusCode,
                            envelope,
                            retryNode,
                            allowEmptyFailover: false,
                            sw,
                            effectiveModel,
                            attempt: 0,
                            logSuccessDetails: false);
                    }
                    catch (OperationCanceledException ex) when (envelope.Ct.IsCancellationRequested)
                    {
                        return ClientExceptionMapper.MapException(ex, nameof(OpenAIClient), envelope.RequestId, "request", _logSink, callerToken: envelope.Ct);
                    }
                    catch (Exception ex)
                    {
                        if (ClientExceptionMapper.IsContentFilteredHttpFailure(ex))
                        {
                            return ClientExceptionMapper.MapException(ex, nameof(OpenAIClient), envelope.RequestId, "request", _logSink, callerToken: envelope.Ct);
                        }
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
                    string authHeader = $"Bearer {node.ApiKey}";
                    request.Headers.TryAddWithoutValidation("Authorization", authHeader);
                    HttpTransport.ApplyDecorators(
                        request,
                        new HttpRequestDecorationContext(
                            endpoint,
                            authHeader,
                            CustomSessionId: null,
                            RequestId: envelope.RequestId,
                            NpcId: envelope.NpcId,
                            Provider: node.Provider));

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
                    if (ClientExceptionMapper.IsContentFilteredHttpFailure(ex))
                    {
                        return ClientExceptionMapper.MapException(ex, nameof(OpenAIClient), envelope.RequestId, "stream", _logSink, callerToken: effectiveCt);
                    }

                    if (ex is HttpTransport.HttpException httpEx &&
                        httpEx.StatusCode == 400 &&
                        json.IndexOf("\"response_format\"", StringComparison.Ordinal) >= 0)
                    {
                        string nextFormat = json.IndexOf("\"json_schema\"", StringComparison.Ordinal) >= 0
                            ? "json_object"
                            : "none";
                        _formatCapabilityCache[BuildFormatCacheKey(node.ApiEndpoint, effectiveModel)] = nextFormat;
                    }

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
    }
}
