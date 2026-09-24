using System;
using System.Linq;
using HarmonyLib;
using RimMind.Presentation.Api;
using RimMind.Presentation.Settings;
using UnityEngine;
using Verse;

namespace RimMind.Presentation
{
    /// <summary>
    /// Abstract base class for all RimMind submodules inheriting from <see cref="Verse.Mod"/>.
    /// Eliminates boilerplate for Harmony patching, ModSettings lazy-loading,
    /// Keyed XML title resolution, and Settings tab integration.
    /// </summary>
    /// <typeparam name="TSettings">Submodule's ModSettings implementation.</typeparam>
    public abstract class RimMindSubmodBase<TSettings> : Mod where TSettings : ModSettings, new()
    {
        private TSettings? _settings;
        private Harmony? _harmony;

        protected RimMindSubmodBase(ModContentPack content) : base(content)
        {
        }

        /// <summary>
        /// Lazy-resolved and cached instance of the submodule settings.
        /// </summary>
        public TSettings Settings => _settings ??= GetSettings<TSettings>();

        /// <summary>
        /// Harmony instance initialized for this submodule.
        /// </summary>
        public Harmony Harmony => _harmony ??= new Harmony(HarmonyPackageId);

        /// <summary>
        /// The package ID passed to Harmony. Defaults to "mcocdaa." + GetType().Name.
        /// </summary>
        protected virtual string HarmonyPackageId => $"mcocdaa.{GetType().Name}";

        /// <summary>
        /// Short module name (e.g. "Advisor", "Dialogue", "BridgeRimChat").
        /// </summary>
        public virtual string SubmoduleName => GetType().Name.Replace("RimMind", "").Replace("Mod", "");

        /// <summary>
        /// Keyed XML translation key for the settings window title.
        /// Default pattern: "RimMind.{SubmoduleName}.SettingsTitle" or "RimMind.{SubmoduleName}.Settings.Category".
        /// </summary>
        protected virtual string SettingsTitleKey => $"RimMind.{SubmoduleName}.SettingsTitle";

        /// <summary>
        /// Secondary translation key pattern used by bridge modules.
        /// </summary>
        protected virtual string SettingsCategoryKey => $"RimMind.{SubmoduleName}.Settings.Category";

        /// <summary>
        /// Fallback display title if Keyed XML translation is missing.
        /// </summary>
        protected virtual string FallbackSettingsTitle => $"RimMind - {SubmoduleName}";

        /// <summary>
        /// Automatically resolves the translated category name, falling back gracefully.
        /// </summary>
        public override string SettingsCategory()
        {
            if (SettingsTitleKey.CanTranslate())
            {
                return SettingsTitleKey.Translate();
            }
            if (SettingsCategoryKey.CanTranslate())
            {
                return SettingsCategoryKey.Translate();
            }
            return FallbackSettingsTitle;
        }

        /// <summary>
        /// Patches all annotated Harmony methods in the submodule's assembly.
        /// </summary>
        protected void InitializeHarmony()
        {
            try
            {
                Harmony.PatchAll(GetType().Assembly);
            }
            catch (Exception ex)
            {
                Verse.Log.Error($"[{GetType().Name}] Harmony patching failed: {ex}");
            }
        }

        /// <summary>
        /// Default implementation draws the submodule's registered <see cref="ISettingsTab"/> if available.
        /// Submodules can override this to delegate to their custom drawer.
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            var tab = RimMindAPI.Extensions<ISettingsTab>()?.All?
                .FirstOrDefault(t => string.Equals(t.OwnerModId, SubmoduleName, StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(t.Id, SubmoduleName, StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(t.OwnerModId, GetType().Name, StringComparison.OrdinalIgnoreCase));
            if (tab != null)
            {
                tab.Draw(inRect);
            }
        }
    }

    /// <summary>
    /// Default empty ModSettings placeholder for submodules without custom configuration.
    /// </summary>
    public sealed class EmptyModSettings : ModSettings
    {
    }

    /// <summary>
    /// Non-generic base class for submodules that do not define custom ModSettings.
    /// Uses <see cref="EmptyModSettings"/>.
    /// </summary>
    public abstract class RimMindSubmodBase : RimMindSubmodBase<EmptyModSettings>
    {
        protected RimMindSubmodBase(ModContentPack content) : base(content)
        {
        }
    }
}
