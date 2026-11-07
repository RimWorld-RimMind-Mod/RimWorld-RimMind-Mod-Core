using System;

namespace RimMind.Presentation.Runtime.Services
{
    internal sealed class GenerationCachedServiceRef<T>
        where T : class
    {
        private readonly object _cacheLock = new object();
        private readonly bool _required;
        private readonly string _scopeLabel;
        private T? _cached;
        private long _boundGeneration = -1;
        private int _boundState;

        public GenerationCachedServiceRef(bool required, string scopeLabel = "runtime")
        {
            _required = required;
            _scopeLabel = string.IsNullOrWhiteSpace(scopeLabel) ? "runtime" : scopeLabel;
        }

        public bool IsRequired => _required;

        public long BoundGeneration
        {
            get
            {
                lock (_cacheLock)
                {
                    return _boundGeneration;
                }
            }
        }

        public void EnsureRequired(string memberName)
        {
            if (!_required)
            {
                throw new InvalidOperationException($"{memberName} is only valid for a required {_scopeLabel} service reference.");
            }
        }

        public void EnsureOptional(string memberName)
        {
            if (_required)
            {
                throw new InvalidOperationException($"{memberName} is only valid for an optional {_scopeLabel} service reference.");
            }
        }

        public bool NeedsRefresh(long generation)
        {
            lock (_cacheLock)
            {
                return generation > _boundGeneration;
            }
        }

        public void Cache<TState>(long generation, TState state, T? value)
            where TState : struct, Enum
        {
            lock (_cacheLock)
            {
                if (generation > _boundGeneration)
                {
                    _cached = value;
                    _boundGeneration = generation;
                    _boundState = Convert.ToInt32(state);
                }
            }
        }

        public void Refresh<TState>(long generation, TState state, Func<T?> resolver)
            where TState : struct, Enum
        {
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            if (!NeedsRefresh(generation))
            {
                return;
            }

            T? value = resolver();
            Cache(generation, state, value);
        }

        public T GetRequiredValue<TState>(
            Action refresh,
            Func<Type, TState, long, Exception> unavailableFactory)
            where TState : struct, Enum
        {
            if (refresh == null)
            {
                throw new ArgumentNullException(nameof(refresh));
            }

            if (unavailableFactory == null)
            {
                throw new ArgumentNullException(nameof(unavailableFactory));
            }

            EnsureRequired("Value");
            refresh();
            lock (_cacheLock)
            {
                if (_cached == null)
                {
                    var state = (TState)Enum.ToObject(typeof(TState), _boundState);
                    throw unavailableFactory(typeof(T), state, _boundGeneration);
                }

                return _cached;
            }
        }

        public T? GetOptionalValue<TState>(
            Action refresh,
            Action<Type, long, TState>? onMissing = null)
            where TState : struct, Enum
        {
            if (refresh == null)
            {
                throw new ArgumentNullException(nameof(refresh));
            }

            EnsureOptional("ValueOrDefault");
            refresh();
            T? value;
            long generation;
            int rawState;
            lock (_cacheLock)
            {
                value = _cached;
                generation = _boundGeneration;
                rawState = _boundState;
            }

            if (value == null && onMissing != null)
            {
                var state = (TState)Enum.ToObject(typeof(TState), rawState);
                onMissing(typeof(T), generation, state);
            }

            return value;
        }
    }
}
