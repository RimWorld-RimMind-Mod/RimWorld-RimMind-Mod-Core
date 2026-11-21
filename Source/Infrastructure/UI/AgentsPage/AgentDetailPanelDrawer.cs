using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Domain.Enums;
using RimMind.Infrastructure.UI.DebugCenter;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using RimMind.Infrastructure.Verse;
using RimMind.Presentation.Runtime.Services;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI.AgentsPage
{
    public sealed class AgentDetailPanelDrawer
    {
        private readonly RuntimeServiceRef<IAIRequestTraceLog> _traceLog =
            RuntimeServiceRef<IAIRequestTraceLog>.Optional();
        private readonly IAIRequestTraceLog? _injectedTraceLog;
        private IAIRequestTraceLog? _cachedLogInstance;
        private long _cachedTraceRevision = long.MinValue;
        private string? _cachedNpcKey;
        private System.Collections.Generic.IReadOnlyList<AgentRequestTraceRow> _cachedTraceRows =
            System.Array.Empty<AgentRequestTraceRow>();
        private Vector2 _permissionsScrollPos;

        internal void ScrollToBottom() => _permissionsScrollPos = new Vector2(0f, 100000f);

        internal int TraceRebuildCount { get; private set; }

        public AgentDetailPanelDrawer(IAIRequestTraceLog? traceLog = null)
        {
            _injectedTraceLog = traceLog;
        }

        public AgentPageViewModel? BuildViewModel(Pawn? selectedPawn)
        {
            if (selectedPawn == null)
                return null;

            var comp = CompPawnAgent.GetComp(selectedPawn);
            string? npcId = comp?.Agent?.NpcId;
            string? pawnId = !string.IsNullOrWhiteSpace(selectedPawn.ThingID)
                ? selectedPawn.ThingID
                : selectedPawn.LabelShortCap;
            var traceRows = BuildTraceRows(npcId ?? pawnId, pawnId);
            if (comp?.Agent == null)
            {
                return AgentPageViewModel.PendingCreation(
                    selectedPawn.LabelShortCap,
                    RequestOverlay.Pending.Count,
                    traceRows);
            }

            return AgentPageViewModel.FromState(
                selectedPawn.LabelShortCap,
                comp.Agent.State,
                RequestOverlay.Pending.Count,
                requestRows: 0,
                traceRows);
        }

        public void Draw(AgentPageRects layout, Pawn? selectedPawn, RimMindLayoutScope scope)
        {
            scope.Record(layout.Detail, "Agents:DetailPanel");
            Widgets.DrawBoxSolid(layout.Detail, RimMindUI.ColorCardBg);

            if (selectedPawn == null)
            {
                RimMindUI.DrawEmptyState(layout.Detail, "RimMind.UI.AgentStateDebug.NoPawn".Translate());
                return;
            }

            var comp = CompPawnAgent.GetComp(selectedPawn);
            DrawDetailHeader(layout.Status, selectedPawn, comp?.Agent);

            if (comp?.Agent == null)
            {
                Rect createRect = layout.ActionBar.Buttons.Count > 0
                    ? layout.ActionBar.Buttons[0].Rect
                    : new Rect(layout.Actions.x, layout.Actions.y, 160f, RimMindUI.BtnHeight);
                if (Widgets.ButtonText(createRect, "RimMind.UI.AgentsPage.CreateStart".Translate()))
                {
                    if (comp != null && comp.EnsureAgentCreated())
                        SafeTransitionTo(comp.Agent, AgentState.Active);
                    else
                        Messages.Message("RimMind.UI.AgentsPage.CreateFailed".Translate(),
                            MessageTypeDefOf.RejectInput, false);
                }
                if (comp != null && layout.Permissions.height > 28f)
                    DrawPermissionsPanel(layout.Permissions, selectedPawn, comp);
                return;
            }

            var agent = comp.Agent;
            DrawActions(layout.ActionBar, agent);
            if (layout.Permissions.height > 28f)
                DrawPermissionsPanel(layout.Permissions, selectedPawn, comp);
        }

        private static void DrawDetailHeader(Rect rect, Pawn pawn, IAgentControl? agent)
        {
            Rect inner = rect.ContractedBy(RimMindUI.Padding);

            Text.Font = GameFont.Medium;
            GUI.color = RimMindUI.ColorHeader;
            const float nameHeight = 32f;
            Rect nameRect = new Rect(inner.x, inner.y, inner.width, nameHeight);
            Widgets.Label(nameRect, pawn.LabelShortCap);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(nameRect, pawn.Name != null ? pawn.Name.ToStringFull : pawn.LabelCap);

            string stateLabel = "RimMind.UI.AgentsPage.Pending".Translate();
            AgentState? state = null;
            if (agent != null)
            {
                state = agent.State;
                stateLabel = AgentActivityStreamDrawer.StateLabel(agent.State);
            }

            var (textColor, bgColor) = state.HasValue
                ? RimMindUI.GetStateBadgeColors(state.Value)
                : RimMindUI.GetStateBadgeColors(AgentState.Dormant, isPendingCreation: true);

            // Pass rect (whose padding gives inner.x) so the badge aligns with inner.x
            RimMindUI.DrawStatusBadge(rect, inner.y + nameHeight + 6f,
                "RimMind.UI.AgentsPage.State".Translate() + ": " + stateLabel, textColor, bgColor);
        }

        private static void DrawActions(ActionBarLayoutResult actionBar, IAgentControl agent)
        {
            foreach (var button in actionBar.Buttons)
            {
                switch (button.Id)
                {
                    case "primary":
                        bool isSingle = agent.State == AgentState.Dormant || agent.State == AgentState.Terminated;
                        Rect btnRect = isSingle
                            ? new Rect(button.Rect.x, button.Rect.y, Mathf.Min(button.Rect.width * 2f + RimMindUiMetrics.ButtonGap, 200f), button.Rect.height)
                            : button.Rect;
                        DrawPrimaryStateButton(btnRect, agent);
                        break;
                    case "force_think":
                        if ((agent.State == AgentState.Active || agent.State == AgentState.Paused)
                            && Widgets.ButtonText(button.Rect, "RimMind.UI.AgentsPage.ForceThink".Translate()))
                            agent.ForceThink();
                        break;
                    case "open_requests":
                        if ((agent.State == AgentState.Active || agent.State == AgentState.Paused)
                            && Widgets.ButtonText(button.Rect, "RimMind.UI.AgentsPage.OpenRequests".Translate()))
                            Find.WindowStack.Add(Window_RimMindHub.OpenAIRequests());
                        break;
                }
            }
        }

        private static void DrawPrimaryStateButton(Rect rect, IAgentControl agent)
        {
            switch (agent.State)
            {
                case AgentState.Active:
                    if (Widgets.ButtonText(rect, "RimMind.UI.AgentsPage.Pause".Translate()))
                        SafeTransitionTo(agent, AgentState.Paused);
                    break;
                case AgentState.Paused:
                    if (Widgets.ButtonText(rect, "RimMind.UI.AgentsPage.Resume".Translate()))
                        SafeTransitionTo(agent, AgentState.Active);
                    break;
                case AgentState.Dormant:
                    if (Widgets.ButtonText(rect, "RimMind.UI.AgentsPage.Activate".Translate()))
                        SafeTransitionTo(agent, AgentState.Active);
                    break;
                case AgentState.Terminated:
                    if (Widgets.ButtonText(rect, "RimMind.UI.AgentsPage.Restart".Translate()))
                        SafeTransitionTo(agent, AgentState.Active);
                    break;
            }
        }

        private static void SafeTransitionTo(IAgentControl? agent, AgentState target)
        {
            if (agent == null) return;
            agent.TransitionTo(target);
        }

        private System.Collections.Generic.IReadOnlyList<AgentRequestTraceRow> BuildTraceRows(
            string? selectedNpcId,
            string? fallbackPawnId = null)
        {
            var log = _injectedTraceLog ?? _traceLog.ValueOrDefault;
            if (log == null)
            {
                _cachedLogInstance = null;
                _cachedTraceRevision = long.MinValue;
                _cachedNpcKey = null;
                _cachedTraceRows = System.Array.Empty<AgentRequestTraceRow>();
                return _cachedTraceRows;
            }

            long revision = log.Revision;
            string cacheKey = string.IsNullOrEmpty(fallbackPawnId) || fallbackPawnId == selectedNpcId
                ? (selectedNpcId ?? string.Empty)
                : $"{selectedNpcId}|{fallbackPawnId}";

            if (ReferenceEquals(_cachedLogInstance, log)
                && _cachedTraceRevision == revision
                && string.Equals(_cachedNpcKey, cacheKey, System.StringComparison.Ordinal))
            {
                return _cachedTraceRows;
            }

            System.Collections.Generic.IReadOnlyList<RimMind.Application.Common.Models.Debug.AIRequestTraceEntry> recentEntries;
            if (log is AIRequestTraceLog concreteLog)
            {
                recentEntries = concreteLog.GetRecent(
                    AgentRequestTraceRowBuilder.DefaultLimit,
                    selectedNpcId,
                    fallbackPawnId);
            }
            else
            {
                var all = log.Entries;
                var filtered = new System.Collections.Generic.List<RimMind.Application.Common.Models.Debug.AIRequestTraceEntry>();
                for (int i = 0; i < all.Count; i++)
                {
                    if (AIRequestTraceLog.MatchesActor(all[i], selectedNpcId, fallbackPawnId))
                        filtered.Add(all[i]);
                }
                recentEntries = filtered;
            }

            _cachedTraceRows = AgentRequestTraceRowBuilder.BuildRecent(
                recentEntries,
                AgentRequestTraceRowBuilder.DefaultLimit);
            _cachedLogInstance = log;
            _cachedTraceRevision = revision;
            _cachedNpcKey = cacheKey;
            TraceRebuildCount++;
            return _cachedTraceRows;
        }

        private void DrawPermissionsPanel(Rect rect, Pawn selectedPawn, CompPawnAgent comp)
        {
            var policy = comp.AutonomyPolicy;
            if (policy == null) return;

            Rect inner = rect.ContractedBy(4f);
            float y = inner.y;

            Text.Font = GameFont.Small;
            GUI.color = RimMindUI.ColorHeader;
            Rect titleRect = new Rect(inner.x, y, inner.width, 20f);
            Widgets.LabelEllipses(titleRect, SafeTranslate("RimMind.UI.AgentsPage.Permissions.Title", "Agent Permissions"));
            TooltipHandler.TipRegion(titleRect, SafeTranslate("RimMind.UI.AgentsPage.Permissions.TitleTip", "Control what autonomous domains this colonist Agent is permitted to execute."));
            GUI.color = Color.white;
            y += 22f;

            const float presetH = 22f;
            const float gap = 4f;
            float presetW = Mathf.Max(40f, (inner.width - gap * 2f) / 3f);

            Rect workOnlyRect = new Rect(inner.x, y, presetW, presetH);
            if (Widgets.ButtonText(workOnlyRect, SafeTranslate("RimMind.UI.AgentsPage.Permissions.PresetWorkOnly", "Work Only")))
                policy.ApplyWorkOnlyNoBlueprintsPreset();
            TooltipHandler.TipRegion(workOnlyRect, SafeTranslate("RimMind.UI.AgentsPage.Permissions.PresetWorkOnlyTip", "Allow survival/resource/worktable autonomy, disable blueprints and Work Tab changes."));

            Rect defaultRect = new Rect(workOnlyRect.xMax + gap, y, presetW, presetH);
            if (Widgets.ButtonText(defaultRect, SafeTranslate("RimMind.UI.AgentsPage.Permissions.PresetDefault", "Default")))
                policy.SetDefaultPreset();
            TooltipHandler.TipRegion(defaultRect, SafeTranslate("RimMind.UI.AgentsPage.Permissions.PresetDefaultTip", "Restore balanced default permissions."));

            Rect fullAutoRect = new Rect(defaultRect.xMax + gap, y, Mathf.Max(40f, inner.xMax - defaultRect.xMax - gap), presetH);
            if (Widgets.ButtonText(fullAutoRect, SafeTranslate("RimMind.UI.AgentsPage.Permissions.PresetFullAuto", "Full Auto")))
                policy.ApplyFullAutonomousPreset();
            TooltipHandler.TipRegion(fullAutoRect, SafeTranslate("RimMind.UI.AgentsPage.Permissions.PresetFullAutoTip", "Enable autonomous execution for blueprints, work priorities, survival, and bills."));
            y += presetH + 6f;

            const float applyAllH = 24f;
            float listHeight = inner.yMax - y - applyAllH - 6f;
            if (listHeight >= 26f)
            {
                var domains = (RimMind.Domain.Agent.Policies.AutonomyDomain[])System.Enum.GetValues(typeof(RimMind.Domain.Agent.Policies.AutonomyDomain));
                const float rowH = 24f;
                const float rowGap = 3f;
                Rect outRect = new Rect(inner.x, y, inner.width, listHeight);
                float totalH = domains.Length * (rowH + rowGap) + 4f;
                bool needsScroll = totalH > outRect.height + 1f;
                float contentW = needsScroll
                    ? Mathf.Max(20f, inner.width - RimMindUiMetrics.ScrollBarWidth)
                    : Mathf.Max(20f, inner.width);
                Rect viewRect = new Rect(0f, 0f, contentW, needsScroll ? totalH : outRect.height);

                Widgets.BeginScrollView(outRect, ref _permissionsScrollPos, viewRect);
                float ry = 0f;
                for (int i = 0; i < domains.Length; i++)
                {
                    var domain = domains[i];
                    var level = policy.GetLevel(domain);
                    const float btnW = 56f;
                    Rect rowRect = new Rect(0f, ry, contentW, rowH);
                    Rect labelRect = new Rect(0f, ry + 2f, Mathf.Max(20f, contentW - btnW - 4f), rowH - 2f);
                    Rect toggleRect = new Rect(Mathf.Max(0f, contentW - btnW), ry, btnW, rowH);

                    Widgets.LabelEllipses(labelRect, DomainLabel(domain));
                    TooltipHandler.TipRegion(rowRect, DomainTooltip(domain));

                    GUI.color = ColorForLevel(level);
                    if (Widgets.ButtonText(toggleRect, LevelShortLabel(level)))
                        policy.SetLevel(domain, NextLevel(level));
                    GUI.color = Color.white;

                    ry += rowH + rowGap;
                }
                Widgets.EndScrollView();
                y = outRect.yMax + 6f;
            }

            Rect applyAllRect = new Rect(inner.x, Mathf.Max(y, inner.yMax - applyAllH), inner.width, applyAllH);
            if (Widgets.ButtonText(applyAllRect, SafeTranslate("RimMind.UI.AgentsPage.Permissions.ApplyAll", "Apply to All Colonists")))
            {
                var colonists = RimMind.Presentation.Api.RimMindPawnLookup.GetEligibleColonists(Find.CurrentMap);
                foreach (var pawn in colonists)
                {
                    if (pawn == null || ReferenceEquals(pawn, selectedPawn)) continue;
                    var peerComp = CompPawnAgent.GetComp(pawn);
                    peerComp?.AutonomyPolicy?.CopyFrom(policy);
                }
                Messages.Message(
                    SafeTranslate("RimMind.UI.AgentsPage.Permissions.AppliedAllToast", "Applied permissions to all active colonists."),
                    MessageTypeDefOf.PositiveEvent,
                    false);
            }
            TooltipHandler.TipRegion(applyAllRect, SafeTranslate("RimMind.UI.AgentsPage.Permissions.ApplyAllTip", "Copy this colonist's permission policy to every active colonist agent on the map."));
        }

        private static RimMind.Domain.Agent.Policies.AutonomyLevel NextLevel(RimMind.Domain.Agent.Policies.AutonomyLevel level)
            => level switch
            {
                RimMind.Domain.Agent.Policies.AutonomyLevel.FullAutonomous => RimMind.Domain.Agent.Policies.AutonomyLevel.RequireApproval,
                RimMind.Domain.Agent.Policies.AutonomyLevel.RequireApproval => RimMind.Domain.Agent.Policies.AutonomyLevel.Disabled,
                _ => RimMind.Domain.Agent.Policies.AutonomyLevel.FullAutonomous
            };

        private static Color ColorForLevel(RimMind.Domain.Agent.Policies.AutonomyLevel level)
            => level switch
            {
                RimMind.Domain.Agent.Policies.AutonomyLevel.FullAutonomous => new Color(0.55f, 0.95f, 0.6f),
                RimMind.Domain.Agent.Policies.AutonomyLevel.RequireApproval => new Color(0.98f, 0.82f, 0.4f),
                _ => new Color(0.85f, 0.5f, 0.5f)
            };

        private static string LevelShortLabel(RimMind.Domain.Agent.Policies.AutonomyLevel level)
            => level switch
            {
                RimMind.Domain.Agent.Policies.AutonomyLevel.FullAutonomous => SafeTranslate("RimMind.AutonomyLevel.Auto", "Auto"),
                RimMind.Domain.Agent.Policies.AutonomyLevel.RequireApproval => SafeTranslate("RimMind.AutonomyLevel.Ask", "Ask"),
                _ => SafeTranslate("RimMind.AutonomyLevel.Off", "Off")
            };

        private static string DomainLabel(RimMind.Domain.Agent.Policies.AutonomyDomain domain)
            => domain switch
            {
                RimMind.Domain.Agent.Policies.AutonomyDomain.EmergencySurvival => SafeTranslate("RimMind.AutonomyDomain.EmergencySurvival", "Survival"),
                RimMind.Domain.Agent.Policies.AutonomyDomain.ResourceMarking => SafeTranslate("RimMind.AutonomyDomain.ResourceMarking", "Resources"),
                RimMind.Domain.Agent.Policies.AutonomyDomain.WorktableBills => SafeTranslate("RimMind.AutonomyDomain.WorktableBills", "Work Bills"),
                RimMind.Domain.Agent.Policies.AutonomyDomain.BlueprintPlacement => SafeTranslate("RimMind.AutonomyDomain.BlueprintPlacement", "Blueprints"),
                RimMind.Domain.Agent.Policies.AutonomyDomain.HighRiskOperations => SafeTranslate("RimMind.AutonomyDomain.HighRiskOperations", "High Risk"),
                RimMind.Domain.Agent.Policies.AutonomyDomain.WorkPriorityModification => SafeTranslate("RimMind.AutonomyDomain.WorkPriorityModification", "Work Tab"),
                _ => domain.ToString()
            };

        private static string DomainTooltip(RimMind.Domain.Agent.Policies.AutonomyDomain domain)
            => domain switch
            {
                RimMind.Domain.Agent.Policies.AutonomyDomain.EmergencySurvival => SafeTranslate("RimMind.AutonomyDomain.EmergencySurvivalTip", "Eating, resting, rescue, tending, and emergency flee actions."),
                RimMind.Domain.Agent.Policies.AutonomyDomain.ResourceMarking => SafeTranslate("RimMind.AutonomyDomain.ResourceMarkingTip", "Designating trees, ore veins, plants, and haulables."),
                RimMind.Domain.Agent.Policies.AutonomyDomain.WorktableBills => SafeTranslate("RimMind.AutonomyDomain.WorktableBillsTip", "Adding or managing crafting and cooking bills on worktables."),
                RimMind.Domain.Agent.Policies.AutonomyDomain.BlueprintPlacement => SafeTranslate("RimMind.AutonomyDomain.BlueprintPlacementTip", "Placing building blueprints or planning rooms."),
                RimMind.Domain.Agent.Policies.AutonomyDomain.HighRiskOperations => SafeTranslate("RimMind.AutonomyDomain.HighRiskOperationsTip", "Arrests, deconstruction, or irreversible dangerous actions."),
                RimMind.Domain.Agent.Policies.AutonomyDomain.WorkPriorityModification => SafeTranslate("RimMind.AutonomyDomain.WorkPriorityModificationTip", "Changing Work Tab numeric priorities (Off by default to protect player schedules)."),
                _ => domain.ToString()
            };

        private static string SafeTranslate(string key, string fallback)
            => RimMind.Presentation.Api.RimMindText.TrOr(key, fallback);
    }
}
