using System;
using System.Collections.Generic;
using System.Net.Http;

namespace RimMind.Infrastructure.Services.Clients
{
    /// <summary>
    /// Extensible request decorator invoked by <see cref="HttpTransport"/> prior to sending HTTP requests.
    /// Allows concrete provider adapters (e.g. OpenCode session routing) to attach custom headers without
    /// embedding vendor literals inside generic transport code.
    /// </summary>
    public interface IHttpRequestDecorator
    {
        string Id { get; }
        void Decorate(HttpRequestMessage request, string? url, string? authHeader, string? customSessionId = null);
    }

    internal static class HttpRequestDecorators
    {
        private static readonly object SyncRoot = new object();
        private static readonly List<IHttpRequestDecorator> Registered = new List<IHttpRequestDecorator>();

        public static void Register(IHttpRequestDecorator decorator)
        {
            if (decorator == null) throw new ArgumentNullException(nameof(decorator));
            lock (SyncRoot)
            {
                int idx = Registered.FindIndex(d => string.Equals(d.Id, decorator.Id, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) Registered[idx] = decorator;
                else Registered.Add(decorator);
            }
        }

        public static void ApplyAll(HttpRequestMessage request, string? url, string? authHeader, string? customSessionId = null)
        {
            if (request == null) return;
            IHttpRequestDecorator[] snapshot;
            lock (SyncRoot)
            {
                if (Registered.Count == 0) return;
                snapshot = Registered.ToArray();
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].Decorate(request, url, authHeader, customSessionId);
            }
        }
    }
}
