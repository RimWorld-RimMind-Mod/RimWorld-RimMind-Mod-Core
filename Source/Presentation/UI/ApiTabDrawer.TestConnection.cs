using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimMind.Application.Common.Helpers;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Client;
using RimMind.Application.Common.Models;
using RimMind.Domain.Enums;
using RimMind.Domain.Llm;
using DomainChatMessage = RimMind.Domain.Llm.ChatMessage;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.Settings;
using RimMind.Presentation.UI.Framework;
using UnityEngine;
using Verse;

namespace RimMind.Presentation.UI
{
    internal static partial class ApiTabDrawer
    {
        private static ConnectionTestOperation? _activeConnectionTest;

        private static void RunConnectionTest(
            ISettingsProvider s,
            RuntimeServiceScope runtimeScope,
            IExtensionRegistry<IAIClientFactory> providerRegistry)
        {
            if (_testPending)
                return;

            IClientManager? clientManager = runtimeScope.GetOptional<IClientManager>();
            NormalizeConnectionSettings(s);
            s.Persist();
            clientManager?.InvalidateCache();
            Log.Message(BuildConnectionDebugLine("start", s, providerRegistry, null, null));

            if (!AIProviderRegistry.RequiresApiKey(s.Provider, providerRegistry))
            if (s.Provider == "player2")
            {
                ConnectionTestOperation operation = BeginConnectionTest(runtimeScope.Token);

                Task.Run(async () =>
                {
                    try
                    {
                        var client = clientManager?.GetPlayer2Client();
                        LogFromBackground(BuildConnectionDebugLine("player2-client", s, providerRegistry, client, client?.IsConfigured()));
                        if (client == null)
                        {
                            LongEventHandler.ExecuteWhenFinished(() =>
                            {
                                TryPublishConnectionTest(
                                    operation,
                                    "RimMind.Settings.Player2.NotAvailable".Translate(),
                                    new Color(0.9f, 0.4f, 0.4f));
                            });
                            return;
                        }

                        var envelope = new LlmRequestEnvelope
                        {
                            RequestId = "test",
                            ScenarioId = "RimMind.Test",
                            ModId = "RimMind.Test",
                            Messages = new List<DomainChatMessage> { new DomainChatMessage { Role = "user", Content = "RimMind.Settings.TestMessage".Translate() } },
                            MaxTokens = RimMindDefaults.TestConnectionMaxTokens,
                            Temperature = 0.7f,
                        };
                        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                        var result = await client.SendAsync(envelope);
                        stopwatch.Stop();
                        long rttMs = stopwatch.ElapsedMilliseconds;
                        if (result.TryGetValue(out var response))
                        {
                            var content = response.Content.Trim();
                            var tok = response.TokensUsed;
                            LongEventHandler.ExecuteWhenFinished(() =>
                            {
                                TryPublishConnectionTest(
                                    operation,
                                    $"OK ({rttMs}ms) {content} ({tok} tok)",
                                    new Color(0.4f, 0.9f, 0.4f));
                            });
                        }
                        else
                        {
                            var error = result.Error.Message;
                            LongEventHandler.ExecuteWhenFinished(() =>
                            {
                                TryPublishConnectionTest(
                                    operation,
                                    $"FAIL ({rttMs}ms) {error}",
                                    new Color(0.9f, 0.4f, 0.4f));
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message;
                        LongEventHandler.ExecuteWhenFinished(() =>
                        {
                            TryPublishConnectionTest(
                                operation,
                                $"FAIL {msg}",
                                new Color(0.9f, 0.4f, 0.4f));
                        });
                    }
                });
                return;
            }
            else if (s.Provider == "extended_service")
            {
                ConnectionTestOperation operation = BeginConnectionTest(runtimeScope.Token);

                Task.Run(async () =>
                {
                    try
                    {
                        var client = clientManager?.GetClient();
                        LogFromBackground(BuildConnectionDebugLine("modelservice-client", s, providerRegistry, client, client?.IsConfigured()));
                        if (client == null || !client.IsConfigured())
                        {
                            LongEventHandler.ExecuteWhenFinished(() =>
                            {
                                TryPublishConnectionTest(
                                    operation,
                                    "RimMind.Settings.ModelService.NoActiveNodes".Translate(),
                                    Color.yellow);
                            });
                            return;
                        }

                        var envelope = new LlmRequestEnvelope
                        {
                            RequestId = "test",
                            ScenarioId = "RimMind.Test",
                            ModId = "RimMind.Test",
                            Messages = new List<DomainChatMessage> { new DomainChatMessage { Role = "user", Content = "RimMind.Settings.TestMessage".Translate() } },
                            MaxTokens = RimMindDefaults.TestConnectionMaxTokens,
                            Temperature = 0.7f,
                        };
                        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                        var result = await client.SendAsync(envelope);
                        stopwatch.Stop();
                        long rttMs = stopwatch.ElapsedMilliseconds;
                        if (result.TryGetValue(out var response))
                        {
                            var content = response.Content.Trim();
                            var tok = response.TokensUsed;
                            LongEventHandler.ExecuteWhenFinished(() =>
                            {
                                TryPublishConnectionTest(
                                    operation,
                                    $"OK ({rttMs}ms) {content} ({tok} tok)",
                                    new Color(0.4f, 0.9f, 0.4f));
                            });
                        }
                        else
                        {
                            var error = result.Error.Message;
                            LongEventHandler.ExecuteWhenFinished(() =>
                            {
                                TryPublishConnectionTest(
                                    operation,
                                    $"FAIL ({rttMs}ms) {error}",
                                    new Color(0.9f, 0.4f, 0.4f));
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message;
                        LongEventHandler.ExecuteWhenFinished(() =>
                        {
                            TryPublishConnectionTest(
                                operation,
                                $"FAIL {msg}",
                                new Color(0.9f, 0.4f, 0.4f));
                        });
                    }
                });
                return;
            }

            if (!s.IsOpenAIConfigured())
            {
                Log.Message(BuildConnectionDebugLine(
                    "openai-settings-not-configured",
                    s,
                    providerRegistry,
                    null,
                    null));
                _testStatus = "RimMind.Settings.Status.NotConfigured".Translate();
                _testStatusColor = Color.yellow;
                return;
            }

            ConnectionTestOperation openAiOperation = BeginConnectionTest(runtimeScope.Token);

            Task.Run(async () =>
            {
                try
                {
                    var client = clientManager?.GetClient();
                    LogFromBackground(BuildConnectionDebugLine("openai-client", s, providerRegistry, client, client?.IsConfigured()));
                    if (client == null)
                    {
                        LongEventHandler.ExecuteWhenFinished(() =>
                        {
                            TryPublishConnectionTest(
                                openAiOperation,
                                "RimMind.Settings.Status.NotConfigured".Translate(),
                                Color.yellow);
                        });
                        return;
                    }

                    var envelope2 = new LlmRequestEnvelope
                    {
                        RequestId = "test",
                        ScenarioId = "RimMind.Test",
                        ModId = "RimMind.Test",
                        Messages = new List<DomainChatMessage> { new DomainChatMessage { Role = "user", Content = "RimMind.Settings.TestMessage".Translate() } },
                        MaxTokens = 60,
                        Temperature = 0.7f,
                    };
                    var stopwatch2 = System.Diagnostics.Stopwatch.StartNew();
                    var result2 = await client.SendAsync(envelope2);
                    stopwatch2.Stop();
                    long rttMs2 = stopwatch2.ElapsedMilliseconds;
                    if (result2.TryGetValue(out var response2))
                    {
                        var content = response2.Content.Trim();
                        var tok = response2.TokensUsed;
                        LongEventHandler.ExecuteWhenFinished(() =>
                        {
                            TryPublishConnectionTest(
                                openAiOperation,
                                $"OK ({rttMs2}ms) {content} ({tok} tok)",
                                new Color(0.4f, 0.9f, 0.4f));
                        });
                    }
                    else
                    {
                        var error = result2.Error.Message;
                        LongEventHandler.ExecuteWhenFinished(() =>
                        {
                            TryPublishConnectionTest(
                                openAiOperation,
                                $"FAIL ({rttMs2}ms) {error}",
                                new Color(0.9f, 0.4f, 0.4f));
                        });
                    }
                }
                catch (Exception ex)
                {
                    var msg = ex.Message;
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        TryPublishConnectionTest(
                            openAiOperation,
                            $"FAIL {msg}",
                            new Color(0.9f, 0.4f, 0.4f));
                    });
                }
            });
        }

        private static ConnectionTestOperation BeginConnectionTest(RuntimeGenerationToken token)
        {
            var operation = new ConnectionTestOperation(token);
            _activeConnectionTest = operation;
            _testPending = true;
            _testStatus = "RimMind.Settings.Status.Testing".Translate();
            _testStatusColor = Color.yellow;
            return operation;
        }

        private static bool TryPublishConnectionTest(
            ConnectionTestOperation operation,
            string status,
            Color color)
        {
            if (!operation.CanPublish())
            {
                if (ReferenceEquals(_activeConnectionTest, operation))
                {
                    _activeConnectionTest = null;
                    _testPending = false;
                }
                return false;
            }

            if (!ReferenceEquals(_activeConnectionTest, operation))
                return false;

            _activeConnectionTest = null;
            _testPending = false;
            _testStatus = status;
            _testStatusColor = color;
            return true;
        }

        private sealed class ConnectionTestOperation
        {
            private readonly GenerationUiOperation _generationOperation;

            public ConnectionTestOperation(RuntimeGenerationToken runtimeToken)
            {
                _generationOperation = new GenerationUiOperation(
                    RuntimeServiceHub.Shared,
                    runtimeToken,
                    LifecycleEventSources.TestConnection);
            }

            public bool CanPublish() => _generationOperation.CanPublish();
        }

        private static void NormalizeConnectionSettings(ISettingsProvider s)
        {
            s.ApiKey = (s.ApiKey ?? string.Empty).Trim();
            s.ApiEndpoint = (s.ApiEndpoint ?? string.Empty).Trim();
            s.ModelName = (s.ModelName ?? string.Empty).Trim();
            s.Player2RemoteUrl = (s.Player2RemoteUrl ?? string.Empty).Trim();
        }

        private static string BuildConnectionDebugLine(
            string stage,
            ISettingsProvider s,
            IExtensionRegistry<IAIClientFactory> providerRegistry,
            IAIClient? client,
            bool? configured)
        {
            string clientType = client == null ? "(null)" : client.GetType().Name;
            string configuredText = configured.HasValue ? configured.Value.ToString() : "(n/a)";
            return $"[RimMind-Core] TestConnection {stage}: provider={s.Provider}, requiresKey={AIProviderRegistry.RequiresApiKey(s.Provider, providerRegistry)}, keyLen={(s.ApiKey ?? string.Empty).Length}, endpointLen={(s.ApiEndpoint ?? string.Empty).Length}, model={s.ModelName}, client={clientType}, clientConfigured={configuredText}";
        }

        private static void LogFromBackground(string message)
        {
            LongEventHandler.ExecuteWhenFinished(() => Log.Message(message));
        }

        private static float EstimateApiHeight()
        {
            return 1250f;
        }

        internal static string GetProviderLabel(string p)
        {
            string key = $"RimMind.Settings.Provider.{NormalizeProviderTranslationSuffix(p)}";
            var translation = key.Translate();
            return translation == key ? p : translation;
        }

        private static string NormalizeProviderTranslationSuffix(string providerId)
        {
            return providerId?.ToLowerInvariant() switch
            {
                "openai" => "OpenAI",
                "player2" => "Player2",
                "extended_service" => "ExtendedService",
                _ => providerId ?? string.Empty
            };
        }

        private static string GetAutoApplyModeLabel(FlywheelAutoApplyMode mode)
        {
            return mode switch
            {
                FlywheelAutoApplyMode.Off => "RimMind.UI.FlywheelAutoApply.Off".Translate(),
                FlywheelAutoApplyMode.LogOnly => "RimMind.UI.FlywheelAutoApply.LogOnly".Translate(),
                FlywheelAutoApplyMode.ApplyWithLog => "RimMind.UI.FlywheelAutoApply.Apply".Translate(),
                _ => mode.ToString()
            };
        }
    }
}
