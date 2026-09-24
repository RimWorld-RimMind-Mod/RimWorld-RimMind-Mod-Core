using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Async;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Pipeline;
using RimMind.Application.Common.Models.Npc;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.Requests;
using RimMind.Application.Features.Requests.Queue;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class RequestSubmissionServiceContract
    {
        [Fact]
        public Task Submission_selects_one_client_runs_one_pipeline_and_returns_one_context()
        {
            return ContractCaseRunner.RunAsync(
                ("configured client runs the pipeline and returns its context", async () =>
                {
                    var queue = new RequestQueue();
                    var client = new StubClient();
                    var pipeline = new SuccessfulPipeline();
                    var service = new RequestSubmissionService(
                        queue,
                        new StubClientManager(client),
                        pipeline,
                        traceLog: null,
                        modelSettings: null,
                        completionFence: new AcceptingFence());
                    Result<LlmResponse, RimMindError>? result = null;
                    LlmRequestContext? context = null;

                    service.Send(
                        new LlmRequestEnvelope { RequestId = "request-1", ModId = "Contracts" },
                        (value, requestContext) =>
                        {
                            result = value;
                            context = requestContext;
                        });

                    Assert.True(SpinWait.SpinUntil(
                        () => queue.PendingCallbackCount > 0,
                        TimeSpan.FromSeconds(1)));
                    queue.Tick();

                    Assert.True(result?.IsOk);
                    Assert.Equal("pipeline-ok", result?.Value.Content);
                    Assert.Same(pipeline.Context, context);
                    Assert.Same(client, context?.Client);
                    await Task.CompletedTask;
                }),
                ("missing client returns one structured error without running the pipeline", async () =>
                {
                    var queue = new RequestQueue();
                    var pipeline = new SuccessfulPipeline();
                    var service = new RequestSubmissionService(
                        queue,
                        new StubClientManager(null),
                        pipeline,
                        traceLog: null,
                        modelSettings: null,
                        completionFence: new AcceptingFence());
                    Result<LlmResponse, RimMindError>? result = null;

                    service.Send(
                        new LlmRequestEnvelope { RequestId = "request-2", ModId = "Contracts" },
                        (value, _) => result = value);

                    Assert.True(result?.IsErr);
                    Assert.Equal(RimMindErrorCode.ClientNotConfigured, result?.Error.Code);
                    Assert.Equal(0, pipeline.ExecutionCount);
                    Assert.Equal(0, queue.TotalQueuedCount);
                    await Task.CompletedTask;
                }));
        }

        private sealed class SuccessfulPipeline : IPipeline<LlmRequestContext>
        {
            public int ExecutionCount { get; private set; }
            public LlmRequestContext? Context { get; private set; }

            public Task ExecuteAsync(LlmRequestContext context)
            {
                ExecutionCount++;
                Context = context;
                context.Result = Result<LlmResponse, RimMindError>.Ok(
                    new LlmResponse { Content = "pipeline-ok" });
                return Task.CompletedTask;
            }
        }

        private sealed class StubClientManager : IClientManager
        {
            private readonly IAIClient? _client;

            public StubClientManager(IAIClient? client) => _client = client;

            public IAIClient? GetClient() => _client;
            public IAIClient? GetPlayer2Client() => null;
            public void InvalidateCache() { }
        }

        private sealed class AcceptingFence : ICompletionFence
        {
            public CancellationToken CancellationToken => CancellationToken.None;
            public bool TryAcceptCompletion() => true;
        }

        private sealed class StubClient : IAIClient
        {
            public bool IsLocalEndpoint => false;
            public bool SupportsStreaming => false;
            public bool SupportsNpcServerState => false;
            public bool IsConfigured() => true;

            public Task<Result<LlmResponse, RimMindError>> SendAsync(LlmRequestEnvelope envelope) =>
                Task.FromResult(Result<LlmResponse, RimMindError>.Ok(
                    new LlmResponse { Content = "client-ok" }));

            public Task<Result<LlmResponse, RimMindError>> SendStreamAsync(
                LlmRequestEnvelope envelope,
                Action<LlmChunk> onChunk,
                CancellationToken ct = default) => SendAsync(envelope);

            public Task<Result<bool, RimMindError>> SpawnNpcAsync(NpcProfile profile) =>
                Task.FromResult(Result<bool, RimMindError>.Ok(true));

            public Task<Result<bool, RimMindError>> KillNpcAsync(string npcId) =>
                Task.FromResult(Result<bool, RimMindError>.Ok(true));

            public Task<Result<List<string>, RimMindError>> QueryNpcMemoriesAsync(
                string npcId,
                string query,
                int limit) => Task.FromResult(
                    Result<List<string>, RimMindError>.Ok(new List<string>()));

            public void Dispose() { }
        }
    }
}
