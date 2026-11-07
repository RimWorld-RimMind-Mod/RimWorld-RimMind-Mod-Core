using System;
using RimMind.Application.Features.GameEvents;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        /// <summary>
        /// Shared game-event bus and significance policy for RimMind submodules.
        /// </summary>
        public static class GameEvents
        {
            public static GameEventHub Hub => GameEventHub.Shared;

            public static void Subscribe<TEvent>(string ownerModId, Action<TEvent> handler)
                => Hub.Subscribe(ownerModId, handler);

            public static int UnsubscribeByOwner(string ownerModId)
                => Hub.UnsubscribeByOwner(ownerModId);

            public static void Publish<TEvent>(TEvent evt)
                => Hub.Publish(evt);

            public static void Clear()
                => Hub.Clear();

            public static bool IsSignificantInjury(
                bool isBad,
                bool tendable,
                bool makesSickThought,
                bool isLifeThreatening,
                float severity,
                float minSeverity = GameEventSignificancePolicy.DefaultMinInjurySeverity)
                => GameEventSignificancePolicy.IsSignificantInjury(
                    isBad,
                    tendable,
                    makesSickThought,
                    isLifeThreatening,
                    severity,
                    minSeverity);
        }
    }
}
