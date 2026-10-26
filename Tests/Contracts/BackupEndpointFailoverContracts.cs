using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Models.Client;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using System.IO;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Models.Npc;
using RimMind.Infrastructure.Services.Clients;
using RimMind.Infrastructure.Services.Clients.Hybrid;
using RimMind.Infrastructure.Services.Clients.OpenAI;
using RimMind.Infrastructure.Services.Clients.Player2;
using RimMind.Infrastructure.Services.Clients.Shared;
using RimMind.Presentation.Settings;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class BackupEndpointFailoverContracts
    {
        private sealed class FakeOpenAISettings : IOpenAISettings
        {
            public string ApiEndpoint { get; set; } = "https://primary.example/v1";
            public string ApiKey { get; set; } = "test-primary-key";
            public string ModelName { get; set; } = "grok-4.7";
            public int MaxTokens { get; set; } = 512;
            public float DefaultTemperature { get; set; } = 0.7f;
            public bool ForceJsonMode { get; set; }
            public bool DebugLogging { get; set; }
            public int RequestTimeoutSeconds { get; set; } = 30;
            public IReadOnlyList<BackupEndpointNode>? BackupEndpoints { get; set; }
            public bool IsConfigured() => !string.IsNullOrWhiteSpace(ApiKey);
        }

        private static LlmRequestEnvelope CreateEnvelope(string id = "req-failover-1")
        {
            return new LlmRequestEnvelope
            {
                RequestId = id,
                ModId = "RimMind.Personality",
                NpcId = "NPC-101",
                Messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "user", Content = "Hello" }
                },
                Ct = CancellationToken.None
            };
        }

        [Fact]
        public async Task SendAsync_FailsOverToOpenCodeBackupOnPrimary503_AndIsolatesPrimaryNode()
        {
            OpenAIClient.InvalidateFormatCache();

            var settings = new FakeOpenAISettings
            {
                BackupEndpoints = new[]
                {
                    new BackupEndpointNode
                    {
                        Name = "OpenCode Go (Backup)",
                        Provider = "opencode",
                        ApiEndpoint = "https://opencode.ai/zen/go/v1",
                        ModelName = "deepseek-v4.1-flash-free",
                        ApiKey = "oc_sk_test_placeholder",
                        Enabled = true
                    }
                }
            };

            var hitUrls = new List<string>();
            var requestJsonPayloads = new List<string>();
            var capturedHeaderNames = new List<string?>();
            var capturedHeaderValues = new List<string?>();

            var client = new OpenAIClient(
                settings,
                postAsyncOverride: (url, json, _, headerName, headerValue, _, _) =>
                {
                    hitUrls.Add(url);
                    requestJsonPayloads.Add(json);
                    capturedHeaderNames.Add(headerName);
                    capturedHeaderValues.Add(headerValue);

                    if (url.StartsWith("https://primary.example", StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(("Service Unavailable", 503L));
                    }

                    return Task.FromResult((
                        "{\"id\":\"chatcmpl-backup\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Backup response OK\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":20,\"completion_tokens\":8,\"total_tokens\":28}}",
                        200L));
                });

            // 1st request: hits Primary (fails with 503) -> fails over to Backup #1 (succeeds)
            var firstResult = await client.SendAsync(CreateEnvelope("req-1"));
            Assert.True(firstResult.IsOk);
            Assert.Equal("Backup response OK", firstResult.Value.Content);
            Assert.Equal(2, hitUrls.Count);
            Assert.Contains("primary.example", hitUrls[0]);
            Assert.Contains("opencode.ai/zen/go/v1", hitUrls[1]);
            Assert.Contains("\"model\":\"grok-4.7\"", requestJsonPayloads[0]);
            Assert.Contains("\"model\":\"deepseek-v4.1-flash-free\"", requestJsonPayloads[1]);
            Assert.Contains("\"max_tokens\":800", requestJsonPayloads[1]);
            Assert.Equal("x-opencode-session", capturedHeaderNames[1]);
            Assert.Contains("rimmind-NPC-101", capturedHeaderValues[1]);

            // 2nd request within 60s cooldown: Primary is isolated, so it routes directly to Backup #1!
            var secondResult = await client.SendAsync(CreateEnvelope("req-2"));
            Assert.True(secondResult.IsOk);
            Assert.Equal(3, hitUrls.Count);
            Assert.Contains("opencode.ai/zen/go/v1", hitUrls[2]);
        }

        [Fact]
        public async Task SendAsync_FailsOverWhenPrimaryReturnsEmptyZeroTokenResponse()
        {
            OpenAIClient.InvalidateFormatCache();

            var settings = new FakeOpenAISettings
            {
                BackupEndpoints = new[]
                {
                    new BackupEndpointNode
                    {
                        Name = "Backup #1",
                        Provider = "opencode",
                        ApiEndpoint = "https://opencode.ai/zen/go/v1",
                        ModelName = "deepseek-v4.1-flash-free",
                        ApiKey = "oc_sk_test_placeholder",
                        Enabled = true
                    }
                }
            };

            var hitUrls = new List<string>();
            var client = new OpenAIClient(
                settings,
                postAsyncOverride: (url, _, _, _, _, _, _) =>
                {
                    hitUrls.Add(url);
                    if (url.StartsWith("https://primary.example", StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(("{\"id\":\"empty\",\"choices\":[],\"usage\":{\"prompt_tokens\":0,\"completion_tokens\":0,\"total_tokens\":0}}", 200L));
                    }

                    return Task.FromResult((
                        "{\"id\":\"chatcmpl-backup\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Recovered from backup\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":5,\"total_tokens\":17}}",
                        200L));
                });

            var result = await client.SendAsync(CreateEnvelope("req-empty"));
            Assert.True(result.IsOk);
            Assert.Equal("Recovered from backup", result.Value.Content);
            Assert.Equal(2, hitUrls.Count);
        }

        [Fact]
        public async Task SendAsync_WalksOrderedBackupChain_WhenPrimaryAndFirstBackupFail()
        {
            OpenAIClient.InvalidateFormatCache();

            var settings = new FakeOpenAISettings
            {
                BackupEndpoints = new[]
                {
                    new BackupEndpointNode
                    {
                        Name = "Backup #1",
                        Provider = "opencode",
                        ApiEndpoint = "https://backup1.example/v1",
                        ModelName = "model-backup-1",
                        ApiKey = "key-backup-1",
                        Enabled = true
                    },
                    new BackupEndpointNode
                    {
                        Name = "Backup #2",
                        Provider = "openai",
                        ApiEndpoint = "https://backup2.example/v1",
                        ModelName = "model-backup-2",
                        ApiKey = "key-backup-2",
                        Enabled = true
                    }
                }
            };

            var hitUrls = new List<string>();
            var client = new OpenAIClient(
                settings,
                postAsyncOverride: (url, _, _, _, _, _, _) =>
                {
                    hitUrls.Add(url);
                    if (!url.StartsWith("https://backup2.example", StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(("Bad Gateway", 502L));
                    }

                    return Task.FromResult((
                        "{\"id\":\"chatcmpl-b2\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Backup #2 OK\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":4,\"total_tokens\":14}}",
                        200L));
                });

            var result = await client.SendAsync(CreateEnvelope("req-chain"));
            Assert.True(result.IsOk);
            Assert.Equal("Backup #2 OK", result.Value.Content);
            Assert.Equal(3, hitUrls.Count);
            Assert.Contains("primary.example", hitUrls[0]);
            Assert.Contains("backup1.example", hitUrls[1]);
            Assert.Contains("backup2.example", hitUrls[2]);
        }

        [Fact]
        public void ApiKeyObfuscator_AndBackupEndpointEntry_RoundTripCleanly()
        {
            const string sampleKey = "oc_sk_sample_backup_key_12345";
            string obfuscated = ApiKeyObfuscator.Obfuscate(sampleKey);
            Assert.NotEqual(sampleKey, obfuscated);
            Assert.Equal(sampleKey, ApiKeyObfuscator.Deobfuscate(obfuscated));

            var entry = new BackupEndpointEntry
            {
                name = "OpenCode Go (Backup)",
                provider = "opencode",
                apiEndpoint = "https://opencode.ai/zen/go/v1",
                modelName = "deepseek-v4.1-flash-free",
                apiKey = sampleKey,
                enabled = true
            };
            var node = entry.ToNode();
            Assert.True(node.IsConfigured);
            Assert.Equal("opencode", node.Provider);
            Assert.Equal("deepseek-v4.1-flash-free", node.ModelName);

            Assert.Equal("beam-free", OpenAIClient.NormalizeModelNameForEndpoint("https://opencode.ai/zen/go/v1", "beam-free"));
            Assert.Equal("longcat-2.5-preview-free", OpenAIClient.NormalizeModelNameForEndpoint("https://opencode.ai/zen/go/v1", "longcat-2.5-preview-free"));
            Assert.Equal("deepseek-v4.1-flash-free", OpenAIClient.NormalizeModelNameForEndpoint("https://opencode.ai/zen/go/v1", "  deepseek-v4.1-flash-free  "));
            Assert.Equal("glm-5.3-flash", OpenAIClient.NormalizeModelNameForEndpoint("https://opencode.ai/zen/go/v1", "glm-5.3-flash"));
            Assert.Equal("deepseek-flash", OpenAIClient.NormalizeModelNameForEndpoint("https://api.deepseek.com", "deepseek-flash"));
        }

        [Fact]
        public void ClientExceptionMapper_AndLogSanitizer_DistinguishTimeoutCancellation_AndRedactSecrets()
        {
            using var uncancelledCts = new CancellationTokenSource();
            var timeoutErr = ClientExceptionMapper.MapException(new TaskCanceledException("request timed out"), "OpenAIClient", "req-1", "request", null, callerToken: uncancelledCts.Token);
            Assert.Equal(RimMindErrorCode.Timeout, timeoutErr.Error.Code);

            using var cancelledCts = new CancellationTokenSource();
            cancelledCts.Cancel();
            var cancelErr = ClientExceptionMapper.MapException(new OperationCanceledException(cancelledCts.Token), "OpenAIClient", "req-2", "request", null, callerToken: cancelledCts.Token);
            Assert.Equal(RimMindErrorCode.Cancelled, cancelErr.Error.Code);

            var ioErr = ClientExceptionMapper.MapException(new IOException("socket closed"), "OpenAIClient", "req-3", "request", null, callerToken: CancellationToken.None);
            Assert.Equal(RimMindErrorCode.ClientTransientFailure, ioErr.Error.Code);

            string rawLog = "Authorization: Bearer sk-secret1234567890abcdef and api_key=\"oc_sk_topsecret987654321\"";
            string sanitized = LogSanitizer.ForLog(rawLog, 500);
            Assert.DoesNotContain("sk-secret1234567890abcdef", sanitized);
            Assert.DoesNotContain("oc_sk_topsecret987654321", sanitized);
            Assert.Contains("***", sanitized);
        }

        [Fact]
        public async Task SendStreamAsync_AccumulatesIndexedToolCalls_HandlesNoSpaceSse_AndBlocksMidStreamFailover()
        {
            OpenAIClient.InvalidateFormatCache();

            var settings = new FakeOpenAISettings
            {
                BackupEndpoints = new[]
                {
                    new BackupEndpointNode
                    {
                        Name = "Backup #1",
                        Provider = "openai",
                        ApiEndpoint = "https://backup.example/v1",
                        ModelName = "gpt-4o-mini",
                        ApiKey = "backup-key",
                        Enabled = true
                    }
                }
            };

            // 1. Pre-first-chunk stream failure fails over to backup and accumulates fragmented tool calls + no-space data: lines
            var hitUrls = new List<string>();
            var streamClient = new OpenAIClient(settings)
            {
                StreamSendOverride = (req, _, _) =>
                {
                    string url = req.RequestUri?.ToString() ?? string.Empty;
                    hitUrls.Add(url);
                    if (url.StartsWith("https://primary.example", StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
                        {
                            Content = new StringContent("Service Unavailable", Encoding.UTF8, "text/plain")
                        });
                    }

                    string ssePayload =
                        "data:{\"id\":\"chatcmpl-stream-1\",\"choices\":[{\"delta\":{\"content\":\"Hello \"},\"finish_reason\":null}]}\n" +
                        "data: {\"id\":\"chatcmpl-stream-1\",\"choices\":[{\"delta\":{\"content\":\"world\",\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"move_\",\"arguments\":\"{\\\"x\\\":\"}}]},\"finish_reason\":null}]}\n" +
                        "data:{\"id\":\"chatcmpl-stream-1\",\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"name\":\"pawn\",\"arguments\":\"10}\"}}]},\"finish_reason\":\"tool_calls\"}],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":7,\"total_tokens\":18}}\n" +
                        "data:[DONE]\n";
                    return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
                    });
                }
            };

            var chunks = new List<string>();
            var firstRes = await streamClient.SendStreamAsync(CreateEnvelope("req-stream-ok"), c => { if (!string.IsNullOrEmpty(c.DeltaContent)) chunks.Add(c.DeltaContent!); }, CancellationToken.None);
            Assert.True(firstRes.IsOk);
            Assert.Equal(2, hitUrls.Count);
            Assert.Equal("Hello world", string.Concat(chunks));
            Assert.Equal("Hello world", firstRes.Value.Content);
            Assert.NotNull(firstRes.Value.ToolCallsJson);
            var toolArray = JArray.Parse(firstRes.Value.ToolCallsJson!);
            Assert.Single(toolArray);
            Assert.Equal("call_1", toolArray[0]["id"]?.ToString());
            Assert.Equal("move_pawn", toolArray[0]["function"]?["name"]?.ToString());
            Assert.Equal("{\"x\":10}", toolArray[0]["function"]?["arguments"]?.ToString());

            // 2. Mid-stream failure AFTER emitting a chunk must NOT fail over to backup (prevents spliced duplicate output)
            OpenAIClient.InvalidateFormatCache();
            var midStreamUrls = new List<string>();
            var midStreamClient = new OpenAIClient(settings)
            {
                StreamSendOverride = (req, _, _) =>
                {
                    midStreamUrls.Add(req.RequestUri?.ToString() ?? string.Empty);
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StreamContent(new FaultingAfterFirstLineStream(
                            "data: {\"id\":\"chatcmpl-mid\",\"choices\":[{\"delta\":{\"content\":\"Partial chunk\"},\"finish_reason\":null}]}\n"))
                    };
                    return Task.FromResult(response);
                }
            };

            var midChunks = new List<string>();
            var midRes = await midStreamClient.SendStreamAsync(CreateEnvelope("req-stream-mid"), c => { if (!string.IsNullOrEmpty(c.DeltaContent)) midChunks.Add(c.DeltaContent!); }, CancellationToken.None);
            Assert.True(midRes.IsErr);
            Assert.Single(midStreamUrls);
            Assert.Single(midChunks);
            Assert.Equal("Partial chunk", midChunks[0]);
        }

        [Fact]
        public async Task HybridAIClient_AndPlayer2Dtos_PreserveStreamBoundaries_AndSnakeCaseToolPayloads()
        {
            // Verify Player2RequestDto serializes snake_case fields for tools, tool_choice, and response_format
            var dto = new Player2RequestDto
            {
                Messages = new List<Player2MessageDto> { new Player2MessageDto { Role = "user", Content = "Act" } },
                Stream = true,
                Tools = new List<object> { new JObject { ["type"] = "function" } },
                ToolChoice = "auto",
                ResponseFormat = new JObject { ["type"] = "json_object" }
            };
            string json = JsonConvert.SerializeObject(dto);
            Assert.Contains("\"tool_choice\":\"auto\"", json);
            Assert.Contains("\"response_format\":{\"type\":\"json_object\"}", json);
            Assert.Contains("\"tools\":[", json);

            // Verify HybridAIClient does not fall back to local client if remote stream already emitted chunks
            var remotePrimary = new StubStreamingClient(emitBeforeFail: true);
            var localFallback = new StubStreamingClient(emitBeforeFail: false);
            var hybrid = new HybridAIClient(remotePrimary, localFallback);

            var emitted = new List<string>();
            var res = await hybrid.SendStreamAsync(CreateEnvelope("req-hybrid"), c => { if (!string.IsNullOrEmpty(c.DeltaContent)) emitted.Add(c.DeltaContent!); }, CancellationToken.None);
            Assert.True(res.IsErr);
            Assert.Equal(1, remotePrimary.StreamCalls);
            Assert.Equal(0, localFallback.StreamCalls);
            Assert.Single(emitted);
        }

        private sealed class FaultingAfterFirstLineStream : Stream
        {
            private readonly byte[] _firstBytes;
            private bool _readFirst;

            public FaultingAfterFirstLineStream(string firstLine)
            {
                _firstBytes = Encoding.UTF8.GetBytes(firstLine);
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (!_readFirst)
                {
                    _readFirst = true;
                    int len = Math.Min(count, _firstBytes.Length);
                    Array.Copy(_firstBytes, 0, buffer, offset, len);
                    return len;
                }
                throw new IOException("Connection reset mid-stream");
            }
        }

        private sealed class StubStreamingClient : IAIClient
        {
            private readonly bool _emitBeforeFail;
            public int StreamCalls { get; private set; }

            public StubStreamingClient(bool emitBeforeFail)
            {
                _emitBeforeFail = emitBeforeFail;
            }

            public bool IsLocalEndpoint => false;
            public bool IsConfigured() => true;
            public bool SupportsStreaming => true;
            public bool SupportsNpcServerState => false;

            public Task<Result<LlmResponse, RimMindError>> SendAsync(LlmRequestEnvelope envelope)
                => Task.FromResult(Result<LlmResponse, RimMindError>.Ok(new LlmResponse { Content = "ok" }));

            public Task<Result<LlmResponse, RimMindError>> SendStreamAsync(LlmRequestEnvelope envelope, Action<LlmChunk> onChunk, CancellationToken ct = default)
            {
                StreamCalls++;
                if (_emitBeforeFail)
                {
                    onChunk(new LlmChunk { DeltaContent = "partial-remote" });
                    return Task.FromResult(Result<LlmResponse, RimMindError>.Err(RimMindErrors.ClientTransient("mid-stream drop")));
                }
                onChunk(new LlmChunk { DeltaContent = "fallback-ok" });
                return Task.FromResult(Result<LlmResponse, RimMindError>.Ok(new LlmResponse { Content = "fallback-ok" }));
            }

            public Task<Result<bool, RimMindError>> SpawnNpcAsync(NpcProfile profile)
                => Task.FromResult(Result<bool, RimMindError>.Ok(true));
            public Task<Result<bool, RimMindError>> KillNpcAsync(string npcId)
                => Task.FromResult(Result<bool, RimMindError>.Ok(true));
            public Task<Result<List<string>, RimMindError>> QueryNpcMemoriesAsync(string npcId, string query, int limit)
                => Task.FromResult(Result<List<string>, RimMindError>.Ok(new List<string>()));
            public void Dispose() { }
        }
    }
}
