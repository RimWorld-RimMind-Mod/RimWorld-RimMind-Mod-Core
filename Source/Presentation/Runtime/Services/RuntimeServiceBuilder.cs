using System;
using System.Collections.Generic;
using System.Linq;

namespace RimMind.Presentation.Runtime.Services
{
    public sealed class RuntimeServiceBuilder
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        private readonly HashSet<Type> _required = new HashSet<Type>();

        public RuntimeServiceBuilder()
            : this(Guid.NewGuid())
        {
        }

        public RuntimeServiceBuilder(Guid runtimeId)
        {
            if (runtimeId == Guid.Empty)
            {
                throw new ArgumentException("A runtime id must not be empty.", nameof(runtimeId));
            }

            RuntimeId = runtimeId;
        }

        public Guid RuntimeId { get; }

        public RuntimeServiceBuilder Bind<T>(T service)
            where T : class
        {
            EnsureService(service, nameof(service));
            var serviceType = typeof(T);
            if (_services.ContainsKey(serviceType))
            {
                throw new InvalidOperationException($"Runtime service '{serviceType.FullName}' is already bound. Use Replace explicitly.");
            }

            _services.Add(serviceType, service);
            return this;
        }

        public RuntimeServiceBuilder Replace<T>(T service)
            where T : class
        {
            EnsureService(service, nameof(service));
            var serviceType = typeof(T);
            if (!_services.ContainsKey(serviceType))
            {
                throw new InvalidOperationException($"Runtime service '{serviceType.FullName}' cannot be replaced because it is not bound.");
            }

            _services[serviceType] = service;
            return this;
        }

        public RuntimeServiceBuilder Require<T>()
            where T : class
        {
            _required.Add(typeof(T));
            return this;
        }

        public RuntimeServiceSnapshot Build()
        {
            var missing = _required
                .Where(requiredType => !_services.ContainsKey(requiredType))
                .OrderBy(requiredType => requiredType.FullName, StringComparer.Ordinal)
                .Select(requiredType => requiredType.FullName)
                .ToArray();

            if (missing.Length > 0)
            {
                throw new InvalidOperationException($"Required runtime services are missing: {string.Join(", ", missing)}.");
            }

            return new RuntimeServiceSnapshot(
                _services,
                RuntimeId,
                0,
                RuntimeLifecycleState.NeverPublished,
                null);
        }

        private static void EnsureService<T>(T service, string parameterName)
            where T : class
        {
            if (service == null)
            {
                throw new ArgumentNullException(parameterName);
            }
        }
    }
}
