using RimMind.Core.Client;
using RimMind.Core.Internal;
using LudeonTK;
using RimWorld;
using Verse;

namespace RimMind.Core.Debug
{
    /// <summary>
    /// 开发模式调试动作（Dev 菜单 → RimMind）。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RimMindCoreDebugActions
    {
        [DebugAction("RimMind", "Test API Connection", actionType = DebugActionType.Action)]
        public static void TestConnection()
        {
            if (!RimMindAPI.IsConfigured())
            {
                Log.Warning("[RimMind] API not configured. Set API Key in mod settings.");
                return;
            }

            var request = new AIRequest
            {
                SystemPrompt = "You are a test assistant. Always reply in JSON format.",
                UserPrompt   = "Reply with: {\"status\":\"ok\",\"message\":\"RimMind works\"}",
                MaxTokens    = 60,
                Temperature  = 0f,
                RequestId    = "Debug_TestConnection",
                ModId        = "Debug",
                ExpireAtTicks = Find.TickManager.TicksGame + 3600,
            };

            RimMindAPI.RequestImmediate(request, response =>
            {
                if (response.Success)
                    Messages.Message("RimMind.Core.Debug.ConnectionSuccess".Translate(response.Content), MessageTypeDefOf.PositiveEvent, false);
                else
                    Messages.Message("RimMind.Core.Debug.ConnectionFailed".Translate(response.Error), MessageTypeDefOf.NegativeEvent, false);
            });

            Messages.Message("RimMind.Core.Debug.RequestSent".Translate(), MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("RimMind", "Show Last Prompt", actionType = DebugActionType.Action)]
        public static void ShowLastPrompt()
        {
            var entries = AIDebugLog.Instance?.Entries;
            if (entries == null || entries.Count == 0)
            {
                Log.Message("[RimMind] No request records.");
                return;
            }
            var last = entries[entries.Count - 1];
            Log.Message($"[RimMind] Last request ({last.Source}):\n" +
                        $"=== System Prompt ===\n{last.FullSystemPrompt}\n" +
                        $"=== User Prompt ===\n{last.FullUserPrompt}\n" +
                        $"=== Response ===\n{last.FullResponse}");
        }

        [DebugAction("RimMind", "Clear Debug Log", actionType = DebugActionType.Action)]
        public static void ClearLog()
        {
            AIDebugLog.Instance?.Clear();
            Log.Message("[RimMind] Debug log cleared.");
        }

        [DebugAction("RimMind", "Clear All Cooldowns", actionType = DebugActionType.Action)]
        public static void ClearCooldowns()
        {
            AIRequestQueue.Instance?.ClearAllCooldowns();
            Log.Message("[RimMind] All cooldowns cleared.");
        }

        [DebugAction("RimMind", "Show Map Context", actionType = DebugActionType.Action)]
        public static void ShowMapContext()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Warning("[RimMind] No map loaded."); return; }
            Log.Message("[RimMind] Map Context:\n" + RimMindAPI.BuildMapContext(map));
        }

        [DebugAction("RimMind", "Show Pawn Context (selected)", actionType = DebugActionType.Action)]
        public static void ShowPawnContext()
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            if (pawn == null) { Log.Warning("[RimMind] Select a pawn first."); return; }
            Log.Message("[RimMind] Full Pawn Prompt:\n" + RimMindAPI.BuildFullPawnPrompt(pawn));
        }
    }
}
