using RimMind.Presentation.Runtime.Services;
using Verse;

namespace RimMind.Presentation.Runtime
{
    internal sealed class VerseLifecycleEventSink : ILifecycleEventSink
    {
        public static readonly VerseLifecycleEventSink Instance = new VerseLifecycleEventSink();

        private VerseLifecycleEventSink()
        {
        }

        public void Emit(LifecycleEvent lifecycleEvent)
        {
            var message = LifecycleEventFormatter.Format(lifecycleEvent);
            if (lifecycleEvent.Kind == LifecycleEventKind.RuntimeBuildRejected)
            {
                Log.Warning(message);
                return;
            }

            Log.Message(message);
        }
    }
}
