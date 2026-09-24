using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Async;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class RuntimeAsyncFenceContract
    {
        [Fact]
        public void Async_completions_are_cancelled_and_fenced_before_side_effects()
        {
            ContractCaseRunner.Run(
                ("queue completion is fenced", () => AssertFence("Application/Features/Requests/Queue/RequestQueue.cs")),
                ("request completion inbox owns the background-to-main-thread boundary", () =>
                {
                    var queue = ReadSource(
                        "Application/Features/Requests/Queue/RequestQueue.cs");
                    var inbox = ReadSource(
                        "Application/Features/Requests/Queue/RequestCompletionInbox.cs");
                    Assert.Contains("RequestCompletionInbox", queue, StringComparison.Ordinal);
                    Assert.DoesNotContain("ConcurrentQueue<PendingCompletion>", queue, StringComparison.Ordinal);
                    Assert.Contains("ConcurrentQueue<PendingCompletion>", inbox, StringComparison.Ordinal);
                    Assert.Contains("TryAcceptCompletion", inbox, StringComparison.Ordinal);
                }),
                ("proactive completions are fenced", () =>
                {
                    var source = AssertFence("Application/Features/Agent/ProactiveBehaviorOrchestrator.cs");
                    Assert.Contains("ReflectAsync(agent, _completionFence.CancellationToken)", source, StringComparison.Ordinal);
                    Assert.Contains("PlanAsync(agent, _completionFence.CancellationToken)", source, StringComparison.Ordinal);
                    Assert.Contains("GenerateDreamAsync(agent, _completionFence.CancellationToken)", source, StringComparison.Ordinal);
                    Assert.Contains("EvaluateEvolutionAsync(agent, _completionFence.CancellationToken)", source, StringComparison.Ordinal);
                }),
                ("proactive game side effects recheck the fence immediately", () =>
                {
                    var source = AssertFence("Application/Features/Agent/ProactiveBehaviorOrchestrator.cs");
                    AssertImmediatelyFenced(source, "_dreamThoughtInjector?.InjectDreamThought");
                    AssertImmediatelyFenced(source, "_traitEvolver?.ApplyTraitEvolution");
                    AssertImmediatelyFenced(source, "_agentBus.Publish");
                }),
                ("pawn agent production wiring supplies the runtime fence", () =>
                {
                    var factory = ReadSource("Presentation/Agent/PawnAgentFactory.cs");
                    var agent = ReadSource("Presentation/Agent/PawnAgent.cs");
                    Assert.Contains("ICompletionFence", factory, StringComparison.Ordinal);
                    Assert.Contains("CompletionFence", factory, StringComparison.Ordinal);
                    Assert.Contains("_completionFence)", factory, StringComparison.Ordinal);
                    Assert.Contains("factory.CompletionFence)", agent, StringComparison.Ordinal);
                }),
                ("proactive executor forwards the thinker runtime fence", () =>
                {
                    var thinker = ReadSource("Presentation/Agent/PawnThinker.cs");
                    var executor = ReadSource("Presentation/Agent/ProactiveBehaviorExecutor.cs");
                    Assert.Contains("new ProactiveBehaviorExecutor(", thinker, StringComparison.Ordinal);
                    Assert.Contains("_completionFence);", thinker, StringComparison.Ordinal);
                    Assert.Contains("ICompletionFence", executor, StringComparison.Ordinal);
                    Assert.Contains("_completionFence);", executor, StringComparison.Ordinal);
                }),
                ("non-streaming queue and client invocation preserve the linked token", () =>
                {
                    var queue = ReadSource("Application/Features/Requests/Queue/RequestQueue.cs");
                    Assert.DoesNotContain("_ => client.SendAsync(envelope)", queue, StringComparison.Ordinal);
                    Assert.True(
                        CountOccurrences(
                            queue,
                            "ct => client.SendAsync(CloneWithCancellationToken(envelope, ct))") >= 2);

                    var middleware = ReadSource("Application/Features/Pipeline/Unified/ClientInvokeMiddleware.cs");
                    Assert.Contains(
                        "context.Envelope = CloneWithCancellationToken(context.Envelope, context.Ct)",
                        middleware,
                        StringComparison.Ordinal);
                    Assert.Contains("client.SendAsync(context.Envelope)", middleware, StringComparison.Ordinal);
                }),
                ("pawn thinker callbacks are fenced", () =>
                {
                    var source = AssertFence("Presentation/Agent/PawnThinker.cs");
                    var consumeStart = source.IndexOf("private void ProcessPendingCallback()", StringComparison.Ordinal);
                    var consumeEnd = source.IndexOf("private void RequestFollowUpThink()", consumeStart, StringComparison.Ordinal);
                    Assert.True(consumeStart >= 0 && consumeEnd > consumeStart);
                    Assert.Contains("TryAcceptCompletion", source.Substring(consumeStart, consumeEnd - consumeStart), StringComparison.Ordinal);
                }));
        }

        [Fact]
        public async Task Queued_completion_is_rechecked_before_callback_consumption()
        {
            var fence = new ControllableCompletionFence();
            var queue = new RequestQueue(completionFence: fence);
            var executorStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseExecutor = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callbackCount = 0;

            queue.Enqueue(
                new LlmRequestEnvelope { RequestId = "generation-fence", ModId = "Contracts" },
                _ => Interlocked.Increment(ref callbackCount),
                async _ =>
                {
                    executorStarted.TrySetResult(true);
                    await releaseExecutor.Task;
                    return Result<LlmResponse, RimMindError>.Ok(new LlmResponse { Content = "old" });
                });

            Assert.Same(
                executorStarted.Task,
                await Task.WhenAny(executorStarted.Task, Task.Delay(TimeSpan.FromSeconds(1))));
            releaseExecutor.TrySetResult(true);
            Assert.True(SpinWait.SpinUntil(
                () => queue.PendingCallbackCount > 0,
                TimeSpan.FromSeconds(1)));

            fence.Retire();
            queue.Tick();

            Assert.Equal(0, callbackCount);
        }

        private static string AssertFence(string relativePath)
        {
            var source = ReadSource(relativePath);
            Assert.Contains("ICompletionFence", source, StringComparison.Ordinal);
            Assert.Contains("CancellationToken", source, StringComparison.Ordinal);
            Assert.Contains("TryAcceptCompletion", source, StringComparison.Ordinal);
            return source;
        }

        private static void AssertImmediatelyFenced(string source, string sideEffect)
        {
            var searchStart = 0;
            while (true)
            {
                var sideEffectIndex = source.IndexOf(sideEffect, searchStart, StringComparison.Ordinal);
                if (sideEffectIndex < 0) return;

                var prefix = source.Substring(0, sideEffectIndex).TrimEnd();
                Assert.EndsWith(
                    "if (!_completionFence.TryAcceptCompletion()) return;",
                    prefix,
                    StringComparison.Ordinal);
                searchStart = sideEffectIndex + sideEffect.Length;
            }
        }

        private static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var offset = 0;
            while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }

            return count;
        }

        private static string ReadSource(string relativePath) =>
            File.ReadAllText(Path.Combine(SourceRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string SourceRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Core", "Source")))
                directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "RimMind-Core", "Source");
        }

        private sealed class ControllableCompletionFence : ICompletionFence
        {
            private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
            private volatile bool _active = true;

            public CancellationToken CancellationToken => _cancellation.Token;

            public bool TryAcceptCompletion() => _active;

            public void Retire()
            {
                _active = false;
                _cancellation.Cancel();
            }
        }
    }
}
