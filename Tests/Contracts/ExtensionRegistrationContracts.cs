using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RimMind.Application.Common.Defaults;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.AgentBus;
using RimMind.Application.Features.Registry;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Enums;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class ExtensionRegistrationContracts
    {
        [Fact]
        public void Builtin_modes_think_for_real_triggers_without_placeholder_capabilities()
        {
            var ticks = new ManualTickProvider { TicksGame = 15000 };
            var manager = new RimMindExtensionManager(
                null, ticks, new AgentBusImpl(), new AgentActionBridgeSlot());
            var registry = new ExtensionRegistry<IAgentMode>();
            manager.RegisterBuiltinModes(registry);
            var mode = registry.FindById(AgentModeId.Proactive.Value)!;
            var agent = new AgentInfo();
            var perceptions = Array.Empty<PerceptionBufferEntry>();

            Assert.True(mode.ShouldThink(agent, perceptions));
            agent.LastThinkTick = ticks.TicksGame - 1;
            Assert.False(mode.ShouldThink(agent, perceptions));
            ticks.TicksGame = agent.LastThinkTick.Value + RimMindDefaults.ProactiveTickInterval;
            Assert.True(mode.ShouldThink(agent, perceptions));
            agent.LastThinkTick = ticks.TicksGame;
            Assert.True(mode.ShouldThink(agent, new[] { new PerceptionBufferEntry() }));

            var extensions = Assert.IsAssignableFrom<IProactiveExtensions>(mode);
            Assert.Null(extensions.ReflectionStrategy);
            Assert.Null(extensions.DailyPlanner);
            Assert.Null(extensions.SocialEventOrganizer);
            Assert.Null(extensions.TraitEvolutionEngine);
            Assert.NotNull(registry.FindById(AgentModeId.Reactive.Value));
        }

        [Fact]
        public void Replacing_single_instance_registrations_emits_structured_warnings()
        {
            var log = new CapturingLogSink();
            var manager = CreateManager(log);
            var firstBridge = new TestActionBridge("first.bridge");
            var replacementBridge = new TestActionBridge("replacement.bridge");

            manager.RegisterAgentIdentityProvider(_ => null);
            manager.RegisterAgentActionBridge(firstBridge);
            Assert.Empty(log.Warnings);

            manager.RegisterAgentIdentityProvider(_ => null);
            manager.RegisterAgentActionBridge(replacementBridge);

            Assert.Collection(
                log.Warnings,
                warning => Assert.Contains("event=agent_identity_provider_replaced", warning),
                warning =>
                {
                    Assert.Contains("event=agent_action_bridge_replaced", warning);
                    Assert.Contains("previous_id=first.bridge", warning);
                    Assert.Contains("replacement_id=replacement.bridge", warning);
                });
        }

        [Fact]
        public void Parameter_tuner_replacement_publishes_one_reused_read_only_snapshot()
        {
            var log = new CapturingLogSink();
            var manager = CreateManager(log);
            MethodInfo? register = typeof(RimMindExtensionManager).GetMethod(
                "RegisterParameterTuner",
                new[] { typeof(IParameterTuner) });
            PropertyInfo? tunersProperty = typeof(RimMindExtensionManager).GetProperty("ParameterTuners");

            Assert.NotNull(register);
            Assert.NotNull(tunersProperty);

            var first = new TestParameterTuner("shared", "first.mod");
            var replacement = new TestParameterTuner("shared", "replacement.mod");
            register.Invoke(manager, new object[] { first });

            var firstRead = Assert.IsAssignableFrom<IReadOnlyList<IParameterTuner>>(tunersProperty.GetValue(manager));
            var secondRead = Assert.IsAssignableFrom<IReadOnlyList<IParameterTuner>>(tunersProperty.GetValue(manager));
            Assert.Same(firstRead, secondRead);
            Assert.True(Assert.IsAssignableFrom<IList>(firstRead).IsReadOnly);

            register.Invoke(manager, new object[] { replacement });

            var replacementRead = Assert.IsAssignableFrom<IReadOnlyList<IParameterTuner>>(tunersProperty.GetValue(manager));
            Assert.NotSame(firstRead, replacementRead);
            Assert.Same(replacement, Assert.Single(replacementRead));
            var warning = Assert.Single(log.Warnings);
            Assert.Contains("event=parameter_tuner_replaced", warning);
            Assert.Contains("tuner_id=shared", warning);
            Assert.Contains("previous_owner=first.mod", warning);
            Assert.Contains("replacement_owner=replacement.mod", warning);
        }

        [Fact]
        public void Reset_clears_all_single_instance_and_tuner_registrations()
        {
            var manager = CreateManager(new CapturingLogSink());
            MethodInfo? register = typeof(RimMindExtensionManager).GetMethod(
                "RegisterParameterTuner",
                new[] { typeof(IParameterTuner) });
            PropertyInfo? tunersProperty = typeof(RimMindExtensionManager).GetProperty("ParameterTuners");
            Assert.NotNull(register);
            Assert.NotNull(tunersProperty);

            manager.RegisterAgentIdentityProvider(_ => null);
            manager.RegisterAgentActionBridge(new TestActionBridge("bridge"));
            register.Invoke(manager, new object[] { new TestParameterTuner("tuner", "owner") });

            manager.Reset();

            Assert.Null(manager.AgentIdentityProvider);
            Assert.Same(NullAgentActionBridge.Instance, manager.AgentActionBridge);
            var tuners = Assert.IsAssignableFrom<IReadOnlyList<IParameterTuner>>(tunersProperty.GetValue(manager));
            Assert.Empty(tuners);
        }

        [Fact]
        public void Runtime_uses_the_registration_snapshot_and_clears_local_state_on_shutdown()
        {
            string source = ReadSource("Presentation/Runtime/RimMindRuntime.cs");

            Assert.DoesNotContain("_parameterTuners.Values.ToList()", source, StringComparison.Ordinal);
            Assert.Contains("ParameterTunersList => _extensionManager.ParameterTuners", source, StringComparison.Ordinal);
            Assert.Contains("_extensionManager.ResetRuntimeLocalState()", source, StringComparison.Ordinal);
        }

        private static RimMindExtensionManager CreateManager(ILogSink log)
            => new RimMindExtensionManager(log, null, new AgentBusImpl(), new AgentActionBridgeSlot());

        private static string ReadSource(string relativePath)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Core", "Source")))
                directory = directory.Parent;
            return File.ReadAllText(Path.Combine(directory!.FullName, "RimMind-Core", "Source", relativePath));
        }

        private sealed class CapturingLogSink : ILogSink
        {
            public List<string> Warnings { get; } = new List<string>();
            public void Message(string msg) { }
            public void Warning(string msg) => Warnings.Add(msg);
            public void Error(string msg) { }
            public void LogFromBackground(string msg, bool isWarning = false) { }
        }

        private sealed class ManualTickProvider : ITickProvider
        {
            public int TicksGame { get; set; }
        }

        private sealed class AgentInfo : IAgentInfo
        {
            public AgentState State => AgentState.Active;
            public string NpcId => "colonist";
            public string Label => "Colonist";
            public int? LastThinkTick { get; set; }
            public int GoalCount => 0;
            public IReadOnlyList<BehaviorRecordDto> GetRecentHistory(int count = 10) => Array.Empty<BehaviorRecordDto>();
            public float GetRecentSuccessRate(int count = 10) => 0;
        }

        private sealed class TestActionBridge : IAgentActionBridge
        {
            public TestActionBridge(string id) => Id = id;
            public string Id { get; }
            public string OwnerModId => Id;
            public void ExecuteAction(string npcId, string actionName, string[]? args = null) { }
            public bool CanExecute(string npcId, string actionName) => false;
            public bool CanExecute(object pawn, string action) => false;
            public void Execute(object pawn, string action, string? targetName = null) { }
            public List<StructuredTool>? GetAvailableTools(object pawn) => null;
        }

        private sealed class TestParameterTuner : IParameterTuner
        {
            public TestParameterTuner(string tunerId, string ownerModId)
            {
                TunerId = tunerId;
                OwnerModId = ownerModId;
            }

            public string Id => TunerId;
            public string OwnerModId { get; }
            public string Name => TunerId;
            public string TunerId { get; }
            public float TuneParameter(string parameterName, float currentValue) => currentValue;
            public bool ShouldApply(string npcId) => true;
        }
    }
}
