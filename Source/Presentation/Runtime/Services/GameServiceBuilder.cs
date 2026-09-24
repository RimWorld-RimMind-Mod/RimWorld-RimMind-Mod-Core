using System;
using System.Collections.Generic;
using System.Linq;

namespace RimMind.Presentation.Runtime.Services
{
    public sealed class GameServiceBuilder
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        private readonly HashSet<Type> _required = new HashSet<Type>();

        public GameServiceBuilder Bind<T>(T service)
            where T : class
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            var serviceType = typeof(T);
            if (_services.ContainsKey(serviceType))
            {
                throw new InvalidOperationException($"Game service '{serviceType.FullName}' is already bound.");
            }

            _services.Add(serviceType, service);
            return this;
        }

        public GameServiceBuilder Require<T>()
            where T : class
        {
            _required.Add(typeof(T));
            return this;
        }

        public GameServiceSnapshot Build()
        {
            var missing = _required
                .Where(requiredType => !_services.ContainsKey(requiredType))
                .OrderBy(requiredType => requiredType.FullName, StringComparer.Ordinal)
                .Select(requiredType => requiredType.FullName)
                .ToArray();

            if (missing.Length > 0)
            {
                throw new InvalidOperationException($"Required game services are missing: {string.Join(", ", missing)}.");
            }

            return new GameServiceSnapshot(
                _services,
                0,
                GameLifecycleState.NeverPublished,
                null);
        }
    }
}
