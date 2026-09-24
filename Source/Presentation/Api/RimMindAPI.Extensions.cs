using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Models.Agent;
using RimMind.Presentation.Runtime;
using Verse;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        public static class Ext
        {
            public static IExtensionRegistry<T> Get<T>() where T : class, IExtension
                => CurrentRuntime.GetExtensionRegistry<T>();

            public static int UnregisterByOwner<T>(string ownerModId) where T : class, IExtension
            {
                var registry = Get<T>();
                return registry?.UnregisterByOwner(ownerModId) ?? 0;
            }

            public static bool ShouldSkipDialogue(Pawn pawn, string trigger)
                => Get<ISkipCheck>().All
                    .Where(s => s.Kind == SkipCheckKind.Dialogue)
                    .Any(s => s.ShouldSkip(new SkipCheckArgs { Pawn = pawn, Trigger = trigger }));

            public static bool ShouldSkipFloatMenu()
                => Get<ISkipCheck>().All
                    .Where(s => s.Kind == SkipCheckKind.FloatMenu)
                    .Any(s => s.ShouldSkip(default));

            public static bool ShouldSkipAction(string intentId)
                => Get<ISkipCheck>().All
                    .Where(s => s.Kind == SkipCheckKind.Action)
                    .Any(s => s.ShouldSkip(new SkipCheckArgs { IntentId = intentId }));

            public static bool ShouldSkipStorytellerIncident()
                => Get<ISkipCheck>().All
                    .Where(s => s.Kind == SkipCheckKind.StorytellerIncident)
                    .Any(s => s.ShouldSkip(default));

            public static void TriggerDialogue(Pawn pawn, string context, Pawn? recipient = null)
            {
                foreach (var trigger in Get<IDialogueTrigger>().All)
                {
                    trigger.Trigger(pawn, context, recipient);
                }
            }

            public static void NotifyIncidentExecuted()
            {
                foreach (var listener in Get<IIncidentExecutedListener>().All)
                {
                    listener.OnIncidentExecuted();
                }
            }

            public static bool CanTriggerDialogue
                => Get<IDialogueTrigger>()?.All.Any() == true;

            public static void RegisterAgentIdentityProvider(Func<Pawn, AgentIdentity?> provider)
                => CurrentRuntime.RegisterAgentIdentityProvider(provider);

            public static AgentIdentity? GetAgentIdentity(Pawn pawn)
                => CurrentRuntime.GetAgentIdentity(pawn);

            public static void RegisterAgentActionBridge(IAgentActionBridge bridge)
                => CurrentRuntime.RegisterAgentActionBridge(bridge);

            public static IAgentActionBridge GetAgentActionBridge()
                => CurrentRuntime.GetAgentActionBridge();

            public static void RegisterParameterTuner(IParameterTuner tuner)
                => CurrentRuntime.RegisterParameterTuner(tuner);

            public static IReadOnlyList<IParameterTuner> ParameterTuners
                => CurrentRuntime.ParameterTunersList;

        }
    }
}
