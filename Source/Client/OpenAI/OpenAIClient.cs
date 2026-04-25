using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using RimMind.Core.Client;
using RimMind.Core.Internal;
using RimMind.Core.Settings;
using Newtonsoft.Json;
using UnityEngine.Networking;
using Verse;

namespace RimMind.Core.Client.OpenAI
{
    public class OpenAIClient : IAIClient
    {
        private sealed class AIHttpException : Exception
        {
            public long StatusCode { get; }

            public AIHttpException(long statusCode, string message) : base(message)
            {
                StatusCode = statusCode;
            }
        }

        private readonly RimMindCoreSettings _settings;

        public OpenAIClient(RimMindCoreSettings settings)
        {
            _settings = settings;
        }

        public bool IsConfigured() => _settings.IsConfigured();

        public bool IsLocalEndpoint => IsLoopbackEndpoint(_settings.apiEndpoint);

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

        public async Task<AIResponse> SendAsync(AIRequest request)
        {
            var response = await SendAsyncInner(request, useResponseFormat: _settings.forceJsonMode && request.UseJsonMode);
            if (!response.Success && IsResponseFormatError(response))
            {
                AIRequestQueue.LogFromBackground($"[RimMind] json_object not supported, retrying without response_format for {request.RequestId}", isWarning: true);
                response = await SendAsyncInner(request, useResponseFormat: false);
            }
            return response;
        }

        private async Task<AIResponse> SendAsyncInner(AIRequest request, bool useResponseFormat)
        {
            string endpoint = FormatEndpoint(_settings.apiEndpoint);
            string json = BuildRequestJson(request, useResponseFormat);

            if (_settings.debugLogging)
                AIRequestQueue.LogFromBackground($"[RimMind] → {request.RequestId}\n{json}");

            var sw = Stopwatch.StartNew();
            try
            {
                (string responseText, long httpStatusCode) = await PostAsync(endpoint, json);
                var parsed = JsonConvert.DeserializeObject<OpenAIResponseDto>(responseText);
                string content = parsed?.choices?[0]?.message?.content ?? string.Empty;
                int tokens = parsed?.usage?.total_tokens ?? 0;
                int promptTokens = parsed?.usage?.prompt_tokens ?? 0;
                int completionTokens = parsed?.usage?.completion_tokens ?? 0;
                int cachedTokens = parsed?.usage?.prompt_tokens_details?.cached_tokens ?? 0;
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
            catch (AIHttpException ex)
            {
                sw.Stop();
                AIRequestQueue.LogFromBackground($"[RimMind] Request failed ({request.RequestId}): {ex.Message}", isWarning: true);
                var response = AIResponse.Failure(request.RequestId, ex.Message);
                response.ProcessingMs = sw.ElapsedMilliseconds;
                response.HttpStatusCode = ex.StatusCode;
                response.RequestPayloadBytes = Encoding.UTF8.GetByteCount(json);
                response.Priority = request.Priority;
                AIDebugLog.Record(request, response, (int)sw.ElapsedMilliseconds);
                return response;
            }
            catch (Exception ex)
            {
                sw.Stop();
                AIRequestQueue.LogFromBackground($"[RimMind] Request failed ({request.RequestId}): {ex.Message}", isWarning: true);
                var response = AIResponse.Failure(request.RequestId, ex.Message);
                response.ProcessingMs = sw.ElapsedMilliseconds;
                response.RequestPayloadBytes = Encoding.UTF8.GetByteCount(json);
                response.Priority = request.Priority;
                AIDebugLog.Record(request, response, (int)sw.ElapsedMilliseconds);
                return response;
            }
        }

        public async Task<AIResponse> SendStructuredAsync(AIRequest request, string? jsonSchema, List<StructuredTool>? tools)
        {
            string endpoint = FormatEndpoint(_settings.apiEndpoint);

            var response = await TrySendStructuredAsync(request, endpoint, jsonSchema, tools, "json_schema");
            if (response.Success || !IsResponseFormatError(response))
                return response;

            AIRequestQueue.LogFromBackground($"[RimMind] json_schema not supported, retrying with json_object for {request.RequestId}", isWarning: true);
            response = await TrySendStructuredAsync(request, endpoint, null, tools, "json_object");
            if (response.Success || !IsResponseFormatError(response))
                return response;

            AIRequestQueue.LogFromBackground($"[RimMind] json_object not supported, retrying without response_format for {request.RequestId}", isWarning: true);
            return await TrySendStructuredAsync(request, endpoint, null, tools, "none");
        }

        public static bool IsResponseFormatError(AIResponse response)
        {
            if (response.HttpStatusCode != 400 && response.HttpStatusCode != 422) return false;
            string err = response.Error ?? "";
            return err.Contains("response_format") || err.Contains("json_schema") || err.Contains("json_object");
        }

        private async Task<AIResponse> TrySendStructuredAsync(AIRequest request, string endpoint, string? jsonSchema, List<StructuredTool>? tools, string formatMode)
        {
            string json = BuildStructuredRequestJson(request, jsonSchema, tools, formatMode);

            if (_settings.debugLogging)
                AIRequestQueue.LogFromBackground($"[RimMind] → Structured {request.RequestId} (format={formatMode})\n{json}");

            var sw = Stopwatch.StartNew();
            try
            {
                (string responseText, long httpStatusCode) = await PostAsync(endpoint, json);
                var parsed = JsonConvert.DeserializeObject<OpenAIResponseDto>(responseText);
                string content = parsed?.choices?[0]?.message?.content ?? string.Empty;
                int tokens = parsed?.usage?.total_tokens ?? 0;
                int promptTokens = parsed?.usage?.prompt_tokens ?? 0;
                int completionTokens = parsed?.usage?.completion_tokens ?? 0;
                int cachedTokens = parsed?.usage?.prompt_tokens_details?.cached_tokens ?? 0;
                var toolCallsDto = parsed?.choices?[0]?.message?.tool_calls;
                sw.Stop();

                if (_settings.debugLogging)
                    AIRequestQueue.LogFromBackground($"[RimMind] ← Structured {request.RequestId} ({tokens} tok)\n{content}");

                var response = new AIResponse
                {
                    Success = true,
                    Content = content,
                    RequestId = request.RequestId,
                    TokensUsed = tokens,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    CachedTokens = cachedTokens,
                    State = AIRequestState.Completed,
                };
                response.ProcessingMs = sw.ElapsedMilliseconds;
                response.HttpStatusCode = httpStatusCode;
                response.RequestPayloadBytes = Encoding.UTF8.GetByteCount(json);
                response.Priority = request.Priority;

                if (toolCallsDto != null && toolCallsDto.Count > 0)
                {
                    var converted = toolCallsDto.Select(tc => new
                    {
                        id = tc.Id,
                        type = tc.Type,
                        function = new
                        {
                            name = tc.Function?.Name,
                            arguments = tc.Function?.Arguments,
                        }
                    }).ToList();
                    response.ToolCallsJson = JsonConvert.SerializeObject(converted);
                }

                AIDebugLog.Record(request, response, (int)sw.ElapsedMilliseconds);
                return response;
            }
            catch (AIHttpException ex)
            {
                sw.Stop();
                AIRequestQueue.LogFromBackground($"[RimMind] Structured request failed ({request.RequestId}): {ex.Message}", isWarning: true);
                var response = AIResponse.Failure(request.RequestId, ex.Message);
                response.ProcessingMs = sw.ElapsedMilliseconds;
                response.HttpStatusCode = ex.StatusCode;
                response.RequestPayloadBytes = Encoding.UTF8.GetByteCount(json);
                response.Priority = request.Priority;
                AIDebugLog.Record(request, response, (int)sw.ElapsedMilliseconds);
                return response;
            }
            catch (Exception ex)
            {
                sw.Stop();
                AIRequestQueue.LogFromBackground($"[RimMind] Structured request failed ({request.RequestId}): {ex.Message}", isWarning: true);
                var response = AIResponse.Failure(request.RequestId, ex.Message);
                response.ProcessingMs = sw.ElapsedMilliseconds;
                response.RequestPayloadBytes = Encoding.UTF8.GetByteCount(json);
                response.Priority = request.Priority;
                AIDebugLog.Record(request, response, (int)sw.ElapsedMilliseconds);
                return response;
            }
        }

        private string BuildRequestJson(AIRequest request, bool useResponseFormat = true)
        {
            List<MessageDto> messages = BuildMessages(request);

            if (useResponseFormat)
                EnsureJsonKeyword(messages);

            var body = new OpenAIRequestDto
            {
                model = _settings.modelName,
                messages = messages,
                max_tokens = request.MaxTokens > 0 ? request.MaxTokens : _settings.maxTokens,
                temperature = request.Temperature,
                stream = false,
            };

            if (useResponseFormat)
                body.response_format = new ResponseFormatDto { type = "json_object" };

            return JsonConvert.SerializeObject(body, Formatting.None,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        private string BuildStructuredRequestJson(AIRequest request, string? jsonSchema, List<StructuredTool>? tools, string formatMode = "json_schema")
        {
            List<MessageDto> messages = BuildMessages(request);

            if (formatMode == "json_object")
                EnsureJsonKeyword(messages);

            var body = new OpenAIRequestDto
            {
                model = _settings.modelName,
                messages = messages,
                max_tokens = request.MaxTokens > 0 ? request.MaxTokens : _settings.maxTokens,
                temperature = request.Temperature,
                stream = false,
            };

            if (formatMode == "json_schema" && !string.IsNullOrEmpty(jsonSchema))
            {
                body.response_format = new ResponseFormatDto
                {
                    type = "json_schema",
                    json_schema = new { name = "response", schema = JsonConvert.DeserializeObject(jsonSchema!) },
                };
            }
            else if (formatMode == "json_object" && (_settings.forceJsonMode || request.UseJsonMode))
            {
                body.response_format = new ResponseFormatDto { type = "json_object" };
            }

            if (tools != null && tools.Count > 0)
            {
                body.tools = new List<ToolDto>();
                foreach (var t in tools)
                {
                    body.tools.Add(new ToolDto
                    {
                        Function = new ToolFunctionDto
                        {
                            Name = t.Name,
                            Description = t.Description,
                            Parameters = t.Parameters != null
                                ? JsonConvert.DeserializeObject(t.Parameters)
                                : new { type = "object", properties = new { } },
                        },
                    });
                }
                if (tools.Any(t => t.ToolChoice == "required"))
                    body.tool_choice = "required";
                else
                    body.tool_choice = "auto";
            }

            return JsonConvert.SerializeObject(body, Formatting.None,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        private List<MessageDto> BuildMessages(AIRequest request)
        {
            var messages = new List<MessageDto>();

            if (request.Messages != null && request.Messages.Count > 0)
            {
                foreach (var m in request.Messages)
                {
                    var dto = new MessageDto { role = m.Role, content = m.Content };
                    if (!string.IsNullOrEmpty(m.ToolCallId))
                        dto.tool_call_id = m.ToolCallId;
                    if (m.ToolCalls != null && m.ToolCalls.Count > 0)
                    {
                        dto.tool_calls = m.ToolCalls.Select(tc => new ToolCallDto
                        {
                            Id = tc.Id,
                            Type = "function",
                            Function = new ToolCallFunctionDto
                            {
                                Name = tc.Name,
                                Arguments = tc.Arguments,
                            },
                        }).ToList();
                    }
                    messages.Add(dto);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(request.SystemPrompt))
                    messages.Add(new MessageDto { role = "system", content = request.SystemPrompt });
                messages.Add(new MessageDto { role = "user", content = request.UserPrompt });
            }

            return messages;
        }

        private static void EnsureJsonKeyword(List<MessageDto> messages)
        {
            foreach (var m in messages)
            {
                if (m.content != null && m.content.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
                    return;
            }
            int lastSys = -1;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].role == "system")
                {
                    lastSys = i;
                    break;
                }
            }
            if (lastSys >= 0)
                messages[lastSys].content = (messages[lastSys].content ?? "") + "\n\nPlease respond in JSON format.";
            else
                messages.Insert(0, new MessageDto { role = "system", content = "Please respond in JSON format." });
        }

        private async Task<(string text, long statusCode)> PostAsync(string url, string jsonBody)
        {
            bool isLocal = IsLoopbackEndpoint(url);
            float connectTimeout = isLocal ? 300f : 60f;
            float readTimeout = 60f;

            using var webRequest = new UnityWebRequest(url, "POST");
            webRequest.uploadHandler = new UploadHandlerRaw(
                Encoding.UTF8.GetBytes(jsonBody));
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Authorization", $"Bearer {_settings.apiKey}");

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
                throw new AIHttpException(webRequest.responseCode, $"HTTP {webRequest.responseCode}: {detail}");
            }

            return (webRequest.downloadHandler.text, webRequest.responseCode);
        }

        private static string FormatEndpoint(string baseUrl)
        {
            if (string.IsNullOrEmpty(baseUrl)) return string.Empty;
            string trimmed = baseUrl.Trim().TrimEnd('/');
            if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return trimmed;
            var uri = new Uri(trimmed);
            string path = uri.AbsolutePath.Trim('/');
            if (!string.IsNullOrEmpty(path))
                return trimmed + "/chat/completions";
            return trimmed + "/v1/chat/completions";
        }
    }
}
