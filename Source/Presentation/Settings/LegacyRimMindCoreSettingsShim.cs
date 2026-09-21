using System;

namespace RimMind.Core.Settings
{
    /// <summary>
    /// Backward-compatibility shim for legacy saved configurations.
    /// When RimWorld loads legacy user configs containing
    /// &lt;ModSettings Class="RimMind.Core.Settings.RimMindCoreSettings"&gt;,
    /// this class allows RimWorld's Scribe to resolve the type directly without
    /// logging a "Could not find class" error.
    /// </summary>
    [Obsolete("Use RimMind.Presentation.Settings.RimMindCoreSettings instead.")]
    public class RimMindCoreSettings : RimMind.Presentation.Settings.RimMindCoreSettings
    {
    }
}
