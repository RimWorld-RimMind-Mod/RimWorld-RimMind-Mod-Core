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
        private const float Divider   = 6f;

        private static readonly Color ColorSuccess = new Color(0.4f, 0.9f, 0.4f);
        private static readonly Color ColorError   = new Color(0.9f, 0.4f, 0.4f);

        private AIDebugEntry? _selected;
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;
        private string _filter = string.Empty;

        public override Vector2 InitialSize => new Vector2(1060f, 640f);

        public Window_AIDebugLog()
        {
            doCloseButton    = true;
            doCloseX         = true;
            resizeable       = true;
            draggable        = true;
            preventCameraMotion = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 标题行
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 90f, 30f),
                "RimMind.Core.UI.DebugLog.Title".Translate());
            Text.Font = GameFont.Small;

            // 工具栏
            Rect topBar = new Rect(inRect.x, inRect.y + 34f, inRect.width, 28f);
            DrawTopBar(topBar);

            Rect body     = new Rect(inRect.x, topBar.yMax + 4f, inRect.width,
                                     inRect.height - topBar.yMax - 4f - CloseButSize.y - 4f);
            Rect leftRect  = new Rect(body.x, body.y, LeftWidth, body.height);
            Rect rightRect = new Rect(body.x + LeftWidth + Divider, body.y,
                                      body.width - LeftWidth - Divider, body.height);

            // 分隔线
            Widgets.DrawLineVertical(body.x + LeftWidth + Divider * 0.5f, body.y, body.height);

            DrawLeftPane(leftRect);
            DrawRightPane(rightRect);
        }

        private void DrawTopBar(Rect r)
        {
            // 搜索框
            Rect searchRect = new Rect(r.x, r.y, 220f, r.height);
            _filter = Widgets.TextField(searchRect, _filter);
            if (string.IsNullOrEmpty(_filter))
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(searchRect.x + 4f, searchRect.y + 2f, searchRect.width, searchRect.height),
                    "RimMind.Core.UI.DebugLog.Search".Translate());
                GUI.color = Color.white;
            }

            // 条目计数
            int count = AIDebugLog.Instance?.Entries.Count ?? 0;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(searchRect.xMax + 8f, r.y + 2f, 120f, r.height),
                "RimMind.Core.UI.DebugLog.RecordCount".Translate(count));
            GUI.color = Color.white;

            // Clear 按钮
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

                // 状态色条
                GUI.color = entry.IsError ? ColorError : ColorSuccess;
                Widgets.DrawBox(new Rect(row.x, row.y, 4f, row.height));
                GUI.color = Color.white;

                Rect textRect = new Rect(row.x + 8f, row.y + 2f, row.width - 10f, row.height - 4f);

                string status = entry.IsError ? "✗" : "✓";
                Widgets.Label(new Rect(textRect.x, textRect.y,      textRect.width, 18f),
                    $"{status} {entry.FormattedTime}  {entry.ElapsedMs}ms");
                Widgets.Label(new Rect(textRect.x, textRect.y + 18f, textRect.width, 18f),
                    entry.Source);
                GUI.color = Color.gray;
                Widgets.Label(new Rect(textRect.x, textRect.y + 36f, textRect.width, 16f),
                    $"{entry.ModelName}  {entry.TokensUsed} tok");
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

            // 复制按钮（复制完整显示内容，包括 Prompt + Response/Error）
            Rect copyBtn = new Rect(r.xMax - 130f, r.y, 126f, 26f);
            if (Widgets.ButtonText(copyBtn, "RimMind.Core.UI.DebugLog.CopyResponse".Translate()))
            {
                string emptyLabel = "RimMind.Core.UI.Empty".Translate();
                string copyText =
                    "─── System Prompt ───\n" +
                    (_selected.FullSystemPrompt.Length > 0 ? _selected.FullSystemPrompt : emptyLabel) +
                    "\n\n─── User Prompt ───\n" +
                    (_selected.FullUserPrompt.Length > 0 ? _selected.FullUserPrompt : emptyLabel) +
                    "\n\n─── Response ───\n" +
                    (_selected.FullResponse.Length > 0 ? _selected.FullResponse : (
                        _selected.IsError ? $"[ERROR] {_selected.ErrorMsg}" : emptyLabel));
                GUIUtility.systemCopyBuffer = copyText;
            }

            // 元数据行
            GUI.color = Color.gray;
            Widgets.Label(new Rect(r.x, r.y + 4f, r.width - 140f, 20f),
                $"{_selected.Source}  |  {_selected.ModelName}  |  {_selected.TokensUsed} tok  |  {_selected.ElapsedMs}ms");
            GUI.color = Color.white;

            string emptyLbl = "RimMind.Core.UI.Empty".Translate();
            string fullText =
                "─── System Prompt ───\n" +
                (_selected.FullSystemPrompt.Length > 0 ? _selected.FullSystemPrompt : emptyLbl) +
                "\n\n─── User Prompt ───\n" +
                (_selected.FullUserPrompt.Length > 0 ? _selected.FullUserPrompt : emptyLbl) +
                "\n\n─── Response ───\n" +
                (_selected.FullResponse.Length > 0 ? _selected.FullResponse : (
                    _selected.IsError ? $"[ERROR] {_selected.ErrorMsg}" : emptyLbl));

            Rect textArea = new Rect(r.x, r.y + 30f, r.width, r.height - 30f);
            float textH = Text.CalcHeight(fullText, textArea.width - 16f);
            Rect viewRect = new Rect(0f, 0f, textArea.width - 16f, Mathf.Max(textH, textArea.height));
            Widgets.BeginScrollView(textArea, ref _rightScroll, viewRect);
            Widgets.TextArea(viewRect, fullText, readOnly: true);
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
