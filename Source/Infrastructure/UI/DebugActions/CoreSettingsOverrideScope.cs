using System;
using RimMind.Presentation;
using Verse;

namespace RimMind.Infrastructure.UI.DebugActions
{
    /// <summary>
    /// RimWorld binding of <see cref="SettingsOverrideScope"/> for Core's settings singleton.
    /// Every in-game harness that changes <c>RimMindCoreMod.Settings</c> (API key, endpoint, model,
    /// overlay layout, concurrency, ...) must open one of these scopes before the first change and
    /// dispose it on every exit path (normal finish, init failure, component destroy).
    /// </summary>
    internal static class CoreSettingsOverrideScope
    {
        /// <summary>Snapshots Core settings (fields above <see cref="ModSettings"/>) for the named harness.</summary>
        public static SettingsOverrideScope Begin(string owner)
        {
            var scope = SettingsOverrideScope.Begin(RimMindCoreMod.Settings, owner, typeof(ModSettings));
            Log.Message($"[RimMind-Core] {owner}: settings override scope opened; player settings will be restored when the run ends and are never persisted with harness values.");
            return scope;
        }

        /// <summary>
        /// Persists Core settings using the player's pre-run snapshot if an override scope is active,
        /// then re-applies the active harness overrides.
        /// </summary>
        public static void WritePlayerSnapshot()
        {
            var settings = RimMindCoreMod.Settings;
            if (!SettingsOverrideScope.TryRedirectActivePersist(settings, settings.Write))
                settings.Write();
        }

        /// <summary>Disposes <paramref name="scope"/> (if any) and reports restore problems. Safe to call repeatedly.</summary>
        public static void End(ref SettingsOverrideScope? scope)
        {
            var s = scope;
            scope = null;
            if (s == null || s.IsDisposed) return;
            try
            {
                s.Dispose();
                if (s.RestoreErrors.Count > 0)
                    Log.Warning($"[RimMind-Core] {s.Owner}: {s.RestoreErrors.Count} setting(s) could not be restored: {string.Join("; ", s.RestoreErrors)}");
                else
                    Log.Message($"[RimMind-Core] {s.Owner}: player settings restored.");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimMind-Core] {s.Owner}: failed to restore player settings: {ex}");
            }
        }
    }
}
