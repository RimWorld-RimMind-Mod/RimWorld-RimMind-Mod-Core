using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Domain.Events;
using RimMind.Presentation.Runtime;

namespace RimMind.Presentation.Perception
{
    public static class PerceptionBridge
    {
        public static void PublishPerception(int pawnId, string type, string content, float importance, IAgentBus eventBus)
        {
            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(content)) return;
            eventBus.Publish(new PerceptionEvent($"NPC-{pawnId}", pawnId, type, content, importance));
        }

        public static void PublishPerceptionBatch(int pawnId, System.Collections.Generic.List<PerceptionBufferEntry> entries, IAgentBus eventBus)
        {
            if (entries == null || entries.Count == 0) return;
            foreach (var entry in entries)
                eventBus.Publish(new PerceptionEvent($"NPC-{pawnId}", pawnId, entry.PerceptionType, entry.Content, entry.Importance));
        }
    }
}
