using HarmonyLib;
using RimMind.Core.Settings;
using RimMind.Core.UI;
using UnityEngine;
using Verse;

namespace RimMind.Core
{
    /// <summary>
    /// Mod 入口。注册 Harmony，持有全局 Settings 单例。
    /// </summary>
    public class RimMindCoreMod : Mod
    {
        public static RimMindCoreSettings Settings { get; private set; } = null!;

        public RimMindCoreMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RimMindCoreSettings>();
            new Harmony("mcocdaa.RimMindCore").PatchAll();

            RimMindAPI.RegisterToggleBehavior("request_overlay",
                () => Settings.requestOverlayEnabled,
                () =>
                {
                    Settings.requestOverlayEnabled = !Settings.requestOverlayEnabled;
                    Settings.Write();
                });
        }

        public override string SettingsCategory() => "RimMind";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            RimMindCoreSettingsUI.Draw(inRect);
        }
    }
}
