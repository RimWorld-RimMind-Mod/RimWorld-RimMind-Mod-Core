using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation.Context;
using RimMind.Presentation.Runtime.Services;
using RimMind.Testing;
using Verse;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class HistoryManagerContract
    {
        [Fact]
        public void History_manager_contracts()
        {
            ContractCaseRunner.Run(
                ("queries return the last complete matching rounds and reject nonpositive limits", QueryWindowIsRoundAndScenarioAware),
                ("committed turns enforce capacity while pending turns stay out of saves", CommittedTurnsEnforceCapacity),
                ("the game component restores history before or after runtime publication", GameComponentBridgesSaveLifecycle));
        }

        private static void QueryWindowIsRoundAndScenarioAware()
        {
            var history = new HistoryManager();
            history.AddTurn("npc", "alpha-1-user", "alpha-1-assistant", "alpha");
            history.AddTurn("npc", "beta-1-user", "beta-1-assistant", "beta");
            history.AddTurn("npc", "alpha-2-user", "alpha-2-assistant", "alpha");
            history.AddPendingTurn("npc", "pending-alpha", "alpha-3-user", "waiting", "alpha");

            Assert.Equal(
                new[] { "alpha-2-user", "alpha-2-assistant" },
                history.GetHistory("npc", 1, "alpha").Select(entry => entry.content));
            Assert.Equal(
                new[] { "alpha-3-user", "waiting" },
                history.GetHistoryForDisplay("npc", 1, "alpha").Select(entry => entry.content));
            Assert.Equal(
                new[] { "beta-1-user", "beta-1-assistant", "alpha-2-user", "alpha-2-assistant" },
                history.GetHistory("npc", 2).Select(entry => entry.content));
            Assert.Empty(history.GetHistory("npc", 0));
            Assert.Empty(history.GetHistoryForDisplay("npc", -1));
        }

        private static void CommittedTurnsEnforceCapacity()
        {
            var direct = new HistoryManager();
            for (var round = 0; round <= 100; round++)
                direct.AddTurn("direct", $"user-{round}", $"assistant-{round}");

            Assert.Equal(150, direct.GetHistoryCount("direct"));
            Assert.Equal("user-26", direct.GetHistory("direct", int.MaxValue)[0].content);

            var pending = new HistoryManager();
            for (var round = 0; round < 100; round++)
                pending.AddTurn("pending", $"user-{round}", $"assistant-{round}");
            pending.AddPendingTurn("pending", "turn-100", "user-100", "waiting");

            var beforeCompletion = pending.GetAllForSaveDict()["pending"];
            Assert.Equal(200, beforeCompletion.Count);
            Assert.DoesNotContain(beforeCompletion, entry => entry.IsPending || entry.Content == "waiting");

            Assert.True(pending.ReplaceAssistantTurn("pending", "turn-100", "assistant-100"));
            Assert.Equal(150, pending.GetHistoryCount("pending"));
            Assert.Equal("assistant-100", pending.GetHistory("pending", int.MaxValue).Last().content);
        }

        private static void GameComponentBridgesSaveLifecycle()
        {
            Scribe_Values.Reset();
            try
            {
                var sourceHub = new RuntimeServiceHub(_ => { });
                var source = new HistoryManager();
                source.AddTurn("npc", "saved-user", "saved-assistant", "chat");
                source.AddPendingTurn("npc", "pending", "unsaved-user", "waiting", "chat");
                Publish(sourceHub, source);
                var sourceComponent = CreateComponent(sourceHub);

                Scribe.mode = LoadSaveMode.Saving;
                sourceComponent.ExposeData();
                string saved = Assert.IsType<string>(Scribe_Values.LastString);
                var persisted = JsonConvert.DeserializeObject<Dictionary<string, List<HistoryEntry>>>(saved)!;
                Assert.Equal(2, persisted["npc"].Count);
                Assert.DoesNotContain(persisted["npc"], entry => entry.IsPending);

                var lateHub = new RuntimeServiceHub(_ => { });
                var lateComponent = CreateComponent(lateHub);
                Scribe_Values.NextString = saved;
                Scribe.mode = LoadSaveMode.LoadingVars;
                lateComponent.ExposeData();
                var lateManager = new HistoryManager();
                Publish(lateHub, lateManager);
                lateComponent.LoadedGame();
                Assert.Equal(
                    new[] { "saved-user", "saved-assistant" },
                    lateManager.GetHistory("npc", 10, "chat").Select(entry => entry.content));

                var earlyHub = new RuntimeServiceHub(_ => { });
                var earlyManager = new HistoryManager();
                Publish(earlyHub, earlyManager);
                var earlyComponent = CreateComponent(earlyHub);
                Scribe_Values.NextString = saved;
                earlyComponent.ExposeData();
                Assert.Equal(
                    new[] { "saved-user", "saved-assistant" },
                    earlyManager.GetHistory("npc", 10, "chat").Select(entry => entry.content));
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
                Scribe_Values.Reset();
            }
        }

        private static HistoryManagerGameComponent CreateComponent(RuntimeServiceHub hub)
            => new HistoryManagerGameComponent(
                new Game(),
                new RuntimeServiceRef<IHistoryManager>(hub, required: false));

        private static void Publish(RuntimeServiceHub hub, IHistoryManager historyManager)
        {
            var builder = new RuntimeServiceBuilder();
            builder.Bind(historyManager).Require<IHistoryManager>();
            var snapshot = builder.Build();
            hub.Publish(snapshot, new RuntimeLifetime(snapshot.RuntimeId, hub.IsCurrent));
        }
    }
}
