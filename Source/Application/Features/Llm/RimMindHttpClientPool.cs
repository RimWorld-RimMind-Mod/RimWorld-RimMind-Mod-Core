using System;
using System.Net.Http;
using System.Threading;

namespace RimMind.Application.Features.Llm
{
    /// <summary>
    /// Process-wide shared <see cref="HttpClient"/> pool and endpoint helpers.
    /// Prevents socket exhaustion and eliminates duplicate static HttpClient instances across Core and ModelService.
    /// </summary>
    public static class RimMindHttpClientPool
    {
        /// <summary>Shared HttpClient with infinite timeout (per-request CancellationToken controls timeout).</summary>
        public static HttpClient Shared { get; } = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        /// <summary>Shared HttpClient with automatic redirects disabled (for credential-safe endpoint &amp; gateway calls).</summary>
        public static HttpClient NoRedirect { get; } = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        /// <summary>Returns true if the given URL targets localhost / loopback / Docker host.</summary>
        public static bool IsLoopbackEndpoint(string? endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) return false;
            if (!Uri.TryCreate(endpoint!.Trim(), UriKind.Absolute, out var uri)) return false;
            if (uri.IsLoopback) return true;
            string host = uri.Host;
            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "host.docker.internal", StringComparison.OrdinalIgnoreCase);
        }
    }
}
