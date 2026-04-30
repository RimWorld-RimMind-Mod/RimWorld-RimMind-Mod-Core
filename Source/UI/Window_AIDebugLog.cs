using System.Collections.Generic;
using System.Linq;
using RimMind.Core.Internal;
using UnityEngine;
using Verse;

namespace RimMind.Core.UI
{
    /// <summary>
    /// AI Debug Log 浮动窗口：左栏请求列表，右栏完整 Prompt + Response。
    /// 通过 Ctrl+点击游戏右下角 AI 图标打开，或通过 Dev 菜单 DebugAction 打开。
    /// </summary>
    public class Window_AIDebugLog : Window
    {
        private const float LeftWidth = 280f;
        private const float Divider = 6f;

        private static readonly Color ColorSuccess = new Color(0.4f, 0.9f, 0.4f);
        private static readonly Color ColorError = new Color(0.9f, 0.4f, 0.4f);

        private AIDebugEntry? _selected;
        private Vector2 _leftScroll;
        private Vector2 _inputScroll;
        private Vector2 _outputScroll;
        private float _splitRatio = 0.5f;
        private bool _draggingSplit;
        private string _filter = string.Empty;

        public override Vector2 InitialSize => new Vector2(1060f, 640f);

        public Window_AIDebugLog()
        {
            doCloseButton = true;
            doCloseX = true;
            resizeable = true;
            draggable = true;
            preventCameraMotion = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 90f, 30f),
                "RimMind.Core.UI.DebugLog.Title".Translate());
            Text.Font = GameFont.Small;

            Rect topBar = new Rect(inRect.x, inRect.y + 34f, inRect.width, 28f);
            DrawTopBar(topBar);

            Rect body = new Rect(inRect.x, topBar.yMax + 4f, inRect.width,
                                     inRect.height - topBar.yMax - 4f - CloseButSize.y - 4f);
            Rect leftRect = new Rect(body.x, body.y, LeftWidth, body.height);
            Rect rightRect = new Rect(body.x + LeftWidth + Divider, body.y,
                                      body.width - LeftWidth - Divider, body.height);

            Widgets.DrawLineVertical(body.x + LeftWidth + Divider * 0.5f, body.y, body.height);

            DrawLeftPane(leftRect);
            DrawRightPane(rightRect);
        }

        private void DrawTopBar(Rect r)
        {
            Rect searchRect = new Rect(r.x, r.y, 220f, r.height);
            _filter = Widgets.TextField(searchRect, _filter);
            if (string.IsNullOrEmpty(_filter))
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(searchRect.x + 4f, searchRect.y + 2f, searchRect.width, searchRect.height),
                    "RimMind.Core.UI.DebugLog.Search".Translate());
                GUI.color = Color.white;
            }

            int count = AIDebugLog.Instance?.Entries.Count ?? 0;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(searchRect.xMax + 8f, r.y + 2f, 120f, r.height),
                "RimMind.Core.UI.DebugLog.RecordCount".Translate(count));
            GUI.color = Color.white;

            Rect clearBtn = new Rect(r.xMax - 84f, r.y, 80f, r.height);
            if (Widgets.ButtonText(clearBtn, "RimMind.Core.UI.DebugLog.Clear".Translate()))
            {
                AIDebugLog.Instance?.Clear();
                _selected = null;
            }
        }

        private void DrawLeftPane(Rect r)
        {
            var entries = GetFilteredEntries();

            Rect viewRect = new Rect(0f, 0f, r.width - 16f, entries.Count * 58f);
            Widgets.BeginScrollView(r, ref _leftScroll, viewRect);

            float y = 0f;
            foreach (var entry in entries)
            {
                Rect row = new Rect(0f, y, viewRect.width, 54f);
                bool isSelected = _selected == entry;

                if (isSelected) Widgets.DrawHighlight(row);
                else if (Mouse.IsOver(row)) Widgets.DrawHighlightIfMouseover(row);

                GUI.color = entry.IsError ? ColorError : ColorSuccess;
                Widgets.DrawBox(new Rect(row.x, row.y, 4f, row.height));
                GUI.color = Color.white;

                Rect textRect = new Rect(row.x + 8f, row.y + 2f, row.width - 10f, row.height - 4f);

                string status = entry.IsError ? "✗" : "✓";
                Widgets.Label(new Rect(textRect.x, textRect.y, textRect.width, 18f),
                    $"{status} {entry.FormattedTime}  {entry.ElapsedMs}ms  [{entry.Priority}]");
                Widgets.Label(new Rect(textRect.x, textRect.y + 18f, textRect.width, 18f),
                    entry.Source);
                GUI.color = Color.gray;
                string telemetry = $"{entry.ModelName}  {entry.TokensUsed} {"RimMind.Core.UI.DebugLog.Tok".Translate()}";
                if (entry.AttemptCount > 1)
                    telemetry += $"  {"RimMind.Core.UI.DebugLog.Retry".Translate()}={entry.AttemptCount}";
                if (entry.QueueWaitMs > 0)
                    telemetry += $"  {"RimMind.Core.UI.DebugLog.Wait".Translate()}={entry.QueueWaitMs}ms";
                Widgets.Label(new Rect(textRect.x, textRect.y + 36f, textRect.width, 16f),
                    telemetry);
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(row)) _selected = entry;

                y += 58f;
                Widgets.DrawLineHorizontal(0f, y - 1f, viewRect.width);
            }

            Widgets.EndScrollView();
        }

        private void DrawRightPane(Rect r)
        {
            if (_selected == null)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(r.x + 8f, r.y + 8f, r.width, 24f),
                    "RimMind.Core.UI.DebugLog.ClickToView".Translate());
                GUI.color = Color.white;
                return;
            }

            Rect copyBtn = new Rect(r.xMax - 130f, r.y, 126f, 26f);
            if (Widgets.ButtonText(copyBtn, "RimMind.Core.UI.DebugLog.CopyResponse".Translate()))
            {
                string emptyLabel = "RimMind.Core.UI.Empty".Translate();
                string sysP = _selected.FullSystemPrompt ?? "";
                string usrP = _selected.FullUserPrompt ?? "";
                string astP = _selected.FullAssistantPrompt ?? "";
                string rsp = _selected.FullResponse ?? "";
                string copyText =
                    "─── System Prompt ───\n" +
                    (sysP.Length > 0 ? sysP : emptyLabel) +
                    "\n\n─── User Prompt ───\n" +
                    (usrP.Length > 0 ? usrP : emptyLabel);
                if (!string.IsNullOrWhiteSpace(astP))
                    copyText += "\n\n─── History (assistant) ───\n" + astP;
                copyText +=
                    "\n\n─── Response ───\n" +
                    (rsp.Length > 0 ? rsp : (
                        _selected.IsError ? $"[ERROR] {_selected.ErrorMsg ?? ""}" : emptyLabel));
                GUIUtility.systemCopyBuffer = copyText;
            }

            GUI.color = Color.gray;
            string metaLine = $"{_selected.Source}  |  {_selected.ModelName}  |  {_selected.TokensUsed} {"RimMind.Core.UI.DebugLog.Tok".Translate()}  |  {_selected.ElapsedMs}ms  |  {_selected.Priority}";
            if (_selected.AttemptCount > 1)
                metaLine += $"  |  {"RimMind.Core.UI.DebugLog.Attempt".Translate()}={_selected.AttemptCount}";
            if (_selected.QueueWaitMs > 0)
                metaLine += $"  |  {"RimMind.Core.UI.DebugLog.QueueWait".Translate()}={_selected.QueueWaitMs}ms";
            if (_selected.HttpStatusCode > 0)
                metaLine += $"  |  HTTP {_selected.HttpStatusCode}";
            Widgets.Label(new Rect(r.x, r.y + 4f, r.width - 140f, 20f), metaLine);
            GUI.color = Color.white;

            string emptyLbl = "RimMind.Core.UI.Empty".Translate();
            string sysPrompt = _selected.FullSystemPrompt ?? "";
            string userPrompt = _selected.FullUserPrompt ?? "";
            string assistantPrompt = _selected.FullAssistantPrompt ?? "";
            string response = _selected.FullResponse ?? "";

            string inputText =
                "─── System Prompt ───\n" +
                (sysPrompt.Length > 0 ? sysPrompt : emptyLbl) +
                "\n\n─── User Prompt ───\n" +
                (userPrompt.Length > 0 ? userPrompt : emptyLbl);

            if (!string.IsNullOrWhiteSpace(assistantPrompt))
                inputText += "\n\n─── History (assistant) ───\n" + assistantPrompt;

            string outputText =
                "─── Response ───\n" +
                (response.Length > 0 ? response : (
                    _selected.IsError ? $"[ERROR] {_selected.ErrorMsg ?? ""}" : emptyLbl));

            float metaHeight = 30f;
            float dividerH = 8f;
            float usableH = r.height - metaHeight - dividerH;
            float inputH = usableH * _splitRatio;
            float outputH = usableH - inputH;

            Rect inputArea = new Rect(r.x, r.y + metaHeight, r.width, inputH);
            Rect dividerRect = new Rect(r.x, r.y + metaHeight + inputH, r.width, dividerH);
            Rect outputArea = new Rect(r.x, r.y + metaHeight + inputH + dividerH, r.width, outputH);

            float inputContentH = Text.CalcHeight(inputText, inputArea.width - 16f);
            Rect inputView = new Rect(0f, 0f, inputArea.width - 16f, Mathf.Max(inputContentH, inputArea.height));
            Widgets.BeginScrollView(inputArea, ref _inputScroll, inputView);
            Widgets.TextArea(inputView, inputText, readOnly: true);
            Widgets.EndScrollView();

            Widgets.DrawHighlight(dividerRect);
            float dividerY = dividerRect.y + dividerH * 0.5f;
            Widgets.DrawLineHorizontal(r.x + 20f, dividerY, r.width - 40f, new Color(0.5f, 0.5f, 0.5f, 0.8f));
            if (Event.current.type == EventType.MouseDown && dividerRect.Contains(Event.current.mousePosition))
            {
                _draggingSplit = true;
                Event.current.Use();
            }
            if (_draggingSplit)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    _splitRatio = Mathf.Clamp01((Event.current.mousePosition.y - r.y - metaHeight) / usableH);
                    Event.current.Use();
                }
                if (Event.current.type == EventType.MouseUp)
                {
                    _draggingSplit = false;
                    Event.current.Use();
                }
            }

            float outputContentH = Text.CalcHeight(outputText, outputArea.width - 16f);
            Rect outputView = new Rect(0f, 0f, outputArea.width - 16f, Mathf.Max(outputContentH, outputArea.height));
            Widgets.BeginScrollView(outputArea, ref _outputScroll, outputView);
            Widgets.TextArea(outputView, outputText, readOnly: true);
            Widgets.EndScrollView();
        }

        private List<AIDebugEntry> GetFilteredEntries()
        {
            var all = AIDebugLog.Instance?.Entries ?? (IReadOnlyList<AIDebugEntry>)new List<AIDebugEntry>();
            if (string.IsNullOrEmpty(_filter))
                return ((IEnumerable<AIDebugEntry>)all).Reverse().ToList();
            return ((IEnumerable<AIDebugEntry>)all)
                .Where(e => e.Source.Contains(_filter) || e.FullResponse.Contains(_filter))
                .Reverse()
                .ToList();
        }
    }
}
