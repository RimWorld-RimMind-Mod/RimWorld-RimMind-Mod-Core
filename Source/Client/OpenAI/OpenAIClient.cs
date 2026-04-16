using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    /// <summary>
    /// OpenAI 兼容 API 客户端（支持 OpenAI / DeepSeek / 本地 Ollama 等）。
    /// 使用 UnityWebRequest（RimWorld 内置），在协程外通过 async/await + Task.Delay 轮询。
    /// </summary>
    public class OpenAIClient : IAIClient
    {
        private readonly RimMindCoreSettings _settings;

        public OpenAIClient(RimMindCoreSettings settings)
        {
            _settings = settings;
        }

        public bool IsConfigured() => _settings.IsConfigured();

        public async Task<AIResponse> SendAsync(AIRequest request)
        {
            string endpoint = FormatEndpoint(_settings.apiEndpoint);
            string json = BuildRequestJson(request);

            if (_settings.debugLogging)
                AIRequestQueue.LogFromBackground($"[RimMind] → {request.RequestId}\n{json}");

            var sw = Stopwatch.StartNew();
            try
            {
                string responseText = await PostAsync(endpoint, json);
                var parsed = JsonConvert.DeserializeObject<OpenAIResponseDto>(responseText);
                string content = parsed?.choices?[0]?.message?.content ?? string.Empty;
                int tokens = parsed?.usage?.total_tokens ?? 0;
                sw.Stop();

                if (_settings.debugLogging)
                    AIRequestQueue.LogFromBackground($"[RimMind] ← {request.RequestId} ({tokens} tok)\n{content}");

                var response = AIResponse.Ok(request.RequestId, content, tokens);
                AIDebugLog.Record(request, response, (int)sw.ElapsedMilliseconds);
                return response;
            }
            catch (Exception ex)
            {
                sw.Stop();
                // 通过安全队列在主线程输出，避免后台线程直接写 Log 引发枚举冲突
                AIRequestQueue.LogFromBackground($"[RimMind] Request failed ({request.RequestId}): {ex.Message}", isWarning: true);
                var response = AIResponse.Failure(request.RequestId, ex.Message);
                AIDebugLog.Record(request, response, (int)sw.ElapsedMilliseconds);
                return response;
            }
        }

        // ── 请求体构建 ────────────────────────────────────────────────────────────

        private string BuildRequestJson(AIRequest request)
        {
            List<MessageDto> messages;

            if (request.Messages != null && request.Messages.Count > 0)
            {
                // 多轮模式（AIDialogue）
                messages = request.Messages
                    .Select(m => new MessageDto { role = m.Role, content = m.Content })
                    .ToList();
            }
            else
            {
                // 单轮模式
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

        // ── HTTP ──────────────────────────────────────────────────────────────────

        private async Task<string> PostAsync(string url, string jsonBody)
        {
            bool isLocal = url.Contains("localhost") || url.Contains("127.0.0.1");
            float connectTimeout = isLocal ? 300f : 60f;
            float readTimeout = 60f;

            using var webRequest = new UnityWebRequest(url, "POST");
            webRequest.uploadHandler = new UploadHandlerRaw(
                System.Text.Encoding.UTF8.GetBytes(jsonBody));
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

            if (webRequest.result == UnityEngine.Networking.UnityWebRequest.Result.ConnectionError ||
                webRequest.result == UnityEngine.Networking.UnityWebRequest.Result.ProtocolError)
            {
                string body    = webRequest.downloadHandler.text;
                string unityErr = webRequest.error ?? "";
                string detail  = body.Length > 0 ? body : unityErr;
                throw new Exception($"HTTP {webRequest.responseCode}: {detail}");
            }

            return webRequest.downloadHandler.text;
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
