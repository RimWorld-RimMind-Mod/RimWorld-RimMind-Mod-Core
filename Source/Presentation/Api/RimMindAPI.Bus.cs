using System;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.UI;
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
