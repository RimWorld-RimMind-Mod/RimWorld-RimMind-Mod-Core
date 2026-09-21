using System;
using System.Linq;
using RimMind.Infrastructure.UI;
using RimMind.Infrastructure.UI.AgentsPage;
using RimMind.Infrastructure.UI.DebugCenter;
using RimMind.Infrastructure.UI.DebugCenter.Pages;
using RimMind.Infrastructure.UI.DebugTables;
using RimMind.Infrastructure.UI.Framework;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using UnityEngine;
using Verse;
using Xunit;

namespace RimMind.Tests.Contracts;

[CollectionDefinition("UI drawing", DisableParallelization = true)]
public sealed class UiDrawingCollection { }

[Collection("UI drawing")]
public sealed class UiDrawingContract : IDisposable
{
    private readonly Color _color = GUI.color;
    private readonly bool _enabled = GUI.enabled;
    private readonly GameFont _font = Text.Font;
    private readonly TextAnchor _anchor = Text.Anchor;
    private readonly Map? _map = Find.CurrentMap;

    public UiDrawingContract()
    {
        Widgets.ResetDrawing();
        GUI.enabled = true;
        Find.CurrentMap = null;
    }

    [Fact]
    public void Settings_entry_draws_title_body_and_button_in_order_inside_nonzero_content_rect()
    {
        var content = new Rect(90f, 140f, 660f, 330f);
        using var scope = RimMindLayoutScope.Begin("settings", content);

        new SettingsEntryDebugCenterPageDrawer().Draw(content, new DebugCenterPageContext(null), scope);

        var title = Assert.Single(Widgets.Draws, d => d.Label == "RimMind.UI.Hub.SettingsEntryTitle");
        var body = Assert.Single(Widgets.Draws, d => d.Label == "RimMind.UI.Hub.SettingsEntryDescription");
        var buttons = Widgets.Draws.Where(d => d.Kind == "ButtonText").ToArray();
        Assert.Equal(2, buttons.Length);
        Assert.Equal("RimMind.UI.Hub.OpenSettings", buttons[0].Label);
        Assert.Equal("RimMind.Settings.OpenContextPayloadInspector", buttons[1].Label);
        AssertInside(content, title.Rect);
        AssertInside(content, body.Rect);
        AssertInside(content, buttons[0].Rect);
        AssertInside(content, buttons[1].Rect);
        Assert.True(title.Rect.yMax <= body.Rect.y);
        Assert.True(body.Rect.yMax < buttons[0].Rect.y);
    }

    [Fact]
    public void Agent_detail_badge_stays_below_the_pawn_name_in_the_status_panel()
    {
        var root = new Rect(75f, 130f, 1000f, 550f);
        var layout = AgentPageLayout.Calculate(root);
        using var scope = RimMindLayoutScope.Begin("detail", root);

        new AgentDetailPanelDrawer().Draw(layout, new Pawn(), scope);

        var name = Assert.Single(Widgets.Draws, d => d.Label == "TestPawn");
        var badge = Assert.Single(Widgets.Draws, d => d.Label.StartsWith("RimMind.UI.AgentsPage.State:"));
        AssertInside(layout.Status, name.Rect);
        Assert.True(badge.Rect.y >= name.Rect.yMax);
        Assert.True(badge.Rect.yMax <= layout.Status.yMax);
    }

    [Fact]
    public void Activity_scroll_content_starts_inside_its_nonzero_viewport()
    {
        var content = new Rect(380f, 165f, 520f, 350f);
        using var scope = RimMindLayoutScope.Begin("activity", content);

        new AgentActivityStreamDrawer().Draw(content, AgentState.Active, 0, scope);

        Assert.NotEmpty(Widgets.Draws);
        Assert.All(Widgets.Draws, d => AssertInside(content, d.Rect));
    }

    [Fact]
    public void Agent_list_scroll_content_starts_inside_its_nonzero_viewport()
    {
        var content = new Rect(70f, 165f, 240f, 350f);
        using var scope = RimMindLayoutScope.Begin("list", content);
        string? selectedId = null;

        new AgentListPanelDrawer().Draw(content, null, ref selectedId, scope);

        Assert.NotEmpty(Widgets.Draws);
        Assert.All(Widgets.Draws, d => AssertInside(content, d.Rect));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Table_cells_fit_the_available_width_and_keep_full_text_accessible(bool compact)
    {
        const string id = "request-with-a-long-but-significant-identifier";
        const string summary = "这是一条需要保留完整内容的中文调试信息\n下一行";
        var model = new DebugTableModel("test", new[]
        {
            DebugTableRow.Create(id, DebugTableStatus.Failed, "", "", "", "", "", summary, "")
        });
        var rect = new Rect(50, 130, 280, 350);
        var scroll = new Vector2();
        using var scope = RimMindLayoutScope.Begin("table", rect);
        var drawer = new RimMindTableDrawer();
        if (compact) drawer.DrawSelectableCompact(rect, model, null, ref scroll, scope);
        else drawer.Draw(rect, model, ref scroll, scope);

        // Verse owns font measurement/ellipsis rendering; the real drawer must pass
        // the full single-line value and expose it through a tooltip, not cut at 15 chars.
        var idCell = Assert.Single(Widgets.Draws, d => d.Kind == "LabelEllipses" && d.Label == id);
        var summaryCell = Assert.Single(Widgets.Draws, d => d.Kind == "LabelEllipses" && d.Label == summary.Replace('\n', ' '));
        Assert.True(idCell.Rect.width > 0 && idCell.Rect.xMax <= summaryCell.Rect.x);
        Assert.Contains(TooltipHandler.Tips, t => t.Text == id);
        Assert.Contains(TooltipHandler.Tips, t => t.Text == summary.Replace('\n', ' '));
    }

    [Theory]
    [InlineData("available", true, "available")]
    [InlineData("disabled", true, "selected")]
    [InlineData("available", false, "selected")]
    public void Tab_host_uses_native_buttons_and_preserves_selection_enablement_and_gui_state(
        string clicked, bool guiEnabled, string expectedSelection)
    {
        var root = new Rect(40f, 70f, 700f, 400f);
        var tabs = new[]
        {
            new TabbedPageTabModel("selected", "selected", "selected", true, true, null),
            new TabbedPageTabModel("available", "available", "available", false, true, null),
            new TabbedPageTabModel("disabled", "disabled", "disabled", false, false, null)
        };
        var incomingColor = new Color(0.2f, 0.3f, 0.4f);
        GUI.color = incomingColor;
        GUI.enabled = guiEnabled;
        Text.Font = GameFont.Tiny;
        Text.Anchor = TextAnchor.LowerRight;
        Widgets.ClickLabel = clicked;
        using var scope = RimMindLayoutScope.Begin("tabs", root);

        string selected = new RimMindTabbedPageHostDrawer().DrawTabs(root, tabs, "selected", scope);

        Assert.Equal(expectedSelection, selected);
        var buttons = Widgets.Draws.Where(d => d.Kind == "ButtonText").ToArray();
        Assert.Equal(3, buttons.Length);
        Assert.Equal(guiEnabled, buttons[0].Enabled);
        Assert.Equal(guiEnabled, buttons[1].Enabled);
        Assert.False(buttons[2].Enabled);
        var highlights = Widgets.Draws.Where(d => d.Kind == "HighlightSelected").ToArray();
        Assert.Single(highlights);
        Assert.Equal(incomingColor, GUI.color);
        Assert.Equal(guiEnabled, GUI.enabled);
        Assert.Equal(GameFont.Tiny, Text.Font);
        Assert.Equal(TextAnchor.LowerRight, Text.Anchor);
    }

    private static void AssertInside(Rect outer, Rect inner)
    {
        Assert.True(inner.x >= outer.x && inner.y >= outer.y,
            $"Draw at ({inner.x}, {inner.y}) precedes content origin ({outer.x}, {outer.y}).");
        Assert.True(inner.xMax <= outer.xMax && inner.yMax <= outer.yMax);
    }

    public void Dispose()
    {
        Widgets.ResetDrawing();
        GUI.color = _color;
        GUI.enabled = _enabled;
        Text.Font = _font;
        Text.Anchor = _anchor;
        Find.CurrentMap = _map;
    }
}
