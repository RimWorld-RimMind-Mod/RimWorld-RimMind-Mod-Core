using System.Collections.Generic;
using RimMind.Core.Agent;
using RimMind.Core.Comps;
using RimMind.Core.Context;
using UnityEngine;
using Verse;

namespace RimMind.Core.UI
{
    public class Window_AgentDialogue : Window
    {
        private readonly Pawn _pawn;
        private readonly PawnAgent _agent;
        private readonly string _npcId;
        private string _inputText = "";
        private Vector2 _scrollPosition;
        private float _lastContentHeight;
        private const int MaxHistoryRounds = 20;
        private int _lastHistoryCount;

        public override Vector2 InitialSize => new Vector2(500f, 500f);

        public Window_AgentDialogue(Pawn pawn) : base()
        {
            _pawn = pawn;
            _agent = CompPawnAgent.GetComp(pawn)?.Agent;
            _npcId = $"NPC-{pawn.ThingID}";
            forcePause = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            string title = $"{_pawn.LabelShortCap} - {"RimMind.Core.UI.AgentDialogue.Title".Translate()}";
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), title);
            Text.Font = GameFont.Small;

            float historyHeight = inRect.height - 70f;
            var historyRect = new Rect(0f, 35f, inRect.width, historyHeight);

            CheckAndUpdateThinking();
            DrawHistory(historyRect);

            float inputY = inRect.height - 30f;
            var inputRect = new Rect(0f, inputY, inRect.width - 100f, 30f);
            var sendRect = new Rect(inRect.width - 95f, inputY, 95f, 30f);

            string prevText = _inputText;
            _inputText = Widgets.TextField(inputRect, _inputText);
            bool inputFocused = GUI.GetNameOfFocusedControl() == "AgentDialogueInput";

            if (Widgets.ButtonText(sendRect, "RimMind.Core.UI.AgentDialogue.Send".Translate()))
            {
                SendMessage();
            }

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && inputFocused)
            {
                SendMessage();
                Event.current.Use();
            }
        }

        private void CheckAndUpdateThinking()
        {
            if (_agent == null || !_agent.IsActive) return;

            var history = HistoryManager.Instance.GetHistory(_npcId, MaxHistoryRounds);
            int currentCount = history?.Count ?? 0;

            if (currentCount != _lastHistoryCount)
            {
                _lastHistoryCount = currentCount;
            }
        }

        private void DrawHistory(Rect rect)
        {
            var history = HistoryManager.Instance.GetHistory(_npcId, MaxHistoryRounds);

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
                        ? "RimMind.Core.UI.AgentDialogue.PlayerLabel".Translate() + ": "
                        : "RimMind.Core.UI.AgentDialogue.AgentLabel".Translate() + ": ";
                    string displayContent = content;
                    if (role == "assistant" && content == "RimMind.Core.UI.AgentDialogue.Thinking".Translate())
                        displayContent = content;
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

        private void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(_inputText)) return;
            if (_agent == null || !_agent.IsActive) return;

            string message = _inputText.Trim();
            _inputText = "";

            string thinkingText = "RimMind.Core.UI.AgentDialogue.Thinking".Translate();
            HistoryManager.Instance.AddTurn(_npcId, message, thinkingText);

            var npcId = _npcId;
            _agent.ForceThink();

            var request = new RimMind.Core.Context.ContextRequest
            {
                NpcId = npcId,
                Scenario = RimMind.Core.Context.ScenarioIds.Dialogue,
                Budget = 0.6f,
                CurrentQuery = message,
                MaxTokens = RimMindCoreMod.Settings.maxTokens,
                Temperature = RimMindCoreMod.Settings.defaultTemperature,
            };

            var engine = RimMindAPI.GetContextEngine();
            var snapshot = engine.BuildSnapshot(request);
            var driver = RimMind.Core.Npc.StorageDriverFactory.GetDriver();

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var result = await driver.ChatAsync(snapshot);
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        var currentHistory = HistoryManager.Instance.GetHistory(npcId, MaxHistoryRounds);
                        if (currentHistory != null)
                        {
                            for (int i = currentHistory.Count - 1; i >= 0; i--)
                            {
                                if (currentHistory[i].role == "assistant" && currentHistory[i].content == thinkingText)
                                {
                                    HistoryManager.Instance.ReplaceLastAssistantTurn(npcId, result.Message ?? "");
                                    break;
                                }
                            }
                        }
                    });
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[RimMind] AgentDialogue chat failed: {ex.Message}");
                }
            });
        }
    }
}
