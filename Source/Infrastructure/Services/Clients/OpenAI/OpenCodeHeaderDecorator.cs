using System;
using System.Net.Http;
using RimMind.Application.Common.Interfaces.Client;

namespace RimMind.Infrastructure.Services.Clients.OpenAI
{
    /// <summary>
    /// Concrete OpenCode Go gateway header decorator (attaches <c>x-opencode-session</c>
    /// when targeting OpenCode endpoints, provider <c>opencode</c>, or <c>oc_sk_</c> keys).
    /// Kept strictly inside the concrete OpenAI client adapter directory so generic transport
    /// and OpenAIClient stay vendor-free.
    /// </summary>
    internal sealed class OpenCodeHeaderDecorator : IHttpRequestDecorator
    {
        public static readonly OpenCodeHeaderDecorator Instance = new OpenCodeHeaderDecorator();

        public string Id => "core.opencode_session_header";

        public static bool Matches(string? url, string? authOrKey, string? provider = null)
        {
            if (!string.IsNullOrEmpty(provider) && string.Equals(provider, "opencode", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrEmpty(url) && url!.IndexOf("opencode", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(authOrKey) && authOrKey!.IndexOf("oc_sk_", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        public void Decorate(HttpRequestMessage request, HttpRequestDecorationContext context)
        {
            if (request == null || context == null) return;
            if (!Matches(context.Url, context.AuthHeader, context.Provider)) return;

            if (!request.Headers.Contains("x-opencode-session"))
            {
                string sessionId = ResolveSessionId(context);
                request.Headers.TryAddWithoutValidation("x-opencode-session", sessionId);
            }
        }

        private static string ResolveSessionId(HttpRequestDecorationContext context)
        {
            if (!string.IsNullOrEmpty(context.CustomSessionId))
                return context.CustomSessionId!;
            if (!string.IsNullOrEmpty(context.NpcId))
                return "rimmind-" + context.NpcId;
            if (!string.IsNullOrEmpty(context.RequestId))
                return "rimmind-" + context.RequestId;
            return "rimmind-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        }
    }
}
