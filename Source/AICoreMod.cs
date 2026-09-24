using System;
using HarmonyLib;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Domain.ValueObjects;
using RimMind.Application.Features.Json;
using RimMind.Application.Features.Flywheel;
using RimMind.Application.Features.Context;
using RimMind.Presentation.Api;
using RimMind.Presentation.UI;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;
using RimMind.Presentation.Settings;
using UnityEngine;
using Verse;

namespace RimMind.Presentation
{
    internal static class BootstrapConstants
    {
        internal const string CurrentModVersion = "2.0.0";
        internal const string HarmonyId = "mcocdaa.RimMindCore";
        internal const string DomainAssemblyName = "0_RimMindDomain";
        internal const string ApplicationAssemblyName = "1_RimMindApplication";
        internal const string CoreAssemblyName = "2_RimMindCore";
    }

    public class RimMindCoreMod : Mod
    {
        public static RimMindCoreMod Instance { get; private set; } = null!;
        public static RimMindCoreSettings Settings { get; private set; } = null!;

        public RimMindCoreMod(ModContentPack content) : base(content)
        {
            Instance = this;
            AssemblyLoadGuard.AssertAssembliesLoaded();
            RimMindErrors.OnWarn = (msg, ex) => Verse.Log.Warning(ex != null ? $"[RimMind] {msg}\n{ex}" : $"[RimMind] {msg}");
            RimMindErrors.OnError = (msg, ex) => Verse.Log.Error(ex != null ? $"[RimMind] {msg}\n{ex}" : $"[RimMind] {msg}");

            Settings = GetSettings<RimMindCoreSettings>();
            var sp = new SettingsProvider(Settings);

            ProcessLifecycleEvents.Publisher.Configure(VerseLifecycleEventSink.Instance);
            RimMindRuntimeHost.Initialize(sp, Settings);
            var scope = RuntimeServiceHub.Shared.Capture();
            var runtime = scope.GetRequired<RimMindRuntime>();

            if (Settings.SavedModVersion != null &&
                Settings.SavedModVersion != BootstrapConstants.CurrentModVersion)
            {
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    RimMindErrors.Warn(
                        $"[RimMind-Core] Saved mod version mismatch. Old saves may not be fully compatible with v{BootstrapConstants.CurrentModVersion}.");
                    RuntimeServiceHub.Shared.Capture()
                        .GetOptional<RimMindRuntime>()?
                        .WindowService?
                        .OpenUpgradeWarning();
                });
            }
            Settings.SavedModVersion = BootstrapConstants.CurrentModVersion;

            JsonTagExtractor.OnWarning = msg => RimMindErrors.Warn(msg);
            new Harmony(BootstrapConstants.HarmonyId).PatchAll();

            RimMindAPI.Extensions<IToggleBehavior>().Register(new CoreOverlayToggle());

            RimMindAPI.RegisterParameterTuner(new FlywheelBuiltinTuner());

            // Mod constructors run before RimWorld activates LoadedLanguage. Translating here
            // emits "No active language" errors and leaves scenario descriptions as raw keys.
            // The registry is not consumed until play-data loading has completed, so defer only
            // the language-dependent registration to the main-thread completion queue.
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                var currentScope = RuntimeServiceHub.Shared.Capture();
                ScenarioRegistry.RegisterCoreScenarios(
                    currentScope.GetRequired<ITranslationService>(),
                    currentScope.GetRequired<ILogSink>());
            });

            // L3: Use instance-based RelevanceTable
            runtime.RelevanceTable.RegisterCoreRelevance();

        }

        public override string SettingsCategory() => "RimMind";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            RimMindCoreSettingsUI.Draw(inRect);
        }
    }

    internal sealed class CoreOverlayToggle : IToggleBehavior
    {
        private readonly RuntimeServiceRef<ISettingsProvider> _settings =
            RuntimeServiceRef<ISettingsProvider>.Optional();

        public string Id => "request_overlay";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;
        public bool IsActive => _settings.ValueOrDefault?.RequestOverlayEnabled == true;

        public void Toggle()
        {
            var settings = _settings.ValueOrDefault;
            if (settings == null)
            {
                RimMindErrors.Warn("[RimMind-Core] Request overlay settings are unavailable while the runtime is stopped.");
                return;
            }

            settings.RequestOverlayEnabled = !settings.RequestOverlayEnabled;
            settings.Persist();
        }
    }

    internal static class AssemblyLoadGuard
    {
        public static void AssertAssembliesLoaded()
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies();
            var domain = System.Linq.Enumerable.FirstOrDefault(
                loaded,
                a => a.GetName().Name == BootstrapConstants.DomainAssemblyName);
            var application = System.Linq.Enumerable.FirstOrDefault(
                loaded,
                a => a.GetName().Name == BootstrapConstants.ApplicationAssemblyName);
            var core = System.Linq.Enumerable.FirstOrDefault(
                loaded,
                a => a.GetName().Name == BootstrapConstants.CoreAssemblyName);

            if (domain == null)
            {
                var msg = $"[RimMind-Core] FATAL: {BootstrapConstants.DomainAssemblyName}.dll not loaded. " +
                          "Check that the dll exists in Assemblies/ folder. " +
                          "If you upgraded from an earlier version, please subscribe to the new mod files.";
                RimMindErrors.Error(msg);
                throw new System.InvalidOperationException(msg);
            }

            if (application == null)
            {
                var msg = $"[RimMind-Core] FATAL: {BootstrapConstants.ApplicationAssemblyName}.dll not loaded. " +
                          "Check that the dll exists in Assemblies/ folder.";
                RimMindErrors.Error(msg);
                throw new System.InvalidOperationException(msg);
            }

            if (core == null)
            {
                var msg = $"[RimMind-Core] FATAL: {BootstrapConstants.CoreAssemblyName}.dll not loaded. " +
                          "Check that the dll exists in Assemblies/ folder.";
                RimMindErrors.Error(msg);
                throw new System.InvalidOperationException(msg);
            }

            Log.Message($"[RimMind-Core] Assemblies loaded for v{BootstrapConstants.CurrentModVersion}: " +
                        $"Domain={domain.GetName().Version} " +
                        $"Application={application.GetName().Version} " +
                        $"Core={core.GetName().Version}");
        }
    }
}
