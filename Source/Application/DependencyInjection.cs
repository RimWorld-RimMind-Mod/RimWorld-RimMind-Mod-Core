using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Flywheel;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Json;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Interfaces.Sensor;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Features.AgentBus;
using RimMind.Application.Features.Flywheel;
using RimMind.Application.Features.Json;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Application.Features.Tools;
using RimMind.Application.Common.Interfaces.Async;

namespace RimMind.Application
{
    /// <summary>
    /// Holds references to all services created by AddApplicationServices.
    /// Allows the Composition Root to use direct references instead of resolving back from ServiceLocator.
    /// </summary>
    public sealed class ApplicationServiceBag
    {
        public IAgentBus AgentBus { get; init; } = null!;
        public IToolRegistry ToolRegistry { get; init; } = null!;
        public IFlywheelParameterStore ParameterStore { get; init; } = null!;
        public IFlywheelRuleEngine RuleEngine { get; init; } = null!;
        public IRequestQueue Queue { get; init; } = null!;
        public IJsonExtractor JsonExtractor { get; init; } = null!;
        public ITelemetryCollector Telemetry { get; init; } = null!;
    }

    public static class DependencyInjection
    {
        public static ApplicationServiceBag AddApplicationServices(
            ISettingsProvider? settingsProvider = null,
            ICompletionFence? completionFence = null)
        {
            var agentBus = new AgentBusImpl();

            var toolRegistry = new ToolRegistry();

            var parameterStore = new FlywheelParameterStore();

            var ruleEngine = new FlywheelRuleEngine(parameterStore);

            var queue = new RequestQueue(
                () => settingsProvider,
                completionFence: completionFence);

            var jsonExtractor = new JsonExtractor();

            var telemetry = new FlywheelTelemetryCollector();

            return new ApplicationServiceBag
            {
                AgentBus = agentBus,
                ToolRegistry = toolRegistry,
                ParameterStore = parameterStore,
                RuleEngine = ruleEngine,
                Queue = queue,
                JsonExtractor = jsonExtractor,
                Telemetry = telemetry
            };
        }
    }
}
