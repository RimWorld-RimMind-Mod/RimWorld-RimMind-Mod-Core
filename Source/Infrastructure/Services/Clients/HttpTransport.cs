using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Infrastructure.Services.Clients.Shared;

namespace RimMind.Infrastructure.Services.Clients
{
    internal static class HttpTransport
    {
        // One process-wide client for every Core LLM call (buffered and streaming).
        // HttpClient.Timeout is disabled: each request carries its own linked CancellationTokenSource
        // so the configured per-request timeout (e.g. 300s for local endpoints) is honoured instead of
        // the 100s HttpClient default, and timeouts stay distinguishable from caller cancellation.
        private static readonly HttpClient _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        /// <summary>Upper bound for the redacted body kept on <see cref="HttpException.ResponseBody"/> for classification.</summary>
        private const int ClassificationBodyMaxChars = 4096;

        public class HttpException : Exception
        {
            public int StatusCode { get; }

            /// <summary>
            /// Redacted provider body (capped) for status/keyword classification only.
            /// <see cref="Exception.Message"/> is the log-safe variant (redacted, ~500 chars).
            /// </summary>
            public string? ResponseBody { get; }

            public HttpException(string message, int statusCode)
                : this(message, statusCode, null)
            {
            }

            public HttpException(string message, int statusCode, string? responseBody)
                : base(LogSanitizer.ForLog(message))
            {
                StatusCode = statusCode;
                ResponseBody = responseBody == null ? null : LogSanitizer.ForLog(responseBody, ClassificationBodyMaxChars);
            }

            /// <summary>Builds the exception used for every non-success HTTP status (buffered and streaming paths).</summary>
            public static HttpException FromResponse(int statusCode, string? body, string? reasonPhrase)
            {
                string detail = !string.IsNullOrWhiteSpace(body)
                    ? body!
                    : (reasonPhrase ?? string.Empty);
                return new HttpException($"HTTP {statusCode}: {detail}".TrimEnd(' ', ':'), statusCode, body);
            }

            /// <summary>Text used for keyword classification (rate_limit, insufficient_quota, ...).</summary>
            public string ClassificationText => ResponseBody ?? Message ?? string.Empty;
        }

        public static void EnsureOpenCodeSessionHeader(HttpRequestMessage request, string? url, string? authHeader, string? customSessionId = null)
        {
            if (!string.IsNullOrEmpty(url) && (url.IndexOf("opencode", StringComparison.OrdinalIgnoreCase) >= 0 || (authHeader != null && authHeader.IndexOf("oc_sk_", StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                if (!request.Headers.Contains("x-opencode-session"))
                {
                    string sessionId = !string.IsNullOrEmpty(customSessionId)
                        ? customSessionId!
                        : "rimmind-" + Guid.NewGuid().ToString("N").Substring(0, 12);
                    request.Headers.TryAddWithoutValidation("x-opencode-session", sessionId);
                }
            }
        }

        public static async Task<(string body, long statusCode)> PostAsync(
            string url, string jsonBody, string? authHeader = null,
            string? headerName = null, string? headerValue = null,
            float connectTimeout = 60f,
            CancellationToken cancellationToken = default)
        {
            using var timeoutCts = connectTimeout > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(connectTimeout)) : null;
            using var linkedCts = timeoutCts != null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
                : null;
            var effectiveToken = linkedCts?.Token ?? cancellationToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            if (authHeader != null)
                request.Headers.TryAddWithoutValidation("Authorization", authHeader);
            if (headerName != null && headerValue != null)
                request.Headers.TryAddWithoutValidation(headerName, headerValue);

            EnsureOpenCodeSessionHeader(request, url, authHeader, headerName == "x-opencode-session" ? headerValue : null);

            using var response = await _http.SendAsync(request, effectiveToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            long statusCode = (long)response.StatusCode;

            if (!response.IsSuccessStatusCode)
                throw HttpException.FromResponse((int)statusCode, body, response.ReasonPhrase);

            return (body, statusCode);
        }

        public static async Task<(string body, long statusCode)> GetAsync(
            string url, string? authHeader = null,
            string? headerName = null, string? headerValue = null,
            float connectTimeout = 60f,
            CancellationToken cancellationToken = default)
        {
            using var timeoutCts = connectTimeout > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(connectTimeout)) : null;
            using var linkedCts = timeoutCts != null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
                : null;
            var effectiveToken = linkedCts?.Token ?? cancellationToken;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (authHeader != null)
                request.Headers.TryAddWithoutValidation("Authorization", authHeader);
            if (headerName != null && headerValue != null)
                request.Headers.TryAddWithoutValidation(headerName, headerValue);

            EnsureOpenCodeSessionHeader(request, url, authHeader, headerName == "x-opencode-session" ? headerValue : null);

            using var response = await _http.SendAsync(request, effectiveToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            long statusCode = (long)response.StatusCode;

            if (!response.IsSuccessStatusCode)
                throw HttpException.FromResponse((int)statusCode, body, response.ReasonPhrase);

            return (body, statusCode);
        }

        /// <summary>
        /// Sends a streaming request on the shared client and returns an open session.
        /// <para>
        /// <paramref name="idleTimeoutSeconds"/> bounds time-to-first-byte and, via
        /// <see cref="StreamingSession.Heartbeat"/>, the gap between received lines, so a long but
        /// healthy generation is not cut while a stalled stream is. When either the caller token or
        /// the idle timeout fires, the response is disposed so a pending (non-cancellable)
        /// <c>ReadLineAsync</c> unblocks.
        /// </para>
        /// Non-success statuses throw the same <see cref="HttpException"/> as the buffered path.
        /// </summary>
        public static Task<StreamingSession> OpenStreamAsync(
            HttpRequestMessage request,
            float idleTimeoutSeconds,
            CancellationToken callerToken)
        {
            return OpenStreamAsync(request, idleTimeoutSeconds, callerToken, _http.SendAsync);
        }

        internal static async Task<StreamingSession> OpenStreamAsync(
            HttpRequestMessage request,
            float idleTimeoutSeconds,
            CancellationToken callerToken,
            Func<HttpRequestMessage, HttpCompletionOption, CancellationToken, Task<HttpResponseMessage>> send)
        {
            var timeoutCts = new CancellationTokenSource();
            TimeSpan idle = idleTimeoutSeconds > 0 ? TimeSpan.FromSeconds(idleTimeoutSeconds) : Timeout.InfiniteTimeSpan;
            if (idleTimeoutSeconds > 0)
                timeoutCts.CancelAfter(idle);
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeoutCts.Token);

            HttpResponseMessage? response = null;
            try
            {
                response = await send(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    int status = (int)response.StatusCode;
                    string? reason = response.ReasonPhrase;
                    string? body = null;
                    try
                    {
                        body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Body is diagnostic only; the status code drives classification.
                    }
                    throw HttpException.FromResponse(status, body, reason);
                }

                var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var session = new StreamingSession(response, stream, timeoutCts, linkedCts, idle);
                response = null; // ownership transferred
                return session;
            }
            catch
            {
                response?.Dispose();
                linkedCts.Dispose();
                timeoutCts.Dispose();
                throw;
            }
        }

        /// <summary>An open SSE response bound to a caller token plus an idle timeout.</summary>
        internal sealed class StreamingSession : IDisposable
        {
            private readonly HttpResponseMessage _response;
            private readonly CancellationTokenSource _timeoutCts;
            private readonly CancellationTokenSource _linkedCts;
            private readonly CancellationTokenRegistration _abortRegistration;
            private readonly TimeSpan _idle;
            private int _disposed;

            public StreamReader Reader { get; }

            /// <summary>Linked token (caller cancellation or idle timeout).</summary>
            public CancellationToken Token => _linkedCts.Token;

            internal StreamingSession(HttpResponseMessage response, Stream stream,
                CancellationTokenSource timeoutCts, CancellationTokenSource linkedCts, TimeSpan idle)
            {
                _response = response;
                _timeoutCts = timeoutCts;
                _linkedCts = linkedCts;
                _idle = idle;
                Reader = new StreamReader(stream, Encoding.UTF8);
                _abortRegistration = linkedCts.Token.Register(Abort);
            }

            /// <summary>Re-arms the idle timeout; call after every received line.</summary>
            public void Heartbeat()
            {
                if (_idle == Timeout.InfiniteTimeSpan || Volatile.Read(ref _disposed) != 0) return;
                try
                {
                    _timeoutCts.CancelAfter(_idle);
                }
                catch (ObjectDisposedException)
                {
                }
            }

            private void Abort()
            {
                try
                {
                    _response.Dispose();
                }
                catch (Exception)
                {
                    // Best effort: disposing unblocks the pending read.
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _abortRegistration.Dispose();
                try { Reader.Dispose(); } catch (Exception) { }
                _response.Dispose();
                _linkedCts.Dispose();
                _timeoutCts.Dispose();
            }
        }
    }
}
