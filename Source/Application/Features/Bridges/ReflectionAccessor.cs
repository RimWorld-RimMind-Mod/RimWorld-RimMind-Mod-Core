using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;

namespace RimMind.Application.Features.Bridges
{
    /// <summary>
    /// Thread-safe reflection cache and structural circuit breaker for soft-dependency bridges.
    /// Caches <see cref="Type"/>, <see cref="FieldInfo"/>, <see cref="PropertyInfo"/>, and <see cref="MethodInfo"/>
    /// lookups, deduplicates missing-member warnings (warn-once), and tracks structural failure state.
    /// </summary>
    public sealed class ReflectionAccessor
    {
        private readonly ConcurrentDictionary<string, Type?> _typeCache =
            new ConcurrentDictionary<string, Type?>(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<(Type Type, string Name, BindingFlags Flags), FieldInfo?> _fieldCache =
            new ConcurrentDictionary<(Type Type, string Name, BindingFlags Flags), FieldInfo?>();

        private readonly ConcurrentDictionary<(Type Type, string Name, BindingFlags Flags), PropertyInfo?> _propertyCache =
            new ConcurrentDictionary<(Type Type, string Name, BindingFlags Flags), PropertyInfo?>();

        private readonly ConcurrentDictionary<MethodCacheKey, MethodInfo?> _methodCache =
            new ConcurrentDictionary<MethodCacheKey, MethodInfo?>();

        private readonly ConcurrentDictionary<string, byte> _warnedKeys =
            new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        private readonly Func<string, Type?>? _defaultTypeResolver;
        private readonly Action<string>? _defaultWarnCallback;
        private Func<string, Type?>? _typeResolver;
        private Action<string>? _warnCallback;
        private int _structurallyDisabled;
        private string? _structuralFailureReason;

        public ReflectionAccessor(
            Func<string, Type?>? resolveType = null,
            Action<string>? warnCallback = null,
            string? logPrefix = null)
        {
            _defaultTypeResolver = resolveType;
            _defaultWarnCallback = warnCallback;
            _typeResolver = resolveType;
            _warnCallback = warnCallback;
            LogPrefix = logPrefix ?? string.Empty;
        }

        public string LogPrefix { get; }

        public Action<string>? WarnCallback
        {
            get => _warnCallback;
            set => _warnCallback = value;
        }

        public Func<string, Type?>? TypeResolver
        {
            get => _typeResolver;
            set => _typeResolver = value;
        }

        /// <summary>
        /// True after <see cref="MarkStructuralFailure"/> has been called and before <see cref="ResetStructuralFailure"/>
        /// or <see cref="ResetForTesting"/> resets the circuit breaker.
        /// </summary>
        public bool IsStructurallyDisabled => Volatile.Read(ref _structurallyDisabled) != 0;

        /// <summary>Last reason passed to <see cref="MarkStructuralFailure"/>, or null when healthy.</summary>
        public string? StructuralFailureReason => _structuralFailureReason;

        public void WarnOnce(string key, string message)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_warnedKeys.TryAdd(key, 1))
            {
                _warnCallback?.Invoke(message);
            }
        }

        public void MarkStructuralFailure(string reason)
        {
            string normalizedReason = string.IsNullOrWhiteSpace(reason) ? "Structural reflection mismatch." : reason;
            _structuralFailureReason = normalizedReason;
            Volatile.Write(ref _structurallyDisabled, 1);
            WarnOnce($"structural:{normalizedReason}", FormatMessage(normalizedReason));
        }

        public void ResetStructuralFailure()
        {
            Volatile.Write(ref _structurallyDisabled, 0);
            _structuralFailureReason = null;
        }

        public void SetCachedType(string typeName, Type? type)
        {
            if (string.IsNullOrEmpty(typeName)) return;
            _typeCache[typeName] = type;
        }

        public Type? GetCachedType(string typeName, bool warnIfMissing = false)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            var resolved = _typeCache.GetOrAdd(typeName, ResolveTypeSafe);
            if (resolved == null && warnIfMissing)
            {
                WarnOnce(
                    $"type:{typeName}",
                    FormatMessage($"Type '{typeName}' not found."));
            }
            return resolved;
        }

        public FieldInfo? GetCachedField(
            Type? type,
            string fieldName,
            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance,
            bool warnIfMissing = true)
        {
            if (type == null || string.IsNullOrEmpty(fieldName)) return null;
            var field = _fieldCache.GetOrAdd(
                (type, fieldName, flags),
                static k => k.Type.GetField(k.Name, k.Flags));
            if (field == null && warnIfMissing)
            {
                WarnOnce(
                    $"field:{type.FullName}:{fieldName}:{flags}",
                    FormatMessage($"Field '{fieldName}' not found on {type.Name}."));
            }
            return field;
        }

        public PropertyInfo? GetCachedProperty(
            Type? type,
            string propertyName,
            BindingFlags flags = BindingFlags.Public | BindingFlags.Static,
            bool warnIfMissing = true)
        {
            if (type == null || string.IsNullOrEmpty(propertyName)) return null;
            var prop = _propertyCache.GetOrAdd(
                (type, propertyName, flags),
                static k => k.Type.GetProperty(k.Name, k.Flags));
            if (prop == null && warnIfMissing)
            {
                WarnOnce(
                    $"prop:{type.FullName}:{propertyName}:{flags}",
                    FormatMessage($"Property '{propertyName}' not found on {type.Name}."));
            }
            return prop;
        }

        public MethodInfo? GetCachedMethod(
            Type? type,
            string methodName,
            BindingFlags flags = BindingFlags.Public | BindingFlags.Static,
            Type[]? parameterTypes = null,
            bool warnIfMissing = true)
        {
            if (type == null || string.IsNullOrEmpty(methodName)) return null;
            var key = new MethodCacheKey(type, methodName, flags, parameterTypes);
            var method = _methodCache.GetOrAdd(
                key,
                static k => k.ParameterTypes == null
                    ? k.Type.GetMethod(k.Name, k.Flags)
                    : k.Type.GetMethod(k.Name, k.Flags, null, k.ParameterTypes, null));
            if (method == null && warnIfMissing)
            {
                WarnOnce(
                    $"method:{type.FullName}:{methodName}:{flags}:{key.SignatureKey}",
                    FormatMessage($"Method '{methodName}' not found on {type.Name}."));
            }
            return method;
        }

        public object? GetStaticPropertyValue(Type? type, string propertyName)
        {
            if (type == null)
            {
                _warnCallback?.Invoke(FormatMessage("GetStaticPropertyValue: type is null."));
                return null;
            }

            try
            {
                var prop = GetCachedProperty(type, propertyName, BindingFlags.Public | BindingFlags.Static);
                if (prop == null) return null;
                return prop.GetValue(null);
            }
            catch (Exception ex)
            {
                _warnCallback?.Invoke(FormatMessage($"GetStaticPropertyValue({type.Name}.{propertyName}) failed: {ex.Message}"));
                return null;
            }
        }

        public object? GetInstanceFieldValue(
            object? instance,
            string fieldName,
            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance)
        {
            if (instance == null)
            {
                _warnCallback?.Invoke(FormatMessage("GetInstanceFieldValue: instance is null."));
                return null;
            }

            try
            {
                var field = GetCachedField(instance.GetType(), fieldName, flags);
                if (field == null) return null;
                return field.GetValue(instance);
            }
            catch (Exception ex)
            {
                _warnCallback?.Invoke(FormatMessage($"GetInstanceFieldValue({instance.GetType().Name}.{fieldName}) failed: {ex.Message}"));
                return null;
            }
        }

        public object? TryGetSingletonInstance(Type? managerType, string instancePropertyName = "Instance")
        {
            if (managerType == null)
            {
                _warnCallback?.Invoke(FormatMessage("TryGetManagerInstance: managerType is null."));
                return null;
            }

            var instance = GetStaticPropertyValue(managerType, instancePropertyName);
            if (instance == null)
            {
                _warnCallback?.Invoke(FormatMessage($"TryGetManagerInstance: instance is null on {managerType.Name}."));
                return null;
            }

            return instance;
        }

        public void ResetForTesting()
        {
            _typeCache.Clear();
            _fieldCache.Clear();
            _propertyCache.Clear();
            _methodCache.Clear();
            _warnedKeys.Clear();
            _typeResolver = _defaultTypeResolver;
            _warnCallback = _defaultWarnCallback;
            Volatile.Write(ref _structurallyDisabled, 0);
            _structuralFailureReason = null;
        }

        private Type? ResolveTypeSafe(string typeName)
        {
            var resolver = _typeResolver;
            if (resolver == null) return null;
            try
            {
                return resolver(typeName);
            }
            catch (Exception ex)
            {
                WarnOnce($"type-ex:{typeName}", FormatMessage($"Failed to resolve type '{typeName}': {ex.Message}"));
                return null;
            }
        }

        private string FormatMessage(string message)
        {
            if (string.IsNullOrEmpty(LogPrefix)) return message;
            if (message.StartsWith(LogPrefix, StringComparison.Ordinal)) return message;
            return $"{LogPrefix} {message}";
        }

        private readonly struct MethodCacheKey : IEquatable<MethodCacheKey>
        {
            public readonly Type Type;
            public readonly string Name;
            public readonly BindingFlags Flags;
            public readonly Type[]? ParameterTypes;
            public readonly string SignatureKey;

            public MethodCacheKey(Type type, string name, BindingFlags flags, Type[]? parameterTypes)
            {
                Type = type;
                Name = name;
                Flags = flags;
                ParameterTypes = parameterTypes;
                if (parameterTypes == null)
                {
                    SignatureKey = "*";
                }
                else if (parameterTypes.Length == 0)
                {
                    SignatureKey = "()";
                }
                else
                {
                    var names = new string[parameterTypes.Length];
                    for (int i = 0; i < parameterTypes.Length; i++)
                    {
                        names[i] = parameterTypes[i]?.FullName ?? "null";
                    }
                    SignatureKey = string.Join(",", names);
                }
            }

            public bool Equals(MethodCacheKey other)
            {
                return ReferenceEquals(Type, other.Type)
                    && string.Equals(Name, other.Name, StringComparison.Ordinal)
                    && Flags == other.Flags
                    && string.Equals(SignatureKey, other.SignatureKey, StringComparison.Ordinal);
            }

            public override bool Equals(object? obj) => obj is MethodCacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Type.GetHashCode();
                    hash = (hash * 397) ^ Name.GetHashCode();
                    hash = (hash * 397) ^ (int)Flags;
                    hash = (hash * 397) ^ SignatureKey.GetHashCode();
                    return hash;
                }
            }
        }
    }
}
