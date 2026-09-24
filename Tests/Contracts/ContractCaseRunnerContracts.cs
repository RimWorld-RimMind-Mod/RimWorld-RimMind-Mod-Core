using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class ContractCaseRunnerContracts
    {
        [Fact]
        public async Task Collects_named_failures_without_hiding_later_scenarios()
        {
            var syncExecutionOrder = new List<string>();
            var syncLaterScenarioRan = false;

            var syncFailure = Assert.Throws<AggregateException>(() =>
                ContractCaseRunner.Run(
                    ("first sync failure", () =>
                    {
                        syncExecutionOrder.Add("first");
                        throw new InvalidOperationException("sync-one");
                    }),
                    ("successful sync scenario", () => syncExecutionOrder.Add("middle")),
                    ("last sync failure", () =>
                    {
                        syncExecutionOrder.Add("last");
                        syncLaterScenarioRan = true;
                        throw new ArgumentException("sync-two");
                    })));

            Assert.True(syncLaterScenarioRan);
            Assert.Equal(new[] { "first", "middle", "last" }, syncExecutionOrder);
            Assert.StartsWith("2/3 contract scenarios failed", syncFailure.Message, StringComparison.Ordinal);
            Assert.Equal(2, syncFailure.InnerExceptions.Count);
            Assert.Contains(syncFailure.InnerExceptions, failure => failure.Message.Contains("first sync failure", StringComparison.Ordinal));
            Assert.Contains(syncFailure.InnerExceptions, failure => failure.Message.Contains("last sync failure", StringComparison.Ordinal));
            Assert.Contains(syncFailure.InnerExceptions.Select(failure => failure.InnerException), inner => inner is InvalidOperationException);
            Assert.Contains(syncFailure.InnerExceptions.Select(failure => failure.InnerException), inner => inner is ArgumentException);

            var asyncLaterScenarioRan = false;
            var asyncFailure = await Assert.ThrowsAsync<AggregateException>(() =>
                ContractCaseRunner.RunAsync(
                    ("first async failure", () => Task.FromException(new InvalidOperationException("async-one"))),
                    ("successful async scenario", () => Task.CompletedTask),
                    ("last async failure", async () =>
                    {
                        await Task.Yield();
                        asyncLaterScenarioRan = true;
                        throw new ArgumentException("async-two");
                    })));

            Assert.True(asyncLaterScenarioRan);
            Assert.StartsWith("2/3 contract scenarios failed", asyncFailure.Message, StringComparison.Ordinal);
            Assert.Equal(2, asyncFailure.InnerExceptions.Count);
            Assert.Contains(asyncFailure.InnerExceptions, failure => failure.Message.Contains("first async failure", StringComparison.Ordinal));
            Assert.Contains(asyncFailure.InnerExceptions, failure => failure.Message.Contains("last async failure", StringComparison.Ordinal));

            ContractCaseRunner.Run();
            await ContractCaseRunner.RunAsync();

            var nullSyncDelegate = Assert.Throws<AggregateException>(() =>
                ContractCaseRunner.Run(("null sync delegate", (Action)null!)));
            Assert.Contains("null sync delegate", nullSyncDelegate.InnerExceptions.Single().Message, StringComparison.Ordinal);

            var nullAsyncDelegate = await Assert.ThrowsAsync<AggregateException>(() =>
                ContractCaseRunner.RunAsync(("null async delegate", (Func<Task>)null!)));
            Assert.Contains("null async delegate", nullAsyncDelegate.InnerExceptions.Single().Message, StringComparison.Ordinal);

            var nullTask = await Assert.ThrowsAsync<AggregateException>(() =>
                ContractCaseRunner.RunAsync(("null task", () => null!)));
            Assert.Contains("null task", nullTask.InnerExceptions.Single().Message, StringComparison.Ordinal);
        }
    }
}
