using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Json;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Interfaces.UI;
using RimMind.Infrastructure.Mechanisms;
using RimMind.Infrastructure.Services.Clients.OpenAI;
using RimMind.Infrastructure.Services.Clients.Player2;
using RimMind.Infrastructure.Services.Verse;
using RimMind.Infrastructure.UI;
using RimMind.Infrastructure.Verse;
using Verse;

namespace RimMind.Infrastructure
{
    /// <summary>
    /// Holds references to all services created by AddInfrastructureServices.
    /// Allows the Composition Root to use direct references instead of resolving back from ServiceLocator.
    /// </summary>
    public sealed class InfrastructureServiceBag
    {
        public IAudioPlayer AudioPlayer { get; init; } = null!;
        public ITickProvider TickProvider { get; init; } = null!;
        public IThreadChecker ThreadChecker { get; init; } = null!;
        public IPathProvider PathProvider { get; init; } = null!;
        public ILogSink LogSink { get; init; } = null!;
        public ITranslationService TranslationService { get; init; } = null!;
        public IGameMechanismRegistry MechanismRegistry { get; init; } = null!;
        public IWindowService WindowService { get; init; } = null!;
        public IAgentActiveChecker AgentActiveChecker { get; init; } = null!;
        public IPlayer2Lifecycle Player2Lifecycle { get; init; } = null!;
        public IAIRequestTraceLog RequestTraceLog { get; init; } = null!;
    }

    public static class DependencyInjection
    {
        public static InfrastructureServiceBag AddInfrastructureServices(
            IToolRegistry toolRegistry, IJsonExtractor jsonExtractor,
            ISettingsProvider? settingsProvider = null)
        {
            var audioPlayer = new NullAudioPlayer();

            var tickProvider = new VerseTickProvider();

            var threadChecker = new VerseThreadChecker();

            var pathProvider = new VersePathProvider();

            var logSink = new VerseLogSink();

            var translationService = new VerseTranslationService();

            var mechanismRegistry = new GameMechanismRegistry(toolRegistry, jsonExtractor);

            var windowService = new WindowService();

            var agentActiveChecker = new AgentActiveChecker();

            var player2Lifecycle = new Player2LifecycleService(settingsProvider);

            var requestTraceLog = new AIRequestTraceLog();

            return new InfrastructureServiceBag
            {
                AudioPlayer = audioPlayer,
                TickProvider = tickProvider,
                ThreadChecker = threadChecker,
                PathProvider = pathProvider,
                LogSink = logSink,
                TranslationService = translationService,
                MechanismRegistry = mechanismRegistry,
                WindowService = windowService,
                AgentActiveChecker = agentActiveChecker,
                Player2Lifecycle = player2Lifecycle,
                RequestTraceLog = requestTraceLog
            };
        }

        public static void AddGameDependentServices()
        {
            // Verse creates save-owned components. RimMindRuntimeGameComponent
            // publishes them together after StartedNewGame/LoadedGame.
        }

        public static void RegisterBuiltinClientFactories(IExtensionRegistry<IAIClientFactory> registry,
            ILogSink? logSink = null, IOpenAISettings? openAISettings = null)
        {
            registry.Register(new OpenAIClientFactory(openAISettings, logSink));
            registry.Register(new Player2ClientFactory(logSink));
        }
    }
}
