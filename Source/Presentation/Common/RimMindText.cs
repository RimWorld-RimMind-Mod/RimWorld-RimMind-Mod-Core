using System;
using Verse;

namespace RimMind.Presentation.Api
{
    /// <summary>
    /// Centralized safe translation helper with main-thread guard, <c>.CanTranslate()</c> check,
    /// and fallback formatting so off-thread calls or missing keys never trigger RimWorld DevMode
    /// pseudo-localization scrambling or format exceptions.
    /// </summary>
    public static class RimMindText
    {
        public static string TrOr(string key, string fallback, params object[] args)
        {
            string effectiveFallback = fallback ?? key ?? string.Empty;
            try
            {
                string template = !string.IsNullOrEmpty(key)
                    && UnityData.IsInMainThread
                    && key.CanTranslate()
                    ? key.TranslateSimple()
                    : effectiveFallback;

                if (args == null || args.Length == 0)
                    return template;

                if (template.IndexOf("{0}", StringComparison.Ordinal) < 0
                    && effectiveFallback.IndexOf("{0}", StringComparison.Ordinal) >= 0)
                {
                    template = effectiveFallback;
                }

                return string.Format(template, args);
            }
            catch
            {
                try
                {
                    return args == null || args.Length == 0
                        ? effectiveFallback
                        : string.Format(effectiveFallback, args);
                }
                catch
                {
                    return effectiveFallback;
                }
            }
        }

        public static string Tr(string key, params object[] args)
            => TrOr(key, key ?? string.Empty, args);
    }
}
