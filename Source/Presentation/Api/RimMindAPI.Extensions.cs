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

            public static bool ShouldSkip(string channel, in SkipCheckArgs args)
            {
                if (string.IsNullOrWhiteSpace(channel)) return false;
                var argsCopy = args;
                return Get<ISkipCheck>().All
                    .Where(s => SkipCheckChannels.Matches(s, channel))
                    .Any(s => s.ShouldSkip(in argsCopy));
            }

            public static bool ShouldSkip(string channel, int pawnId = 0, int recipientPawnId = 0, string? tag = null)
            {
                Pawn? pawn = pawnId > 0 ? RimMindPawnLookup.FindPawnByNumber(pawnId) : null;
                Pawn? recipient = recipientPawnId > 0 ? RimMindPawnLookup.FindPawnByNumber(recipientPawnId) : null;
                var args = new SkipCheckArgs
                {
                    Pawn = pawn,
                    RecipientPawn = recipient,
                    PawnId = pawnId,
                    RecipientPawnId = recipientPawnId,
                    Trigger = tag,
                    IntentId = tag,
                    Tag = tag
                };
                return ShouldSkip(channel, in args);
            }

            public static bool ShouldSkipDialogue(Pawn pawn, string trigger)
                => ShouldSkip(
                    SkipCheckChannels.Dialogue,
                    new SkipCheckArgs
                    {
                        Pawn = pawn,
                        PawnId = pawn?.thingIDNumber ?? 0,
                        Trigger = trigger,
                        Tag = trigger
                    });

            public static bool ShouldSkipFloatMenu()
                => ShouldSkip(SkipCheckChannels.FloatMenu, default);

            public static bool ShouldSkipAction(string intentId)
                => ShouldSkip(
                    SkipCheckChannels.Action,
                    new SkipCheckArgs
                    {
                        IntentId = intentId,
                        Tag = intentId
                    });

            public static bool ShouldSkipStorytellerIncident()
                => ShouldSkip(SkipCheckChannels.StorytellerIncident, default);

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

        public static bool ShouldSkip(string channel, int pawnId = 0, int recipientPawnId = 0, string? tag = null)
            => Ext.ShouldSkip(channel, pawnId, recipientPawnId, tag);

        public static bool ShouldSkip(string channel, in SkipCheckArgs args)
            => Ext.ShouldSkip(channel, in args);
    }
}
