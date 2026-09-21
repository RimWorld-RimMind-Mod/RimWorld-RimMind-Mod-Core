using System.Linq;
using System.Text;
using RimMind.Presentation.UI.Layout;
using LudeonTK;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public static partial class RimMindCoreDebugActions
    {
        [DebugAction("RimMind", "Show Agent State (selected)", actionType = DebugActionType.Action)]
        public static void ShowAgentState()
        {
            Pawn? pawn = Find.Selector.SingleSelectedThing as Pawn;
            Find.WindowStack.Add(new Window_AgentStateDebug(pawn));
        }
        [DebugAction("RimMind", "ToolCall Debug", actionType = DebugActionType.Action)]
        public static void OpenToolCallDebug()
        {
            Find.WindowStack.Add(new Window_ToolCallDebug());
        }

        [DebugAction("RimMind", "Mechanism Status", actionType = DebugActionType.Action)]
        public static void OpenMechanismStatus()
        {
            Find.WindowStack.Add(new Window_MechanismStatus());
        }

        [DebugAction("RimMind", "Agent Mode Debug", actionType = DebugActionType.Action)]
        public static void OpenAgentModeDebug()
        {
            Pawn? pawn = Find.Selector.SingleSelectedThing as Pawn;
            Find.WindowStack.Add(new Window_AgentModeDebug(pawn));
        }

        [DebugAction("RimMind", "Agent State Window (selected)", actionType = DebugActionType.Action)]
        public static void OpenAgentStateDebug()
        {
            Pawn? pawn = Find.Selector.SingleSelectedThing as Pawn;
            Find.WindowStack.Add(new Window_AgentStateDebug(pawn));
        }

        [DebugAction("RimMind", "Context Keys Window", actionType = DebugActionType.Action)]
        public static void OpenContextKeyDebug()
        {
            Find.WindowStack.Add(new Window_ContextKeyDebug());
        }
        [DebugAction("RimMind", "Agent Flow Lab", actionType = DebugActionType.Action)]
        public static void OpenAgentFlowLab()
        {
            Pawn? pawn = Find.Selector.SingleSelectedThing as Pawn;
            Find.WindowStack.Add(new Window_AgentFlowLab(pawn));
        }

        [DebugAction("RimMind", "Agent Progress Float", actionType = DebugActionType.Action)]
        public static void OpenAgentProgressFloat()
        {
            Find.WindowStack.Add(new Window_AgentProgressFloat());
        }

        [DebugAction("RimMind", "Context Payload Inspector", actionType = DebugActionType.Action)]
        public static void OpenContextPayloadInspector()
        {
            Pawn? pawn = Find.Selector.SingleSelectedThing as Pawn;
            Find.WindowStack.Add(new Window_ContextPayloadInspector(pawn));
        }

        [DebugAction("RimMind", "Test Live AI Request (Payload Inspector)", actionType = DebugActionType.Action)]
        public static void OpenContextPayloadInspectorTest() => OpenContextPayloadInspector();
        [DebugAction("RimMind", "Dump UI Layout Conflicts", actionType = DebugActionType.Action)]
        public static void DumpUiLayoutConflicts()
        {
            var all = LayoutConflictStore.GetAll().ToList();
            if (all.Count == 0)
            {
                Log.Message("[RimMind-Core] No UI layout reports yet. Open a RimMind window first.");
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine("[RimMind-Core] === UI Layout Conflict Report ===");
            foreach (var r in all.OrderBy(r => r.WindowName))
            {
                sb.AppendLine($"  [{r.WindowName}] {r.Conflicts.Count} conflict(s)");
                foreach (var c in r.Conflicts)
                    sb.AppendLine($"    - {c.Message}");
            }
            var worst = LayoutConflictStore.GetWorst();
            if (worst != null && worst.HasConflicts)
                sb.AppendLine($"  WORST: {worst.WindowName} ({worst.Conflicts.Count} conflicts)");
            Log.Message(sb.ToString());
        }

        [DebugAction("RimMind", "Toggle UI Layout Conflict Overlay", actionType = DebugActionType.Action)]
        public static void ToggleUiLayoutOverlay()
        {
            LayoutConflictStore.ShowOverlay = !LayoutConflictStore.ShowOverlay;
            Log.Message($"[RimMind-Core] UI layout conflict overlay: {(LayoutConflictStore.ShowOverlay ? "ON" : "OFF")}");
        }

    }
}
