using System;
using System.Collections.Generic;
using RimMind.Application.Common.Helpers;
using RimMind.Application.Common.Models.Psychology;
using RimMind.Application.Features.GameEvents;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class P3ToolkitAndGameEventsContracts
    {
        [Fact]
        public void KeyedCooldownGate_enforces_cooldown_save_reload_rollback_and_bounded_pruning()
        {
            ContractCaseRunner.Run(
                ("gate blocks within cooldown window and allows after expiration", () =>
                {
                    var gate = new KeyedCooldownGate<int>(maxEntries: 8);
                    Assert.False(gate.IsOnCooldown(42, currentTick: 1000, cooldownTicks: 500));
                    Assert.True(gate.TryEnter(42, currentTick: 1000, cooldownTicks: 500));
                    Assert.True(gate.IsOnCooldown(42, currentTick: 1200, cooldownTicks: 500));
                    Assert.False(gate.TryEnter(42, currentTick: 1499, cooldownTicks: 500));
                    Assert.False(gate.IsOnCooldown(42, currentTick: 1500, cooldownTicks: 500));
                    Assert.True(gate.TryEnter(42, currentTick: 1500, cooldownTicks: 500));
                }),
                ("save reload tick rollback allows immediate re-entry and updates recorded tick", () =>
                {
                    var gate = new KeyedCooldownGate<string>(maxEntries: 8);
                    Assert.True(gate.TryEnter("pawn-1", currentTick: 50000, cooldownTicks: 2500));
                    Assert.False(gate.IsOnCooldown("pawn-1", currentTick: 1000, cooldownTicks: 2500));
                    Assert.True(gate.TryEnter("pawn-1", currentTick: 1000, cooldownTicks: 2500));
                    Assert.True(gate.TryGetLastTick("pawn-1", out int recorded));
                    Assert.Equal(1000, recorded);
                    Assert.False(gate.TryEnter("pawn-1", currentTick: 1200, cooldownTicks: 2500));
                }),
                ("capacity cap prunes expired and oldest entries automatically", () =>
                {
                    var gate = new KeyedCooldownGate<int>(maxEntries: 3);
                    Assert.True(gate.TryEnter(1, currentTick: 100, cooldownTicks: 500));
                    Assert.True(gate.TryEnter(2, currentTick: 200, cooldownTicks: 500));
                    Assert.True(gate.TryEnter(3, currentTick: 300, cooldownTicks: 500));
                    Assert.Equal(3, gate.Count);

                    // Adding a 4th entry while none are expired evicts the oldest (key 1 at tick 100)
                    Assert.True(gate.TryEnter(4, currentTick: 400, cooldownTicks: 500));
                    Assert.Equal(3, gate.Count);
                    Assert.False(gate.IsOnCooldown(1, currentTick: 400, cooldownTicks: 500));
                    Assert.True(gate.IsOnCooldown(2, currentTick: 400, cooldownTicks: 500));
                    Assert.True(gate.IsOnCooldown(3, currentTick: 400, cooldownTicks: 500));
                    Assert.True(gate.IsOnCooldown(4, currentTick: 400, cooldownTicks: 500));

                    gate.Clear();
                    Assert.Equal(0, gate.Count);
                }));
        }

        [Fact]
        public void ActivityCooldownScaler_clamps_multiplier_and_respects_floor()
        {
            ContractCaseRunner.Run(
                ("neutral and non-positive scales preserve base cooldown", () =>
                {
                    Assert.Equal(1500, ActivityCooldownScaler.Scale(1500, 1.0f));
                    Assert.Equal(1500, ActivityCooldownScaler.Scale(1500, 0f));
                    Assert.Equal(1500, ActivityCooldownScaler.Scale(1500, -1f));
                    Assert.Equal(0, ActivityCooldownScaler.Scale(0, 2.0f));
                }),
                ("inverse scale is clamped between 0.35x and 3.5x", () =>
                {
                    Assert.Equal(750, ActivityCooldownScaler.Scale(1500, 2.0f));
                    Assert.Equal(3000, ActivityCooldownScaler.Scale(1500, 0.5f));
                    Assert.Equal(525, ActivityCooldownScaler.Scale(1500, 100.0f));
                    Assert.Equal(5250, ActivityCooldownScaler.Scale(1500, 0.05f));
                }),
                ("minimum floor overrides scaled cooldown when higher", () =>
                {
                    Assert.Equal(2500, ActivityCooldownScaler.Scale(3000, 2.0f, minFloorTicks: 2500));
                    Assert.Equal(3000, ActivityCooldownScaler.Scale(6000, 2.0f, minFloorTicks: 2500));
                }));
        }

        [Fact]
        public void GameEventHub_and_SignificancePolicy_preserve_contracts()
        {
            ContractCaseRunner.Run(
                ("hub delivers typed events, isolates subscriber exceptions, and unsubscribes by owner", () =>
                {
                    var errors = new List<(string Owner, Exception Error)>();
                    var hub = new GameEventHub((owner, ex) => errors.Add((owner, ex)));
                    var received = new List<string>();

                    hub.Subscribe<SkillLevelUpGameEvent>("RimMind.Dialogue", evt =>
                        received.Add($"dlg:{evt.PawnId}:{evt.SkillDefName}:{evt.OldLevel}->{evt.NewLevel}"));
                    hub.Subscribe<SkillLevelUpGameEvent>("RimMind.Faulty", _ =>
                        throw new InvalidOperationException("simulated subscriber failure"));
                    hub.Subscribe<SkillLevelUpGameEvent>("RimMind.Memory", evt =>
                        received.Add($"mem:{evt.PawnId}:{evt.SkillDefName}:{evt.OldLevel}->{evt.NewLevel}"));

                    hub.Publish(new SkillLevelUpGameEvent(7, "Shooting", 5, 6));

                    Assert.Equal(new[] { "dlg:7:Shooting:5->6", "mem:7:Shooting:5->6" }, received);
                    var err = Assert.Single(errors);
                    Assert.Equal("RimMind.Faulty", err.Owner);

                    received.Clear();
                    Assert.Equal(1, hub.UnsubscribeByOwner("RimMind.Dialogue"));
                    hub.Publish(new SkillLevelUpGameEvent(7, "Shooting", 6, 7));
                    Assert.Equal(new[] { "mem:7:Shooting:6->7" }, received);

                    hub.Clear();
                    Assert.Equal(0, hub.SubscriberCount);
                }),
                ("unified injury significance rule requires bad/tendable/sick flag and severity or life-threat", () =>
                {
                    Assert.False(GameEventSignificancePolicy.IsSignificantInjury(
                        isBad: false, tendable: false, makesSickThought: false, isLifeThreatening: true, severity: 0.9f));
                    Assert.False(GameEventSignificancePolicy.IsSignificantInjury(
                        isBad: true, tendable: false, makesSickThought: false, isLifeThreatening: false, severity: 0.15f));
                    Assert.True(GameEventSignificancePolicy.IsSignificantInjury(
                        isBad: true, tendable: false, makesSickThought: false, isLifeThreatening: false, severity: 0.20f));
                    Assert.True(GameEventSignificancePolicy.IsSignificantInjury(
                        isBad: false, tendable: true, makesSickThought: false, isLifeThreatening: true, severity: 0.05f));
                }),
                ("ThoughtSpec clean label fallback strips RimMind prefix to terminal segment", () =>
                {
                    Assert.Equal("SPECIAL_MOOD", ThoughtSpec.FormatCleanLabelFallback("RimMind.Dialogue.Thought.SPECIAL_MOOD"));
                    Assert.Equal("CUSTOM_TAG", ThoughtSpec.FormatCleanLabelFallback("CUSTOM_TAG"));
                }));
        }
    }
}
