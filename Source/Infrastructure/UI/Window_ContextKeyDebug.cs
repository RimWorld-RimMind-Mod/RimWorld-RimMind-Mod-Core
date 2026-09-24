using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Extensions;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Domain.ValueObjects;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using RimMind.Presentation.Api;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public class Window_ContextKeyDebug : RimMindWindowBase
    {
        private Vector2 _scrollPos = Vector2.zero;
        private const float Padding = 6f;
        private const float LineH = 22f;
        private const float BtnHeight = 24f;

        private string _duplicateResult = "";
        private string _selectedKeyDetail = "";
        private long _selectionGeneration = long.MinValue;

        private ContextLayer? _layerFilter;
        private string? _ownerFilter;

        private static readonly ContextLayer[] AllLayers =
            System.Enum.GetValues(typeof(ContextLayer)).Cast<ContextLayer>().ToArray();

        public override Vector2 InitialSize => new Vector2(720f, 560f);

        public Window_ContextKeyDebug()
        {
            forcePause = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            doCloseX = true;
        }

        protected override void DrawContents(Rect inRect, RimMindLayoutScope scope)
        {
            RuntimeServiceScope runtimeScope = RuntimeServiceHub.Shared.Capture();
            RefreshGeneration(runtimeScope.Generation);
            var registry = runtimeScope.GetOptional<IContextKeyRegistry>();

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            TablePageLayoutResult table = TablePageLayout.Calculate(inRect, rowCount: 24, columnCount: 4);
            Rect headerRect = table.Toolbar;
            Rect filterRect = table.Header;

            scope.Record(headerRect, "Header:Title");
            scope.Record(filterRect, "Filter:Bar");

            GUI.color = new Color(0.7f, 0.8f, 1f);
            Text.Font = GameFont.Medium;
            Widgets.Label(headerRect, "RimMind.UI.ContextKeyDebug.Title".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            DrawFilters(filterRect, registry, scope);

            Rect bodyRect = table.Body;
            scope.Record(bodyRect, "Body");

            if (registry == null)
            {
                DrawEmptyState(bodyRect);
                return;
            }

            var keys = registry.GetAll();
            if (keys.Count == 0)
            {
                DrawEmptyState(bodyRect);
                return;
            }

            var filtered = ApplyFilters(keys);
            if (filtered.Count == 0)
            {
                DrawFilterEmptyState(bodyRect);
                return;
            }

            float leftW = bodyRect.width * 0.4f;
            float rightW = bodyRect.width - leftW - Padding;

            Rect leftRect = new Rect(bodyRect.x, bodyRect.y, leftW, bodyRect.height - BtnHeight - Padding);
            Rect rightRect = new Rect(bodyRect.x + leftW + Padding, bodyRect.y, rightW, bodyRect.height - BtnHeight - Padding);
            Rect btnRect = new Rect(bodyRect.x, bodyRect.y + bodyRect.height - BtnHeight, bodyRect.width, BtnHeight);
            scope.Record(leftRect, "List:Keys");
            scope.Record(rightRect, "Detail:SelectedKey");
            scope.Record(btnRect, "Bottom:TestButton");

            DrawKeyList(leftRect, filtered, scope);
            DrawKeyDetail(rightRect, filtered, scope);
            DrawTestButton(btnRect, registry, scope);
        }

        private void RefreshGeneration(long generation)
        {
            if (_selectionGeneration == generation)
                return;

            _selectionGeneration = generation;
            _selectedKeyDetail = string.Empty;
            _duplicateResult = string.Empty;
            _ownerFilter = null;
        }

        private List<KeyMeta> ApplyFilters(IReadOnlyList<KeyMeta> keys)
        {
            var result = keys.AsEnumerable();
            if (_layerFilter.HasValue)
                result = result.Where(k => k.Layer == _layerFilter.Value);
            if (!_ownerFilter.NullOrEmpty())
                result = result.Where(k => k.OwnerMod == _ownerFilter);
            return result.ToList();
        }

        private void DrawFilters(
            Rect rect,
            IContextKeyRegistry? registry,
            RimMindLayoutScope scope)
        {
            float btnW = 140f;
            float gap = 8f;

            Rect layerBtnRect = new Rect(rect.x, rect.y, btnW, BtnHeight);
            scope.Record(layerBtnRect, "Button:LayerFilter");
            string layerLabel = _layerFilter.HasValue
                ? "RimMind.UI.ContextKeyDebug.Layer".Translate(_layerFilter.Value.ToString())
                : "RimMind.UI.ContextKeyDebug.FilterLayer".Translate();
            if (Widgets.ButtonText(layerBtnRect, layerLabel))
                CycleLayerFilter();

            Rect ownerBtnRect = new Rect(rect.x + btnW + gap, rect.y, btnW, BtnHeight);
            scope.Record(ownerBtnRect, "Button:OwnerFilter");
            string ownerLabel = !_ownerFilter.NullOrEmpty()
                ? "RimMind.UI.ContextKeyDebug.OwnerMod".Translate(_ownerFilter)
                : "RimMind.UI.ContextKeyDebug.FilterOwner".Translate();
            if (Widgets.ButtonText(ownerBtnRect, ownerLabel))
                CycleOwnerFilter(registry);
        }

        private void CycleLayerFilter()
        {
            if (_layerFilter == null)
            {
                _layerFilter = AllLayers[0];
                return;
            }

            int idx = System.Array.IndexOf(AllLayers, _layerFilter.Value);
            if (idx < 0 || idx >= AllLayers.Length - 1)
                _layerFilter = null;
            else
                _layerFilter = AllLayers[idx + 1];
        }

        private void CycleOwnerFilter(IContextKeyRegistry? registry)
        {
            if (registry == null) return;

            var owners = registry.GetAll()
                .Select(k => k.OwnerMod ?? "Unknown")
                .Distinct()
                .OrderBy(o => o)
                .ToList();

            if (owners.Count == 0) return;

            if (_ownerFilter == null)
            {
                _ownerFilter = owners[0];
                return;
            }

            int idx = owners.IndexOf(_ownerFilter);
            if (idx < 0 || idx >= owners.Count - 1)
                _ownerFilter = null;
            else
                _ownerFilter = owners[idx + 1];
        }

        private void DrawEmptyState(Rect rect)
        {
            float centerY = rect.y + rect.height / 2f;

            GUI.color = Color.grey;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.x, centerY - 30f, rect.width, LineH),
                "RimMind.UI.ContextKeyDebug.Empty".Translate());

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            string hint = "RimMind.UI.ContextKeyDebug.EmptyHint".Translate();
            float hintH = Text.CalcHeight(hint, rect.width - 24f);
            Widgets.Label(new Rect(rect.x + 12f, centerY, rect.width - 24f, hintH), hint);
            Text.Font = GameFont.Small;

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawFilterEmptyState(Rect rect)
        {
            GUI.color = Color.grey;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, "RimMind.UI.ContextKeyDebug.FilterEmpty".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawKeyList(Rect rect, IReadOnlyList<KeyMeta> keys, RimMindLayoutScope scope)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.12f, 0.5f));

            var grouped = keys.GroupBy(k => k.Layer).OrderBy(g => g.Key).ToList();

            float contentH = 0f;
            foreach (var group in grouped)
            {
                contentH += LineH;
                foreach (var _ in group)
                    contentH += LineH;
            }

            Rect viewRect = new Rect(rect.x, rect.y, rect.width - 16f, contentH);
            Widgets.BeginScrollView(rect, ref _scrollPos, viewRect);
            scope.Record(rect, "ScrollView:KeyListOuter");
            scope.Record(viewRect, "ScrollView:KeyListContent");

            float y = viewRect.y;
            foreach (var group in grouped)
            {
                GUI.color = new Color(0.6f, 0.75f, 0.9f);
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(viewRect.x + Padding, y, viewRect.width - Padding * 2, LineH),
                    "RimMind.UI.ContextKeyDebug.Layer".Translate(group.Key.ToString()));
                GUI.color = Color.white;
                y += LineH;

                foreach (var key in group)
                {
                    Rect entryRect = new Rect(viewRect.x, y, viewRect.width, LineH);
                    scope.Record(entryRect, $"KeyEntry:{key.Key}");
                    bool selected = _selectedKeyDetail == key.Key;
                    if (selected)
                        Widgets.DrawBoxSolid(entryRect, new Color(0.25f, 0.35f, 0.55f, 0.6f));

                    if (Widgets.ButtonInvisible(entryRect))
                        _selectedKeyDetail = key.Key;

                    bool hasProvider = key.ValueProvider != null || key.HasAsyncProvider();
                    GUI.color = selected ? Color.white : (hasProvider ? new Color(0.8f, 0.8f, 0.8f) : new Color(0.5f, 0.5f, 0.5f));
                    Widgets.Label(new Rect(entryRect.x + Padding * 2, entryRect.y + 2f,
                        entryRect.width - Padding * 3, LineH), key.Key);
                    GUI.color = Color.white;
                    y += LineH;
                }
            }

            Widgets.EndScrollView();
        }

        private void DrawKeyDetail(Rect rect, IReadOnlyList<KeyMeta> keys, RimMindLayoutScope scope)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.12f, 0.3f));

            if (_selectedKeyDetail.NullOrEmpty())
            {
                GUI.color = Color.grey;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "RimMind.UI.ContextKeyDebug.SelectKey".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            var selected = keys.FirstOrDefault(k => k.Key == _selectedKeyDetail);
            if (selected == null)
            {
                GUI.color = Color.grey;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "RimMind.UI.ContextKeyDebug.SelectKey".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            float x = rect.x + Padding;
            float y = rect.y + Padding;
            float labelW = rect.width - Padding * 2;

            GUI.color = new Color(0.85f, 0.9f, 1f);
            Widgets.Label(new Rect(x, y, labelW, LineH),
                "RimMind.UI.ContextKeyDebug.Key".Translate(selected.Key));
            GUI.color = Color.white;
            y += LineH + Padding;

            Widgets.Label(new Rect(x, y, labelW, LineH),
                "RimMind.UI.ContextKeyDebug.Layer".Translate(selected.Layer.ToString()));
            y += LineH + Padding;

            Widgets.Label(new Rect(x, y, labelW, LineH),
                "RimMind.UI.ContextKeyDebug.Priority".Translate(
                    selected.Priority.ToString("F3"),
                    selected.AdaptivePriority.ToString("F3")));
            y += LineH + Padding;

            Widgets.Label(new Rect(x, y, labelW, LineH),
                "RimMind.UI.ContextKeyDebug.OwnerMod".Translate(selected.OwnerMod ?? "Unknown"));
            y += LineH + Padding;

            bool hasProvider = selected.ValueProvider != null || selected.HasAsyncProvider();
            GUI.color = hasProvider ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.5f, 0.4f);
            Widgets.Label(new Rect(x, y, labelW, LineH),
                "RimMind.UI.ContextKeyDebug.Provider".Translate(
                    hasProvider
                        ? "RimMind.UI.ContextKeyDebug.HasProvider".Translate()
                        : "RimMind.UI.ContextKeyDebug.NoProvider".Translate()));
            GUI.color = Color.white;
            y += LineH + Padding;

            var relevanceTable = RimMindAPI.Context.RelevanceTable;
            if (relevanceTable != null)
            {
                string[] scenarios = {
                    RimMindAPI.Context.ScenarioDecision,
                    RimMindAPI.Context.ScenarioDialogue,
                    RimMindAPI.Context.ScenarioPersonality,
                    RimMindAPI.Context.ScenarioStoryteller,
                    RimMindAPI.Context.ScenarioMemory
                };
                string[] scenarioLabels = {
                    "Decision", "Dialogue", "Personality", "Storyteller", "Memory"
                };

                var relevanceParts = new List<string>();
                for (int i = 0; i < scenarios.Length; i++)
                {
                    float rel = relevanceTable.GetRelevance(scenarios[i], selected.Key);
                    if (rel > 0.01f)
                        relevanceParts.Add($"{scenarioLabels[i]}={rel:F2}");
                }

                string scenariosStr = relevanceParts.Count > 0
                    ? string.Join(", ", relevanceParts)
                    : "RimMind.UI.AgentStateDebug.NoData".Translate();
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                float scenariosH = Text.CalcHeight(
                    "RimMind.UI.ContextKeyDebug.Scenarios".Translate(scenariosStr), labelW);
                Widgets.Label(new Rect(x, y, labelW, scenariosH),
                    "RimMind.UI.ContextKeyDebug.Scenarios".Translate(scenariosStr));
                GUI.color = Color.white;
                y += scenariosH + Padding;
            }

            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(new Rect(x, y, labelW, LineH),
                "RimMind.UI.ContextKeyDebug.CacheScope".Translate(selected.CacheScope.ToString()));
            GUI.color = Color.white;
            y += LineH + Padding;

            if (!selected.OverrideSource.NullOrEmpty())
            {
                GUI.color = new Color(1f, 0.7f, 0.3f);
                Widgets.Label(new Rect(x, y, labelW, LineH),
                    "RimMind.UI.ContextKeyDebug.OverrideSource".Translate(selected.OverrideSource));
                GUI.color = Color.white;
                y += LineH + Padding;
            }

            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(new Rect(x, y, labelW, LineH),
                $"Effective priority: {selected.GetEffectivePriority():F3} | Score: {selected.CurrentScore:F3} | Updates: {selected.UpdateCount}");
            GUI.color = Color.white;
            y += LineH + Padding;

            if (selected.LastUpdatedTick > 0)
            {
                int elapsed = Find.TickManager.TicksGame - selected.LastUpdatedTick;
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(new Rect(x, y, labelW, LineH),
                    $"Last updated: {elapsed} ticks ago | Last included: tick {selected.LastIncludedTick}");
                GUI.color = Color.white;
            }
        }

        private void DrawTestButton(
            Rect rect,
            IContextKeyRegistry registry,
            RimMindLayoutScope scope)
        {
            float btnW = 160f;
            Rect testBtn = new Rect(rect.x, rect.y, btnW, BtnHeight);
            scope.Record(testBtn, "Button:TestDuplicates");
            if (Widgets.ButtonText(testBtn, "RimMind.UI.ContextKeyDebug.TestDuplicates".Translate()))
            {
                var keys = registry.GetAll();
                var keyCounts = new Dictionary<string, List<string>>();
                foreach (var key in keys)
                {
                    if (!keyCounts.ContainsKey(key.Key))
                        keyCounts[key.Key] = new List<string>();
                    keyCounts[key.Key].Add(key.OwnerMod ?? "Unknown");
                }

                var duplicates = keyCounts.Where(kvp => kvp.Value.Count > 1).ToList();
                if (duplicates.Count == 0)
                {
                    _duplicateResult = "RimMind.UI.ContextKeyDebug.NoDuplicates".Translate();
                }
                else
                {
                    var parts = new List<string>();
                    foreach (var dup in duplicates)
                    {
                        parts.Add("RimMind.UI.ContextKeyDebug.DuplicateWarning".Translate(
                            dup.Key, string.Join(", ", dup.Value)));
                    }
                    _duplicateResult = string.Join("\n", parts);
                }

                Log.Message($"[RimMind-Core] ContextKey duplicate test: {duplicates.Count} duplicates found");
            }

            if (!_duplicateResult.NullOrEmpty())
            {
                float resultW = rect.width - btnW - Padding * 2;
                Rect resultRect = new Rect(rect.x + btnW + Padding, rect.y, resultW, BtnHeight);
                bool hasDuplicates = !_duplicateResult.Contains("RimMind.UI.ContextKeyDebug.NoDuplicates".Translate());
                GUI.color = hasDuplicates ? new Color(1f, 0.5f, 0.4f) : new Color(0.4f, 1f, 0.4f);
                string displayText = _duplicateResult.Length > 80
                    ? _duplicateResult.Substring(0, 80) + "..."
                    : _duplicateResult;
                Widgets.Label(resultRect, displayText);
                GUI.color = Color.white;
            }
        }
    }
}
