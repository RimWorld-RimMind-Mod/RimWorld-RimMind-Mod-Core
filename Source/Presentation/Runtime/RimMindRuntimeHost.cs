using System;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Application.Common.Interfaces.Client;
using RimMind.Presentation.Runtime.Composition;
using RimMind.Presentation.Runtime.Services;

namespace RimMind.Presentation.Runtime
{
    internal static class RimMindRuntimeHost
    {
        private static readonly object Sync = new object();
        private static ExtensionRegistryCatalog _extensions = new ExtensionRegistryCatalog();
        private static readonly AgentActionBridgeSlot ActionBridge = new AgentActionBridgeSlot();
        private static RuntimeComposition? _current;

        public static void Initialize(
            ISettingsProvider? settingsProvider = null,
            IOpenAISettings? openAiSettings = null)
        {
            if (!TryCompose(settingsProvider, openAiSettings, initializeOnly: true, out var error))
                throw new InvalidOperationException("[RimMind-Core] Runtime composition failed.", error);
        }

        public static bool TryRecompose(
            ISettingsProvider? settingsProvider,
            IOpenAISettings? openAiSettings,
            out Exception? error)
            => TryCompose(settingsProvider, openAiSettings, initializeOnly: false, out error);

        private static bool TryCompose(
            ISettingsProvider? settingsProvider,
            IOpenAISettings? openAiSettings,
            bool initializeOnly,
            out Exception? error)
        {
            RuntimeComposition? retiredComposition = null;
            RuntimeLifetime? retiredLifetime = null;
            RuntimeComposition? rejectedCandidate = null;
            long retiredGeneration = 0;
            var published = false;

            lock (Sync)
            {
                if (initializeOnly && _current != null)
                {
                    error = null;
                    return true;
                }

                RuntimeComposition? candidate = null;
                var runtimeId = Guid.NewGuid();
                try
                {
                    RuntimeServiceHub.Shared.RecordBuildStarted(runtimeId);
                    var root = new RimMindCompositionRoot();
                    candidate = root.Compose(
                        runtimeId,
                        settingsProvider,
                        openAiSettings,
                        _extensions.Fork(),
                        ActionBridge);
                    var snapshot = candidate.Services.Build();
                    var publication = RuntimeServiceHub.Shared.Publish(
                        snapshot,
                        candidate.Lifetime,
                        retireReplacedLifetime: false);
                    retiredComposition = _current;
                    retiredLifetime = publication.RetiredLifetime;
                    retiredGeneration = publication.RetiredSnapshot.Generation;
                    _current = candidate;
                    _extensions = candidate.Extensions;
                    published = true;
                    candidate = null;
                    error = null;
                }
                catch (Exception ex)
                {
                    rejectedCandidate = candidate;
                    RuntimeServiceHub.Shared.RecordBuildFailure(runtimeId, ex);
                    error = ex;
                }
            }

            rejectedCandidate?.Dispose();
            if (!published)
            {
                return false;
            }

            Retire(retiredLifetime, retiredComposition, retiredGeneration);
            return true;
        }

        public static void Shutdown()
        {
            RuntimeComposition? retiredComposition;
            RuntimeLifetime? retiredLifetime;
            long retiredGeneration;

            lock (Sync)
            {
                if (_current == null) return;
                retiredComposition = _current;
                _current = null;
                var publication = RuntimeServiceHub.Shared.Stop(retireReplacedLifetime: false);
                retiredLifetime = publication.RetiredLifetime;
                retiredGeneration = publication.RetiredSnapshot.Generation;
                ActionBridge.Reset();
                _extensions = new ExtensionRegistryCatalog();
            }

            RimMindRuntimeGameComponent.StopGameServices();
            Retire(retiredLifetime, retiredComposition, retiredGeneration);
        }

        private static void Retire(
            RuntimeLifetime? lifetime,
            RuntimeComposition? composition,
            long generation)
        {
            lifetime?.Retire();
            if (composition == null) return;
            var runtimeId = composition.Services.RuntimeId;
            composition.Dispose();
            RuntimeServiceHub.Shared.RecordRuntimeRetired(runtimeId, generation);
        }
    }
}
