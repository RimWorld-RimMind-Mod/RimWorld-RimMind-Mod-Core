using RimMind.Application.Common.Interfaces.Abstractions;

namespace RimMind.Infrastructure.Services.Verse
{
    public sealed class VerseTickProvider : ITickProvider
    {
        public int TicksGame => global::Verse.Find.TickManager?.TicksGame ?? 0;
    }
}
