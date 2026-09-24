using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Common.Models.Npc;
using RimMind.Application.Features.Pipeline.Unified;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Services.Clients;
using RimMind.Testing;
using Xunit;

namespace RimMind.IntegrationTests.Contracts
{
    public sealed class RuntimeAdapterContracts
    {
        [Fact]
        public async Task Client_adapter_routes_non_streaming_requests_without_losing_context()
        {
            await ContractCaseRunner.RunAsync(
                ("missing client short-circuits with a stable error", async () =>
                {
                    var context = Context("missing-client");
                    await new ClientInvokeMiddleware().InvokeAsync(context, _ => Task.CompletedTask);
                    Assert.True(context.IsShortCircuited);
                    Assert.True(context.Result!.Value.IsErr);
                    Assert.Equal(RimMindErrorCode.ClientNotConfigured, context.Result.Value.Error.Code);
                }),
                ("regular request selects non-streaming transport", async () =>
                {
                    var client = new RecordingClient();
                    var context = Context("regular", client);
                    await new ClientInvokeMiddleware().InvokeAsync(context, _ => Task.CompletedTask);
                    Assert.Equal(1, client.NonStreamingCalls);
                    Assert.Equal(0, client.StreamingCalls);
                }),
                ("unsupported streaming falls back to regular transport", async () =>
                {
                    var client = new RecordingClient { SupportsStreamingValue = false };
                    var context = Context("fallback", client, streaming: true);
                    await new ClientInvokeMiddleware().InvokeAsync(context, _ => Task.CompletedTask);
                    Assert.Equal(1, client.NonStreamingCalls);
                    Assert.Equal(0, client.StreamingCalls);
                }),
                ("queue cancellation is copied to the transport envelope", async () =>
                {
                    using var cancellation = new CancellationTokenSource();
                    var client = new RecordingClient();
                    var context = Context("token", client, cancellation.Token);
                    await new ClientInvokeMiddleware().InvokeAsync(context, _ => Task.CompletedTask);
                    Assert.Equal(cancellation.Token, client.LastEnvelope!.Ct);
                }),
                ("successful result and downstream continuation are preserved", async () =>
                {
                    var client = new RecordingClient();
                    var context = Context("success", client);
                    bool downstream = false;
                    await new ClientInvokeMiddleware().InvokeAsync(context, _ =>
                    {
                        downstream = true;
                        return Task.CompletedTask;
                    });
                    Assert.True(downstream);
                    Assert.True(context.Result!.Value.IsOk);
                    Assert.Equal("regular-response", context.Result.Value.Value.Content);
                }));
        }

        [Fact]
        public async Task Client_adapter_routes_streaming_requests_and_normalizes_results()
        {
            await ContractCaseRunner.RunAsync(
                ("streaming-capable client receives streaming call", async () =>
                {
                    var client = new RecordingClient { SupportsStreamingValue = true };
                    var context = Context("stream", client, streaming: true);
                    await new ClientInvokeMiddleware().InvokeAsync(context, _ => Task.CompletedTask);
                    Assert.Equal(0, client.NonStreamingCalls);
                    Assert.Equal(1, client.StreamingCalls);
                }),
                ("streaming call receives the request cancellation token", async () =>
                {
                    using var cancellation = new CancellationTokenSource();
                    var client = new RecordingClient { SupportsStreamingValue = true };
                    var context = Context("stream-token", client, cancellation.Token, streaming: true);
                    await new ClientInvokeMiddleware().InvokeAsync(context, _ => Task.CompletedTask);
                    Assert.Equal(cancellation.Token, client.StreamingToken);
                }),
                ("streaming chunks are accepted before the final response", async () =>
                {
                    var client = new RecordingClient
                    {
                        SupportsStreamingValue = true,
                        EmitChunks = true
                    };
                    var context = Context("chunks", client, streaming: true);
                    await new ClientInvokeMiddleware().InvokeAsync(context, _ => Task.CompletedTask);
                    Assert.Equal(2, client.EmittedChunks);
                    Assert.Equal("stream-response", context.Result!.Value.Value.Content);
                }),
                ("final response retains transport metrics", async () =>
                {
                    var client = new RecordingClient { SupportsStreamingValue = true };
                    var context = Context("metrics", client, streaming: true);
                    await new ClientInvokeMiddleware().InvokeAsync(context, _ => Task.CompletedTask);
                    LlmResponse response = context.Result!.Value.Value;
                    Assert.Equal(9, response.TokensUsed);
                    Assert.Equal(5, response.PromptTokens);
                    Assert.Equal(4, response.CompletionTokens);
                    Assert.Equal(200, response.HttpStatusCode);
                }),
                ("transport error remains an error result", async () =>
                {
                    var client = new RecordingClient
                    {
                        SupportsStreamingValue = true,
                        StreamingResult = Result<LlmResponse, RimMindError>.Err(
                            RimMindErrors.ClientTransient("stream failed"))
                    };
                    var context = Context("stream-error", client, streaming: true);
                    await new ClientInvokeMiddleware().InvokeAsync(context, _ => Task.CompletedTask);
                    Assert.True(context.Result!.Value.IsErr);
                    Assert.Equal(RimMindErrorCode.ClientTransientFailure, context.Result.Value.Error.Code);
                }));
        }

        [Fact]
        public void Http_adapter_exception_preserves_transport_status()
        {
            ContractCaseRunner.Run(
                ("rate limit status is retained", () => AssertHttpException(429, "rate limited")),
                ("authentication status is retained", () => AssertHttpException(401, "unauthorized")),
                ("forbidden status is retained", () => AssertHttpException(403, "forbidden")),
                ("server status is retained", () => AssertHttpException(503, "unavailable")),
                ("unknown transport status is retained", () => AssertHttpException(0, "transport failure")));
        }

        private static LlmRequestContext Context(
            string requestId,
            IAIClient? client = null,
            CancellationToken cancellationToken = default,
            bool streaming = false)
        {
            var envelope = new LlmRequestEnvelope
            {
                RequestId = requestId,
                TraceId = $"trace-{requestId}",
                ScenarioId = "contracts",
                ModId = "RimMindCore",
                IsStreaming = streaming,
                Messages = new List<ChatMessage>
                {
                    new() { Role = "user", Content = "request" }
                }
            };
            return new LlmRequestContext(envelope, ct: cancellationToken)
            {
                Client = client
            };
        }

        private static void AssertHttpException(int statusCode, string message)
        {
            var exception = new HttpTransport.HttpException(message, statusCode);
            Assert.Equal(statusCode, exception.StatusCode);
            Assert.Equal(message, exception.Message);
        }

        private sealed class RecordingClient : IAIClient
        {
            public bool IsLocalEndpoint => false;
            public bool SupportsStreaming => SupportsStreamingValue;
            public bool SupportsNpcServerState => false;
            public bool SupportsStreamingValue { get; set; }
            public bool EmitChunks { get; set; }
            public int EmittedChunks { get; private set; }
            public int NonStreamingCalls { get; private set; }
            public int StreamingCalls { get; private set; }
            public CancellationToken StreamingToken { get; private set; }
            public LlmRequestEnvelope? LastEnvelope { get; private set; }
            public Result<LlmResponse, RimMindError>? StreamingResult { get; set; }

            public bool IsConfigured() => true;

            public Task<Result<LlmResponse, RimMindError>> SendAsync(LlmRequestEnvelope envelope)
            {
                NonStreamingCalls++;
                LastEnvelope = envelope;
                return Task.FromResult(Result<LlmResponse, RimMindError>.Ok(new LlmResponse
                {
                    RequestId = envelope.RequestId,
                    Content = "regular-response"
                }));
            }

            public Task<Result<LlmResponse, RimMindError>> SendStreamAsync(
                LlmRequestEnvelope envelope,
                Action<LlmChunk> onChunk,
                CancellationToken ct = default)
            {
                StreamingCalls++;
                StreamingToken = ct;
                LastEnvelope = envelope;
                if (EmitChunks)
                {
                    onChunk(new LlmChunk { DeltaContent = "stream-", IsLast = false });
                    onChunk(new LlmChunk { DeltaContent = "response", IsLast = true });
                    EmittedChunks = 2;
                }

                return Task.FromResult(StreamingResult ??
                    Result<LlmResponse, RimMindError>.Ok(new LlmResponse
                    {
                        RequestId = envelope.RequestId,
                        Content = "stream-response",
                        TokensUsed = 9,
                        PromptTokens = 5,
                        CompletionTokens = 4,
                        HttpStatusCode = 200,
                        State = AIRequestState.Completed
                    }));
            }

            public Task<Result<bool, RimMindError>> SpawnNpcAsync(NpcProfile profile) =>
                Task.FromResult(Result<bool, RimMindError>.Ok(true));

            public Task<Result<bool, RimMindError>> KillNpcAsync(string npcId) =>
                Task.FromResult(Result<bool, RimMindError>.Ok(true));

            public Task<Result<List<string>, RimMindError>> QueryNpcMemoriesAsync(
                string npcId,
                string query,
                int limit) =>
                Task.FromResult(Result<List<string>, RimMindError>.Ok(new List<string>()));

            public void Dispose()
            {
            }
        }
    }
}
