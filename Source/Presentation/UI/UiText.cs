#nullable enable
using System;
using System.Collections.Generic;
using Verse;

namespace RimMind.Presentation.UI
{
#if NET48 || V1_6 || V1_7
    /// <summary>
    /// Shared main-thread UI translation helper with active-language caching and CanTranslate guards.
    /// Eliminates per-frame key.Translate() lookups and protects against DevMode pseudo-localization scrambling.
    /// </summary>
    public static partial class UiText
    {
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static object? _cachedLanguage;

        internal static Func<string, bool>? CanTranslateOverride { get; set; }
        internal static Func<string, string>? TranslateOverride { get; set; }
        internal static Func<string, object[], string>? TranslateArgsOverride { get; set; }

        public static int CacheCount => Cache.Count;

        public static void ClearCache()
        {
            Cache.Clear();
            _cachedLanguage = null;
        }

        public static string Get(string key, string? fallback = null)
        {
            if (string.IsNullOrEmpty(key))
            {
                return fallback ?? string.Empty;
            }

            if (!UnityData.IsInMainThread)
            {
                return fallback ?? key;
            }

            EnsureCacheValid();

            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (!CanTranslateKey(key))
            {
                return fallback ?? key;
            }

            var translated = TranslateKey(key);
            Cache[key] = translated;
            return translated;
        }

        public static string Format(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            if (args == null || args.Length == 0)
            {
                return Get(key);
            }

            if (!UnityData.IsInMainThread || !CanTranslateKey(key))
            {
                return key;
            }

            return TranslateKeyWithArgs(key, args);
        }

        private static void EnsureCacheValid()
        {
            var activeLanguage = LanguageDatabase.activeLanguage;
            if (!ReferenceEquals(_cachedLanguage, activeLanguage))
            {
                Cache.Clear();
                _cachedLanguage = activeLanguage;
            }
        }

        private static bool CanTranslateKey(string key)
        {
            if (CanTranslateOverride != null)
            {
                return CanTranslateOverride(key);
            }

            return key.CanTranslate();
        }

        private static string TranslateKey(string key)
        {
            if (TranslateOverride != null)
            {
                return TranslateOverride(key);
            }

            return key.Translate().ToString();
        }

        private static string TranslateKeyWithArgs(string key, object[] args)
        {
            if (TranslateArgsOverride != null)
            {
                return TranslateArgsOverride(key, args);
            }

#pragma warning disable CS0618
            return key.Translate(args).ToString();
#pragma warning restore CS0618
        }
    }
#endif
}
