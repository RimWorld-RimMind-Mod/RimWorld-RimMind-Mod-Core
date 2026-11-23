using System;
using System.Collections.Generic;
using System.Net.Http;

namespace RimMind.Application.Common.Interfaces.Client
{
    /// <summary>
    /// Typed context passed to <see cref="IHttpRequestDecorator"/> instances prior to sending an HTTP request.
    /// Carries provider, request, NPC, and session metadata so provider-specific header decorators do not
    /// need vendor checks inside shared transport or OpenAI client code.
    /// </summary>
    public sealed record HttpRequestDecorationContext(
        string? Url,
        string? AuthHeader,
        string? CustomSessionId = null,
        string? RequestId = null,
        string? NpcId = null,
        string? Provider = null);

    /// <summary>
    /// Extensible request decorator invoked by HTTP transports prior to sending requests.
    /// Allows concrete provider adapters (e.g. OpenCode session routing) to attach custom headers without
    /// embedding vendor literals inside generic transport or client code.
    /// </summary>
    public interface IHttpRequestDecorator
    {
        string Id { get; }
        void Decorate(HttpRequestMessage request, HttpRequestDecorationContext context);
    }

    /// <summary>
    /// Thread-safe registry of <see cref="IHttpRequestDecorator"/> extensions applied to outbound HTTP requests.
    /// </summary>
    public static class HttpRequestDecoratorRegistry
    {
        private static readonly object SyncRoot = new object();
        private static readonly List<IHttpRequestDecorator> Registered = new List<IHttpRequestDecorator>();

        public static void Register(IHttpRequestDecorator decorator)
        {
            if (decorator == null) throw new ArgumentNullException(nameof(decorator));
            if (string.IsNullOrWhiteSpace(decorator.Id))
                throw new ArgumentException("Decorator Id cannot be null or whitespace.", nameof(decorator));

            lock (SyncRoot)
            {
                int idx = Registered.FindIndex(d => string.Equals(d.Id, decorator.Id, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) Registered[idx] = decorator;
                else Registered.Add(decorator);
            }
        }

        public static bool Unregister(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            lock (SyncRoot)
            {
                int removed = Registered.RemoveAll(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
                return removed > 0;
            }
        }

        public static void ApplyAll(HttpRequestMessage request, HttpRequestDecorationContext context)
        {
            if (request == null || context == null) return;
            IHttpRequestDecorator[] snapshot;
            lock (SyncRoot)
            {
                if (Registered.Count == 0) return;
                snapshot = Registered.ToArray();
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].Decorate(request, context);
            }
        }

        public static void Clear()
        {
            lock (SyncRoot)
            {
                Registered.Clear();
            }
        }
    }
}
