using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Debug;
using RimMind.Application.Common.Models.Tools;
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
    public sealed class CapabilitiesDebugCenterPageDrawer : IRuntimeBoundDebugCenterPageDrawer
    {
        private const string SubTabToolCalls = "tool_calls";
        private const string SubTabMechanisms = "mechanisms";
        private const string SubTabContextKeys = "context_keys";

        private readonly RimMindTabbedPageHostDrawer _tabDrawer = new();
        private readonly RimMindTableDrawer _tableDrawer = new();
        private readonly ToolRegistryDebugTableModelBuilder _toolRegistryBuilder = new();
        private readonly MechanismsDebugCenterPageDrawer _mechanismsDrawer = new(new MechanismsDebugTableModelBuilder());
        private readonly ContextKeysDebugCenterPageDrawer _contextKeysDrawer = new(new ContextKeysDebugTableModelBuilder());

        private IToolRegistry? _toolRegistry;
        private string _activeSubTab = SubTabToolCalls;
        private string _selectedCategory = "All";
        private string _searchQuery = string.Empty;
        private string? _selectedToolId;
        private Vector2 _toolTableScroll;
        private Vector2 _toolDetailScroll;

        internal void ScrollToBottom()
        {
            _toolTableScroll = new Vector2(0, 100000f);
            _toolDetailScroll = new Vector2(0, 100000f);
            _mechanismsDrawer.ScrollToBottom();
            _contextKeysDrawer.ScrollToBottom();
        }

        public IDisposable? Bind(RuntimeServiceScope scope)
        {
            _toolRegistry = scope.GetOptional<IToolRegistry>();
            _toolRegistryBuilder.Bind(_toolRegistry, scope.Generation);

            IDisposable? mechLease = _mechanismsDrawer.Bind(scope);
            IDisposable? ctxLease = _contextKeysDrawer.Bind(scope);
            if (mechLease == null && ctxLease == null)
                return null;

            return new CompositeDisposable(mechLease, ctxLease);
        }

        public void Draw(Rect rect, DebugCenterPageContext context, RimMindLayoutScope scope)
        {
            ApplyRequestedSubTab(context.Navigation.ConsumeRequestedSubTabId());

            var tabs = BuildTabs();
            TabbedPageLayoutResult layout = TabbedPageLayout.Calculate(rect, tabs);
            _activeSubTab = _tabDrawer.DrawTabs(layout, tabs, _activeSubTab, scope);

            switch (_activeSubTab)
            {
                case SubTabMechanisms:
                    _mechanismsDrawer.Draw(layout.Content, context, scope);
                    break;
                case SubTabContextKeys:
                    _contextKeysDrawer.Draw(layout.Content, context, scope);
                    break;
                default:
                    DrawToolCatalog(layout.Content, scope);
                    break;
            }
        }

        private void ApplyRequestedSubTab(string? requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return;

            string normalized = requested!.Trim().ToLowerInvariant();
            if (normalized is SubTabToolCalls or SubTabMechanisms or SubTabContextKeys)
                _activeSubTab = normalized;
        }

        private List<TabbedPageTabModel> BuildTabs()
        {
            return new List<TabbedPageTabModel>
            {
                new TabbedPageTabModel(
                    SubTabToolCalls,
                    SafeTranslate("RimMind.UI.Hub.Tab.ToolCalls", "ToolCalls"),
                    "RimMind.UI.Hub.Tab.ToolCalls",
                    _activeSubTab == SubTabToolCalls,
                    enabled: true,
                    tooltipKey: "RimMind.UI.Capabilities.Tip.ToolCalls"),
                new TabbedPageTabModel(
                    SubTabMechanisms,
                    SafeTranslate("RimMind.UI.Hub.Tab.Mechanisms", "Mechanisms"),
                    "RimMind.UI.Hub.Tab.Mechanisms",
                    _activeSubTab == SubTabMechanisms,
                    enabled: true,
                    tooltipKey: "RimMind.UI.Capabilities.Tip.Mechanisms"),
                new TabbedPageTabModel(
                    SubTabContextKeys,
                    SafeTranslate("RimMind.UI.Hub.Tab.ContextKeys", "Context Keys"),
                    "RimMind.UI.Hub.Tab.ContextKeys",
                    _activeSubTab == SubTabContextKeys,
                    enabled: true,
                    tooltipKey: "RimMind.UI.Capabilities.Tip.ContextKeys")
            };
        }

        private void DrawToolCatalog(Rect rect, RimMindLayoutScope scope)
        {
            IToolRegistry? registry = _toolRegistry ?? TryResolveGlobalTools();
            IReadOnlyList<ToolDefinition> allDefs = registry?.GetAllDefinitions() ?? Array.Empty<ToolDefinition>();

            const float filterBarHeight = 28f;
            Rect filterRect = new Rect(rect.x, rect.y, rect.width, filterBarHeight);
            DrawToolFilterBar(filterRect, allDefs, scope);

            Rect bodyRect = new Rect(
                rect.x,
                filterRect.yMax + RimMindUiMetrics.SectionGap,
                rect.width,
                Mathf.Max(1f, rect.height - filterBarHeight - RimMindUiMetrics.SectionGap));

            IReadOnlyList<ToolDefinition> filtered = ToolRegistryDebugTableModelBuilder.FilterDefinitions(
                allDefs,
                _selectedCategory,
                _searchQuery);
            DebugTableModel model = ToolRegistryDebugTableModelBuilder.Build(
                registry,
                _selectedCategory,
                _searchQuery);

            if (filtered.Count == 0)
            {
                _tableDrawer.Draw(bodyRect, model, ref _toolTableScroll, scope);
                return;
            }

            SplitPageLayoutResult split = SplitPageLayout.Calculate(bodyRect, 0.44f, 240f, 360f, 300f);
            _selectedToolId = _tableDrawer.DrawSelectableCompact(
                split.List,
                model,
                _selectedToolId,
                ref _toolTableScroll,
                scope);

            ToolDefinition selectedDef = filtered.FirstOrDefault(d => string.Equals(d.Id, _selectedToolId, StringComparison.OrdinalIgnoreCase))
                ?? filtered[0];
            _selectedToolId = selectedDef.Id;

            scope.Record(split.Detail, "Capabilities:ToolDetail");
            DrawToolDetail(split.Detail, selectedDef);
        }

        private void DrawToolFilterBar(Rect rect, IReadOnlyList<ToolDefinition> allDefs, RimMindLayoutScope scope)
        {
            scope.Record(rect, "Capabilities:FilterBar");
            float x = rect.x;
            const float gap = 6f;

            float catWidth = Mathf.Min(150f, rect.width * 0.28f);
            Rect catRect = new Rect(x, rect.y, catWidth, rect.height);
            string catLabel = _selectedCategory == "All"
                ? SafeTranslate("RimMind.UI.Capabilities.CategoryAll", "Category: All")
                : $"{SafeTranslate("RimMind.UI.Capabilities.CategoryPrefix", "Category")}: {_selectedCategory}";
            if (Widgets.ButtonText(catRect, catLabel))
            {
                var categories = new List<string> { "All" };
                categories.AddRange(allDefs
                    .Select(d => d.Category)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase));

                var options = categories.Select(cat => new FloatMenuOption(
                    cat == "All" ? SafeTranslate("RimMind.UI.Traces.Filter.All", "All") : cat,
                    () => _selectedCategory = cat)).ToList();
                Find.WindowStack.Add(new FloatMenu(options));
            }
            TooltipHandler.TipRegion(catRect, SafeTranslate("RimMind.UI.Capabilities.FilterCategoryTip", "Filter registered AI tools by category."));
            x = catRect.xMax + gap;

            const float resetW = 64f;
            float countW = 72f;
            float searchW = Mathf.Max(80f, rect.xMax - x - resetW - countW - gap * 2f);
            Rect searchRect = new Rect(x, rect.y, searchW, rect.height);
            _searchQuery = Widgets.TextField(searchRect, _searchQuery ?? string.Empty);
            TooltipHandler.TipRegion(searchRect, SafeTranslate("RimMind.UI.Capabilities.SearchTip", "Search tool ID, display name, mod, domain, or schema keywords."));
            x = searchRect.xMax + gap;

            Rect resetRect = new Rect(x, rect.y, resetW, rect.height);
            if (Widgets.ButtonText(resetRect, SafeTranslate("RimMind.UI.Traces.Filter.Reset", "Reset")))
            {
                _selectedCategory = "All";
                _searchQuery = string.Empty;
            }
            x = resetRect.xMax + gap;

            Rect countRect = new Rect(x, rect.y, Mathf.Max(20f, rect.xMax - x), rect.height);
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = RimMindUI.ColorMuted;
            Widgets.Label(countRect, $"{allDefs.Count}");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawToolDetail(Rect rect, ToolDefinition def)
        {
            Widgets.DrawBoxSolid(rect, RimMindUI.ColorCardBg);
            Rect inner = rect.ContractedBy(RimMindUiMetrics.Padding);
            ToolManifest? manifest = def.Manifest;

            string title = manifest != null && !string.IsNullOrWhiteSpace(manifest.DisplayName)
                ? $"{manifest.DisplayName} ({def.Id})"
                : def.Id;

            var blocks = new List<(string Header, string Body, bool IsTitle)>
            {
                (title, string.Empty, true),
                (SafeTranslate("RimMind.UI.Capabilities.MetaHeader", "Manifest & Policy"),
                    $"ID: {def.Id}\n" +
                    $"Category: {def.Category ?? "-"}\n" +
                    $"OwnerMod: {manifest?.OwnerModId ?? "-"}\n" +
                    $"Kind: {manifest?.Kind.ToString() ?? "-"}\n" +
                    $"DefaultDomain: {manifest?.DefaultDomain?.ToString() ?? "-"}\n" +
                    $"Risk: {manifest?.RiskLevel.ToString() ?? "-"}\n" +
                    $"RequiresApproval: {manifest?.RequiresApproval ?? false}",
                    false),
                (SafeTranslate("RimMind.UI.Capabilities.DescriptionHeader", "Description"),
                    string.IsNullOrWhiteSpace(def.Description) ? "-" : def.Description,
                    false),
                (SafeTranslate("RimMind.UI.Capabilities.SchemaHeader", "Parameters Schema (JSON)"),
                    TraceContentFormatter.FormatStructuredText(def.ParametersSchema, maxChars: 12000),
                    false)
            };

            float contentWidth = Mathf.Max(1f, inner.width - RimMindUiMetrics.ScrollBarWidth);
            Text.Font = GameFont.Small;
            float totalHeight = 8f;
            foreach (var b in blocks)
            {
                totalHeight += b.IsTitle ? 28f : 22f;
                if (!string.IsNullOrEmpty(b.Body))
                    totalHeight += Mathf.Max(24f, Text.CalcHeight(b.Body, contentWidth)) + 10f;
            }

            Rect view = new Rect(0f, 0f, contentWidth, Mathf.Max(inner.height + 1f, totalHeight));
            Widgets.BeginScrollView(inner, ref _toolDetailScroll, view);

            float y = 0f;
            foreach (var b in blocks)
            {
                if (b.IsTitle)
                {
                    Text.Font = GameFont.Medium;
                    GUI.color = RimMindUI.ColorHeader;
                    Widgets.LabelEllipses(new Rect(0f, y, contentWidth, 26f), b.Header);
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    y += 28f;
                    continue;
                }

                GUI.color = RimMindUI.ColorHeader;
                Widgets.Label(new Rect(0f, y, contentWidth, 20f), b.Header);
                GUI.color = Color.white;
                y += 22f;

                float bodyH = Mathf.Max(24f, Text.CalcHeight(b.Body, contentWidth));
                Widgets.Label(new Rect(0f, y, contentWidth, bodyH), b.Body);
                y += bodyH + 10f;
            }

            Widgets.EndScrollView();
        }

        private static IToolRegistry? TryResolveGlobalTools()
        {
            try
            {
                return RimMindAPI.Tools;
            }
            catch
            {
                return null;
            }
        }

        private static string SafeTranslate(string key, string fallback)
            => key.CanTranslate() ? key.Translate().ToString() : fallback;

        private sealed class CompositeDisposable : IDisposable
        {
            private readonly IDisposable? _a;
            private readonly IDisposable? _b;

            public CompositeDisposable(IDisposable? a, IDisposable? b)
            {
                _a = a;
                _b = b;
            }

            public void Dispose()
            {
                _a?.Dispose();
                _b?.Dispose();
            }
        }
    }
}
