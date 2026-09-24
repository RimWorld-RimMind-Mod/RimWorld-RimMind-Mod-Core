using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RimMind.Presentation.Runtime.Services
{
    public sealed class RuntimeServiceSnapshot
    {
        private readonly IReadOnlyDictionary<Type, object> _services;

        internal RuntimeServiceSnapshot(
            IDictionary<Type, object> services,
            Guid runtimeId,
            long generation,
            RuntimeLifecycleState state,
            DateTimeOffset? publishedAtUtc)
        {
            _services = new ReadOnlyDictionary<Type, object>(new Dictionary<Type, object>(services));
            RuntimeId = runtimeId;
            Generation = generation;
            State = state;
            PublishedAtUtc = publishedAtUtc;
        }

        public Guid RuntimeId { get; }

        public long Generation { get; }

        public RuntimeLifecycleState State { get; }

        public DateTimeOffset? PublishedAtUtc { get; }

        public int ServiceCount => _services.Count;

        public T GetRequired<T>()
            where T : class
        {
            var service = GetOptional<T>();
            if (service == null)
            {
                throw new RuntimeServiceUnavailableException(typeof(T), State, Generation);
            }

            return service;
        }

        public T? GetOptional<T>()
            where T : class
        {
            return _services.TryGetValue(typeof(T), out var service) ? (T)service : null;
        }

        public bool Contains<T>()
            where T : class
        {
            return _services.ContainsKey(typeof(T));
        }

        internal RuntimeServiceSnapshot WithPublication(
            long generation,
            RuntimeLifecycleState state,
            DateTimeOffset publishedAtUtc)
        {
            return new RuntimeServiceSnapshot(
                new Dictionary<Type, object>(_services),
                RuntimeId,
                generation,
                state,
                publishedAtUtc);
        }

        internal static RuntimeServiceSnapshot CreateEmpty(
            Guid runtimeId,
            long generation,
            RuntimeLifecycleState state,
            DateTimeOffset? publishedAtUtc)
        {
            return new RuntimeServiceSnapshot(
                new Dictionary<Type, object>(),
                runtimeId,
                generation,
                state,
                publishedAtUtc);
        }
    }
}
