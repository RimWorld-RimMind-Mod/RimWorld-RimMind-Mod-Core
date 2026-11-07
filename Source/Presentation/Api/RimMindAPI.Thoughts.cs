using RimMind.Application.Common.Models.Psychology;
using RimMind.Domain.Agent.Psychology;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Psychology;
using Verse;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        /// <summary>
        /// Public facade for AI thought label formatting and dynamic thought injection.
        /// </summary>
        public static class Thoughts
        {
            private static readonly VerseThoughtInjector DefaultInjector = new VerseThoughtInjector();

            public static Result<RimMindDynamicThought, RimMindError> Inject(ThoughtSpec spec)
                => DefaultInjector.Inject(spec);

            public static string FormatCleanLabel(string rawTagOrKey, string defaultPrefixToStrip = "RimMind.")
            {
                if (string.IsNullOrEmpty(rawTagOrKey)) return string.Empty;
                if (UnityData.IsInMainThread && rawTagOrKey.CanTranslate())
                {
                    return rawTagOrKey.Translate().ToString();
                }
                return ThoughtSpec.FormatCleanLabelFallback(rawTagOrKey, defaultPrefixToStrip);
            }
        }
    }
}
