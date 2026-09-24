using RimMind.Application.Common.Defaults;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Features.Registry;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.Runtime.Composition
{
    internal static class SettingsComposition
    {
        public static void Compose(
            RuntimeServiceBuilder services,
            ISettingsProvider resolvedSettings,
            IOpenAISettings? openAISettings)
        {
            services.Bind<ISettingsProvider>(resolvedSettings);

            if (resolvedSettings is IContextSettings contextSettings)
                services.Bind<IContextSettings>(contextSettings);
            if (resolvedSettings is IContextBudgetSettings budgetSettings)
                services.Bind<IContextBudgetSettings>(budgetSettings);
            if (resolvedSettings is IContextIncludeSettings includeSettings)
                services.Bind<IContextIncludeSettings>(includeSettings);
            if (resolvedSettings is IContextEnvironmentSettings environmentSettings)
                services.Bind<IContextEnvironmentSettings>(environmentSettings);
            if (resolvedSettings is IAIModelSettings aiModelSettings)
                services.Bind<IAIModelSettings>(aiModelSettings);
            if (resolvedSettings is IApiCredentialSettings apiCredSettings)
                services.Bind<IApiCredentialSettings>(apiCredSettings);
            if (resolvedSettings is ICircuitBreakerSettings circuitBreakerSettings)
                services.Bind<ICircuitBreakerSettings>(circuitBreakerSettings);
            if (resolvedSettings is IContextCalibrationSettings calibrationSettings)
                services.Bind<IContextCalibrationSettings>(calibrationSettings);
            if (resolvedSettings is IQueueSettings queueSettings)
                services.Bind<IQueueSettings>(queueSettings);
            if (resolvedSettings is IAgentTickSettings tickSettings)
                services.Bind<IAgentTickSettings>(tickSettings);
            if (resolvedSettings is IDebugSettings debugSettings)
                services.Bind<IDebugSettings>(debugSettings);
            if (resolvedSettings is IOverlaySettings overlaySettings)
                services.Bind<IOverlaySettings>(overlaySettings);
            if (resolvedSettings is IPromptSettings promptSettings)
                services.Bind<IPromptSettings>(promptSettings);
            if (resolvedSettings is IFlywheelSettings flywheelSettings)
                services.Bind<IFlywheelSettings>(flywheelSettings);

            if (openAISettings != null)
                services.Bind<IOpenAISettings>(openAISettings);
            else if (resolvedSettings is IOpenAISettings openAISettingsFromProvider)
                services.Bind<IOpenAISettings>(openAISettingsFromProvider);
        }

        public static void ComposeApplicationServices(
            RuntimeServiceBuilder services,
            Application.ApplicationServiceBag appBag)
        {
            services.Bind(appBag.AgentBus);
            services.Bind(appBag.ToolRegistry);
            services.Bind(appBag.ParameterStore);
            services.Bind(appBag.RuleEngine);
            services.Bind(appBag.Queue);
            services.Bind<ITickableRequestQueue>((ITickableRequestQueue)appBag.Queue);
            services.Bind(appBag.JsonExtractor);
            services.Bind(appBag.Telemetry);
        }

        public static void ComposeInfrastructureServices(
            RuntimeServiceBuilder services,
            RimMind.Infrastructure.InfrastructureServiceBag infraBag)
        {
            services.Bind(infraBag.AudioPlayer);
            services.Bind(infraBag.TickProvider);
            services.Bind(infraBag.ThreadChecker);
            services.Bind(infraBag.PathProvider);
            services.Bind(infraBag.LogSink);
            services.Bind(infraBag.TranslationService);
            services.Bind(infraBag.MechanismRegistry);
            services.Bind(infraBag.WindowService);
            services.Bind(infraBag.AgentActiveChecker);
            services.Bind(infraBag.Player2Lifecycle);
            services.Bind(infraBag.RequestTraceLog);
        }

        public static void ComposeDefaultExtensionRegistries(
            RuntimeServiceBuilder services,
            ExtensionRegistryCatalog extensions)
        {
            var modCooldownRegistry = extensions.GetExtensionRegistry<IModCooldown>();
            modCooldownRegistry.Register(NullModCooldown.Instance);
            services.Bind(modCooldownRegistry);

            var dialogueTriggerRegistry = extensions.GetExtensionRegistry<IDialogueTrigger>();
            dialogueTriggerRegistry.Register(NullDialogueTrigger.Instance);
            services.Bind(dialogueTriggerRegistry);

            var incidentListenerRegistry = extensions.GetExtensionRegistry<IIncidentExecutedListener>();
            incidentListenerRegistry.Register(NullIncidentExecutedListener.Instance);
            services.Bind(incidentListenerRegistry);

            var skipCheckRegistry = extensions.GetExtensionRegistry<ISkipCheck>();
            skipCheckRegistry.Register(NullSkipCheck.Instance);
            services.Bind(skipCheckRegistry);
        }
    }
}
