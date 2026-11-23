using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Client;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Infrastructure.Services.Clients.Shared;
using RimMind.Presentation.Api;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.UI;
using RimMind.Presentation.UI.Framework;
using UnityEngine;

namespace RimMind.Infrastructure.UI.DebugCenter
{
    public static class LiveAiProbeState
    {
        private static readonly object Lock = new();
        private static bool _isTesting;
        private static string? _lastStatus;
        private static long _lastLatencyMs;
        private static int _lastTokensUsed;
        private static string? _lastSnippet;
        private static string? _lastError;
        private static Color _statusColor = RimMindUI.ColorMuted;
        private static bool _offlineSimulationMode;
        private static GenerationUiOperation? _activeOperation;

        public static bool IsTesting
        {
            get { lock (Lock) return _isTesting; }
            private set { lock (Lock) _isTesting = value; }
        }

        public static string LastStatus
        {
            get
            {
                lock (Lock)
                {
                    if (_lastStatus != null)
                        return _lastStatus;
                }
                return UiText.Get("RimMind.UI.Hub.PingNotRun", "Not tested");
            }
            private set { lock (Lock) _lastStatus = value; }
        }

        public static long LastLatencyMs
        {
            get { lock (Lock) return _lastLatencyMs; }
            private set { lock (Lock) _lastLatencyMs = value; }
        }

        public static int LastTokensUsed
        {
            get { lock (Lock) return _lastTokensUsed; }
            private set { lock (Lock) _lastTokensUsed = value; }
        }

        public static string? LastSnippet
        {
            get { lock (Lock) return _lastSnippet; }
            private set { lock (Lock) _lastSnippet = value; }
        }

        public static string? LastError
        {
            get { lock (Lock) return _lastError; }
            private set { lock (Lock) _lastError = value; }
        }

        public static Color StatusColor
        {
            get { lock (Lock) return _statusColor; }
            private set { lock (Lock) _statusColor = value; }
        }

        public static bool OfflineSimulationMode
        {
            get { lock (Lock) return _offlineSimulationMode; }
            set { lock (Lock) _offlineSimulationMode = value; }
        }

        public static void TriggerProbe(ISettingsProvider settings, IClientManager? clientManager)
        {
            if (IsTesting)
                return;

            var operation = GenerationUiOperation.Capture(
                RuntimeServiceHub.Shared,
                LifecycleEventSources.LiveAiProbe);

            if (OfflineSimulationMode)
            {
                lock (Lock)
                {
                    _activeOperation = operation;
                    _isTesting = true;
                    _lastStatus = UiText.Get("RimMind.Settings.Status.Testing", "Testing...");
                    _statusColor = RimMindUI.ColorPaused;
                }
                Task.Run(async () =>
                {
                    await Task.Delay(180).ConfigureAwait(false);
                    RimMindAPI.MainThread.Post(() => PublishResult(
                        operation,
                        status: "200 OK (Offline Sim)",
                        latencyMs: 180,
                        tokensUsed: 12,
                        snippet: "{\"status\":\"ok\",\"simulated\":true}",
                        error: null,
                        color: RimMindUI.ColorActive));
                });
                return;
            }

            if (!settings.IsConfigured)
            {
                lock (Lock)
                {
                    _lastStatus = UiText.Get("RimMind.Settings.Status.NotConfigured", "Not configured");
                    _lastError = "API key or endpoint missing in Settings.";
                    _statusColor = RimMindUI.ColorPaused;
                }
                return;
            }

            lock (Lock)
            {
                _activeOperation = operation;
                _isTesting = true;
                _lastStatus = UiText.Get("RimMind.Settings.Status.Testing", "Testing...");
                _statusColor = RimMindUI.ColorPaused;
                _lastError = null;
                _lastSnippet = null;
            }

            string provider = settings.Provider;
            Task.Run(async () =>
            {
                try
                {
                    IAIClient? client = clientManager?.GetClient();
                    if (client == null)
                    {
                        RimMindAPI.MainThread.Post(() => PublishResult(
                            operation,
                            status: "FAIL (No Client)",
                            latencyMs: 0,
                            tokensUsed: 0,
                            snippet: null,
                            error: $"AIClient unavailable for provider '{provider}'.",
                            color: RimMindUI.ColorError));
                        return;
                    }

                    var envelope = new LlmRequestEnvelope
                    {
                        RequestId = "debug_probe_" + Guid.NewGuid().ToString("N")[..8],
                        ScenarioId = "RimMind.DebugProbe",
                        ModId = "RimMind.Debug",
                        Messages = new List<Domain.Llm.ChatMessage>
                        {
                            new Domain.Llm.ChatMessage
                            {
                                Role = "user",
                                Content = "Ping. Respond strictly with: OK"
                            }
                        },
                        MaxTokens = 40,
                        Temperature = 0.1f
                    };

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    Result<LlmResponse, RimMindError> result = await client.SendAsync(envelope).ConfigureAwait(false);
                    sw.Stop();
                    long elapsed = sw.ElapsedMilliseconds;

                    if (result.TryGetValue(out var response))
                    {
                        int tokens = response?.TokensUsed ?? 0;
                        string snippet = response?.Content?.Trim() ?? "OK";
                        RimMindAPI.MainThread.Post(() => PublishResult(
                            operation,
                            status: "200 OK",
                            latencyMs: elapsed,
                            tokensUsed: tokens,
                            snippet: snippet,
                            error: null,
                            color: RimMindUI.ColorActive));
                    }
                    else
                    {
                        string codeStr = result.Error != null ? ((int)result.Error.Code).ToString() : "FAIL";
                        string errorMsg = LogSanitizer.ForLog(result.Error?.Message ?? "Unknown error");
                        RimMindAPI.MainThread.Post(() => PublishResult(
                            operation,
                            status: $"{codeStr} (FAIL)",
                            latencyMs: elapsed,
                            tokensUsed: 0,
                            snippet: null,
                            error: errorMsg,
                            color: RimMindUI.ColorError));
                    }
                }
                catch (Exception ex)
                {
                    string safeError = LogSanitizer.ForLog(ex.Message);
                    RimMindAPI.MainThread.Post(() => PublishResult(
                        operation,
                        status: "ERROR",
                        latencyMs: 0,
                        tokensUsed: 0,
                        snippet: null,
                        error: safeError,
                        color: RimMindUI.ColorError));
                }
            });
        }

        private static void PublishResult(
            GenerationUiOperation operation,
            string status,
            long latencyMs,
            int tokensUsed,
            string? snippet,
            string? error,
            Color color)
        {
            if (!operation.CanPublish())
            {
                lock (Lock)
                {
                    if (ReferenceEquals(_activeOperation, operation))
                    {
                        _activeOperation = null;
                        _isTesting = false;
                    }
                }
                return;
            }

            lock (Lock)
            {
                if (!ReferenceEquals(_activeOperation, operation))
                    return;

                _activeOperation = null;
                _isTesting = false;
                _lastStatus = status;
                _lastLatencyMs = latencyMs;
                _lastTokensUsed = tokensUsed;
                _lastSnippet = snippet;
                _lastError = error;
                _statusColor = color;
            }
        }
    }
}
