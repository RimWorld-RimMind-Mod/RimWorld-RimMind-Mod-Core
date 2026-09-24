using RimMind.Application.Common.Interfaces.Abstractions;
using Verse;

#pragma warning disable CS0618

namespace RimMind.Infrastructure.Services.Verse
{
    public sealed class VerseTranslationService : ITranslationService
    {
        public string Translate(string key)
        {
            try { return key.Translate(); }
            catch { return key; }
        }
        public string Translate(string key, object arg0)
        {
            try { return key.Translate(arg0); }
            catch { return key; }
        }
        public string Translate(string key, object arg0, object arg1)
        {
            try { return key.Translate(arg0, arg1); }
            catch { return key; }
        }
        public string Translate(string key, object arg0, object arg1, object arg2)
        {
            try { return key.Translate(arg0, arg1, arg2); }
            catch { return key; }
        }
    }
}

#pragma warning restore CS0618
