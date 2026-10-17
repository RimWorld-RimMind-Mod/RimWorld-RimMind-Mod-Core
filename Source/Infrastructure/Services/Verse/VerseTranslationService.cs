using RimMind.Application.Common.Interfaces.Abstractions;
using Verse;

#pragma warning disable CS0618

namespace RimMind.Infrastructure.Services.Verse
{
    public sealed class VerseTranslationService : ITranslationService
    {
        public static string TranslateSafe(string key)
        {
            try
            {
                if (!string.IsNullOrEmpty(key) && key.CanTranslate())
                    return key.TranslateSimple();
                return key ?? string.Empty;
            }
            catch { return key ?? string.Empty; }
        }

        public static string TranslateSafe(string key, params object[] args)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return string.Empty;
                string template = key.CanTranslate() ? key.TranslateSimple() : key;
                if (args == null || args.Length == 0)
                    return template;
                return string.Format(template, args);
            }
            catch
            {
                return key ?? string.Empty;
            }
        }

        public string Translate(string key) => TranslateSafe(key);

        public string Translate(string key, object arg0)
        {
            if (arg0 is object[] arr)
                return TranslateSafe(key, arr);
            return TranslateSafe(key, new object[] { arg0 });
        }

        public string Translate(string key, object arg0, object arg1) => TranslateSafe(key, arg0, arg1);

        public string Translate(string key, object arg0, object arg1, object arg2) => TranslateSafe(key, arg0, arg1, arg2);
    }
}

#pragma warning restore CS0618
