using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Models.Debug;
using RimMind.Infrastructure.UI.DebugTables;
using RimMind.Infrastructure.UI.Framework;
using RimMind.Presentation.Api;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.DebugCenter.Pages
{
    public sealed class AIRequestsDebugCenterPageDrawer : IRuntimeBoundDebugCenterPageDrawer
    {
        private const string SubTabRollout = "rollout";
        private const string SubTabEphemeral = "ephemeral";

        private readonly RimMindTabbedPageHostDrawer _tabDrawer = new();
        private readonly RimMindTableDrawer _tableDrawer = new();

        private IAIRequestTraceLog? _log;
        private long _cachedRevision = long.MinValue;
        private long _cachedGeneration = long.MinValue;
        private IReadOnlyList<AIRequestTraceEntry> _cachedEntries = Array.Empty<AIRequestTraceEntry>();
        private IReadOnlyList<AgentRolloutEpisode> _cachedRollouts = Array.Empty<AgentRolloutEpisode>();
        private IReadOnlyList<AIRequestTraceEntry>? _displaySnapshot;

        private string _activeSubTab = SubTabRollout;
        private bool _subTabPinnedByUser;
        private string? _selectedRolloutId;
        private string? _selectedRequestId;

        private string _rolloutActorFilter = string.Empty;
        private AIRequestTraceState? _rolloutStateFilter;
        private string _rolloutSearchQuery = string.Empty;

        private string _ephemeralModFilter = string.Empty;
        private string _ephemeralScenarioFilter = string.Empty;
        private string _ephemeralActorFilter = string.Empty;
        private AIRequestTraceState? _ephemeralStateFilter;
        private string _ephemeralSearchQuery = string.Empty;
        private bool _includeRolloutTurnsInEphemeral;

        private readonly HashSet<string> _expandedLayers = new(StringComparer.OrdinalIgnoreCase) { "L4", "User" };
        private readonly HashSet<string> _expandedPromptSections = new(StringComparer.OrdinalIgnoreCase);

        private Vector2 _tableScrollPosition;
        private Vector2 _detailScrollPosition;

        // Local display-only data: never published to the live trace log or request queue.
        internal void UseDisplaySnapshot(IReadOnlyList<AIRequestTraceEntry> entries)
        {
            _displaySnapshot = entries;
            _subTabPinnedByUser = false;
        }

        internal void ScrollToBottom()
        {
            _tableScrollPosition = new Vector2(0, 100000f);
            _detailScrollPosition = new Vector2(0, 100000f);
        }

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
                _selectedRolloutId = null;
                _cachedGeneration = scope.Generation;
                _cachedRevision = long.MinValue;
                _cachedEntries = Array.Empty<AIRequestTraceEntry>();
                _cachedRollouts = Array.Empty<AgentRolloutEpisode>();
            }

            return null;
        }

        public void Draw(Rect rect, DebugCenterPageContext context, RimMindLayoutScope scope)
        {
            ApplyRequestedSubTab(context.Navigation.ConsumeRequestedSubTabId());

            if (_log == null && _displaySnapshot == null)
            {
                DrawEmptyTable(rect, SafeTranslate("RimMind.UI.AIRequestsPage.TraceUnavailable", "AI trace log unavailable."), scope);
                return;
            }

            RefreshSnapshot();
            if (!_subTabPinnedByUser && _displaySnapshot != null && _cachedRollouts.Count == 0 && _cachedEntries.Count > 0)
                _activeSubTab = SubTabEphemeral;

            var ephemeralBaseline = AgentRolloutModelBuilder.FilterEphemeral(
                _cachedEntries,
                new EphemeralTraceFilter(IncludeRolloutTurns: false),
                ResolveActorDisplayName);

            var tabs = BuildTabs(_cachedRollouts.Count, ephemeralBaseline.Count);
            TabbedPageLayoutResult tabLayout = TabbedPageLayout.Calculate(rect, tabs);
            string nextTab = _tabDrawer.DrawTabs(tabLayout, tabs, _activeSubTab, scope);
            if (!string.Equals(nextTab, _activeSubTab, StringComparison.Ordinal))
            {
                _activeSubTab = nextTab;
                _subTabPinnedByUser = true;
                _detailScrollPosition = Vector2.zero;
            }

            if (_activeSubTab == SubTabEphemeral)
                DrawEphemeralView(tabLayout.Content, scope);
            else
                DrawRolloutView(tabLayout.Content, scope);
        }

        private void ApplyRequestedSubTab(string? requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return;

            string normalized = requested!.Trim().ToLowerInvariant();
            if (normalized is SubTabRollout or SubTabEphemeral)
            {
                _activeSubTab = normalized;
                _subTabPinnedByUser = true;
            }
        }

        private List<TabbedPageTabModel> BuildTabs(int rolloutCount, int ephemeralCount)
        {
            string rolloutLabel = $"{SafeTranslate("RimMind.UI.Traces.SubTab.Rollout", "Agent Rollout")} ({rolloutCount})";
            string ephemeralLabel = $"{SafeTranslate("RimMind.UI.Traces.SubTab.Ephemeral", "One-Off Requests")} ({ephemeralCount})";
            return new List<TabbedPageTabModel>
            {
                new TabbedPageTabModel(
                    SubTabRollout,
                    rolloutLabel,
                    "RimMind.UI.Traces.SubTab.Rollout",
                    _activeSubTab == SubTabRollout,
                    enabled: true,
                    tooltipKey: "RimMind.UI.Traces.SubTab.RolloutTip"),
                new TabbedPageTabModel(
                    SubTabEphemeral,
                    ephemeralLabel,
                    "RimMind.UI.Traces.SubTab.Ephemeral",
                    _activeSubTab == SubTabEphemeral,
                    enabled: true,
                    tooltipKey: "RimMind.UI.Traces.SubTab.EphemeralTip")
            };
        }

        private void DrawRolloutView(Rect rect, RimMindLayoutScope scope)
        {
            const float filterHeight = 28f;
            Rect filterRect = new Rect(rect.x, rect.y, rect.width, filterHeight);
            DrawRolloutFilterBar(filterRect, scope);

            Rect bodyRect = new Rect(
                rect.x,
                filterRect.yMax + RimMindUiMetrics.SectionGap,
                rect.width,
                Mathf.Max(1f, rect.height - filterHeight - RimMindUiMetrics.SectionGap));

            IReadOnlyList<AgentRolloutEpisode> rollouts = AgentRolloutModelBuilder.BuildRollouts(
                _cachedEntries,
                _rolloutActorFilter,
                _rolloutStateFilter,
                _rolloutSearchQuery,
                ResolveActorDisplayName);

            if (rollouts.Count == 0)
            {
                RimMindUI.DrawEmptyState(
                    bodyRect,
                    SafeTranslate("RimMind.UI.Traces.Rollout.Empty", "No agent rollout trajectories match the current filter."));
                return;
            }

            SplitPageLayoutResult split = SplitPageLayout.Calculate(bodyRect, 0.37f, 230f, 320f, 340f);
            scope.Record(split.List, "Rollout:EpisodeList");
            scope.Record(split.Detail, "Rollout:TrajectoryDetail");

            AgentRolloutEpisode selected = rollouts.FirstOrDefault(e => e.RolloutId == _selectedRolloutId) ?? rollouts[0];
            _selectedRolloutId = selected.RolloutId;

            DrawRolloutList(split.List, rollouts);
            DrawRolloutDetail(split.Detail, selected);
        }

        private void DrawRolloutFilterBar(Rect rect, RimMindLayoutScope scope)
        {
            scope.Record(rect, "Rollout:FilterBar");
            float x = rect.x;
            const float gap = 6f;

            float actorW = Mathf.Min(150f, rect.width * 0.24f);
            Rect actorRect = new Rect(x, rect.y, actorW, rect.height);
            string actorLabel = string.IsNullOrWhiteSpace(_rolloutActorFilter)
                ? SafeTranslate("RimMind.UI.Traces.Filter.ActorAll", "Actor: All")
                : _rolloutActorFilter;
            if (Widgets.ButtonText(actorRect, actorLabel))
            {
                var actors = AgentRolloutModelBuilder.GetDistinctActors(_cachedEntries, ResolveActorDisplayName);
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption(SafeTranslate("RimMind.UI.Traces.Filter.All", "All"), () => _rolloutActorFilter = string.Empty)
                };
                opts.AddRange(actors.Select(a => new FloatMenuOption(a, () => _rolloutActorFilter = a)));
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            TooltipHandler.TipRegion(actorRect, SafeTranslate("RimMind.UI.Traces.Filter.ActorTip", "Filter trajectories by colonist or actor."));
            x = actorRect.xMax + gap;

            float stateW = Mathf.Min(120f, rect.width * 0.18f);
            Rect stateRect = new Rect(x, rect.y, stateW, rect.height);
            string stateLabel = _rolloutStateFilter.HasValue
                ? StateLabelFor(_rolloutStateFilter.Value)
                : SafeTranslate("RimMind.UI.Traces.Filter.StateAll", "State: All");
            if (Widgets.ButtonText(stateRect, stateLabel))
            {
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption(SafeTranslate("RimMind.UI.Traces.Filter.All", "All"), () => _rolloutStateFilter = null),
                    new FloatMenuOption(StateLabelFor(AIRequestTraceState.Running), () => _rolloutStateFilter = AIRequestTraceState.Running),
                    new FloatMenuOption(StateLabelFor(AIRequestTraceState.Completed), () => _rolloutStateFilter = AIRequestTraceState.Completed),
                    new FloatMenuOption(StateLabelFor(AIRequestTraceState.Failed), () => _rolloutStateFilter = AIRequestTraceState.Failed)
                };
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            x = stateRect.xMax + gap;

            const float resetW = 64f;
            float searchW = Mathf.Max(80f, rect.xMax - x - resetW - gap);
            Rect searchRect = new Rect(x, rect.y, searchW, rect.height);
            _rolloutSearchQuery = Widgets.TextField(searchRect, _rolloutSearchQuery ?? string.Empty);
            TooltipHandler.TipRegion(searchRect, SafeTranslate("RimMind.UI.Traces.Filter.SearchTip", "Search prompts, thoughts, tool names, JSON arguments, or outcomes."));
            x = searchRect.xMax + gap;

            Rect resetRect = new Rect(x, rect.y, Mathf.Max(40f, rect.xMax - x), rect.height);
            if (Widgets.ButtonText(resetRect, SafeTranslate("RimMind.UI.Traces.Filter.Reset", "Reset")))
            {
                _rolloutActorFilter = string.Empty;
                _rolloutStateFilter = null;
                _rolloutSearchQuery = string.Empty;
            }
        }

        private void DrawRolloutList(Rect rect, IReadOnlyList<AgentRolloutEpisode> rollouts)
        {
            Widgets.DrawBoxSolid(rect, RimMindUI.ColorSectionBg);
            Rect inner = rect.ContractedBy(4f);
            const float rowH = 66f;
            const float rowGap = 4f;
            float contentW = Mathf.Max(1f, inner.width - RimMindUiMetrics.ScrollBarWidth);
            float viewH = Mathf.Max(inner.height + 1f, rollouts.Count * (rowH + rowGap));
            Rect view = new Rect(0f, 0f, contentW, viewH);

            Widgets.BeginScrollView(inner, ref _tableScrollPosition, view);
            float y = 0f;
            for (int i = 0; i < rollouts.Count; i++)
            {
                AgentRolloutEpisode ep = rollouts[i];
                Rect card = new Rect(0f, y, contentW, rowH);
                bool selected = ep.RolloutId == _selectedRolloutId;
                Widgets.DrawBoxSolid(card, selected ? RimMindUI.ColorTabActive : RimMindUI.ColorCardBg);
                if (Widgets.ButtonInvisible(card))
                {
                    _selectedRolloutId = ep.RolloutId;
                    _detailScrollPosition = Vector2.zero;
                }

                Rect pad = card.ContractedBy(6f);
                Rect titleRect = new Rect(pad.x, pad.y, pad.width - 78f, 20f);
                Rect stateRect = new Rect(pad.xMax - 76f, pad.y, 76f, 20f);
                GUI.color = RimMindUI.ColorHeader;
                Widgets.LabelEllipses(titleRect, $"{ep.ActorLabel} ({ep.TurnCount}T)");
                GUI.color = ColorForState(ep.TerminalState);
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(stateRect, StateLabelFor(ep.TerminalState));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = RimMindUI.ColorMuted;

                string outcomeTag = ep.FinalOutcome != null
                    ? (ep.FinalOutcome.Succeeded ? $"OK:{ep.FinalOutcome.ActionIntent}" : $"ERR:{ep.FinalOutcome.ActionIntent}")
                    : ep.ScenarioId;
                string line2 = $"Tools:{ep.TotalToolCalls}{(ep.FailedToolCalls > 0 ? $" ({ep.FailedToolCalls}!)" : string.Empty)}  Tok:{ep.TotalTokens}  Reuse:{ep.PrefixReuseRatio:P0}  {ep.TotalElapsedMs}ms";
                Widgets.LabelEllipses(new Rect(pad.x, pad.y + 20f, pad.width, 18f), line2);
                Widgets.LabelEllipses(new Rect(pad.x, pad.y + 38f, pad.width, 18f), outcomeTag);
                GUI.color = Color.white;

                y += rowH + rowGap;
            }
            Widgets.EndScrollView();
        }

        private void DrawRolloutDetail(Rect rect, AgentRolloutEpisode episode)
        {
            Widgets.DrawBoxSolid(rect, RimMindUI.ColorCardBg);
            Rect inner = rect.ContractedBy(RimMindUiMetrics.Padding);
            float contentW = Mathf.Max(1f, inner.width - RimMindUiMetrics.ScrollBarWidth);

            var blocks = BuildRolloutDetailBlocks(episode);
            float totalH = MeasureBlocksHeight(blocks, contentW);
            Rect view = new Rect(0f, 0f, contentW, Mathf.Max(inner.height + 1f, totalH));
            Widgets.BeginScrollView(inner, ref _detailScrollPosition, view);
            DrawDetailBlocks(blocks, contentW);
            Widgets.EndScrollView();
        }

        private List<DetailBlock> BuildRolloutDetailBlocks(AgentRolloutEpisode episode)
        {
            var blocks = new List<DetailBlock>();

            string headerMeta =
                $"Rollout: {episode.RolloutId}\n" +
                $"Actor: {episode.ActorLabel} ({episode.ActorId}) | Mod: {episode.ModId} | Scenario: {episode.ScenarioId}\n" +
                $"Turns: {episode.TurnCount} | ToolCalls: {episode.TotalToolCalls} (Failed: {episode.FailedToolCalls}) | Elapsed: {episode.TotalElapsedMs} ms\n" +
                $"Tokens: total={episode.TotalTokens} (prompt={episode.PromptTokens}, completion={episode.CompletionTokens}, cached={episode.CachedTokens}, reuse={episode.PrefixReuseRatio:P1})";
            blocks.Add(new DetailBlock(
                $"rollout:{episode.RolloutId}:meta",
                SafeTranslate("RimMind.UI.Traces.Rollout.SummaryHeader", "Rollout Summary"),
                headerMeta,
                RimMindUI.ColorHeader,
                Collapsible: false,
                DefaultExpanded: true));

            if (episode.FinalOutcome != null)
            {
                var o = episode.FinalOutcome;
                string outcomeBody =
                    $"Status: {(o.Succeeded ? SafeTranslate("RimMind.UI.Traces.Outcome.Succeeded", "Succeeded") : SafeTranslate("RimMind.UI.Traces.Outcome.Failed", "Failed"))}\n" +
                    $"Action: {o.ActionIntent}\n" +
                    (string.IsNullOrWhiteSpace(o.ParamPreview) ? string.Empty : $"Params: {o.ParamPreview}\n") +
                    $"Detail: {o.Detail ?? "-"}";
                blocks.Add(new DetailBlock(
                    $"rollout:{episode.RolloutId}:outcome",
                    SafeTranslate("RimMind.UI.Traces.Outcome.Title", "In-Game Action Outcome"),
                    outcomeBody.TrimEnd(),
                    o.Succeeded ? RimMindUI.ColorActive : RimMindUI.ColorError,
                    Collapsible: false,
                    DefaultExpanded: true));
            }

            AIRequestTraceEntry lastTurn = episode.Turns[episode.Turns.Count - 1];
            if (lastTurn.Layers.Count > 0)
            {
                for (int i = 0; i < lastTurn.Layers.Count; i++)
                {
                    AIRequestLayerTrace layer = lastTurn.Layers[i];
                    string reuseTag = layer.IsPrefixReused ? " [Prefix Reused]" : string.Empty;
                    string title = $"[{layer.LayerTag}] {layer.Role} ({layer.CharCount} chars){reuseTag}";
                    string key = $"layer:{layer.LayerTag}:{i}";
                    blocks.Add(new DetailBlock(
                        key,
                        title,
                        TraceContentFormatter.FormatStructuredText(layer.Content, 12000),
                        layer.IsPrefixReused ? RimMindUI.ColorActive : RimMindUI.ColorHeader,
                        Collapsible: true,
                        DefaultExpanded: _expandedLayers.Contains(layer.LayerTag)));
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(lastTurn.SystemPrompt))
                {
                    blocks.Add(new DetailBlock(
                        "layer:L0",
                        "[L0] system",
                        TraceContentFormatter.FormatStructuredText(lastTurn.SystemPrompt, 12000),
                        RimMindUI.ColorHeader,
                        Collapsible: true,
                        DefaultExpanded: false));
                }
                if (!string.IsNullOrWhiteSpace(lastTurn.UserPrompt))
                {
                    blocks.Add(new DetailBlock(
                        "layer:L4",
                        "[L4] user",
                        TraceContentFormatter.FormatStructuredText(lastTurn.UserPrompt, 12000),
                        RimMindUI.ColorHeader,
                        Collapsible: true,
                        DefaultExpanded: true));
                }
            }

            for (int t = 0; t < episode.Turns.Count; t++)
            {
                AIRequestTraceEntry turn = episode.Turns[t];
                string turnTitle = $"Turn {t + 1}/{episode.Turns.Count} — {turn.Model} | {StateLabelFor(turn.State)} | {turn.ElapsedMs}ms | tok={turn.TokensUsed} (cached={turn.CachedTokens})";
                var sb = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(turn.UserPrompt))
                {
                    sb.AppendLine("[Input / Perception]");
                    sb.AppendLine(TraceContentFormatter.FormatStructuredText(turn.UserPrompt, 3500));
                    sb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(turn.ReasoningContent))
                {
                    sb.AppendLine("[Reasoning]");
                    sb.AppendLine(TraceContentFormatter.FormatStructuredText(turn.ReasoningContent, 6000));
                    sb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(turn.Response))
                {
                    sb.AppendLine("[Response]");
                    sb.AppendLine(TraceContentFormatter.FormatStructuredText(turn.Response, 6000));
                    sb.AppendLine();
                }

                if (turn.ToolCalls.Count > 0)
                {
                    sb.AppendLine($"[ToolCalls ({turn.ToolCalls.Count})]");
                    for (int c = 0; c < turn.ToolCalls.Count; c++)
                    {
                        AIRequestToolCallTrace tc = turn.ToolCalls[c];
                        sb.AppendLine($"• {tc.ToolName} [{(tc.Succeeded ? "OK" : "ERR")}]");
                        if (!string.IsNullOrWhiteSpace(tc.ArgumentsJson))
                            sb.AppendLine("  Args: " + TraceContentFormatter.FormatToolArguments(tc.ArgumentsJson, 2500).Replace("\n", "\n  "));
                        if (!string.IsNullOrWhiteSpace(tc.ResultContent))
                            sb.AppendLine("  Result: " + TraceContentFormatter.FormatStructuredText(tc.ResultContent, 2500).Replace("\n", "\n  "));
                        if (!string.IsNullOrWhiteSpace(tc.Error))
                            sb.AppendLine("  Error: " + tc.Error);
                    }
                    sb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(turn.Error))
                    sb.AppendLine("[Error] " + turn.Error);

                if (turn.ActionOutcome != null)
                {
                    sb.AppendLine($"[Outcome] {(turn.ActionOutcome.Succeeded ? "OK" : "ERR")} {turn.ActionOutcome.ActionIntent}: {turn.ActionOutcome.Detail}");
                }

                blocks.Add(new DetailBlock(
                    $"turn:{episode.RolloutId}:{t}",
                    turnTitle,
                    sb.ToString().TrimEnd(),
                    ColorForState(turn.State),
                    Collapsible: true,
                    DefaultExpanded: true));
            }

            return blocks;
        }

        private void DrawEphemeralView(Rect rect, RimMindLayoutScope scope)
        {
            const float filterBarH = 58f;
            Rect filterRect = new Rect(rect.x, rect.y, rect.width, filterBarH);
            DrawEphemeralFilterBar(filterRect, scope);

            Rect bodyRect = new Rect(
                rect.x,
                filterRect.yMax + RimMindUiMetrics.SectionGap,
                rect.width,
                Mathf.Max(1f, rect.height - filterBarH - RimMindUiMetrics.SectionGap));

            var filter = new EphemeralTraceFilter(
                _ephemeralModFilter,
                _ephemeralScenarioFilter,
                _ephemeralActorFilter,
                _ephemeralStateFilter,
                _ephemeralSearchQuery,
                _includeRolloutTurnsInEphemeral);

            IReadOnlyList<AIRequestTraceEntry> filtered = AgentRolloutModelBuilder.FilterEphemeral(
                _cachedEntries,
                filter,
                ResolveActorDisplayName);
            DebugTableModel model = AIRequestsDebugTableModelBuilder.Build(filtered);

            if (filtered.Count == 0)
            {
                _tableDrawer.Draw(bodyRect, model, ref _tableScrollPosition, scope);
                return;
            }

            SplitPageLayoutResult split = SplitPageLayout.Calculate(bodyRect, 0.4f, 240f, 320f, 320f);
            _selectedRequestId = _tableDrawer.DrawSelectableCompact(
                split.List,
                model,
                _selectedRequestId,
                ref _tableScrollPosition,
                scope);

            AIRequestTraceEntry selected = filtered.FirstOrDefault(e => e.RequestId == _selectedRequestId) ?? filtered[0];
            _selectedRequestId = selected.RequestId;

            scope.Record(split.Detail, "AIRequests:Detail");
            DrawEphemeralDetail(split.Detail, selected);
        }

        private void DrawEphemeralFilterBar(Rect rect, RimMindLayoutScope scope)
        {
            scope.Record(rect, "Ephemeral:FilterBar");
            const float gap = 6f;
            const float rowH = 26f;
            Rect row1 = new Rect(rect.x, rect.y, rect.width, rowH);
            Rect row2 = new Rect(rect.x, rect.y + rowH + 6f, rect.width, rowH);

            float x = row1.x;
            float dropW = Mathf.Min(135f, (row1.width - 70f - gap * 4f) / 4f);

            Rect modRect = new Rect(x, row1.y, dropW, rowH);
            string modLabel = string.IsNullOrWhiteSpace(_ephemeralModFilter)
                ? SafeTranslate("RimMind.UI.Traces.Filter.ModAll", "Mod: All")
                : _ephemeralModFilter;
            if (Widgets.ButtonText(modRect, modLabel))
            {
                var mods = AgentRolloutModelBuilder.GetDistinctMods(_cachedEntries);
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption(SafeTranslate("RimMind.UI.Traces.Filter.All", "All"), () => _ephemeralModFilter = string.Empty)
                };
                opts.AddRange(mods.Select(m => new FloatMenuOption(m, () => _ephemeralModFilter = m)));
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            x = modRect.xMax + gap;

            Rect scenRect = new Rect(x, row1.y, dropW, rowH);
            string scenLabel = string.IsNullOrWhiteSpace(_ephemeralScenarioFilter)
                ? SafeTranslate("RimMind.UI.Traces.Filter.ScenarioAll", "Scenario: All")
                : _ephemeralScenarioFilter;
            if (Widgets.ButtonText(scenRect, scenLabel))
            {
                var scenarios = AgentRolloutModelBuilder.GetDistinctScenarios(_cachedEntries);
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption(SafeTranslate("RimMind.UI.Traces.Filter.All", "All"), () => _ephemeralScenarioFilter = string.Empty)
                };
                opts.AddRange(scenarios.Select(s => new FloatMenuOption(s, () => _ephemeralScenarioFilter = s)));
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            x = scenRect.xMax + gap;

            Rect actorRect = new Rect(x, row1.y, dropW, rowH);
            string actorLabel = string.IsNullOrWhiteSpace(_ephemeralActorFilter)
                ? SafeTranslate("RimMind.UI.Traces.Filter.ActorAll", "Actor: All")
                : _ephemeralActorFilter;
            if (Widgets.ButtonText(actorRect, actorLabel))
            {
                var actors = AgentRolloutModelBuilder.GetDistinctActors(_cachedEntries, ResolveActorDisplayName);
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption(SafeTranslate("RimMind.UI.Traces.Filter.All", "All"), () => _ephemeralActorFilter = string.Empty)
                };
                opts.AddRange(actors.Select(a => new FloatMenuOption(a, () => _ephemeralActorFilter = a)));
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            x = actorRect.xMax + gap;

            Rect stateRect = new Rect(x, row1.y, dropW, rowH);
            string stateLabel = _ephemeralStateFilter.HasValue
                ? StateLabelFor(_ephemeralStateFilter.Value)
                : SafeTranslate("RimMind.UI.Traces.Filter.StateAll", "State: All");
            if (Widgets.ButtonText(stateRect, stateLabel))
            {
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption(SafeTranslate("RimMind.UI.Traces.Filter.All", "All"), () => _ephemeralStateFilter = null),
                    new FloatMenuOption(StateLabelFor(AIRequestTraceState.Running), () => _ephemeralStateFilter = AIRequestTraceState.Running),
                    new FloatMenuOption(StateLabelFor(AIRequestTraceState.Completed), () => _ephemeralStateFilter = AIRequestTraceState.Completed),
                    new FloatMenuOption(StateLabelFor(AIRequestTraceState.Failed), () => _ephemeralStateFilter = AIRequestTraceState.Failed)
                };
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            x = stateRect.xMax + gap;

            Rect resetRect = new Rect(x, row1.y, Mathf.Max(50f, row1.xMax - x), rowH);
            if (Widgets.ButtonText(resetRect, SafeTranslate("RimMind.UI.Traces.Filter.Reset", "Reset")))
            {
                _ephemeralModFilter = string.Empty;
                _ephemeralScenarioFilter = string.Empty;
                _ephemeralActorFilter = string.Empty;
                _ephemeralStateFilter = null;
                _ephemeralSearchQuery = string.Empty;
                _includeRolloutTurnsInEphemeral = false;
            }

            const float toggleW = 190f;
            float searchW = Mathf.Max(100f, row2.width - toggleW - gap);
            Rect searchRect = new Rect(row2.x, row2.y, searchW, rowH);
            _ephemeralSearchQuery = Widgets.TextField(searchRect, _ephemeralSearchQuery ?? string.Empty);
            TooltipHandler.TipRegion(searchRect, SafeTranslate("RimMind.UI.Traces.Filter.SearchTip", "Search prompts, thoughts, tool names, JSON arguments, or outcomes."));

            Rect toggleRect = new Rect(searchRect.xMax + gap, row2.y, toggleW, rowH);
            Widgets.CheckboxLabeled(
                toggleRect,
                SafeTranslate("RimMind.UI.Traces.Filter.IncludeRollout", "Include Rollout Turns"),
                ref _includeRolloutTurnsInEphemeral);
            TooltipHandler.TipRegion(toggleRect, SafeTranslate("RimMind.UI.Traces.Filter.IncludeRolloutTip", "When unchecked, only non-agent one-off requests (dialogue, personality, storyteller, etc.) are listed."));
        }

        private void DrawEphemeralDetail(Rect rect, AIRequestTraceEntry entry)
        {
            Widgets.DrawBoxSolid(rect, RimMindUI.ColorCardBg);
            Rect inner = rect.ContractedBy(RimMindUiMetrics.Padding);
            float contentW = Mathf.Max(1f, inner.width - RimMindUiMetrics.ScrollBarWidth);

            var blocks = BuildEphemeralDetailBlocks(entry);
            float totalH = MeasureBlocksHeight(blocks, contentW);
            Rect view = new Rect(0f, 0f, contentW, Mathf.Max(inner.height + 1f, totalH));
            Widgets.BeginScrollView(inner, ref _detailScrollPosition, view);
            DrawDetailBlocks(blocks, contentW);
            Widgets.EndScrollView();
        }

        private List<DetailBlock> BuildEphemeralDetailBlocks(AIRequestTraceEntry entry)
        {
            string mod = AgentRolloutModelBuilder.ResolveModId(entry);
            string scen = AgentRolloutModelBuilder.ResolveScenarioId(entry);
            string actor = AgentRolloutModelBuilder.ResolveActorLabel(entry, ResolveActorDisplayName);

            var blocks = new List<DetailBlock>
            {
                new DetailBlock(
                    $"ephem:{entry.RequestId}:meta",
                    SafeTranslate("RimMind.UI.AIRequestsPage.Detail.Meta", "Metadata"),
                    $"RequestId: {entry.RequestId}\n" +
                    $"TraceId: {entry.TraceId}\n" +
                    $"State: {StateLabelFor(entry.State)} | Elapsed: {entry.ElapsedMs} ms\n" +
                    $"Mod: {(string.IsNullOrEmpty(mod) ? "-" : mod)} | Scenario: {(string.IsNullOrEmpty(scen) ? "-" : scen)} | Actor: {actor}\n" +
                    $"Model: {entry.Model}\n" +
                    $"Tokens: total={entry.TokensUsed} (prompt={entry.PromptTokens}, completion={entry.CompletionTokens}, cached={entry.CachedTokens})\n" +
                    $"Source: {entry.Source}",
                    ColorForState(entry.State),
                    Collapsible: false,
                    DefaultExpanded: true),
                new DetailBlock(
                    "ephem:system",
                    SafeTranslate("RimMind.UI.AIRequestsPage.Detail.System", "System Prompt"),
                    FormatOrEmpty(entry.SystemPrompt),
                    RimMindUI.ColorHeader,
                    Collapsible: true,
                    DefaultExpanded: false),
                new DetailBlock(
                    "ephem:user",
                    SafeTranslate("RimMind.UI.AIRequestsPage.Detail.User", "User Prompt"),
                    FormatOrEmpty(entry.UserPrompt),
                    RimMindUI.ColorHeader,
                    Collapsible: true,
                    DefaultExpanded: true)
            };

            if (!string.IsNullOrWhiteSpace(entry.ReasoningContent))
            {
                blocks.Add(new DetailBlock(
                    $"ephem:{entry.RequestId}:reasoning",
                    SafeTranslate("RimMind.UI.Traces.Detail.Reasoning", "Reasoning"),
                    TraceContentFormatter.FormatStructuredText(entry.ReasoningContent, 8000),
                    RimMindUI.ColorHeader,
                    Collapsible: true,
                    DefaultExpanded: true));
            }

            blocks.Add(new DetailBlock(
                $"ephem:{entry.RequestId}:response",
                SafeTranslate("RimMind.UI.AIRequestsPage.Detail.Response", "Response"),
                FormatOrEmpty(entry.Response),
                RimMindUI.ColorHeader,
                Collapsible: false,
                DefaultExpanded: true));

            if (entry.ToolCalls.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var tc in entry.ToolCalls)
                {
                    sb.AppendLine($"• {tc.ToolName} [{(tc.Succeeded ? "OK" : "ERR")}]");
                    if (!string.IsNullOrWhiteSpace(tc.ArgumentsJson))
                        sb.AppendLine("  Args: " + TraceContentFormatter.FormatToolArguments(tc.ArgumentsJson, 2500).Replace("\n", "\n  "));
                    if (!string.IsNullOrWhiteSpace(tc.ResultContent))
                        sb.AppendLine("  Result: " + TraceContentFormatter.FormatStructuredText(tc.ResultContent, 2500).Replace("\n", "\n  "));
                    if (!string.IsNullOrWhiteSpace(tc.Error))
                        sb.AppendLine("  Error: " + tc.Error);
                }

                blocks.Add(new DetailBlock(
                    $"ephem:{entry.RequestId}:tools",
                    SafeTranslate("RimMind.UI.AIRequestsPage.Detail.ToolCalls", "Tool Calls"),
                    sb.ToString().TrimEnd(),
                    RimMindUI.ColorHeader,
                    Collapsible: false,
                    DefaultExpanded: true));
            }

            if (entry.ActionOutcome != null)
            {
                var o = entry.ActionOutcome;
                blocks.Add(new DetailBlock(
                    $"ephem:{entry.RequestId}:outcome",
                    SafeTranslate("RimMind.UI.Traces.Outcome.Title", "In-Game Action Outcome"),
                    $"{(o.Succeeded ? "Succeeded" : "Failed")}: {o.ActionIntent}\n{o.ParamPreview}\n{o.Detail}".Trim(),
                    o.Succeeded ? RimMindUI.ColorActive : RimMindUI.ColorError,
                    Collapsible: false,
                    DefaultExpanded: true));
            }

            if (!string.IsNullOrWhiteSpace(entry.Error))
            {
                blocks.Add(new DetailBlock(
                    $"ephem:{entry.RequestId}:error",
                    SafeTranslate("RimMind.UI.AIRequestsPage.Detail.Error", "Error"),
                    entry.Error!,
                    RimMindUI.ColorError,
                    Collapsible: false,
                    DefaultExpanded: true));
            }

            return blocks;
        }

        private float MeasureBlocksHeight(IReadOnlyList<DetailBlock> blocks, float width)
        {
            Text.Font = GameFont.Small;
            float y = 0f;
            for (int i = 0; i < blocks.Count; i++)
            {
                DetailBlock b = blocks[i];
                y += 24f;
                bool expanded = IsExpanded(b);
                if (expanded)
                {
                    float bodyH = Mathf.Max(22f, Text.CalcHeight(b.Body, Mathf.Max(20f, width - 8f)));
                    y += bodyH + 10f;
                }
                y += 6f;
            }
            return Mathf.Max(1f, y + 12f);
        }

        private void DrawDetailBlocks(IReadOnlyList<DetailBlock> blocks, float width)
        {
            Text.Font = GameFont.Small;
            float y = 0f;
            for (int i = 0; i < blocks.Count; i++)
            {
                DetailBlock b = blocks[i];
                bool expanded = IsExpanded(b);
                Rect headerRect = new Rect(0f, y, width, 24f);
                Widgets.DrawBoxSolid(headerRect, RimMindUI.ColorSectionBg);

                string prefix = b.Collapsible ? (expanded ? "[-] " : "[+] ") : string.Empty;
                GUI.color = b.AccentColor;
                Widgets.LabelEllipses(new Rect(headerRect.x + 6f, headerRect.y + 2f, headerRect.width - 12f, 20f), prefix + b.Title);
                GUI.color = Color.white;

                if (b.Collapsible && Widgets.ButtonInvisible(headerRect))
                    ToggleExpanded(b);

                y += 24f;

                if (expanded)
                {
                    float bodyW = Mathf.Max(20f, width - 8f);
                    float bodyH = Mathf.Max(22f, Text.CalcHeight(b.Body, bodyW));
                    Rect bodyRect = new Rect(4f, y + 2f, bodyW, bodyH);
                    Widgets.Label(bodyRect, b.Body);
                    y += bodyH + 10f;
                }

                y += 6f;
            }
        }

        private bool IsExpanded(DetailBlock block)
        {
            if (!block.Collapsible)
                return true;

            if (_expandedPromptSections.TryGetValue(block.Key, out _))
                return true;

            if (block.Key.StartsWith("layer:", StringComparison.OrdinalIgnoreCase))
            {
                string tag = ExtractLayerTag(block.Key);
                return _expandedLayers.Contains(tag);
            }

            return block.DefaultExpanded && !_hiddenSections.Contains(block.Key);
        }

        private readonly HashSet<string> _hiddenSections = new(StringComparer.OrdinalIgnoreCase);

        private void ToggleExpanded(DetailBlock block)
        {
            if (block.Key.StartsWith("layer:", StringComparison.OrdinalIgnoreCase))
            {
                string tag = ExtractLayerTag(block.Key);
                if (!_expandedLayers.Add(tag))
                    _expandedLayers.Remove(tag);
                return;
            }

            if (block.DefaultExpanded)
            {
                if (!_hiddenSections.Add(block.Key))
                    _hiddenSections.Remove(block.Key);
            }
            else
            {
                if (!_expandedPromptSections.Add(block.Key))
                    _expandedPromptSections.Remove(block.Key);
            }
        }

        private static string ExtractLayerTag(string key)
        {
            // key format: layer:<Tag> or layer:<Tag>:<index>
            int first = key.IndexOf(':');
            if (first < 0) return key;
            int second = key.IndexOf(':', first + 1);
            return second > first
                ? key.Substring(first + 1, second - first - 1)
                : key.Substring(first + 1);
        }

        private void RefreshSnapshot()
        {
            if (_displaySnapshot != null)
            {
                if (!ReferenceEquals(_cachedEntries, _displaySnapshot))
                {
                    _cachedEntries = _displaySnapshot;
                    _cachedRollouts = AgentRolloutModelBuilder.BuildRollouts(_cachedEntries, actorNameResolver: ResolveActorDisplayName);
                }
                return;
            }

            if (_log == null)
                return;

            long revision = _log.Revision;
            if (revision == _cachedRevision)
                return;

            _cachedEntries = _log.Entries;
            _cachedRollouts = AgentRolloutModelBuilder.BuildRollouts(_cachedEntries, actorNameResolver: ResolveActorDisplayName);
            _cachedRevision = revision;
        }

        private void DrawEmptyTable(Rect rect, string title, RimMindLayoutScope scope)
        {
            DebugTableModel model = new DebugTableModel(title, Array.Empty<DebugTableRow>());
            _tableDrawer.Draw(rect, model, ref _tableScrollPosition, scope);
        }

        private string FormatOrEmpty(string? raw)
            => string.IsNullOrWhiteSpace(raw)
                ? SafeTranslate("RimMind.UI.AIRequestsPage.Detail.EmptySection", "(empty)")
                : TraceContentFormatter.FormatStructuredText(raw, 12000);

        private static string ResolveActorDisplayName(string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId))
                return string.Empty;

            try
            {
                Pawn? pawn = RimMindPawnLookup.FindPawnById(actorId);
                if (pawn != null && !string.IsNullOrWhiteSpace(pawn.LabelShortCap))
                    return pawn.LabelShortCap;
            }
            catch
            {
                // Ignore in test/non-main-thread environments
            }

            return actorId;
        }

        private static Color ColorForState(AIRequestTraceState state) => state switch
        {
            AIRequestTraceState.Running => RimMindUI.ColorPending,
            AIRequestTraceState.Completed => RimMindUI.ColorActive,
            AIRequestTraceState.Failed => RimMindUI.ColorError,
            _ => Color.white
        };

        private static string StateLabelFor(AIRequestTraceState state)
            => state switch
            {
                AIRequestTraceState.Running => SafeTranslate("RimMind.UI.AIRequestsPage.State.Running", "Running"),
                AIRequestTraceState.Completed => SafeTranslate("RimMind.UI.AIRequestsPage.State.Completed", "Completed"),
                AIRequestTraceState.Failed => SafeTranslate("RimMind.UI.AIRequestsPage.State.Failed", "Failed"),
                _ => state.ToString()
            };

        private static string SafeTranslate(string key, string fallback)
            => RimMindText.TrOr(key, fallback);

        private sealed record DetailBlock(
            string Key,
            string Title,
            string Body,
            Color AccentColor,
            bool Collapsible,
            bool DefaultExpanded);
    }
}
