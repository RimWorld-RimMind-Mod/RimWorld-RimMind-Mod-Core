using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Models.Client;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Services.Clients.OpenAI;
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
            Assert.Contains("\"model\":\"deepseek-v4.1-flash\"", requestJsonPayloads[1]);
            Assert.Contains("\"max_tokens\":2048", requestJsonPayloads[1]);
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
        }
    }
}
