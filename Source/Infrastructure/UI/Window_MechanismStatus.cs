using System.Collections.Generic;
using System.Linq;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Domain.Enums;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using RimMind.Presentation.Api;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public class Window_MechanismStatus : RimMindWindowBase
    {
        private Vector2 _scrollPos = Vector2.zero;
        private MechanismScope? _scopeFilter;
        private MechanismRisk? _riskFilter;
        private const float Padding = 6f;
        private const float LineH = 22f;
        private const float BtnHeight = 24f;

        private static readonly MechanismScope[] AllScopes =
            System.Enum.GetValues(typeof(MechanismScope)).Cast<MechanismScope>().ToArray();

        private static readonly MechanismRisk[] AllRisks =
            System.Enum.GetValues(typeof(MechanismRisk)).Cast<MechanismRisk>().ToArray();

        public override Vector2 InitialSize => new Vector2(640f, 520f);

        public Window_MechanismStatus()
        {
            forcePause = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            doCloseX = true;
        }

        protected override void DrawContents(Rect inRect, RimMindLayoutScope scope)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            TablePageLayoutResult table = TablePageLayout.Calculate(inRect, rowCount: 16, columnCount: 4);
            Rect headerRect = table.Toolbar;
            Rect filterRect = table.Header;
            Rect contentRect = table.Body;

            scope.Record(headerRect, "Header:Title");
            scope.Record(filterRect, "Filter:Bar");
            scope.Record(contentRect, "Content:List");
            scope.Record(table.BottomBar, "Bottom:TableBar");

            GUI.color = new Color(0.7f, 0.8f, 1f);
            Text.Font = GameFont.Medium;
            Widgets.Label(headerRect, "RimMind.UI.MechanismStatus.Title".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            DrawFilters(filterRect, scope);
            DrawContent(contentRect, scope);
        }

        private void DrawFilters(Rect rect, RimMindLayoutScope scope)
        {
            float btnW = 140f;
            float gap = 8f;

            Rect scopeBtnRect = new Rect(rect.x, rect.y, btnW, BtnHeight);
            scope.Record(scopeBtnRect, "Button:ScopeFilter");
            string scopeLabel = _scopeFilter.HasValue
                ? "RimMind.UI.MechanismStatus.Scope".Translate(_scopeFilter.Value.ToString())
                : "RimMind.UI.MechanismStatus.FilterScope".Translate();
            if (Widgets.ButtonText(scopeBtnRect, scopeLabel))
            {
                CycleScopeFilter();
            }

            Rect riskBtnRect = new Rect(rect.x + btnW + gap, rect.y, btnW, BtnHeight);
            scope.Record(riskBtnRect, "Button:RiskFilter");
            string riskLabel = _riskFilter.HasValue
                ? "RimMind.UI.MechanismStatus.Risk".Translate(_riskFilter.Value.ToString())
                : "RimMind.UI.MechanismStatus.FilterRisk".Translate();
            if (Widgets.ButtonText(riskBtnRect, riskLabel))
            {
                CycleRiskFilter();
            }
        }

        private void CycleScopeFilter()
        {
            if (_scopeFilter == null)
            {
                _scopeFilter = AllScopes[0];
                return;
            }

            int idx = System.Array.IndexOf(AllScopes, _scopeFilter.Value);
            if (idx < 0 || idx >= AllScopes.Length - 1)
            {
                _scopeFilter = null;
            }
            else
            {
                _scopeFilter = AllScopes[idx + 1];
            }
        }

        private void CycleRiskFilter()
        {
            if (_riskFilter == null)
            {
                _riskFilter = AllRisks[0];
                return;
            }

            int idx = System.Array.IndexOf(AllRisks, _riskFilter.Value);
            if (idx < 0 || idx >= AllRisks.Length - 1)
            {
                _riskFilter = null;
            }
            else
            {
                _riskFilter = AllRisks[idx + 1];
            }
        }

        private void DrawContent(Rect rect, RimMindLayoutScope scope)
        {
            IGameMechanismRegistry? registry = RimMindAPI.Mechanisms;
            if (registry == null)
            {
                DrawEmpty(rect);
                return;
            }

            IReadOnlyList<IGameMechanism> all = registry.All;
            if (all == null || all.Count == 0)
            {
                DrawEmpty(rect);
                return;
            }

            List<IGameMechanism> filtered = all
                .Where(m => _scopeFilter == null || m.Scope == _scopeFilter.Value)
                .Where(m => _riskFilter == null || m.Risk == _riskFilter.Value)
                .ToList();

            if (filtered.Count == 0)
            {
                DrawEmpty(rect);
                return;
            }

            IToolRegistry? toolRegistry = RimMindAPI.Tools;
            IReadOnlyList<IToolHandler> tools = toolRegistry?.All ?? (IReadOnlyList<IToolHandler>)new List<IToolHandler>();

            float contentH = 0f;
            float[] heights = new float[filtered.Count];
            for (int i = 0; i < filtered.Count; i++)
            {
                float h = CalcEntryHeight(filtered[i], tools, rect.width);
                heights[i] = h;
                contentH += h + Padding;
            }

            Rect viewRect = new Rect(rect.x, rect.y, rect.width - 16f, contentH);
            Widgets.BeginScrollView(rect, ref _scrollPos, viewRect);
            scope.Record(rect, "ScrollView:Outer");
            scope.Record(viewRect, "ScrollView:Content");

            float y = rect.y;
            for (int i = 0; i < filtered.Count; i++)
            {
                IGameMechanism mech = filtered[i];
                float entryH = heights[i];

                Rect entryRect = new Rect(viewRect.x, y, viewRect.width, entryH);
                scope.Record(entryRect, $"MechanismEntry:{mech.MechanismId}");
                Widgets.DrawBoxSolid(entryRect, new Color(0.12f, 0.12f, 0.16f, 0.7f));

                DrawMechanismEntry(entryRect, mech, tools);

                y += entryH + Padding;
            }

            Widgets.EndScrollView();
        }

        private float CalcEntryHeight(IGameMechanism mech, IReadOnlyList<IToolHandler> tools, float width)
        {
            float h = LineH + Padding;
            h += LineH + Padding;

            string ops = FormatOperations(mech.SupportedOperations);
            h += LineH + Padding;

            if (!mech.Docs.Summary.NullOrEmpty())
            {
                h += Text.CalcHeight(
                    "RimMind.UI.MechanismStatus.Description".Translate(mech.Docs.Summary),
                    width - Padding * 4) + Padding;
            }

            List<string> toolIds = GetToolIdsForMechanism(mech.MechanismId, tools);
            if (toolIds.Count > 0)
            {
                string toolStr = string.Join(", ", toolIds);
                h += Text.CalcHeight("RimMind.UI.MechanismStatus.ToolMapping".Translate(toolStr), width - Padding * 4) + Padding;
            }
            else
            {
                h += LineH + Padding;
            }

            return h;
        }

        private void DrawMechanismEntry(Rect rect, IGameMechanism mech, IReadOnlyList<IToolHandler> tools)
        {
            float x = rect.x + Padding;
            float y = rect.y + Padding;
            float labelW = rect.width - Padding * 2;

            GUI.color = new Color(0.85f, 0.9f, 1f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(x, y, labelW, LineH), mech.MechanismId);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            y += LineH + Padding;

            GUI.color = Color.grey;
            string scopeText = "RimMind.UI.MechanismStatus.Scope".Translate(mech.Scope.ToString());
            string riskText = "RimMind.UI.MechanismStatus.Risk".Translate(mech.Risk.ToString());
            string ownerText = "RimMind.UI.MechanismStatus.OwnerMod".Translate(mech.OwnerModId ?? "Unknown");
            Widgets.Label(new Rect(x, y, labelW, LineH), $"{scopeText}  |  {riskText}  |  {ownerText}");
            y += LineH + Padding;

            string ops = FormatOperations(mech.SupportedOperations);
            Widgets.Label(new Rect(x, y, labelW, LineH),
                "RimMind.UI.MechanismStatus.Operations".Translate(ops));
            y += LineH + Padding;

            if (!mech.Docs.Summary.NullOrEmpty())
            {
                string descLabel = "RimMind.UI.MechanismStatus.Description".Translate(mech.Docs.Summary);
                float descH = Text.CalcHeight(descLabel, labelW - Padding * 2);
                GUI.color = new Color(0.65f, 0.65f, 0.65f);
                Widgets.Label(new Rect(x + Padding, y, labelW - Padding * 2, descH), descLabel);
                GUI.color = Color.white;
                y += descH + Padding;
            }

            List<string> toolIds = GetToolIdsForMechanism(mech.MechanismId, tools);
            if (toolIds.Count > 0)
            {
                string toolStr = string.Join(", ", toolIds);
                string toolLabel = "RimMind.UI.MechanismStatus.ToolMapping".Translate(toolStr);
                float toolH = Text.CalcHeight(toolLabel, labelW - Padding * 2);
                Widgets.Label(new Rect(x + Padding, y, labelW - Padding * 2, toolH), toolLabel);
            }
            else
            {
                GUI.color = new Color(0.5f, 0.5f, 0.5f);
                Widgets.Label(new Rect(x + Padding, y, labelW - Padding * 2, LineH),
                    "RimMind.UI.MechanismStatus.ToolMapping".Translate("RimMind.UI.MechanismStatus.NoToolMapping".Translate()));
                GUI.color = Color.white;
            }
        }

        private void DrawEmpty(Rect rect)
        {
            float centerY = rect.y + rect.height / 2f;

            GUI.color = Color.grey;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.x, centerY - 30f, rect.width, LineH),
                "RimMind.UI.MechanismStatus.Empty".Translate());

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            string hint = "RimMind.UI.MechanismStatus.EmptyHint".Translate();
            float hintH = Text.CalcHeight(hint, rect.width - 24f);
            Widgets.Label(new Rect(rect.x + 12f, centerY, rect.width - 24f, hintH), hint);
            Text.Font = GameFont.Small;

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private static string FormatOperations(IReadOnlyList<MechanismOperationType> operations)
        {
            if (operations == null || operations.Count == 0)
                return "";
            return string.Join(", ", operations.Select(o => o.ToString()));
        }

        private static List<string> GetToolIdsForMechanism(string mechanismId, IReadOnlyList<IToolHandler> tools)
        {
            var result = new List<string>();
            string prefix = mechanismId + ".";
            foreach (IToolHandler tool in tools)
            {
                if (tool?.Definition?.Id != null && tool.Definition.Id.StartsWith(prefix))
                {
                    result.Add(tool.Definition.Id);
                }
            }
            result.Sort();
            return result;
        }
    }
}
