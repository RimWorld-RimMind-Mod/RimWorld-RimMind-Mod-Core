using RimMind.Infrastructure.UI.DebugCenter;
using RimMind.Infrastructure.UI.DebugCenter.Pages;
using RimMind.Infrastructure.UI.DebugTables;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Presentation.UI.Layout;
using UnityEngine;

namespace RimMind.Infrastructure.UI.DebugCenter.Pages;

internal sealed class OverviewDebugCenterPageDrawer : TestDebugCenterPageDrawer
{
}

internal sealed class AgentsDebugCenterPageDrawer : TestDebugCenterPageDrawer
{
}

internal sealed class AIRequestsDebugCenterPageDrawer : TestDebugCenterPageDrawer
{
    public AIRequestsDebugCenterPageDrawer(IAIRequestTraceLog? log) { }
}

internal sealed class ToolCallsDebugCenterPageDrawer : TestDebugCenterPageDrawer
{
    public ToolCallsDebugCenterPageDrawer(ToolCallsDebugTableModelBuilder modelBuilder) { }
}

internal sealed class MechanismsDebugCenterPageDrawer : TestDebugCenterPageDrawer
{
    public MechanismsDebugCenterPageDrawer(MechanismsDebugTableModelBuilder modelBuilder) { }
}

internal sealed class ContextKeysDebugCenterPageDrawer : TestDebugCenterPageDrawer
{
    public ContextKeysDebugCenterPageDrawer(ContextKeysDebugTableModelBuilder modelBuilder) { }
}

internal abstract class TestDebugCenterPageDrawer : IDebugCenterPageDrawer
{
    public void Draw(Rect rect, DebugCenterPageContext context, RimMindLayoutScope scope)
    {
    }
}
