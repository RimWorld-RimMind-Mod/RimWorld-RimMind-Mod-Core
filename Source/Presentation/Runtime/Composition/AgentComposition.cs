using System;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Agent.Perception;
using RimMind.Application.Common.Interfaces.Agent.Psychology;
using RimMind.Application.Common.Interfaces.Agent.Social;
using RimMind.Application.Common.Interfaces.Async;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Npc;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Features.Agent;
using RimMind.Application.Features.Agent.InnerVoice;
using RimMind.Application.Features.Agent.Modes;
using RimMind.Application.Features.Agent.Psychology;
using RimMind.Application.Features.Agent.Social;
using RimMind.Presentation.Agent;
using RimMind.Presentation.Llm;
using RimMind.Presentation.Runtime.Services;
using Verse;

namespace RimMind.Presentation.Runtime.Composition
{
    internal sealed class AgentCompositionServices
    {
        public IPawnAgentFactoryVerse PawnAgentFactory { get; init; } = null!;
        public IGameContextBuilder GameContextBuilder { get; init; } = null!;
        public IResponseDispatcher ResponseDispatcher { get; init; } = null!;
    }

    internal static class AgentComposition
    {
        public static AgentCompositionServices ComposeAgents(
            RuntimeServiceBuilder services,
            ExtensionRegistryCatalog extensions,
            ISettingsProvider resolvedSettings,
            IAgentBus agentBus,
            IActionExecutor actionExecutor,
            InnerVoiceHandler innerVoiceHandler,
            IPsychologyWatcher? psychologyWatcher,
            ITickProvider tickProvider,
            ILogSink logSink,
            INpcManagerAccessor npcManagers,
            ICompletionFence completionFence,
            Func<Pawn, AgentIdentity?> identityProvider,
            IThoughtInjector? thoughtInjector = null)
        {
            services.Bind<IAgentIdentityProvider>(new AgentIdentityProviderAdapter(identityProvider));

            var informationDiffuser = new DefaultInformationDiffuser(agentBus, tickProvider);
            services.Bind<IInformationDiffuser>(informationDiffuser);

            var traitEvolver = new RimMind.Infrastructure.Social.VerseTraitEvolver();
            services.Bind<ITraitEvolver>(traitEvolver);

            IDreamThoughtInjector? dreamThoughtInjector = null;
            if (thoughtInjector != null)
            {
                dreamThoughtInjector = new RimMind.Infrastructure.Social.VerseDreamThoughtInjector(thoughtInjector);
                services.Bind<IDreamThoughtInjector>(dreamThoughtInjector);
            }

            var agentLoopScheduler = new AgentLoopScheduler(logSink);
            services.Bind<IAgentLoopScheduler>(agentLoopScheduler);

            var tickSettings = resolvedSettings as IAgentTickSettings
                ?? throw new InvalidOperationException("The settings provider must implement IAgentTickSettings.");

            var pawnAgentFactory = new PawnAgentFactory(
                tickSettings, agentBus, actionExecutor,
                innerVoiceHandler, psychologyWatcher, tickProvider,
                dreamThoughtInjector, traitEvolver,
                logSink, extensions.GetExtensionRegistry<IPerceptionSource>(),
                completionFence);
            services.Bind<IPawnAgentFactoryVerse>(pawnAgentFactory);
            services.Bind<IPawnAgentFactory>(pawnAgentFactory);
            services.Bind(extensions.GetExtensionRegistry<IPerceptionSource>());

            var scopedAgentFactory = new ScopedAgentFactory();
            services.Bind<IScopedAgentFactory>(scopedAgentFactory);

            var scopedAgentManager = new ScopedAgentManager(scopedAgentFactory, agentLoopScheduler);
            services.Bind<IScopedAgentManager>(scopedAgentManager);

            var gameContextBuilder = new GameContextBuilder(
                new PawnContextBuilder(resolvedSettings, logSink),
                new MapContextBuilder(resolvedSettings),
                npcManagers);
            services.Bind<IGameContextBuilder>(gameContextBuilder);

            var responseDispatcher = new ResponseDispatcher(agentBus);
            services.Bind<IResponseDispatcher>(responseDispatcher);

            var modePolicyRegistry = extensions.GetExtensionRegistry<IModeTransitionPolicy>();
            modePolicyRegistry.Register(new DefaultModeTransitionPolicy());
            services.Bind(modePolicyRegistry);

            return new AgentCompositionServices
            {
                PawnAgentFactory = pawnAgentFactory,
                GameContextBuilder = gameContextBuilder,
                ResponseDispatcher = responseDispatcher
            };
        }

        private sealed class AgentIdentityProviderAdapter : IAgentIdentityProvider
        {
            private readonly Func<Pawn, AgentIdentity?> _provider;

            public AgentIdentityProviderAdapter(Func<Pawn, AgentIdentity?> provider)
            {
                _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            }

            public AgentIdentity? GetAgentIdentity(object pawn)
                => _provider((Pawn)pawn);
        }
    }
}
