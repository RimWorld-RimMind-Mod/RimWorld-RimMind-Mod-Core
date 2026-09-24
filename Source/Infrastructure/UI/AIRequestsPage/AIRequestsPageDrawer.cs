using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Debug;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using RimMind.Presentation.Runtime.Services;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.AIRequestsPage
{
    public sealed class AIRequestsPageDrawer
    {
        private readonly GenerationSelectionState<string> _selectionState = new();
        private IReadOnlyList<AIRequestTraceEntry> _cachedEntries = System.Array.Empty<AIRequestTraceEntry>();
        private long _cachedRevision = long.MinValue;
        private string? _selectedRequestId;
        private Vector2 _scrollPosition;
        private Vector2 _detailScrollPosition;
        private const float RowHeight = 48f;
        private const float RowContentHeight = 44f;
        private const int RowPreviewChars = 120;

        private sealed record DetailSection(string Title, string Body);

        public void Draw(Rect rect, RimMindLayoutScope? scope = null)
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            if (_selectionState.Refresh(runtimeScope.Generation))
            {
                _selectedRequestId = null;
                _cachedRevision = long.MinValue;
                _cachedEntries = System.Array.Empty<AIRequestTraceEntry>();
            }

            var log = runtimeScope.GetOptional<IAIRequestTraceLog>();
            if (log == null)
            {
                Widgets.Label(rect, "RimMind.UI.AIRequestsPage.TraceUnavailable".Translate());
                return;
            }

            if (_cachedRevision != log.Revision)
            {
                _cachedEntries = log.Entries;
                _cachedRevision = log.Revision;
            }
            IReadOnlyList<AIRequestTraceEntry> entries = _cachedEntries;

            if (entries.Count == 0)
            {
                SetSelection(null);
                Widgets.Label(rect, "RimMind.UI.AIRequestsPage.Empty".Translate());
                return;
            }

            SplitPageLayoutResult split = SplitPageLayout.Calculate(rect, 0.4f, 240f, 300f, 320f);
            TablePageLayoutResult table = TablePageLayout.Calculate(split.List, entries.Count, 2);
            scope?.Record(table.Toolbar, "AIRequests:ListToolbar");
            scope?.Record(table.Header, "AIRequests:ListHeader");
            scope?.Record(table.Body, "AIRequests:ListBody");
            scope?.Record(table.BottomBar, "AIRequests:ListBottom");
            scope?.Record(split.Detail, "AIRequests:Detail");

            AIRequestTraceEntry selected = ResolveSelectedEntry(entries);
            DrawList(table.Body, entries);
            DrawDetail(split.Detail, selected);
        }

        private void DrawList(Rect rect, IReadOnlyList<AIRequestTraceEntry> entries)
        {
            float contentHeight = entries.Count * RowHeight;
            Rect viewRect = new(rect.x, rect.y, rect.width - (contentHeight > rect.height ? 16f : 0f), contentHeight);

            Widgets.BeginScrollView(rect, ref _scrollPosition, viewRect);
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                Rect row = new(viewRect.x, viewRect.y + i * RowHeight, viewRect.width, RowContentHeight);

                if (entry.RequestId == _selectedRequestId)
                    Widgets.DrawHighlight(row);

                Widgets.DrawBoxSolid(new Rect(row.x, row.y, 4f, row.height), ColorFor(entry.State));
                if (Widgets.ButtonInvisible(row))
                    SetSelection(entry.RequestId);
                Widgets.Label(new Rect(row.x + 8f, row.y + 2f, row.width - 12f, 22f), entry.Source);
                Widgets.Label(new Rect(row.x + 8f, row.y + 22f, row.width - 12f, 22f), TruncateForRow(entry.UserPrompt));
                if (entry.State == AIRequestTraceState.Failed && !string.IsNullOrWhiteSpace(entry.Error))
                    TooltipHandler.TipRegion(row, entry.Error);
            }
            Widgets.EndScrollView();
        }

        private AIRequestTraceEntry ResolveSelectedEntry(IReadOnlyList<AIRequestTraceEntry> entries)
        {
            AIRequestTraceEntry? selected = entries.FirstOrDefault(e => e.RequestId == _selectedRequestId);
            if (selected != null)
                return selected;

            selected = entries[0];
            SetSelection(selected.RequestId);
            return selected;
        }

        private void SetSelection(string? requestId)
        {
            _selectedRequestId = requestId;
            _selectionState.Select(requestId);
        }

        private void DrawDetail(Rect rect, AIRequestTraceEntry entry)
        {
            var sections = BuildDetailSections(entry);
            float contentWidth = CalculateDetailContentWidth(rect.width);
            float viewHeight = Mathf.Max(rect.height + 1f, CalculateDetailViewHeight(sections, contentWidth));
            Rect view = new(rect.x, rect.y, contentWidth, viewHeight);
            Widgets.BeginScrollView(rect, ref _detailScrollPosition, view);

            float y = view.y;
            foreach (var section in sections)
                y = DrawSection(view, y, section.Title, section.Body);

            Widgets.EndScrollView();
        }

        private static List<DetailSection> BuildDetailSections(AIRequestTraceEntry entry)
        {
            return new List<DetailSection>
            {
                new DetailSection("RimMind.UI.AIRequestsPage.Detail.Meta".Translate(),
                    $"{entry.RequestId}\n{StateLabelFor(entry.State)}\n{entry.Source}\n{entry.Model}\n{entry.ElapsedMs} ms\n{entry.TokensUsed} tokens"),
                new DetailSection("RimMind.UI.AIRequestsPage.Detail.System".Translate(), entry.SystemPrompt),
                new DetailSection("RimMind.UI.AIRequestsPage.Detail.User".Translate(), entry.UserPrompt),
                new DetailSection("RimMind.UI.AIRequestsPage.Detail.Assistant".Translate(), entry.AssistantPrompt),
                new DetailSection("RimMind.UI.AIRequestsPage.Detail.Response".Translate(), entry.Response),
                new DetailSection("RimMind.UI.AIRequestsPage.Detail.Error".Translate(), entry.Error ?? string.Empty),
                new DetailSection("RimMind.UI.AIRequestsPage.Detail.ToolCalls".Translate(), FormatToolCalls(entry))
            };
        }

        private static float CalculateDetailContentWidth(float rectWidth)
            => Mathf.Max(1f, rectWidth - 16f);

        private static float CalculateDetailViewHeight(IReadOnlyList<DetailSection> sections, float width)
        {
            Text.Font = GameFont.Small;
            float y = 0f;
            foreach (var section in sections)
            {
                y += 24f;
                y += CalculateSectionHeight(section.Body, width);
                y += 12f;
            }

            return Mathf.Max(1f, y + 16f);
        }

        private static string ResolveSectionBody(string body)
            => string.IsNullOrWhiteSpace(body)
                ? "RimMind.UI.AIRequestsPage.Detail.EmptySection".Translate()
                : body;

        private static float CalculateSectionHeight(string body, float width)
            => Mathf.Max(32f, Text.CalcHeight(ResolveSectionBody(body), width));

        private static float DrawSection(Rect view, float y, string title, string body)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(view.x, y, view.width, 24f), title);
            y += 24f;

            string text = ResolveSectionBody(body);
            float height = CalculateSectionHeight(body, view.width);
            Widgets.Label(new Rect(view.x, y, view.width, height), text);
            return y + height + 12f;
        }

        private static string FormatToolCalls(AIRequestTraceEntry entry)
        {
            if (entry.ToolCalls.Count == 0)
                return string.Empty;

            return string.Join("\n", entry.ToolCalls.Select(t =>
                $"{t.ToolName} [{(t.Succeeded ? "ok" : "error")}] {t.Error ?? string.Empty}"));
        }

        private static string TruncateForRow(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string oneLine = value.Replace("\r", " ").Replace("\n", " ");
            return oneLine.Length <= RowPreviewChars
                ? oneLine
                : oneLine.Substring(0, RowPreviewChars) + "...";
        }

        private static string StateLabelFor(AIRequestTraceState state)
            => state switch
            {
                AIRequestTraceState.Running => "RimMind.UI.AIRequestsPage.State.Running".Translate(),
                AIRequestTraceState.Completed => "RimMind.UI.AIRequestsPage.State.Completed".Translate(),
                AIRequestTraceState.Failed => "RimMind.UI.AIRequestsPage.State.Failed".Translate(),
                _ => state.ToString()
            };

        private static Color ColorFor(AIRequestTraceState state)
            => state switch
            {
                AIRequestTraceState.Running => new Color(1f, 0.65f, 0.2f),
                AIRequestTraceState.Completed => new Color(0.4f, 1f, 0.4f),
                AIRequestTraceState.Failed => new Color(1f, 0.25f, 0.25f),
                _ => Color.white
            };
    }
}
