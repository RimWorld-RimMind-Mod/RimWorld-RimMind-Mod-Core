using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RimMind.Presentation.Runtime.Services
{
    public sealed class GameServiceSnapshot
    {
        private readonly IReadOnlyDictionary<Type, object> _services;

        internal GameServiceSnapshot(
            IDictionary<Type, object> services,
            long generation,
            GameLifecycleState state,
            DateTimeOffset? publishedAtUtc)
        {
            _services = new ReadOnlyDictionary<Type, object>(new Dictionary<Type, object>(services));
            Generation = generation;
            State = state;
            PublishedAtUtc = publishedAtUtc;
        }

        public long Generation { get; }

        public GameLifecycleState State { get; }

        public DateTimeOffset? PublishedAtUtc { get; }

        public int ServiceCount => _services.Count;

        public T GetRequired<T>()
            where T : class
        {
            var service = GetOptional<T>();
            if (service == null)
            {
                throw new GameServiceUnavailableException(typeof(T), State, Generation);
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

        internal GameServiceSnapshot WithPublication(
            long generation,
            GameLifecycleState state,
            DateTimeOffset publishedAtUtc)
        {
            return new GameServiceSnapshot(
                new Dictionary<Type, object>(_services),
                generation,
                state,
                publishedAtUtc);
        }

        internal static GameServiceSnapshot CreateEmpty(
            long generation,
            GameLifecycleState state,
            DateTimeOffset? publishedAtUtc)
        {
            return new GameServiceSnapshot(
                new Dictionary<Type, object>(),
                generation,
                state,
                publishedAtUtc);
        }
    }
}
