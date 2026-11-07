using System;
using System.Collections.Generic;

namespace RimMind.Application.Features.Bridges
{
    /// <summary>
    /// Tick-cached soft-dependency detector for optional external mods and their entry-point API types.
    /// Avoids per-call <c>ModsConfig.IsActive</c> and reflection lookups while automatically refreshing
    /// when the game tick advances past <see cref="CacheIntervalTicks"/> or rolls back across save loads.
    /// </summary>
    public sealed class SoftDependencyDetector
    {
        public const int DefaultCacheIntervalTicks = 300;

        private readonly Func<string, bool> _defaultIsModActive;
        private readonly Func<string, Type?> _defaultResolveType;
        private readonly Func<int> _defaultGetCurrentTick;

        private Func<string, bool> _isModActive;
        private Func<string, Type?> _resolveType;
        private Func<int> _getCurrentTick;

        private bool _hasCachedModActive;
        private bool _cachedModActive;
        private int _cacheTick;
        private bool? _forcedModActive;

        private bool _hasResolvedApiType;
        private Type? _resolvedApiType;
        private bool? _forcedApiAvailable;

        public SoftDependencyDetector(
            string packageId,
            IReadOnlyList<string>? candidateTypeNames = null,
            Func<string, bool>? isModActive = null,
            Func<string, Type?>? resolveType = null,
            Func<int>? getCurrentTick = null,
            int cacheIntervalTicks = DefaultCacheIntervalTicks)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                throw new ArgumentException("PackageId cannot be null or whitespace.", nameof(packageId));

            PackageId = packageId;
            CandidateTypeNames = candidateTypeNames ?? Array.Empty<string>();
            CacheIntervalTicks = cacheIntervalTicks > 0 ? cacheIntervalTicks : DefaultCacheIntervalTicks;

            _defaultIsModActive = isModActive ?? (_ => false);
            _defaultResolveType = resolveType ?? (_ => null);
            _defaultGetCurrentTick = getCurrentTick ?? (() => 0);

            _isModActive = _defaultIsModActive;
            _resolveType = _defaultResolveType;
            _getCurrentTick = _defaultGetCurrentTick;
            _cacheTick = -CacheIntervalTicks;
        }

        public string PackageId { get; }

        public IReadOnlyList<string> CandidateTypeNames { get; }

        public int CacheIntervalTicks { get; }

        public bool IsModActive()
        {
            if (_forcedModActive.HasValue)
                return _forcedModActive.Value;

            int now = SafeGetCurrentTick();
            if (!_hasCachedModActive || now < _cacheTick || now - _cacheTick > CacheIntervalTicks)
            {
                _cachedModActive = SafeIsModActive();
                _hasCachedModActive = true;
                _cacheTick = now;
            }

            return _cachedModActive;
        }

        public Type? ResolveApiType()
        {
            if (_forcedApiAvailable.HasValue && !_forcedApiAvailable.Value)
                return null;

            if (!IsModActive())
                return null;

            if (!_hasResolvedApiType)
            {
                _resolvedApiType = null;
                for (int i = 0; i < CandidateTypeNames.Count; i++)
                {
                    string candidate = CandidateTypeNames[i];
                    if (string.IsNullOrWhiteSpace(candidate)) continue;
                    var resolved = SafeResolveType(candidate);
                    if (resolved != null)
                    {
                        _resolvedApiType = resolved;
                        break;
                    }
                }
                _hasResolvedApiType = true;
            }

            return _resolvedApiType;
        }

        public bool IsAvailable()
        {
            if (_forcedApiAvailable.HasValue)
                return _forcedApiAvailable.Value;

            if (!IsModActive())
                return false;

            if (CandidateTypeNames.Count == 0)
                return true;

            return ResolveApiType() != null;
        }

        public void ResetCache()
        {
            _hasCachedModActive = false;
            _cachedModActive = false;
            _cacheTick = -CacheIntervalTicks;
            _forcedModActive = null;
            _hasResolvedApiType = false;
            _resolvedApiType = null;
            _forcedApiAvailable = null;
        }

        public void SetForcedModActiveForTesting(bool? active)
        {
            _forcedModActive = active;
        }

        public void SetForcedApiAvailableForTesting(bool? available)
        {
            _forcedApiAvailable = available;
        }

        public void ConfigureForTesting(
            Func<string, bool>? isModActive = null,
            Func<string, Type?>? resolveType = null,
            Func<int>? getCurrentTick = null)
        {
            if (isModActive != null) _isModActive = isModActive;
            if (resolveType != null) _resolveType = resolveType;
            if (getCurrentTick != null) _getCurrentTick = getCurrentTick;
            ResetCache();
        }

        public void ResetDelegatesForTesting()
        {
            _isModActive = _defaultIsModActive;
            _resolveType = _defaultResolveType;
            _getCurrentTick = _defaultGetCurrentTick;
            ResetCache();
        }

        private bool SafeIsModActive()
        {
            try
            {
                return _isModActive(PackageId);
            }
            catch
            {
                return false;
            }
        }

        private Type? SafeResolveType(string typeName)
        {
            try
            {
                return _resolveType(typeName);
            }
            catch
            {
                return null;
            }
        }

        private int SafeGetCurrentTick()
        {
            try
            {
                return _getCurrentTick();
            }
            catch
            {
                return 0;
            }
        }
    }
}
