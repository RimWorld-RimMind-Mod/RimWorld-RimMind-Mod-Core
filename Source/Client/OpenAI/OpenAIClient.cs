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
            string endpoint = FormatEndpoint(_settings.apiEndpoint);
            string json = BuildRequestJson(request);

            if (_settings.debugLogging)
                AIRequestQueue.LogFromBackground($"[RimMind] → {request.RequestId}\n{json}");

            var sw = Stopwatch.StartNew();
            try
            {
                (string responseText, long httpStatusCode) = await PostAsync(endpoint, json);
                var parsed = JsonConvert.DeserializeObject<OpenAIResponseDto>(responseText);
                string content = parsed?.choices?[0]?.message?.content ?? string.Empty;
                int tokens = parsed?.usage?.total_tokens ?? 0;
                sw.Stop();

                if (_settings.debugLogging)
                    AIRequestQueue.LogFromBackground($"[RimMind] ← {request.RequestId} ({tokens} tok)\n{content}");

                var response = AIResponse.Ok(request.RequestId, content, tokens);
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

        private string BuildRequestJson(AIRequest request)
        {
            List<MessageDto> messages;

            if (request.Messages != null && request.Messages.Count > 0)
            {
                messages = request.Messages
                    .Select(m => new MessageDto { role = m.Role, content = m.Content })
                    .ToList();
            }
            else
            {
                messages = new List<MessageDto>();
                if (!string.IsNullOrEmpty(request.SystemPrompt))
                    messages.Add(new MessageDto { role = "system", content = request.SystemPrompt });
                messages.Add(new MessageDto { role = "user", content = request.UserPrompt });
            }

            var body = new OpenAIRequestDto
            {
                model = _settings.modelName,
                messages = messages,
                max_tokens = request.MaxTokens > 0 ? request.MaxTokens : _settings.maxTokens,
                temperature = request.Temperature,
                stream = false,
            };

            if (_settings.forceJsonMode && request.UseJsonMode)
                body.response_format = new ResponseFormatDto { type = "json_object" };

            return JsonConvert.SerializeObject(body, Formatting.None,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
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
                string body    = webRequest.downloadHandler.text;
                string unityErr = webRequest.error ?? "";
                string detail  = body.Length > 0 ? body : unityErr;
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
