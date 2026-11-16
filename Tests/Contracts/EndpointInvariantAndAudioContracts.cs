using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Audio;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Client;
using RimMind.Application.Common.Models.Pipeline;
using RimMind.Application.Features.Llm;
using RimMind.Application.Features.Pipeline.Unified;
using RimMind.Application.Features.Registry;
using RimMind.Domain.Audio;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Services.Clients.OpenAI;
using RimMind.Testing;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class EndpointInvariantAndAudioContracts
    {
        private sealed class FakeOpenAISettings : IOpenAISettings
        {
            public string ApiEndpoint { get; set; } = "https://primary.example/v1";
            public string ApiKey { get; set; } = "test-primary-key";
            public string ModelName { get; set; } = "deepseek-flash";
            public int MaxTokens { get; set; } = 512;
            public float DefaultTemperature { get; set; } = 0.7f;
            public bool ForceJsonMode { get; set; }
            public bool DebugLogging { get; set; }
            public int RequestTimeoutSeconds { get; set; } = 30;
            public IReadOnlyList<BackupEndpointNode>? BackupEndpoints { get; set; }
            public bool IsConfigured() => !string.IsNullOrWhiteSpace(ApiKey);
        }

        private sealed class StubCircuitBreakerSettings : ICircuitBreakerSettings
        {
            public int CircuitBreakerFailureThreshold { get; }
            public int CircuitBreakerOpenDurationSec { get; }

            public StubCircuitBreakerSettings(int threshold, int openDurationSec)
            {
                CircuitBreakerFailureThreshold = threshold;
                CircuitBreakerOpenDurationSec = openDurationSec;
            }
        }

        private sealed class FakeSpeechSynthesizer : ISpeechSynthesizer
        {
            public string Id => "test_tts";
            public string OwnerModId => "RimMind.Tests";
            public string DisplayLabel => "Test TTS";
            public bool IsConfigured() => true;

            public Task<Result<SpeechSynthesisResult, RimMindError>> SynthesizeAsync(SpeechSynthesisRequest request) =>
                Task.FromResult(Result<SpeechSynthesisResult, RimMindError>.Ok(new SpeechSynthesisResult
                {
                    AudioBytes = new byte[] { 1, 2, 3, 4 },
                    Format = request.PreferredFormat,
                    ProviderId = Id
                }));
        }

        private sealed class FakeSpeechRecognizer : ISpeechRecognizer
        {
            public string Id => "test_asr";
            public string OwnerModId => "RimMind.Tests";
            public string DisplayLabel => "Test ASR";
            public bool IsConfigured() => true;

            public Task<Result<SpeechRecognitionResult, RimMindError>> TranscribeAsync(SpeechRecognitionRequest request) =>
                Task.FromResult(Result<SpeechRecognitionResult, RimMindError>.Ok(new SpeechRecognitionResult
                {
                    RequestId = "req-asr-1",
                    Text = request.VocabularyHints ?? "transcribed",
                    DurationMs = 350,
                    ProviderId = Id
                }));
        }

        [Fact]
        public void ReasoningEffort_is_preserved_across_builder_clone_and_trace_hints()
        {
            var defaultEnvelope = LlmRequestEnvelopeBuilder
                .ForNpc("pawn-0")
                .ForScenarioId("RimMind.Test")
                .Build();
            Assert.Equal(ReasoningEffort.Auto, defaultEnvelope.ReasoningEffort);

            var built = LlmRequestEnvelopeBuilder
                .ForNpc("pawn-1")
                .ForScenarioId("RimMind.Dialogue")
                .WithReasoningEffort(ReasoningEffort.None)
                .Build();
            Assert.Equal(ReasoningEffort.None, built.ReasoningEffort);

            using var cts = new CancellationTokenSource();
            var cloned = built.Clone(cts.Token);
            Assert.Equal(ReasoningEffort.None, cloned.ReasoningEffort);
            Assert.Equal(cts.Token, cloned.Ct);

            var traced = LlmRequestEnvelopeBuilder
                .ForNpc("pawn-2")
                .ForScenarioId("RimMind.Test")
                .WithReasoningEffort(ReasoningEffort.Max)
                .Build()
                .WithTraceAndHints("trace-99", new Dictionary<string, string> { ["k"] = "v" });
            Assert.Equal(ReasoningEffort.Max, traced.ReasoningEffort);
            Assert.Equal("trace-99", traced.TraceId);
        }

        [Fact]
        public async Task ContentFiltered_and_quota_errors_do_not_trip_circuit_breaker_or_retry()
        {
            await ContractCaseRunner.RunAsync(
                ("CircuitBreaker ignores ContentFiltered across repeated requests", async () =>
                {
                    var cb = new CircuitBreakerMiddleware(new StubCircuitBreakerSettings(threshold: 2, openDurationSec: 30));
                    for (int i = 0; i < 4; i++)
                    {
                        var ctx = new LlmRequestContext(new LlmRequestEnvelope
                        {
                            RequestId = $"req-mod-{i}",
                            ModId = "RimMind.Tests"
                        });
                        await cb.InvokeAsync(ctx, c =>
                        {
                            c.Result = Result<LlmResponse, RimMindError>.Err(RimMindErrors.ContentFiltered("Content Exists Risk"));
                            return Task.CompletedTask;
                        });
                        Assert.Equal(0, cb.ConsecutiveFailuresCount);
                        Assert.Equal("Closed", cb.CurrentStateName);
                    }
                }),
                ("RetryMiddleware stops immediately on ContentFiltered and ClientQuotaOrAuthFailure", async () =>
                {
                    var retry = new RetryMiddleware(maxRetries: 3, delay: TimeSpan.FromMilliseconds(1));

                    int filteredCalls = 0;
                    var ctxFiltered = new LlmRequestContext(new LlmRequestEnvelope { RequestId = "r-cf", ModId = "Tests" });
                    await retry.InvokeAsync(ctxFiltered, c =>
                    {
                        filteredCalls++;
                        c.Result = Result<LlmResponse, RimMindError>.Err(RimMindErrors.ContentFiltered("blocked"));
                        return Task.CompletedTask;
                    });
                    Assert.Equal(1, filteredCalls);
                    Assert.Equal(RimMindErrorCode.ContentFiltered, ctxFiltered.Result!.Value.Error.Code);

                    int quotaCalls = 0;
                    var ctxQuota = new LlmRequestContext(new LlmRequestEnvelope { RequestId = "r-quota", ModId = "Tests" });
                    await retry.InvokeAsync(ctxQuota, c =>
                    {
                        quotaCalls++;
                        c.Result = Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientQuotaOrAuth("402 Insufficient Balance"));
                        return Task.CompletedTask;
                    });
                    Assert.Equal(1, quotaCalls);
                    Assert.Equal(RimMindErrorCode.ClientQuotaOrAuthFailure, ctxQuota.Result!.Value.Error.Code);

                    int transientCalls = 0;
                    var ctxTransient = new LlmRequestContext(new LlmRequestEnvelope { RequestId = "r-tr", ModId = "Tests" });
                    await retry.InvokeAsync(ctxTransient, c =>
                    {
                        transientCalls++;
                        c.Result = Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientTransient("503"));
                        return Task.CompletedTask;
                    });
                    Assert.Equal(4, transientCalls);
                }));
        }

        [Fact]
        public async Task OpenAIClient_content_moderation_returns_immediately_without_downgrade_or_failover()
        {
            await ContractCaseRunner.RunAsync(
                ("HTTP 400 Content Exists Risk skips json_schema downgrade and backup failover", async () =>
                {
                    OpenAIClient.InvalidateFormatCache();
                    int callCount = 0;

                    var settings = new FakeOpenAISettings
                    {
                        BackupEndpoints = new[]
                        {
                            new BackupEndpointNode
                            {
                                Name = "Backup",
                                ApiEndpoint = "https://backup.example/v1",
                                ModelName = "gpt-4o-mini",
                                ApiKey = "backup-key",
                                Enabled = true
                            }
                        }
                    };

                    using var client = new OpenAIClient(settings, null, (url, body, auth, hn, hv, to, ct) =>
                    {
                        callCount++;
                        return Task.FromResult(("{\"error\":{\"message\":\"Content Exists Risk\",\"type\":\"invalid_request_error\"}}", 400L));
                    });

                    var env = new LlmRequestEnvelope
                    {
                        RequestId = "req-400-risk",
                        JsonSchema = "{\"type\":\"object\"}",
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "hi" } }
                    };

                    var result = await client.SendAsync(env);
                    Assert.True(result.IsErr);
                    Assert.Equal(RimMindErrorCode.ContentFiltered, result.Error.Code);
                    Assert.Equal(1, callCount);
                }),
                ("HTTP 200 finish_reason=content_filter stops immediately without failing over to backup", async () =>
                {
                    OpenAIClient.InvalidateFormatCache();
                    int callCount = 0;

                    var settings = new FakeOpenAISettings
                    {
                        BackupEndpoints = new[]
                        {
                            new BackupEndpointNode
                            {
                                Name = "Backup",
                                ApiEndpoint = "https://backup.example/v1",
                                ModelName = "gpt-4o-mini",
                                ApiKey = "backup-key",
                                Enabled = true
                            }
                        }
                    };

                    using var client = new OpenAIClient(settings, null, (url, body, auth, hn, hv, to, ct) =>
                    {
                        callCount++;
                        return Task.FromResult(("{\"id\":\"cmpl-1\",\"choices\":[{\"finish_reason\":\"content_filter\",\"message\":{\"role\":\"assistant\",\"content\":\"\"}}]}", 200L));
                    });

                    var env = new LlmRequestEnvelope
                    {
                        RequestId = "req-200-cf",
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "hi" } }
                    };

                    var result = await client.SendAsync(env);
                    Assert.True(result.IsErr);
                    Assert.Equal(RimMindErrorCode.ContentFiltered, result.Error.Code);
                    Assert.Equal(1, callCount);
                }));
        }

        [Fact]
        public async Task OpenAIClient_fails_over_on_insufficient_system_resource_and_whitespace_trap_and_reads_prompt_cache_hit_tokens()
        {
            await ContractCaseRunner.RunAsync(
                ("finish_reason=insufficient_system_resource fails over to backup and extracts prompt_cache_hit_tokens", async () =>
                {
                    OpenAIClient.InvalidateFormatCache();
                    int callCount = 0;

                    var settings = new FakeOpenAISettings
                    {
                        BackupEndpoints = new[]
                        {
                            new BackupEndpointNode
                            {
                                Name = "Backup",
                                ApiEndpoint = "https://backup.example/v1",
                                ModelName = "deepseek-flash",
                                ApiKey = "backup-key",
                                Enabled = true
                            }
                        }
                    };

                    using var client = new OpenAIClient(settings, null, (url, body, auth, hn, hv, to, ct) =>
                    {
                        callCount++;
                        if (callCount == 1)
                        {
                            return Task.FromResult(("{\"id\":\"cmpl-1\",\"choices\":[{\"finish_reason\":\"insufficient_system_resource\",\"message\":{\"role\":\"assistant\",\"content\":\"\"}}]}", 200L));
                        }
                        return Task.FromResult(("{\"id\":\"cmpl-2\",\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"Recovered\"}}],\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":10,\"total_tokens\":110,\"prompt_cache_hit_tokens\":80}}", 200L));
                    });

                    var env = new LlmRequestEnvelope
                    {
                        RequestId = "req-isr",
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "status" } }
                    };

                    var result = await client.SendAsync(env);
                    Assert.True(result.IsOk);
                    Assert.Equal(2, callCount);
                    Assert.Equal("Recovered", result.Value.Content);
                    Assert.Equal(80, result.Value.CachedTokens);
                }),
                ("whitespace-only content trap fails over to backup node", async () =>
                {
                    OpenAIClient.InvalidateFormatCache();
                    int callCount = 0;

                    var settings = new FakeOpenAISettings
                    {
                        BackupEndpoints = new[]
                        {
                            new BackupEndpointNode
                            {
                                Name = "Backup",
                                ApiEndpoint = "https://backup.example/v1",
                                ModelName = "deepseek-flash",
                                ApiKey = "backup-key",
                                Enabled = true
                            }
                        }
                    };

                    using var client = new OpenAIClient(settings, null, (url, body, auth, hn, hv, to, ct) =>
                    {
                        callCount++;
                        if (callCount == 1)
                        {
                            return Task.FromResult(("{\"id\":\"cmpl-ws\",\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"    \"}}]}", 200L));
                        }
                        return Task.FromResult(("{\"id\":\"cmpl-ok\",\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"{\\\"ok\\\":true}\"}}],\"usage\":{\"total_tokens\":20}}", 200L));
                    });

                    var env = new LlmRequestEnvelope
                    {
                        RequestId = "req-ws",
                        Messages = new List<ChatMessage> { new ChatMessage { Role = "user", Content = "json please" } }
                    };

                    var result = await client.SendAsync(env);
                    Assert.True(result.IsOk);
                    Assert.Equal(2, callCount);
                    Assert.Equal("{\"ok\":true}", result.Value.Content);
                }));
        }

        [Fact]
        public async Task Speech_synthesizer_and_recognizer_extension_ports_register_and_execute()
        {
            var ttsRegistry = new ExtensionRegistry<ISpeechSynthesizer>();
            var asrRegistry = new ExtensionRegistry<ISpeechRecognizer>();

            ttsRegistry.Register(new FakeSpeechSynthesizer());
            asrRegistry.Register(new FakeSpeechRecognizer());

            var tts = ttsRegistry.FindById("test_tts");
            var asr = asrRegistry.FindById("test_asr");
            Assert.NotNull(tts);
            Assert.NotNull(asr);
            Assert.True(tts!.IsConfigured());
            Assert.True(asr!.IsConfigured());

            var synthResult = await tts.SynthesizeAsync(new SpeechSynthesisRequest
            {
                Text = "Colony alert!",
                PreferredFormat = AudioContainerFormat.Mp3
            });
            Assert.True(synthResult.IsOk);
            Assert.Equal(AudioContainerFormat.Mp3, synthResult.Value.Format);
            Assert.Equal("test_tts", synthResult.Value.ProviderId);

            var recResult = await asr.TranscribeAsync(new SpeechRecognitionRequest
            {
                AudioBytes = new byte[] { 10, 20 },
                VocabularyHints = "Cassandra,RimMind"
            });
            Assert.True(recResult.IsOk);
            Assert.Equal("Cassandra,RimMind", recResult.Value.Text);
            Assert.Equal("test_asr", recResult.Value.ProviderId);
        }
    }
}
