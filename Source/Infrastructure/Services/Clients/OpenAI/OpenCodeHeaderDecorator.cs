using System;
using System.Net.Http;

namespace RimMind.Infrastructure.Services.Clients.OpenAI
{
    /// <summary>
    /// Concrete OpenCode Go gateway header decorator (attaches <c>x-opencode-session</c>
    /// when targeting OpenCode endpoints or <c>oc_sk_</c> keys). Kept strictly inside the
    /// concrete OpenAI client adapter directory so generic transport stays vendor-free.
    /// </summary>
    internal sealed class OpenCodeHeaderDecorator : IHttpRequestDecorator
    {
        public static readonly OpenCodeHeaderDecorator Instance = new OpenCodeHeaderDecorator();

        public string Id => "core.opencode_session_header";

        public static bool Matches(string? url, string? authOrKey)
        {
            if (!string.IsNullOrEmpty(url) && url!.IndexOf("opencode", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(authOrKey) && authOrKey!.IndexOf("oc_sk_", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        public void Decorate(HttpRequestMessage request, string? url, string? authHeader, string? customSessionId = null)
        {
            if (request == null) return;
            if (!Matches(url, authHeader)) return;

            if (!request.Headers.Contains("x-opencode-session"))
            {
                string sessionId = !string.IsNullOrEmpty(customSessionId)
                    ? customSessionId!
                    : "rimmind-" + Guid.NewGuid().ToString("N").Substring(0, 12);
                request.Headers.TryAddWithoutValidation("x-opencode-session", sessionId);
            }
        }
    }
}
