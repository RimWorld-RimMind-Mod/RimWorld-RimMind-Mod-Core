using System;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.UI;
using RimMind.Application.Features.AgentBus;
using RimMind.Domain.Events;
using RimMind.Presentation.Perception;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;
using System.Collections.Generic;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        public static class Bus
        {
            private static readonly RuntimeServiceRef<IAgentBus> AgentBuses =
                RuntimeServiceRef<IAgentBus>.Required();
            private static readonly RuntimeServiceRef<IOverlayService> Overlays =
                RuntimeServiceRef<IOverlayService>.Required();

            public static IAgentBus GetAgentBus() => AgentBuses.Value;

            private static readonly RuntimeServiceRef<IAgentBus> OptionalAgentBuses =
                RuntimeServiceRef<IAgentBus>.Optional();

            /// <summary>
            /// Registers a module-owned event type under <paramref name="name"/> so name-based
            /// subscriptions (for example context-key <c>invalidationTriggers</c>) resolve to it.
            /// Unknown names never fall back to the base <see cref="AgentBusEvent"/>; subscriptions made
            /// before this call are bound when it runs.
            /// </summary>
            public static void RegisterEventType<T>(string name) where T : AgentBusEvent
            {
                IAgentBus? bus = OptionalAgentBuses.ValueOrDefault;
                if (bus != null)
                {
                    bus.RegisterEventType(name, typeof(T));
                    return;
                }
                AgentBusImpl.RegisterKnownEventType(name, typeof(T));
            }

            /// <summary>
            /// Publishes <paramref name="evt"/> on the main thread: inline when called on the main thread,
            /// otherwise queued through <see cref="MainThread.Post"/> (drained every frame, also while paused). Resolves the bus of the runtime that is
            /// current when the action runs, and silently drops the event when no runtime is active.
            /// </summary>
            public static void PublishOnMainThread<T>(T evt) where T : AgentBusEvent
            {
                if (evt == null) return;
                if (MainThreadSnapshot.IsMainThread)
                {
                    OptionalAgentBuses.ValueOrDefault?.Publish(evt);
                    return;
                }
                MainThread.Post(() =>
                {
                    OptionalAgentBuses.ValueOrDefault?.Publish(evt);
                });
            }

            public static void PublishPerception(int pawnId, string type, string content, float importance = 0.5f)
                => PerceptionBridge.PublishPerception(pawnId, type, content, importance, GetAgentBus());

            public static void RegisterPendingRequest(RequestEntry entry)
                => Overlays.Value.RegisterPendingRequest(entry);

            public static IReadOnlyList<RequestEntry> GetPendingRequests()
                => Overlays.Value.GetPendingRequests();

            public static bool DismissPendingRequest(RequestEntry entry)
                => Overlays.Value.TryDismiss(entry);

            internal static IAIClient? GetClient()
                => CurrentRuntime.GetClient();

            public static void InvalidateClientCache()
                => CurrentRuntime.InvalidateClientCache();

            public static IAIClient? GetPlayer2Client()
                => CurrentRuntime.GetPlayer2Client();
        }
    }
}
