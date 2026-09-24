using System;
using System.Collections.Generic;
using System.IO;
using RimMind.Application.Common.Interfaces.Agent;
using RimMind.Application.Common.Interfaces.Agent.Modes;
using RimMind.Application.Common.Models.Agent;
using RimMind.Application.Features.Agent;
using RimMind.Domain.Agent.Modes;
using RimMind.Domain.Enums;
using RimMind.Infrastructure.UI;
using RimMind.Infrastructure.UI.DebugCenter.Overview;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using RimMind.Testing;
using UnityEngine;
using Verse;
using Xunit;

namespace RimMind.Tests.Contracts
{
    public sealed class DebugCenterUiRegressionContract
    {
        [Fact]
        public void Debug_center_layout_state_and_window_lifecycle_remain_stable()
        {
            ContractCaseRunner.Run(
                ("dormant agents are reported as dormant rather than pending work", () =>
                {
                    var scheduler = new AgentLoopScheduler();
                    scheduler.Register("pawn:1", AgentLoopKind.Pawn, new DormantAgent());

                    AgentLoopSnapshot snapshot = scheduler.GetSnapshot();
                    var dormant = snapshot.GetType().GetProperty("DormantAgents");

                    Assert.NotNull(dormant);
                    Assert.Equal(1, dormant!.GetValue(snapshot));
                    Assert.Null(snapshot.GetType().GetProperty("PendingAgents"));
                }),
                ("seven debug tabs use two balanced rows that both fill full width", () =>
                {
                    var tabs = new List<TabbedPageTabModel>();
                    for (int i = 0; i < 7; i++)
                        tabs.Add(new TabbedPageTabModel($"tab-{i}", $"Tab {i}", $"Tab.{i}", i == 0, true, null));

                    TabbedPageLayoutResult layout = TabbedPageLayout.Calculate(new Rect(0f, 0f, 748f, 120f), tabs);

                    Assert.Equal(2, layout.RowCount);
                    Assert.Equal(layout.TabRects[0].Rect.y, layout.TabRects[3].Rect.y);
                    Assert.True(layout.TabRects[4].Rect.y > layout.TabRects[3].Rect.y);
                    for (int i = 0; i < 4; i++)
                        Assert.Equal(layout.TabRects[0].Rect.width, layout.TabRects[i].Rect.width, 3);
                    Assert.Equal(layout.Body.xMax, layout.TabRects[3].Rect.xMax, 1);
                    for (int i = 4; i < 7; i++)
                        Assert.Equal(layout.TabRects[4].Rect.width, layout.TabRects[i].Rect.width, 3);
                    Assert.Equal(layout.Body.xMax, layout.TabRects[6].Rect.xMax, 1);
                }),
                ("closed window-stack snapshots do not draw disposed window contents", () =>
                {
                    var window = new ProbeWindow { IsOpen = false };

                    window.DoWindowContents(new Rect(0f, 0f, 200f, 100f));

                    Assert.Equal(0, window.DrawCount);
                }),
                ("overview geometry is local and covers its final action row", () =>
                {
                    DebugCenterOverviewLayoutResult layout = DebugCenterOverviewLayout.Calculate(
                        new Rect(120f, 80f, 716f, 398f));

                    Assert.Equal(0f, layout.ViewRect.x);
                    Assert.Equal(0f, layout.ViewRect.y);
                    Assert.True(layout.Cards[2].y > layout.Cards[0].yMax);
                    Assert.True(layout.Summary.y > layout.Cards[2].yMax);
                    Assert.True(layout.LifecycleRuntime.y > layout.LifecycleHeader.yMax);
                    Assert.True(layout.QuickActions.y > layout.LifecycleRuntime.yMax);
                    Assert.True(layout.ViewRect.height >= layout.QuickActions.yMax);
                }),
                ("window drawing restores shared IMGUI state", () =>
                {
                    GUI.color = new Color(0.2f, 0.3f, 0.4f, 0.5f);
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.LowerRight;

                    new GuiStateProbeWindow().DoWindowContents(new Rect(0f, 0f, 200f, 100f));

                    Assert.Equal(new Color(0.2f, 0.3f, 0.4f, 0.5f), GUI.color);
                    Assert.Equal(GameFont.Tiny, Text.Font);
                    Assert.Equal(TextAnchor.LowerRight, Text.Anchor);
                }),
                ("overview uses a scroll viewport and one coordinate space", () =>
                {
                    string drawer = ReadSource("Infrastructure/UI/DebugCenter/Pages/OverviewDebugCenterPageDrawer.cs");
                    Assert.Contains("BeginScrollView", drawer, StringComparison.Ordinal);
                    Assert.Contains("_scrollPosition", drawer, StringComparison.Ordinal);
                    Assert.Contains("DebugCenterOverviewLayout.Calculate", drawer, StringComparison.Ordinal);
                    Assert.Contains("layout.ViewRect", drawer, StringComparison.Ordinal);
                    Assert.DoesNotContain("OverviewContentHeight", drawer, StringComparison.Ordinal);
                    Assert.DoesNotContain("new Rect(rect.x, rect.y", drawer, StringComparison.Ordinal);
                    Assert.DoesNotContain("y - rect.y", drawer, StringComparison.Ordinal);
                    Assert.DoesNotContain("model.AgentSummary", drawer, StringComparison.Ordinal);

                    string model = ReadSource("Infrastructure/UI/DebugCenter/Overview/DebugCenterOverviewModel.cs");
                    Assert.DoesNotContain(" active / ", model, StringComparison.Ordinal);
                    Assert.DoesNotContain(" pending / ", model, StringComparison.Ordinal);
                }),
                ("empty debug tables render one localized state without a zero-height scroll", () =>
                {
                    string drawer = ReadSource("Infrastructure/UI/Framework/RimMindTableDrawer.cs");
                    Assert.Contains("DrawEmptyTableBody", drawer, StringComparison.Ordinal);
                    Assert.Contains("RimMind.UI.DebugTable.Empty", drawer, StringComparison.Ordinal);

                    foreach (string language in new[] { "English", "ChineseSimplified" })
                    {
                        string keyed = File.ReadAllText(Path.Combine(
                            CoreRoot(),
                            "Languages",
                            language,
                            "Keyed",
                            "RimMind_Core.xml"));
                        Assert.Contains("RimMind.UI.DebugTable.Empty", keyed, StringComparison.Ordinal);
                    }
                }),
                ("layout autotest opens the complete debug hub", () =>
                {
                    string actions = ReadDebugActionSources();
                    Assert.Contains("new Window_RimMindHub()", actions, StringComparison.Ordinal);
                }));
        }

        private sealed class ProbeWindow : RimMindWindowBase
        {
            public int DrawCount { get; private set; }

            protected override void DrawContents(Rect inRect, RimMindLayoutScope scope)
            {
                DrawCount++;
            }
        }

        private sealed class GuiStateProbeWindow : RimMindWindowBase
        {
            protected override void DrawContents(Rect inRect, RimMindLayoutScope scope)
            {
                GUI.color = Color.red;
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
            }
        }

        private sealed class DormantAgent : IAgentControl
        {
            public bool IsActive => false;
            public AgentModeId CurrentModeId => AgentModeId.Dormant;
            public IAgentMode CurrentMode => null!;
            public bool IsPawnValid => true;
            public AgentState State => AgentState.Dormant;
            public string NpcId => "NPC-1";
            public string Label => "Dormant pawn";
            public int? LastThinkTick { get; set; }
            public int GoalCount => 0;
            public void Tick() { }
            public bool TransitionTo(AgentState newState) => false;
            public void ForceThink() { }
            public void SwitchMode(AgentModeId modeId) { }
            public void Cleanup() { }
            public void Destroy() { }
            public void ResubscribeEvents() { }
            public bool RemoveGoal(string goalDescription) => false;
            public void RecordBehavior(BehaviorRecordDto record) { }
            public IReadOnlyList<BehaviorRecordDto> GetRecentHistory(int count = 10) => Array.Empty<BehaviorRecordDto>();
            public float GetRecentSuccessRate(int count = 10) => 0f;
            public string GetDebugInfo() => string.Empty;
            public object? ConsumePendingJob() => null;
        }

        private static string ReadDebugActionSources() =>
            string.Join(Environment.NewLine, new[]
            {
                ReadSource("Infrastructure/UI/AICoreDebugActions.cs"),
                ReadSource("Infrastructure/UI/DebugActions/AICoreDebugActions.Requests.cs"),
                ReadSource("Infrastructure/UI/DebugActions/AICoreDebugActions.ContextAndAgents.cs"),
                ReadSource("Infrastructure/UI/DebugActions/AICoreDebugActions.Windows.cs"),
                ReadSource("Infrastructure/UI/DebugActions/AICoreDebugActions.Autotests.cs"),
            });

        private static string ReadSource(string relativePath) =>
            File.ReadAllText(Path.Combine(CoreRoot(), "Source", relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string CoreRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "RimMind-Core", "Source")))
                directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "RimMind-Core");
        }
    }
}
