using System;
using RimMind.Infrastructure.Verse;
using Verse;
using Verse.AI;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        public static class Agent
        {
            /// <summary>
            /// Starts a continuous macro-intent session lease on a colonist pawn's <see cref="CompPawnAgent"/>.
            /// Returns true if the pawn has an agent component and the session was started.
            /// </summary>
            public static bool StartIntentSession(
                Pawn? pawn,
                string intentId,
                int startTick,
                int durationTicks = 2500,
                float priority = 7.8f,
                object? target = null,
                Func<Pawn, Job?>? jobProvider = null,
                Func<Pawn, bool>? completionPredicate = null)
            {
                if (pawn == null) return false;
                var comp = pawn.TryGetComp<CompPawnAgent>();
                if (comp == null) return false;

                var session = new AgentIntentSession(
                    intentId: intentId,
                    startTick: startTick,
                    durationTicks: durationTicks,
                    priority: priority,
                    target: target,
                    jobProvider: jobProvider,
                    completionPredicate: completionPredicate);
                comp.StartIntentSession(session);
                return true;
            }

            public static void ClearIntentSession(Pawn? pawn)
            {
                if (pawn == null) return;
                pawn.TryGetComp<CompPawnAgent>()?.ClearIntentSession();
            }

            public static bool HasActiveIntentSession(Pawn? pawn)
            {
                if (pawn == null) return false;
                return pawn.TryGetComp<CompPawnAgent>()?.HasActiveIntentSession == true;
            }
        }
    }
}
