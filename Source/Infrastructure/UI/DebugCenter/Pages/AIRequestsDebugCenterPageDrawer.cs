using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Debug;
using RimMind.Infrastructure.UI.DebugTables;
using RimMind.Infrastructure.UI.Framework;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using RimMind.Presentation.Runtime.Services;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.DebugCenter.Pages
{
    public sealed class AIRequestsDebugCenterPageDrawer : IRuntimeBoundDebugCenterPageDrawer
    {
        private readonly RimMindTableDrawer _tableDrawer = new();
        private IAIRequestTraceLog? _log;
        private string? _selectedRequestId;
        private long _cachedRevision = long.MinValue;
        private long _cachedGeneration = long.MinValue;
        private IReadOnlyList<AIRequestTraceEntry> _cachedEntries = System.Array.Empty<AIRequestTraceEntry>();
        private DebugTableModel? _cachedModel;
        private Vector2 _tableScrollPosition;
        private Vector2 _detailScrollPosition;
        private IReadOnlyList<AIRequestTraceEntry>? _displaySnapshot;

        // Local display-only data: never published to the live trace log or request queue.
        internal void UseDisplaySnapshot(IReadOnlyList<AIRequestTraceEntry> entries) => _displaySnapshot = entries;
        internal void ScrollToBottom()
        {
            _tableScrollPosition = new Vector2(0, 100000f);
            _detailScrollPosition = new Vector2(0, 100000f);
        }

        private sealed record DetailSection(string Title, string Body);

        public AIRequestsDebugCenterPageDrawer(IAIRequestTraceLog? log = null)
        {
            _log = log;
        }

        public IDisposable? Bind(RuntimeServiceScope scope)
        {
            IAIRequestTraceLog? log = scope.GetOptional<IAIRequestTraceLog>();
            if (_cachedGeneration != scope.Generation || !ReferenceEquals(_log, log))
            {
                _log = log;
                _selectedRequestId = null;
                _cachedGeneration = scope.Generation;
                _cachedRevision = long.MinValue;
                _cachedEntries = Array.Empty<AIRequestTraceEntry>();
                _cachedModel = null;
            }
            return null;
        }

        public void Draw(Rect rect, DebugCenterPageContext context, RimMindLayoutScope scope)
        {
            if (_log == null && _displaySnapshot == null)
            {
                DrawEmptyTable(rect, "RimMind.UI.AIRequestsPage.TraceUnavailable".Translate(), scope);
                return;
            }

            RefreshSnapshot();
            IReadOnlyList<AIRequestTraceEntry> entries = _cachedEntries;
            DebugTableModel model = _cachedModel!;
            if (entries.Count == 0)
            {
                _tableDrawer.Draw(rect, model, ref _tableScrollPosition, scope);
                return;
            }

            SplitPageLayoutResult split = SplitPageLayout.Calculate(rect, 0.4f, 240f, 300f, 320f);
            AIRequestTraceEntry selectedEntry = ResolveSelectedEntry(entries);
            _selectedRequestId = _tableDrawer.DrawSelectableCompact(
                split.List,
                model,
                _selectedRequestId,
                ref _tableScrollPosition,
                scope);
            selectedEntry = ResolveSelectedEntry(entries);

            scope.Record(split.Detail, "AIRequests:Detail");
            DrawDetail(split.Detail, selectedEntry);
        }

        private void RefreshSnapshot()
        {
            if (_displaySnapshot != null)
            {
                if (!ReferenceEquals(_cachedEntries, _displaySnapshot) || _cachedModel == null)
                {
                    _cachedEntries = _displaySnapshot;
                    _cachedModel = AIRequestsDebugTableModelBuilder.Build(_cachedEntries);
                }
                return;
            }
            if (_log == null)
                return;

            long revision = _log.Revision;
            if (_cachedModel != null && revision == _cachedRevision)
                return;

            _cachedEntries = _log.Entries;
            _cachedModel = AIRequestsDebugTableModelBuilder.Build(_cachedEntries);
            _cachedRevision = revision;
        }

        private void DrawEmptyTable(Rect rect, string title, RimMindLayoutScope scope)
        {
            DebugTableModel model = new DebugTableModel(title, System.Array.Empty<DebugTableRow>());
            _tableDrawer.Draw(rect, model, ref _tableScrollPosition, scope);
        }

        private AIRequestTraceEntry ResolveSelectedEntry(IReadOnlyList<AIRequestTraceEntry> entries)
        {
            AIRequestTraceEntry? selectedEntry = entries.FirstOrDefault(e => e.RequestId == _selectedRequestId);
            if (selectedEntry != null)
                return selectedEntry;

            selectedEntry = entries[0];
            _selectedRequestId = selectedEntry.RequestId;
            return selectedEntry;
        }

        private void DrawDetail(Rect rect, AIRequestTraceEntry entry)
        {
            var sections = BuildDetailSections(entry);
            float contentWidth = Mathf.Max(1f, rect.width - RimMindUiMetrics.ScrollBarWidth);
            float viewHeight = Mathf.Max(rect.height + 1f, CalculateDetailViewHeight(sections, contentWidth));
            Rect view = new Rect(rect.x, rect.y, contentWidth, viewHeight);
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

        private static float CalculateDetailViewHeight(IReadOnlyList<DetailSection> sections, float width)
        {
            Text.Font = GameFont.Small;
            float y = 0f;
            foreach (var section in sections)
            {
                y += 24f;
                y += Mathf.Max(32f, Text.CalcHeight(ResolveSectionBody(section.Body), width));
                y += 12f;
            }

            return Mathf.Max(1f, y + 16f);
        }

        private static float DrawSection(Rect view, float y, string title, string body)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(view.x, y, view.width, 24f), title);
            y += 24f;

            string text = ResolveSectionBody(body);
            float height = Mathf.Max(32f, Text.CalcHeight(text, view.width));
            Widgets.Label(new Rect(view.x, y, view.width, height), text);
            return y + height + 12f;
        }

        private static string ResolveSectionBody(string body)
            => string.IsNullOrWhiteSpace(body)
                ? "RimMind.UI.AIRequestsPage.Detail.EmptySection".Translate()
                : body;

        private static string FormatToolCalls(AIRequestTraceEntry entry)
        {
            if (entry.ToolCalls.Count == 0)
                return string.Empty;

            return string.Join("\n", entry.ToolCalls.Select(t =>
                $"{t.ToolName} [{(t.Succeeded ? "ok" : "error")}] {t.Error ?? string.Empty}"));
        }

        private static string StateLabelFor(AIRequestTraceState state)
            => state switch
            {
                AIRequestTraceState.Running => "RimMind.UI.AIRequestsPage.State.Running".Translate(),
                AIRequestTraceState.Completed => "RimMind.UI.AIRequestsPage.State.Completed".Translate(),
                AIRequestTraceState.Failed => "RimMind.UI.AIRequestsPage.State.Failed".Translate(),
                _ => state.ToString()
            };
    }
}
