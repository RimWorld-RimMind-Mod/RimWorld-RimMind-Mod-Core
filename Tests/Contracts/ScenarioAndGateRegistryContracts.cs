using System.Collections.Generic;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Features.Context;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    [Collection("ScenarioRegistry")]
    public sealed class ScenarioAndGateRegistryContracts
    {
        [Fact]
        public void Scenario_descriptor_and_relevance_weights_support_module_registration_and_fallback()
        {
            ContractCaseRunner.Run(
                ("module descriptor registration seeds scenario metadata and relevance weights", () =>
                {
                    ScenarioRegistry.Clear();
                    var table = new RelevanceTableImpl();
                    table.RegisterCoreRelevance();

                    ScenarioRegistry.Register(new ScenarioDescriptor
                    {
                        Id = "custom_module_scenario",
                        DefaultBaseRounds = 5,
                        Description = "Custom module scenario",
                        DefaultBudget = 0.55f,
                        L4Mode = L4Mode.MemoryOnly,
                        DefaultExcludeKeys = new[] { "combat_status" },
                        OwnerModId = "RimMind.Custom",
                        RelevanceWeights = new Dictionary<string, float>
                        {
                            ["custom_key"] = 0.92f,
                            ["mood"] = 0.77f
                        }
                    });

                    var meta = ScenarioRegistry.Get("custom_module_scenario");
                    Assert.NotNull(meta);
                    Assert.Equal(5, meta!.DefaultBaseRounds);
                    Assert.Equal("RimMind.Custom", meta.OwnerModId);
                    Assert.Equal(0.92f, table.GetRelevance("custom_module_scenario", "custom_key"));
                    Assert.Equal(0.77f, table.GetRelevance("custom_module_scenario", "mood"));
                    Assert.Equal(0.5f, table.GetRelevance("custom_module_scenario", "unregistered_key"));
                }),
                ("module scenario registration takes precedence over core fallback defaults", () =>
                {
                    ScenarioRegistry.Clear();
                    var table = new RelevanceTableImpl();
                    table.RegisterCoreRelevance();

                    ScenarioRegistry.Register(new ScenarioDescriptor
                    {
                        Id = ScenarioIds.Dialogue,
                        DefaultBaseRounds = 12,
                        Description = "Module Dialogue Scenario",
                        DefaultBudget = 0.65f,
                        L4Mode = L4Mode.BudgetControlled,
                        OwnerModId = RimMindOwnerConsts.DialogueModId,
                        RelevanceWeights = new Dictionary<string, float>
                        {
                            ["dialogue_relation"] = 0.95f,
                            ["mood"] = 0.91f
                        }
                    });

                    // Core fallback runs afterwards (simulating LongEventHandler order) and must not overwrite module registration.
                    ScenarioRegistry.RegisterCoreScenarios();

                    var meta = ScenarioRegistry.Get(ScenarioIds.Dialogue);
                    Assert.NotNull(meta);
                    Assert.Equal(12, meta!.DefaultBaseRounds);
                    Assert.Equal(RimMindOwnerConsts.DialogueModId, meta.OwnerModId);
                    Assert.Equal(0.95f, table.GetRelevance(ScenarioIds.Dialogue, "dialogue_relation"));
                    Assert.Equal(0.91f, table.GetRelevance(ScenarioIds.Dialogue, "mood"));
                    // Core fallback key not overridden by module remains available.
                    Assert.Equal(0.6f, table.GetRelevance(ScenarioIds.Dialogue, "health"));

                    ScenarioRegistry.Clear();
                }));
        }

        [Fact]
        public void Open_skip_check_channels_and_dialogue_trigger_kinds_preserve_compatibility()
        {
            ContractCaseRunner.Run(
                ("legacy ISkipCheck maps to canonical channel constants", () =>
                {
                    ISkipCheck legacyDialogue = new LegacySkipCheck(SkipCheckKind.Dialogue);
                    ISkipCheck legacyAction = new LegacySkipCheck(SkipCheckKind.Action);

                    Assert.Equal(SkipCheckChannels.Dialogue, SkipCheckChannels.ResolveChannel(legacyDialogue));
                    Assert.True(SkipCheckChannels.Matches(legacyDialogue, SkipCheckChannels.Dialogue));
                    Assert.True(SkipCheckChannels.Matches(legacyAction, SkipCheckChannels.Action));
                    Assert.False(SkipCheckChannels.Matches(legacyDialogue, SkipCheckChannels.Action));
                }),
                ("IChannelSkipCheck supports arbitrary custom string channels", () =>
                {
                    ISkipCheck customGate = new CustomChannelSkipCheck("custom_mod_gate");
                    Assert.Equal("custom_mod_gate", SkipCheckChannels.ResolveChannel(customGate));
                    Assert.True(SkipCheckChannels.Matches(customGate, "custom_mod_gate"));
                    Assert.True(SkipCheckChannels.Matches(customGate, "CUSTOM_MOD_GATE"));

                    var args = new SkipCheckArgs
                    {
                        PawnId = 10,
                        RecipientPawnId = 20,
                        Tag = "probe_tag"
                    };
                    Assert.Equal(10, args.PawnId);
                    Assert.Equal(20, args.RecipientPawnId);
                    Assert.Equal("probe_tag", args.Tag);
                    Assert.True(customGate.ShouldSkip(in args));
                }),
                ("DialogueTriggerKinds constants match canonical trigger names", () =>
                {
                    Assert.Equal("Chitchat", DialogueTriggerKinds.Chitchat);
                    Assert.Equal("Auto", DialogueTriggerKinds.Auto);
                    Assert.Equal("PlayerInput", DialogueTriggerKinds.PlayerInput);
                    Assert.Equal("Hediff", DialogueTriggerKinds.Hediff);
                    Assert.Equal("LevelUp", DialogueTriggerKinds.LevelUp);
                    Assert.Equal("Thought", DialogueTriggerKinds.Thought);
                }));
        }

        private sealed class LegacySkipCheck : ISkipCheck
        {
            public LegacySkipCheck(SkipCheckKind kind) => Kind = kind;
            public string Id => "legacy_check";
            public string OwnerModId => "TestMod";
            public SkipCheckKind Kind { get; }
            public bool ShouldSkip(in SkipCheckArgs args) => true;
        }

        private sealed class CustomChannelSkipCheck : IChannelSkipCheck
        {
            public CustomChannelSkipCheck(string channel) => Channel = channel;
            public string Id => "custom_channel_check";
            public string OwnerModId => "TestMod";
            public SkipCheckKind Kind => SkipCheckKind.Action;
            public string Channel { get; }
            public bool ShouldSkip(in SkipCheckArgs args) => args.Tag == "probe_tag" && args.PawnId == 10;
        }
    }
}
