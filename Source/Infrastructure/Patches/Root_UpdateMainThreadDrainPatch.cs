using HarmonyLib;
using RimMind.Presentation.Api;
using Verse;

namespace RimMind.Infrastructure.Patches
{
    /// <summary>
    /// Drains <see cref="RimMindAPI.MainThread"/> once per frame on the Unity main thread.
    /// <c>Root.Update</c> is the base of both <c>Root_Entry.Update</c> (main menu) and
    /// <c>Root_Play.Update</c> (in game), so posted work runs with or without a loaded game.
    /// Draining is deferred while a long event is active, matching the timing callers used to
    /// get from <c>LongEventHandler.ExecuteWhenFinished</c>.
    /// </summary>
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class Root_UpdateMainThreadDrainPatch
    {
        [HarmonyPostfix]
        private static void DrainMainThreadQueue()
        {
            if (RimMindAPI.MainThread.PendingCount == 0)
                return;
            if (LongEventHandler.AnyEventNowOrWaiting)
                return;
            RimMindAPI.MainThread.DrainOnMainThread();
        }
    }
}
