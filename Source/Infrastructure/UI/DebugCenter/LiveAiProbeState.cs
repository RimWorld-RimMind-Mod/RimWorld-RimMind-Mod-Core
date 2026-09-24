using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Client;
using RimMind.Domain.Common;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.DebugCenter
{
    public static class LiveAiProbeState
    {
        private static readonly object Lock = new();
        private static bool _isTesting;
        private static string _lastStatus = "RimMind.UI.Hub.PingNotRun".Translate();
        private static long _lastLatencyMs;
        private static int _lastTokensUsed;
        private static string? _lastSnippet;
        private static string? _lastError;
        private static Color _statusColor = RimMindUI.ColorMuted;
        private static bool _offlineSimulationMode;

        public static bool IsTesting
        {
            get { lock (Lock) return _isTesting; }
            private set { lock (Lock) _isTesting = value; }
        }

        public static string LastStatus
        {
            get { lock (Lock) return _lastStatus; }
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

            if (OfflineSimulationMode)
            {
                IsTesting = true;
                LastStatus = "RimMind.Settings.Status.Testing".Translate();
                StatusColor = RimMindUI.ColorPaused;
                Task.Run(async () =>
                {
                    await Task.Delay(180).ConfigureAwait(false);
                    lock (Lock)
                    {
                        _lastStatus = "200 OK (Offline Sim)";
                        _lastLatencyMs = 180;
                        _lastTokensUsed = 12;
                        _lastSnippet = "{\"status\":\"ok\",\"simulated\":true}";
                        _lastError = null;
                        _statusColor = RimMindUI.ColorActive;
                        _isTesting = false;
                    }
                });
                return;
            }

            if (!settings.IsConfigured)
            {
                lock (Lock)
                {
                    _lastStatus = "RimMind.Settings.Status.NotConfigured".Translate();
                    _lastError = "API key or endpoint missing in Settings.";
                    _statusColor = RimMindUI.ColorPaused;
                }
                return;
            }

            IsTesting = true;
            LastStatus = "RimMind.Settings.Status.Testing".Translate();
            StatusColor = RimMindUI.ColorPaused;
            LastError = null;
            LastSnippet = null;

            Task.Run(async () =>
            {
                try
                {
                    IAIClient? client = settings.Provider == "player2"
                        ? clientManager?.GetPlayer2Client()
                        : clientManager?.GetClient();

                    if (client == null)
                    {
                        lock (Lock)
                        {
                            _lastStatus = "FAIL (No Client)";
                            _lastError = $"AIClient unavailable for provider '{settings.Provider}'.";
                            _statusColor = RimMindUI.ColorError;
                            _isTesting = false;
                        }
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

                    lock (Lock)
                    {
                        _lastLatencyMs = sw.ElapsedMilliseconds;
                        if (result.TryGetValue(out var response))
                        {
                            _lastStatus = "200 OK";
                            _lastTokensUsed = response?.TokensUsed ?? 0;
                            _lastSnippet = response?.Content?.Trim() ?? "OK";
                            _lastError = null;
                            _statusColor = RimMindUI.ColorActive;
                        }
                        else
                        {
                            string codeStr = result.Error != null ? ((int)result.Error.Code).ToString() : "FAIL";
                            _lastStatus = $"{codeStr} (FAIL)";
                            _lastError = result.Error?.Message ?? "Unknown error";
                            _lastTokensUsed = 0;
                            _statusColor = RimMindUI.ColorError;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (Lock)
                    {
                        _lastStatus = "ERROR";
                        _lastError = ex.Message;
                        _statusColor = RimMindUI.ColorError;
                    }
                }
                finally
                {
                    lock (Lock)
                    {
                        _isTesting = false;
                    }
                }
            });
        }
    }
}
