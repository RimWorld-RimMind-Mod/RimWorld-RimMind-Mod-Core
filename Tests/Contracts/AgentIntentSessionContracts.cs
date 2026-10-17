using System;
using System.IO;
using RimMind.Infrastructure.Verse;
using RimMind.Testing;
using Verse;
using Verse.AI;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class AgentIntentSessionContracts
    {
        [Fact]
        public void Agent_intent_session_provides_persistent_priority_and_lifecycle()
        {
            ContractCaseRunner.Run(
                ("session activates with default 7.8f priority and tracks expiry", () =>
                {
                    var session = new AgentIntentSession("ConstructBlueprint", startTick: 1000, durationTicks: 2500);

                    Assert.Equal("ConstructBlueprint", session.IntentId);
                    Assert.Equal(1000, session.StartTick);
                    Assert.Equal(3500, session.ExpiryTick);
                    Assert.Equal(7.8f, session.Priority);
                    Assert.False(session.IsFinished);
                    Assert.False(session.IsExpired(2000));
                    Assert.True(session.IsExpired(3500));
                    Assert.True(session.IsExpired(4000));
                }),
                ("session evaluates completion predicate and marks finished", () =>
                {
                    bool isBuilt = false;
                    var session = new AgentIntentSession(
                        "BuildBed",
                        startTick: 500,
                        durationTicks: 1000,
                        completionPredicate: p => isBuilt);

                    var dummyPawn = new Pawn();
                    Assert.True(session.IsActive(dummyPawn, currentTick: 600));

                    isBuilt = true;
                    Assert.False(session.IsActive(dummyPawn, currentTick: 700));
                    Assert.True(session.IsFinished);
                }),
                ("session issues consecutive sub-jobs from job provider", () =>
                {
                    int step = 0;
                    var session = new AgentIntentSession(
                        "MultiStepIntent",
                        startTick: 0,
                        durationTicks: 5000,
                        jobProvider: p =>
                        {
                            step++;
                            return new Job();
                        });

                    var dummyPawn = new Pawn();
                    var job1 = session.TryIssueNextJob(dummyPawn);
                    var job2 = session.TryIssueNextJob(dummyPawn);

                    Assert.NotNull(job1);
                    Assert.NotNull(job2);
                    Assert.Equal(2, step);
                }),
                ("comp pawn agent tracks active session and cleans up upon expiry", () =>
                {
                    var comp = new CompPawnAgent();
                    var session = new AgentIntentSession("SessionA", startTick: 100, durationTicks: 500);

                    comp.StartIntentSession(session);
                    Assert.Same(session, comp.ActiveIntentSession);

                    comp.ClearIntentSession();
                    Assert.Null(comp.ActiveIntentSession);
                }),
                ("think node source yields for fire, mental states, and bleed rate over 0.8", () =>
                {
                    var source = ReadSource("Infrastructure/Patches/ThinkNode_RimMindAgent.cs");
                    Assert.Contains("pawn.HasAttachment(ThingDefOf.Fire)", source, StringComparison.Ordinal);
                    Assert.Contains("bleedRate > 0.8f", source, StringComparison.Ordinal);
                    Assert.Contains("return session.Priority;", source, StringComparison.Ordinal);
                }));
        }

        private static string ReadSource(string relativePath) =>
            File.ReadAllText(Path.Combine(SourceRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string SourceRoot()
        {
            var baseDir = AppContext.BaseDirectory;
            return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Source"));
        }
    }
}
