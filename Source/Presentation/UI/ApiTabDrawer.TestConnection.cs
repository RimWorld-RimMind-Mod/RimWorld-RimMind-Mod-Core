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

            // Each provider selects its client independently of RequiresApiKey. (Previously a brace-less
            // `if (!RequiresApiKey) if (player2) {...} else if (extended_service) {...}` bound the else-if to the
            // inner if, so extended_service silently fell through to the OpenAI path whenever it required a key.)
            Func<IAIClient?> getClient;
            string stage;
            string notConfiguredKey;
            bool requireConfiguredClient;
            if (s.Provider == "player2")
            {
                getClient = () => clientManager?.GetPlayer2Client();
                stage = "player2-client";
                notConfiguredKey = "RimMind.Settings.Player2.NotAvailable";
                requireConfiguredClient = false;
            }
            else if (s.Provider == "extended_service")
            {
                getClient = () => clientManager?.GetClient();
                stage = "modelservice-client";
                notConfiguredKey = "RimMind.Settings.ModelService.NoActiveNodes";
                requireConfiguredClient = true;
            }
            else
            {
                if (!s.IsOpenAIConfigured())
                {
                    Log.Message(BuildConnectionDebugLine("openai-settings-not-configured", s, providerRegistry, null, null));
                    _testStatus = "RimMind.Settings.Status.NotConfigured".Translate();
                    _testStatusColor = Color.yellow;
                    return;
                }

                getClient = () => clientManager?.GetClient();
                stage = "openai-client";
                notConfiguredKey = "RimMind.Settings.Status.NotConfigured";
                requireConfiguredClient = false;
            }

            RunConnectionProbe(s, providerRegistry, runtimeScope.Token, getClient, stage, notConfiguredKey, requireConfiguredClient);
        }

        private static void RunConnectionProbe(
            ISettingsProvider s,
            IExtensionRegistry<IAIClientFactory> providerRegistry,
            RuntimeGenerationToken token,
            Func<IAIClient?> getClient,
            string stage,
            string notConfiguredKey,
            bool requireConfiguredClient)
        {
            ConnectionTestOperation operation = BeginConnectionTest(token);

            // Translate on the main thread: Verse's LanguageDatabase is not thread-safe.
            string testMessage = "RimMind.Settings.TestMessage".Translate();
            string notConfiguredText = notConfiguredKey.Translate();
            Color notConfiguredColor = requireConfiguredClient || notConfiguredKey == "RimMind.Settings.Status.NotConfigured"
                ? Color.yellow
                : new Color(0.9f, 0.4f, 0.4f);
            string okFormat = TranslateOrFallback("RimMind.Settings.TestResult.Ok", "OK ({0}ms) {1} ({2} tok)");
            string failFormat = TranslateOrFallback("RimMind.Settings.TestResult.Fail", "FAIL ({0}ms) {1}");
            string exceptionFormat = TranslateOrFallback("RimMind.Settings.TestResult.Exception", "FAIL {0}");
            var okColor = new Color(0.4f, 0.9f, 0.4f);
            var failColor = new Color(0.9f, 0.4f, 0.4f);

            Task.Run(async () =>
            {
                try
                {
                    var client = getClient();
                    LogFromBackground(BuildConnectionDebugLine(stage, s, providerRegistry, client, client?.IsConfigured()));
                    if (client == null || (requireConfiguredClient && !client.IsConfigured()))
                    {
                        PostToMainThread(() => TryPublishConnectionTest(operation, notConfiguredText, notConfiguredColor));
                        return;
                    }

                    var envelope = new LlmRequestEnvelope
                    {
                        RequestId = "test",
                        ScenarioId = "RimMind.Test",
                        ModId = "RimMind.Test",
                        Messages = new List<DomainChatMessage> { new DomainChatMessage { Role = "user", Content = testMessage } },
                        MaxTokens = RimMindDefaults.TestConnectionMaxTokens,
                        Temperature = 0.7f,
                    };
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var result = await client.SendAsync(envelope);
                    stopwatch.Stop();
                    long rttMs = stopwatch.ElapsedMilliseconds;
                    if (result.TryGetValue(out var response))
                    {
                        string status = SafeFormat(okFormat, rttMs, (response.Content ?? string.Empty).Trim(), response.TokensUsed);
                        PostToMainThread(() => TryPublishConnectionTest(operation, status, okColor));
                    }
                    else
                    {
                        string status = SafeFormat(failFormat, rttMs, result.Error.Message);
                        PostToMainThread(() => TryPublishConnectionTest(operation, status, failColor));
                    }
                }
                catch (Exception ex)
                {
                    string status = SafeFormat(exceptionFormat, ex.Message);
                    PostToMainThread(() => TryPublishConnectionTest(operation, status, failColor));
                }
            });
        }

        private static string TranslateOrFallback(string key, string fallback)
        {
            return key.CanTranslate() ? (string)key.Translate() : fallback;
        }

        private static string SafeFormat(string format, params object[] args)
        {
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format + " " + string.Join(" ", args);
            }
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

        /// <summary>Marshals a background continuation to the main thread (thread-safe).</summary>
        private static void PostToMainThread(Action action)
        {
            RimMind.Presentation.Api.RimMindAPI.MainThread.Post(action);
        }

        private static void LogFromBackground(string message)
        {
            PostToMainThread(() => Log.Message(message));
        }

        internal static float EstimateApiHeight(ISettingsProvider? s = null)
        {
            int backupCount = s?.BackupEndpoints?.Count
                ?? RimMindCoreMod.Settings?.backupEndpoints?.Count
                ?? 0;
            return 1250f + backupCount * 56f + 120f;
        }

        internal static string GetProviderLabel(string p)
        {
            string key = $"RimMind.Settings.Provider.{NormalizeProviderTranslationSuffix(p)}";
            return key.CanTranslate() ? (string)key.Translate() : p;
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
