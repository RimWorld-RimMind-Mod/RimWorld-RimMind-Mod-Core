using System;
using System.Collections.Concurrent;
using System.Threading;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Storage;
using RimMind.Application.Common.Models;
using RimMind.Application.Common.Models.Context;
using RimMind.Application.Features.Llm;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Presentation.UI.Layout;
using RimMind.Presentation.UI.Framework;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation.Runtime.Services;

using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public class Window_AgentDialogue : RimMindWindowBase
    {
        private readonly Pawn _pawn;
        private readonly string _npcId;
        private readonly RuntimeBinding _runtimeBinding = new RuntimeBinding();
        private DialogueRuntimeLease? _runtimeLease;
        private string _inputText = "";
        private Vector2 _scrollPosition;
        private float _lastContentHeight;
        private const int MaxHistoryRounds = RimMindDefaults.MaxHistoryRounds;

        private IHistoryManager? GetHistoryManager()
            => _runtimeLease?.HistoryManager;

        private ISettingsProvider? GetSettingsProvider()
            => _runtimeLease?.SettingsProvider;

        private IContextBuilder? GetContextEngine()
            => _runtimeLease?.ContextEngine;

        private IRemoteSyncService? GetSyncService()
            => _runtimeLease?.SyncService;

        private string _streamingText = "";
        private bool _isStreaming;
        private string _thinkingText = "";
        private static long _nextTurnSequence;
        private long _lastRequestId;
        private DialogueRequestTerminalState _lastRequestState = DialogueRequestTerminalState.Idle;
        private DialogueRequestState? _activeRequest;

        public override Vector2 InitialSize => new Vector2(500f, 500f);

        public Window_AgentDialogue(Pawn pawn) : base()
        {
            _pawn = pawn;
            _npcId = $"NPC-{pawn.thingIDNumber}";
            forcePause = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
        }

        protected override void DrawContents(Rect inRect, RimMindLayoutScope scope)
        {
            RefreshRuntimeBinding();
            RefreshActiveRequest();
            Text.Font = GameFont.Medium;
            string title = $"{_pawn.LabelShortCap} - {"RimMind.UI.AgentDialogue.Title".Translate()}";
            Rect titleRect = new Rect(0f, 0f, inRect.width, 30f);
            scope.Record(titleRect, "Header:Title");
            Widgets.Label(titleRect, title);
            Text.Font = GameFont.Small;

            // NPC sync actions area (below title, above history)
            float syncAreaHeight = NpcSyncActions.MeasureHeight(_npcId);
            var syncRect = new Rect(0f, 35f, inRect.width, syncAreaHeight);
            scope.Record(syncRect, "Sync:Actions");
            NpcSyncActions.DrawNpcSyncActions(syncRect, _npcId, GetSyncService());

            float historyTop = 35f + syncAreaHeight + 4f;
            const float requestStatusHeight = 20f;
            float historyHeight = inRect.height - 70f - syncAreaHeight - 4f - requestStatusHeight;
            var historyRect = new Rect(0f, historyTop, inRect.width, historyHeight);
            scope.Record(historyRect, "History:List");

            DrawHistory(historyRect);

            var requestStatusRect = new Rect(0f, historyRect.yMax + 2f, inRect.width, requestStatusHeight - 2f);
            scope.Record(requestStatusRect, "Status:Request");
            DrawRequestStatus(requestStatusRect);

            float inputY = inRect.height - 30f;
            var inputRect = new Rect(0f, inputY, inRect.width - 100f, 30f);
            var sendRect = new Rect(inRect.width - 95f, inputY, 95f, 30f);
            scope.Record(inputRect, "Input:TextField");
            scope.Record(sendRect, "Button:Send");

            GUI.SetNextControlName("AgentDialogueInput");
            _inputText = Widgets.TextField(inputRect, _inputText);
            bool inputFocused = GUI.GetNameOfFocusedControl() == "AgentDialogueInput";

            GUI.enabled = _activeRequest == null;
            bool sendClicked = Widgets.ButtonText(sendRect, "RimMind.UI.AgentDialogue.Send".Translate());
            GUI.enabled = true;
            if (sendClicked)
            {
                SendMessage();
            }

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && inputFocused)
            {
                SendMessage();
                Event.current.Use();
            }
        }

        private void DrawHistory(Rect rect)
        {
            var history = GetHistoryManager()?.GetHistoryForDisplay(_npcId, MaxHistoryRounds);

            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.8f));

            float contentHeight = 0f;
            float lineSpacing = 24f;

            if (history != null)
                contentHeight = history.Count * lineSpacing + 10f;

            if (contentHeight < rect.height) contentHeight = rect.height;

            var contentRect = new Rect(0f, 0f, rect.width - 16f, contentHeight);
            var viewRect = new Rect(rect.x, rect.y, rect.width, rect.height);

            Widgets.BeginScrollView(viewRect, ref _scrollPosition, contentRect);

            float y = 5f;
            if (history != null)
            {
                foreach (var (role, content) in history)
                {
                    string prefix = role == "user"
                        ? "RimMind.UI.AgentDialogue.PlayerLabel".Translate() + ": "
                        : "RimMind.UI.AgentDialogue.AgentLabel".Translate() + ": ";
                    string displayContent = content;
                    if (role == "assistant"
                        && DialogueHistoryProjection.TryResolve(content, out DialogueTurnId turnId, out string projectedContent))
                    {
                        displayContent = _isStreaming
                            && _activeRequest?.TurnId == turnId
                            && !string.IsNullOrEmpty(_streamingText)
                            ? _streamingText
                            : projectedContent;
                    }
                    string line = prefix + displayContent;
                    float height = Text.CalcHeight(line, contentRect.width - 10f) + 4f;
                    var lineRect = new Rect(5f, y, contentRect.width - 10f, height);
                    Widgets.Label(lineRect, line);
                    y += height;
                }
            }

            contentRect.height = Mathf.Max(y + 10f, rect.height);
            _lastContentHeight = contentRect.height;

            Widgets.EndScrollView();

            if (_lastContentHeight > rect.height)
            {
                _scrollPosition.y = _lastContentHeight - rect.height;
            }
        }

        private void DrawRequestStatus(Rect rect)
        {
            string requestId = _lastRequestId > 0 ? _lastRequestId.ToString() : "-";
            string text = "RimMind.UI.AgentDialogue.RequestStatus".Translate(
                requestId,
                LocalizeRequestState(_lastRequestState));
            Text.Font = GameFont.Tiny;
            GUI.color = _lastRequestState == DialogueRequestTerminalState.Failed
                ? RimMindUI.ColorError
                : RimMindUI.ColorMuted;
            Widgets.Label(rect, text);
            TooltipHandler.TipRegion(rect, text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void SendMessage()
        {
            RefreshRuntimeBinding();
            RefreshActiveRequest();
            if (_activeRequest != null) return;
            if (string.IsNullOrWhiteSpace(_inputText)) return;
            IAgentControl? agent = _runtimeLease?.Agent.Resolve();
            if (agent == null || !agent.IsActive) return;

            string message = _inputText.Trim();
            _inputText = "";

            _thinkingText = "RimMind.UI.AgentDialogue.Thinking".Translate();
            _streamingText = "";
            _isStreaming = true;
            RuntimeServiceScope runtimeScope = _runtimeLease!.Scope;
            RuntimeGenerationToken runtimeToken = runtimeScope.Token;
            var turnId = new DialogueTurnId(
                _npcId,
                Interlocked.Increment(ref _nextTurnSequence));
            IHistoryManager? historyManager = runtimeScope.GetOptional<IHistoryManager>();
            var request = new DialogueRequestState(turnId, runtimeToken, historyManager);
            _activeRequest = request;
            _lastRequestId = request.RequestId;
            _lastRequestState = DialogueRequestTerminalState.Streaming;
            string placeholder = DialogueHistoryProjection.CreatePlaceholder(request.TurnId);
            DialogueHistoryProjection.ReplaceAssistantTurnById(request.TurnId, _thinkingText);
            historyManager?.AddPendingTurn(
                _npcId,
                request.TurnId.StableId,
                message,
                placeholder,
                "Dialogue");

            agent.ForceThink();

            var settings = runtimeScope.GetOptional<ISettingsProvider>();

            var envelope = LlmRequestEnvelopeBuilder
                .ForNpc(agent.NpcId ?? $"NPC-{_pawn.thingIDNumber}", gameStateInfo: new GameStateInfo().AddSection("dialogue_input", message))
                .ForScenarioId(ScenarioIds.Dialogue)
                .WithModId("RimMind.Dialogue")
                .WithMaxTokens(settings?.MaxTokens ?? RimMindDefaults.MaxTokens)
                .WithTemperature(settings?.DefaultTemperature ?? RimMindDefaults.DefaultTemperature)
                .Streaming(chunk =>
                {
                    if (!string.IsNullOrEmpty(chunk.DeltaContent))
                    {
                        LongEventHandler.ExecuteWhenFinished(() =>
                        {
                            if (!TryAccept(request))
                                return;
                            _streamingText += chunk.DeltaContent;
                        });
                    }
                })
                .Build();

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var result = await RimMind.Presentation.Api.RimMindAPI.SendAsync(envelope);
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        if (!TryAccept(request))
                            return;
                        if (result.IsOk)
                        {
                            request.HistoryManager?.ReplaceAssistantTurn(
                                _npcId,
                                request.TurnId.StableId,
                                result.Value.Content ?? "");
                            DialogueHistoryProjection.ReplaceAssistantTurnById(
                                request.TurnId,
                                result.Value.Content ?? "");
                        }
                        else
                        {
                            request.HistoryManager?.RemoveTurn(_npcId, request.TurnId.StableId);
                        }
                        CompleteRequest(
                            request,
                            result.IsOk
                                ? DialogueRequestTerminalState.Completed
                                : DialogueRequestTerminalState.Failed);
                    });
                }
                catch (System.Exception ex)
                {
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        if (!TryAccept(request))
                            return;
                        request.HistoryManager?.RemoveTurn(_npcId, request.TurnId.StableId);
                        CompleteRequest(request, DialogueRequestTerminalState.Failed);
                    });
                    RimMindErrors.Warn($"[RimMind-Core] AgentDialogue chat failed: {ex.Message}");
                }
            });
        }

        private void RefreshRuntimeBinding()
        {
            DialogueRuntimeLease? candidate = null;
            _runtimeBinding.Refresh(scope =>
            {
                candidate = new DialogueRuntimeLease(_pawn, scope);
                return candidate;
            });
            if (candidate != null
                && !candidate.IsDisposed
                && _runtimeBinding.BoundGeneration == candidate.Scope.Generation)
            {
                _runtimeLease = candidate;
            }
        }

        public override void PreClose()
        {
            _runtimeBinding.Dispose();
            _runtimeLease = null;
            base.PreClose();
        }

        private void RefreshActiveRequest()
        {
            DialogueRequestState? request = _activeRequest;
            if (request != null && !RuntimeServiceHub.Shared.IsCurrent(request.RuntimeToken))
                DiscardRequest(request);
        }

        private bool TryAccept(DialogueRequestState request)
        {
            if (!ReferenceEquals(_activeRequest, request))
                return false;
            if (RuntimeServiceHub.Shared.IsCurrent(request.RuntimeToken))
                return true;
            DiscardRequest(request);
            return false;
        }

        private void CompleteRequest(
            DialogueRequestState request,
            DialogueRequestTerminalState terminalState)
        {
            if (!ReferenceEquals(_activeRequest, request))
                return;
            DialogueHistoryProjection.Remove(request.TurnId);
            _activeRequest = null;
            _isStreaming = false;
            _streamingText = "";
            _lastRequestState = terminalState;
        }

        private void DiscardRequest(DialogueRequestState request)
        {
            if (!ReferenceEquals(_activeRequest, request))
                return;
            request.RecordStaleOnce(RuntimeServiceHub.Shared);
            request.HistoryManager?.RemoveTurn(_npcId, request.TurnId.StableId);
            DialogueHistoryProjection.Remove(request.TurnId);
            _activeRequest = null;
            _isStreaming = false;
            _streamingText = "";
            _lastRequestState = DialogueRequestTerminalState.Discarded;
        }

        private static string LocalizeRequestState(DialogueRequestTerminalState state)
            => (state switch
            {
                DialogueRequestTerminalState.Streaming => "RimMind.UI.AgentDialogue.State.Streaming",
                DialogueRequestTerminalState.Completed => "RimMind.UI.AgentDialogue.State.Completed",
                DialogueRequestTerminalState.Failed => "RimMind.UI.AgentDialogue.State.Failed",
                DialogueRequestTerminalState.Discarded => "RimMind.UI.AgentDialogue.State.Discarded",
                _ => "RimMind.UI.AgentDialogue.State.Idle"
            }).Translate();

        private enum DialogueRequestTerminalState
        {
            Idle,
            Streaming,
            Completed,
            Failed,
            Discarded
        }

        private sealed class DialogueRequestState
        {
            private bool _staleRecorded;

            public DialogueRequestState(
                DialogueTurnId turnId,
                RuntimeGenerationToken runtimeToken,
                IHistoryManager? historyManager)
            {
                TurnId = turnId;
                RuntimeToken = runtimeToken;
                HistoryManager = historyManager;
            }

            public long RequestId => TurnId.Sequence;
            public DialogueTurnId TurnId { get; }
            public RuntimeGenerationToken RuntimeToken { get; }
            public IHistoryManager? HistoryManager { get; }

            public void RecordStaleOnce(RuntimeServiceHub runtimeHub)
            {
                if (_staleRecorded)
                    return;
                _staleRecorded = true;
                runtimeHub.RecordStaleCompletion(LifecycleEventSources.AgentDialogue);
            }
        }

        private sealed class DialogueRuntimeLease : IDisposable
        {
            private bool _disposed;

            public DialogueRuntimeLease(Pawn pawn, RuntimeServiceScope scope)
            {
                Scope = scope;
                Agent = new CurrentAgentBinding<IAgentControl>(
                    () => _disposed
                        ? null
                        : CompPawnAgent.GetComp(pawn)?.ResolveCurrentAgent(scope));
                HistoryManager = scope.GetOptional<IHistoryManager>();
                SettingsProvider = scope.GetOptional<ISettingsProvider>();
                ContextEngine = scope.GetOptional<IContextBuilder>();
                SyncService = scope.GetOptional<IRemoteSyncService>();
            }

            public RuntimeServiceScope Scope { get; }
            public bool IsDisposed => _disposed;
            public CurrentAgentBinding<IAgentControl> Agent { get; }
            public IHistoryManager? HistoryManager { get; }
            public ISettingsProvider? SettingsProvider { get; }
            public IContextBuilder? ContextEngine { get; }
            public IRemoteSyncService? SyncService { get; }

            public void Dispose()
            {
                _disposed = true;
            }
        }

        private readonly struct DialogueTurnId : IEquatable<DialogueTurnId>
        {
            public DialogueTurnId(string npcId, long sequence)
            {
                NpcId = npcId ?? string.Empty;
                Sequence = sequence;
                StableId = Guid.NewGuid().ToString("N");
            }

            public string NpcId { get; }
            public long Sequence { get; }
            public string StableId { get; }

            public bool Equals(DialogueTurnId other)
                => string.Equals(StableId, other.StableId, StringComparison.Ordinal);

            public override bool Equals(object? obj)
                => obj is DialogueTurnId other && Equals(other);

            public override int GetHashCode()
                => StableId?.GetHashCode() ?? 0;

            public static bool operator ==(DialogueTurnId left, DialogueTurnId right) => left.Equals(right);
            public static bool operator !=(DialogueTurnId left, DialogueTurnId right) => !left.Equals(right);
        }

        private static class DialogueHistoryProjection
        {
            private static readonly ConcurrentDictionary<DialogueTurnId, string> Contents = new();
            private static readonly ConcurrentDictionary<string, DialogueTurnId> TurnIds = new();

            public static string CreatePlaceholder(DialogueTurnId turnId)
            {
                string placeholder = $"[[RimMindDialogueTurn:{turnId.StableId}]]";
                TurnIds[placeholder] = turnId;
                Contents.TryAdd(turnId, string.Empty);
                return placeholder;
            }

            public static void ReplaceAssistantTurnById(DialogueTurnId turnId, string content)
                => Contents[turnId] = content ?? string.Empty;

            public static void Remove(DialogueTurnId turnId)
            {
                string placeholder = $"[[RimMindDialogueTurn:{turnId.StableId}]]";
                Contents.TryRemove(turnId, out _);
                TurnIds.TryRemove(placeholder, out _);
            }

            public static bool TryResolve(
                string placeholder,
                out DialogueTurnId turnId,
                out string content)
            {
                if (TurnIds.TryGetValue(placeholder, out turnId)
                    && Contents.TryGetValue(turnId, out string? projected))
                {
                    content = projected;
                    return true;
                }

                turnId = default;
                content = placeholder;
                return false;
            }
        }
    }
}
